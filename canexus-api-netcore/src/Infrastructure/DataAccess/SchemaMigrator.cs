
using Domain.Entities;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Infrastructure.DataAccess;

/// <summary>
/// 数据库迁移器（P1-A3）：基线登记 + 增量迁移 + 启动校验。
/// <list type="bullet">
/// <item>基线登记：迁移记录表无记录时，把 CodeFirst 模型当前全部表登记为 baseline-v1（不重建表，结构由 CodeFirst 同步）；重复执行幂等。</item>
/// <item>增量应用：按 MigrationId 顺序执行未应用的迁移，脚本与记录写入同事务（失败整体回滚，生产可回滚）；重复执行幂等；重复 MigrationId 冲突即终止。</item>
/// <item>启动校验：模型表须已登记（基线或任一已应用增量），否则抛异常阻止启动（避免只改实体不补迁移）。</item>
/// </list>
/// <para>并发安全：多副本同时启动时，记录表唯一索引（MigrationId）保证只登记一次，重复键冲突视为已由他实例完成。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="logger">日志器。</param>
public sealed class SchemaMigrator(ISqlSugarClient client, ILogger<SchemaMigrator> logger)
{
    /// <summary>基线迁移 ID。</summary>
    public const string BaselineMigrationId = "baseline-v1";

    /// <summary>登记基线：迁移记录表无记录时登记当前模型全部表（幂等；多副本冲突由唯一索引兜底）。</summary>
    /// <param name="modelTableNames">模型表名清单（来自 CodeFirst 实体扫描）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task EnsureBaselineAsync(
        IReadOnlyList<string> modelTableNames,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureMigrationTableExistsAsync(cancellationToken);

        if (await client.Queryable<SchemaMigrationRecord>().AnyAsync(cancellationToken))
        {
            return;
        }

        var tables = modelTableNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var record = SchemaMigrationRecord.CreateBaseline(tables, ComputeHash(string.Join("|", tables)));

