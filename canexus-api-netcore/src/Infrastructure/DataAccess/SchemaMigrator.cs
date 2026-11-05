
using System.Security.Cryptography;
using System.Text;
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
        EnsureMigrationTableExists();

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
        EnsureMigrationTableExists();

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
                ComputeHash($"{migration.MigrationId}|{migration.Description}"),
                migration.CreatedTableNames);

            var result = await client.Ado.UseTranAsync(
                async () =>
                {
                    // 原生 SQL 经事务执行器运行（裸 Ado.ExecuteCommand 不挂接事务，会自动提交）
                    var runner = new TransactionSqlRunner(
                        (System.Data.Common.DbConnection)client.Ado.Connection,
                        (System.Data.Common.DbTransaction)client.Ado.Transaction);
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
        if (!client.DbMaintenance.IsAnyTable("sys_schema_migration"))
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

    /// <summary>确保迁移记录表存在（CodeFirst 建表，与模型表同机制）。</summary>
    private void EnsureMigrationTableExists()
    {
        if (!client.DbMaintenance.IsAnyTable("sys_schema_migration"))
        {
            client.CodeFirst.InitTables(typeof(SchemaMigrationRecord));
        }
    }

    /// <summary>计算脚本指纹（SHA-256 十六进制）。</summary>
    /// <param name="content">指纹内容。</param>
    /// <returns>指纹。</returns>
    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

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
