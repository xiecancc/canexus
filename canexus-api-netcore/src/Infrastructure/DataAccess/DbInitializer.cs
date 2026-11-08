using Domain.Entities;
using Infrastructure.Shared.DataAccess;
using Infrastructure.Shared.Locking;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Infrastructure.DataAccess;

/// <summary>
/// 数据库初始化器（T-13）：按「库 / 结构 / 种子」三层分别判定，判定结论写入启动日志与就绪状态。
/// <list type="bullet">
/// <item>库层：用剥离库名的管理连接查 information_schema.SCHEMATA，不存在时按配置建库或明确拒绝。</item>
/// <item>结构层：以 sys_schema_migration 已应用版本与代码期望版本比对（一致跳过 / 落后按序应用 /
/// 代码更旧拒绝启动），表存在判定统一走 information_schema；已就绪的启动不执行任何 DDL。</item>
/// <item>种子层：由宿主在结构就绪后调用 SeedDataInitializer（幂等、独立分布式锁）。</item>
/// </list>
/// <para>扫描配置程序集中继承 <see cref="DomainBase"/> 体系的具体实体类型（排除抽象类与泛型定义）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="options">数据访问配置。</param>
/// <param name="logger">日志器（记录初始化过程与三层判定结论）。</param>
/// <param name="migrator">数据库迁移器（版本判定 / 脚本应用 / 启动校验）。</param>
/// <param name="migrations">增量迁移清单。</param>
/// <param name="state">初始化结论状态（供 /health/ready 输出）。</param>
/// <param name="distributedLock">分布式锁（多实例并发启动保护 DDL 关键区）；未注册时降级为无锁。</param>
/// <param name="environment">宿主环境（Production 禁止应用自动改表）；未注册时按非生产处理。</param>
public sealed class DbInitializer(
    ISqlSugarClient client,
    DatabaseOption options,
    ILogger<DbInitializer> logger,
    SchemaMigrator migrator,
    IEnumerable<ISchemaMigration> migrations,
    DatabaseInitializationState state,
    IDistributedLock? distributedLock = null,
    IHostEnvironment? environment = null)
{
    /// <summary>结构初始化分布式锁键（与种子初始化 seed:initialize 分开，避免相互阻塞）。</summary>
    private const string SchemaLockKey = "schema:initialize";

    /// <summary>按三层判定执行初始化；任一阶段失败即抛异常阻止启动（fail-fast）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entityTypes = options.EntityAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsConcreteEntityType)
            .Distinct()
            .ToArray();
        var modelTableNames = entityTypes
            .Select(type => client.EntityMaintenance.GetEntityInfo(type).DbTableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var orderedMigrations = SchemaMigrationCatalog.Order(migrations);

        await InitializeDatabaseLayerAsync(cancellationToken);
        await InitializeSchemaLayerAsync(entityTypes, modelTableNames, orderedMigrations, cancellationToken);
    }

    /// <summary>库层：管理连接查 information_schema.SCHEMATA，不存在时按配置建库或明确拒绝。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    /// <exception cref="InvalidOperationException">库不存在且当前配置禁止自动建库时抛出。</exception>
    private async Task InitializeDatabaseLayerAsync(CancellationToken cancellationToken)
    {
        var (managementConnection, databaseName) = DatabaseSchemaProbe.Split(options.ConnectionString);
        var exists = await DatabaseSchemaProbe.DatabaseExistsAsync(
            managementConnection, databaseName, cancellationToken);

        if (!exists)
        {
            // 生产默认不授予建库权限：必须显式开启 AutoCreateDatabase 且 AllowSchemaInitialization
            var allowed = options.AutoCreateDatabase
                && (environment?.IsProduction() != true || options.AllowSchemaInitialization);
            if (!allowed)
            {
                throw new InvalidOperationException(
                    $"数据库 {databaseName} 不存在，且当前配置禁止应用自动建库。"
                    + "请由运维预建数据库（或显式配置 Database:AutoCreateDatabase / AllowSchemaInitialization）。");
            }

            await DatabaseSchemaProbe.CreateDatabaseAsync(managementConnection, databaseName, cancellationToken);
            logger.LogInformation("数据库初始化[库层]：{Database} 不存在，已用管理连接创建", databaseName);
        }
        else
        {
            logger.LogInformation("数据库初始化[库层]：{Database} 已存在（information_schema.SCHEMATA）", databaseName);
        }

        state.MarkDatabase(DatabaseInitializationStage.Ready);
    }

    /// <summary>结构层：版本比对驱动，就绪即跳过一切 DDL；落后按脚本补齐；代码更旧拒绝启动。</summary>
    /// <param name="entityTypes">模型实体类型。</param>
    /// <param name="modelTableNames">模型表名清单。</param>
    /// <param name="orderedMigrations">按版本排序的迁移清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task InitializeSchemaLayerAsync(
        IReadOnlyList<Type> entityTypes,
        IReadOnlyList<string> modelTableNames,
        IReadOnlyList<ISchemaMigration> orderedMigrations,
        CancellationToken cancellationToken)
    {
        // 迁移机制关闭：结构由外部自行管理，保留原 CodeFirst 开关行为，不参与版本判定
        if (!options.MigrationsEnabled)
        {
            if (entityTypes.Count > 0 && (options.AutoCreateTable || options.AutoSyncStructure))
            {
                client.CodeFirst.InitTables(entityTypes.ToArray());
            }

            logger.LogInformation("数据库初始化[结构层]：MigrationsEnabled=false，跳过版本判定（结构由外部管理）");
            state.MarkSchema(DatabaseInitializationStage.Ready, 0, 0);
            return;
        }

        var status = await migrator.GetStatusAsync(modelTableNames, orderedMigrations, cancellationToken);
        state.MarkSchema(DatabaseInitializationStage.Pending, status.AppliedVersion, status.ExpectedVersion);

        if (status.Readiness == SchemaReadiness.CodeBehind)
        {
            throw new InvalidOperationException(
                $"数据库结构版本 {status.AppliedVersion} 高于代码期望版本 {status.ExpectedVersion}"
                + $"（无法识别的已应用迁移：{string.Join(", ", status.UnknownRecords)}）。"
                + "当前代码更旧，禁止启动以免结构不兼容；请部署与数据库版本匹配的代码。");
        }

        if (status.Readiness == SchemaReadiness.Ready)
        {
            await ValidateOnlyAsync(entityTypes, modelTableNames, orderedMigrations, cancellationToken);
            logger.LogInformation(
                "数据库初始化[结构层]：已应用版本 {Applied} 与代码期望 {Expected} 一致，模型表齐全，本次启动不执行任何 DDL",
                status.AppliedVersion, status.ExpectedVersion);
            state.MarkSchema(DatabaseInitializationStage.Ready, status.AppliedVersion, status.ExpectedVersion);
            return;
        }

        // 非就绪路径：DDL 关键区由分布式锁保护；锁不可用时降级为无锁，靠唯一索引与幂等脚本兜底
        await using var handle = distributedLock is null
            ? null
            : await distributedLock.AcquireAsync(
                SchemaLockKey, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), cancellationToken);
        if (handle is null)
        {
            logger.LogWarning("数据库初始化[结构层]：{LockKey} 不可用，降级为无锁执行（依赖幂等脚本与唯一索引兜底）", SchemaLockKey);
        }

        // 锁内二次判定：另一实例可能已完成初始化
        status = await migrator.GetStatusAsync(modelTableNames, orderedMigrations, cancellationToken);
        if (status.Readiness == SchemaReadiness.CodeBehind)
        {
            throw new InvalidOperationException(
                $"数据库结构版本 {status.AppliedVersion} 高于代码期望版本 {status.ExpectedVersion}，当前代码更旧，禁止启动");
        }

        if (status.Readiness == SchemaReadiness.Uninitialized)
        {
            await InitializeTablesAsync(entityTypes, modelTableNames, status, cancellationToken);
        }

        await migrator.EnsureBaselineAsync(modelTableNames, cancellationToken);
        await migrator.ApplyPendingAsync(orderedMigrations, cancellationToken);
        await ValidateOnlyAsync(entityTypes, modelTableNames, orderedMigrations, cancellationToken);

        var final = await migrator.GetStatusAsync(modelTableNames, orderedMigrations, cancellationToken);
        if (final.Readiness != SchemaReadiness.Ready)
        {
            throw new InvalidOperationException(
                $"数据库结构初始化后仍未就绪（{final.Readiness}）：缺失表 {string.Join(", ", final.MissingTables)}；"
                + $"已应用版本 {final.AppliedVersion} / 期望 {final.ExpectedVersion}");
        }

        logger.LogInformation(
            "数据库初始化[结构层]：由 {Before} 推进到版本 {Applied}，已应用 {Count} 个迁移脚本",
            status.Readiness, final.AppliedVersion, orderedMigrations.Count);
        state.MarkSchema(DatabaseInitializationStage.Ready, final.AppliedVersion, final.ExpectedVersion);
    }

    /// <summary>结构未初始化路径：非生产或显式授权时按现有 CodeFirst 一次性建表并修正引擎；生产默认拒绝。</summary>
    /// <param name="entityTypes">模型实体类型。</param>
    /// <param name="modelTableNames">模型表名清单。</param>
    /// <param name="status">判定快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task InitializeTablesAsync(
        IReadOnlyList<Type> entityTypes,
        IReadOnlyList<string> modelTableNames,
        SchemaStatus status,
        CancellationToken cancellationToken)
    {
        var canInitialize = environment?.IsProduction() != true || options.AllowSchemaInitialization;
        if (!canInitialize)
        {
            throw new InvalidOperationException(
                "数据库结构未初始化（缺表或无迁移记录），Production 禁止由应用自动改表。"
                + $"缺失表：{string.Join(", ", status.MissingTables)}。"
                + "请先经迁移通道建立结构，或显式配置 Database:AllowSchemaInitialization=true 授权一次性初始化。");
        }

        if (!options.AutoCreateTable && !options.AutoSyncStructure)
        {
            throw new InvalidOperationException(
                "数据库结构未初始化，但 AutoCreateTable 与 AutoSyncStructure 均为关闭，无法建立结构。");
        }

        if (entityTypes.Count == 0)
        {
            return;
        }

        client.CodeFirst.InitTables(entityTypes.ToArray());
        logger.LogInformation(
            "数据库初始化[结构层]：首次建表完成，共 {EntityCount} 个实体类型（缺表 {MissingCount} 张）",
            entityTypes.Count, status.MissingTables.Count);

        // 事务支持要求 InnoDB：仅首次初始化路径执行，结构就绪的启动不触碰任何 ALTER TABLE
        foreach (var tableName in modelTableNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engine = await client.Ado.GetScalarAsync(
                "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName",
                new SugarParameter("@tableName", tableName));
            if (!string.Equals(engine?.ToString(), "InnoDB", StringComparison.OrdinalIgnoreCase))
            {
                await client.Ado.ExecuteCommandAsync($"ALTER TABLE `{tableName}` ENGINE = InnoDB");
            }
        }
    }

    /// <summary>只读校验：已应用脚本正文未被篡改、模型表均已登记迁移、可选的模型结构与 information_schema 一致。</summary>
    /// <param name="entityTypes">模型实体类型（结构校验开启时生成模型快照）。</param>
    /// <param name="modelTableNames">模型表名清单。</param>
    /// <param name="orderedMigrations">按版本排序的迁移清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task ValidateOnlyAsync(
        IReadOnlyList<Type> entityTypes,
        IReadOnlyList<string> modelTableNames,
        IReadOnlyList<ISchemaMigration> orderedMigrations,
        CancellationToken cancellationToken)
    {
        if (!options.MigrationsEnabled)
        {
            return;
        }

        await migrator.ValidateAppliedScriptsAsync(orderedMigrations, cancellationToken);
        await migrator.ValidateModelAgainstMigrationsAsync(modelTableNames, orderedMigrations, cancellationToken);

        // T5 #13：可选的模型结构校验（默认关闭）。脚本正文指纹只能发现「已应用脚本被改写」，
        // 无法发现「绕过迁移通道直接改表 / 脚本落地不完整」；此处用 EntityMaintenance 模型快照
        // 与 information_schema 列/主键比对补上这一段。默认关闭以免存量库历史漂移阻断启动。
        if (options.ValidateModelStructure)
        {
            await new ModelStructureValidator(client).ValidateAsync(entityTypes, cancellationToken);
        }
    }

    /// <summary>判断类型是否为具体实体类型：类、非抽象、非泛型定义，且基类链中存在实体体系基类（或为显式纳入的基础设施消息实体）。</summary>
    /// <param name="type">待判断类型。</param>
    /// <returns>是具体实体类型时返回 true。</returns>
    private static bool IsConcreteEntityType(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
        {
            return false;
        }

        // 基础设施消息实体（P1-R3）：OutboxMessage 不继承 DomainBase 体系（无乐观锁 / 软删除 / 租户过滤语义），
        // 但仍须参与 CodeFirst 建表，故显式纳入
        if (type == typeof(OutboxMessage))
        {
            return true;
        }

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current == typeof(DomainBase))
            {
                return true;
            }
        }

        return false;
    }
}