        try
        {
            await client.Insertable(record).ExecuteCommandAsync();
            logger.LogInformation("数据库基线登记完成：{Count} 张模型表（baseline-v1）", tables.Length);
        }
        catch (Exception exception) when (IsDuplicateKey(exception))
        {
            logger.LogInformation("数据库基线已由其他副本登记，跳过");
        }
    }

    /// <summary>应用未执行的增量迁移（按 MigrationId 顺序；脚本与记录同事务，失败整体回滚）。</summary>
    /// <param name="migrations">增量迁移清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    /// <exception cref="InvalidOperationException">迁移清单含重复 MigrationId 时抛出。</exception>
    public async Task ApplyPendingAsync(
        IReadOnlyList<ISchemaMigration> migrations,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureMigrationTableExistsAsync(cancellationToken);

        var appliedIds = (await client.Queryable<SchemaMigrationRecord>()
                .Select(x => x.MigrationId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var pending = migrations
            .Where(migration => !appliedIds.Contains(migration.MigrationId))
            .OrderBy(migration => migration.MigrationId, StringComparer.Ordinal)
            .ToArray();

        // 清单内重复 ID（均未应用）：冲突即终止，杜绝歧义
        var duplicates = pending
            .GroupBy(migration => migration.MigrationId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"迁移清单存在重复 MigrationId：{string.Join(", ", duplicates)}");
        }

        foreach (var migration in pending)
        {
            var record = SchemaMigrationRecord.CreateIncremental(
                migration.MigrationId,
                migration.Description,
                SchemaMigrationCatalog.ScriptHash(migration),
                migration.CreatedTableNames);

            var result = await client.Ado.UseTranAsync(
                async () =>
                {
                    // 原生 SQL 经事务执行器运行（裸 Ado.ExecuteCommand 不挂接事务，会自动提交）
                    var runner = new TransactionSqlRunner(
                        (System.Data.Common.DbConnection)client.Ado.Connection,
                        (System.Data.Common.DbTransaction)client.Ado.Transaction);

                    // 脚本正文是结构变更的唯一执行来源（指纹即正文哈希）；ApplyAsync 保留为扩展点，
                    // 供需要 ORM 协同的迁移使用，但不得再自行偷偷执行未登记的 DDL
                    foreach (var script in migration.Scripts)
                    {
                        await runner.ExecuteAsync(script, cancellationToken);
                    }

                    await migration.ApplyAsync(client, runner, cancellationToken);
                    await client.Insertable(record).ExecuteCommandAsync();
                    return true;
                });

            if (result.IsSuccess)
            {
                logger.LogInformation("已应用数据库迁移 {MigrationId}：{Description}", migration.MigrationId, migration.Description);
                continue;
            }

            if (IsDuplicateKey(result.ErrorException))
            {
                // 另一副本已应用同一迁移（唯一索引冲突）：视为完成，不重复执行
                logger.LogInformation("迁移 {MigrationId} 已由其他副本应用，跳过", migration.MigrationId);
                continue;
            }

            throw result.ErrorException
                ?? new InvalidOperationException($"迁移 {migration.MigrationId} 应用失败，已回滚");
        }
    }

    /// <summary>启动校验：模型表须已登记（基线或已应用增量），否则抛出指明缺失迁移的表（避免只改实体不补迁移）。</summary>
    /// <param name="modelTableNames">模型表名清单。</param>
    /// <param name="migrations">增量迁移清单（读取已应用记录的建表清单）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    /// <exception cref="InvalidOperationException">存在未登记迁移的模型表时抛出。</exception>
    public async Task ValidateModelAgainstMigrationsAsync(
        IReadOnlyList<string> modelTableNames,
        IReadOnlyList<ISchemaMigration> migrations,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await TableExistsAsync("sys_schema_migration", cancellationToken))
        {
            // 迁移机制未启用过的库不做校验（由 DbInitializer 先登记基线）
            return;
        }

        var records = await client.Queryable<SchemaMigrationRecord>().ToListAsync(cancellationToken);
        var registered = records
            .SelectMany(record => (record.TableNames ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);

        // 已登记集合以记录为准，无需迁移清单二次合并（记录即审计事实）
        _ = migrations;

        var missing = modelTableNames
            .Where(name => !registered.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"模型存在未登记迁移的表（新增表须补增量迁移并登记）：{string.Join(", ", missing)}");
        }
    }

    /// <summary>以 information_schema 判定表是否存在（不使用 DbMaintenance.IsAnyTable）。</summary>
    /// <param name="tableName">表名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在返回 true。</returns>
    public async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var count = await client.Ado.GetScalarAsync(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table",
            new SugarParameter("@table", tableName));
        return Convert.ToInt64(count) > 0;
    }

    /// <summary>
    /// 结构就绪判定（T-13）：比对 sys_schema_migration 已应用版本与代码期望版本，并核对模型表齐全。
    /// <para>一致 → Ready（启动不执行任何 DDL）；落后 → Pending；缺表 / 无迁移表 → Uninitialized；
    /// 库中存在代码无法识别的更高版本 → CodeBehind（拒绝启动）。</para>
    /// </summary>
    /// <param name="modelTableNames">模型表名清单。</param>
    /// <param name="orderedMigrations">按版本排序的代码迁移清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>判定快照。</returns>
    public async Task<SchemaStatus> GetStatusAsync(
        IReadOnlyList<string> modelTableNames,
        IReadOnlyList<ISchemaMigration> orderedMigrations,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expectedVersion = SchemaMigrationCatalog.ExpectedVersion(orderedMigrations);
        var missingTables = await DatabaseSchemaProbe.FindMissingTablesAsync(client, modelTableNames, cancellationToken);
        var hasMigrationTable = await TableExistsAsync("sys_schema_migration", cancellationToken);
        if (!hasMigrationTable)
        {
            return new SchemaStatus(
                SchemaReadiness.Uninitialized, SchemaMigrationCatalog.BaselineVersion,
                expectedVersion, missingTables, Array.Empty<string>());
        }

        var records = await client.Queryable<SchemaMigrationRecord>().ToListAsync(cancellationToken);
        var knownIds = orderedMigrations
            .Select(migration => migration.MigrationId)
            .ToHashSet(StringComparer.Ordinal);
        var unknownRecords = records
            .Select(record => record.MigrationId)
            .Where(id => !knownIds.Contains(id) && id != BaselineMigrationId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var appliedVersion = records
            .Select(record => SchemaMigrationCatalog.VersionOf(record.MigrationId))
            .Where(version => version.HasValue)
            .Select(version => version!.Value)
            .DefaultIfEmpty(SchemaMigrationCatalog.BaselineVersion)
            .Max();

        if (unknownRecords.Length > 0 || appliedVersion > expectedVersion)
        {
            return new SchemaStatus(
                SchemaReadiness.CodeBehind, appliedVersion, expectedVersion, missingTables, unknownRecords);
        }

        if (missingTables.Count > 0 || !records.Any(record => record.MigrationId == BaselineMigrationId))
        {
            return new SchemaStatus(
                SchemaReadiness.Uninitialized, appliedVersion, expectedVersion, missingTables, unknownRecords);
        }

        if (appliedVersion < expectedVersion)
        {
            return new SchemaStatus(
                SchemaReadiness.Pending, appliedVersion, expectedVersion, missingTables, unknownRecords);
        }

        return new SchemaStatus(
            SchemaReadiness.Ready, appliedVersion, expectedVersion, missingTables, unknownRecords);
    }

    /// <summary>
    /// 校验已应用迁移的脚本正文指纹：正文变更即拒绝启动；升级前的旧口径指纹做一次性自愈。
    /// <para>自愈范围严格限定为「记录值恰好等于旧口径哈希」，避免把真实篡改当成兼容升级。</para>
    /// </summary>
    /// <param name="orderedMigrations">按版本排序的代码迁移清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    /// <exception cref="InvalidOperationException">已应用迁移的脚本正文发生变更时抛出。</exception>
    public async Task ValidateAppliedScriptsAsync(
        IReadOnlyList<ISchemaMigration> orderedMigrations,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await TableExistsAsync("sys_schema_migration", cancellationToken))
        {
            return;
        }

        var records = await client.Queryable<SchemaMigrationRecord>().ToListAsync(cancellationToken);
        foreach (var migration in orderedMigrations)
        {
            var record = records.FirstOrDefault(item => item.MigrationId == migration.MigrationId);
            if (record is null)
            {
                continue;
            }

            var expected = SchemaMigrationCatalog.ScriptHash(migration);
            if (string.Equals(record.ScriptHash, expected, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(record.ScriptHash, SchemaMigrationCatalog.LegacyHash(migration), StringComparison.Ordinal))
            {
                // 升级前记录以 ID + 说明作为指纹：一次性回写为脚本正文指纹（不改结构）
                await client.Ado.ExecuteCommandAsync(
                    "UPDATE sys_schema_migration SET ScriptHash = @hash WHERE MigrationId = @id",
                    new SugarParameter("@hash", expected),
                    new SugarParameter("@id", migration.MigrationId));
                logger.LogInformation("迁移 {MigrationId} 的脚本指纹已升级为正文哈希", migration.MigrationId);
                continue;
            }

            throw new InvalidOperationException(
                $"迁移 {migration.MigrationId} 的脚本正文已变更，但库中记录为已应用；"
                + "请新增更高版本的迁移，禁止改写已应用的脚本");
        }
    }

    /// <summary>确保迁移记录表存在（仅在结构初始化路径调用；表存在时不会建表）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task EnsureMigrationTableExistsAsync(CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync("sys_schema_migration", cancellationToken))
        {
            client.CodeFirst.InitTables(typeof(SchemaMigrationRecord));
        }
    }

    /// <summary>计算指纹（委托目录实现，口径唯一）。</summary>
    /// <param name="content">正文。</param>
    /// <returns>指纹。</returns>
    private static string ComputeHash(string content) => SchemaMigrationCatalog.ComputeHash(content);

    /// <summary>判断异常是否为记录表唯一索引冲突（MySQL 1062 Duplicate entry）。</summary>
    /// <param name="exception">异常。</param>
    /// <returns>是重复键冲突时返回 true。</returns>
    private static bool IsDuplicateKey(Exception? exception)
    {
        // 按驱动错误码判定（MySqlException.Number == 1062），不解析异常消息文本——
        // 消息随 MySQL 服务端 locale / 驱动版本变化，字符串匹配跨环境脆弱（P1-R4）
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlConnector.MySqlException { Number: 1062 })
            {
                return true;
            }
        }

        return false;
    }
}
