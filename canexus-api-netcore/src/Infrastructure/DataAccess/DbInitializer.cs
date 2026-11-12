
using Domain.Entities;
using Infrastructure.Shared.DataAccess;
using Infrastructure.Shared.Locking;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Infrastructure.DataAccess;

/// <summary>
/// 数据库初始化器：按「库 / 结构 / 种子」三层分别判定，判定结论写入启动日志与就绪状态。
/// <list type="bullet">
/// <item>库层：用剥离库名的管理连接查 information_schema.SCHEMATA，不存在时按配置建库或明确拒绝。</item>
/// <item>结构层：缺表时由 CodeFirst 依实体模型一次性建表并修正为 InnoDB；随后按序执行全部幂等迁移脚本。
/// 表齐全时不执行任何建表 DDL，只重跑幂等迁移，避免误删手工列。</item>
/// <item>种子层：由宿主在结构就绪后调用 SeedDataInitializer（幂等、独立分布式锁）。</item>
/// </list>
/// <para>框架处于开发初期，所有数据库均为临时库，结构可直接删除重建；每次启动只按当前模型与
/// 幂等迁移清单收敛结构。未来新增结构变更只需追加 <see cref="ISchemaMigration"/>。</para>
/// <para>扫描配置程序集中继承 <see cref="DomainBase"/> 体系的具体实体类型（排除抽象类与泛型定义）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="options">数据访问配置。</param>
/// <param name="logger">日志器（记录初始化过程与三层判定结论）。</param>
/// <param name="migrator">迁移执行器（按序执行幂等脚本）。</param>
/// <param name="migrations">迁移清单。</param>
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

    /// <summary>结构层：缺表时 CodeFirst 一次性建表，随后按序执行全部幂等迁移；表齐全时不建表。</summary>
    /// <param name="entityTypes">模型实体类型。</param>
    /// <param name="modelTableNames">模型表名清单。</param>
    /// <param name="orderedMigrations">按序号排序的迁移清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task InitializeSchemaLayerAsync(
        IReadOnlyList<Type> entityTypes,
        IReadOnlyList<string> modelTableNames,
        IReadOnlyList<ISchemaMigration> orderedMigrations,
        CancellationToken cancellationToken)
    {
        // 迁移机制关闭：仅跳过迁移通道；缺表仍会按开关执行一次 CodeFirst 建表，
        // 结构最终完全由外部管理时需同时关闭 AutoCreateTable 与 AutoSyncStructure 两个开关
        if (!options.MigrationsEnabled)
        {
            if (entityTypes.Count > 0 && options.ShouldInitializeStructure)
            {
                client.CodeFirst.InitTables(entityTypes.ToArray());
            }

            logger.LogInformation("数据库初始化[结构层]：MigrationsEnabled=false，跳过迁移（仍按开关执行一次性 CodeFirst 建表；结构最终由外部管理需同时关闭两个开关）");
            state.MarkSchema(DatabaseInitializationStage.Ready);
            return;
        }

        // DDL 关键区由分布式锁保护；锁不可用时降级为无锁，靠幂等脚本兜底
        await using var handle = distributedLock is null
            ? null
            : await distributedLock.AcquireAsync(
                SchemaLockKey, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), cancellationToken);
        if (handle is null)
        {
            logger.LogWarning("数据库初始化[结构层]：{LockKey} 不可用，降级为无锁执行（依赖幂等脚本）", SchemaLockKey);
        }

        var missingTables = DatabaseSchemaProbe.FindMissingTables(client, modelTableNames, cancellationToken);
        if (missingTables.Count > 0)
        {
            await InitializeTablesAsync(entityTypes, modelTableNames, missingTables.Count, cancellationToken);
        }
        else
        {
            logger.LogInformation(
                "数据库初始化[结构层]：{Count} 张模型表齐全，本次启动不执行建表 DDL", modelTableNames.Count);
        }

        // 无历史记录：每次启动按序重跑全部幂等脚本
        await migrator.ApplyAsync(orderedMigrations, cancellationToken);

        if (options.ValidateModelStructure)
        {
            // 可选的模型结构校验：比对实体模型快照与 information_schema 列 / 主键
            await new ModelStructureValidator(client).ValidateAsync(entityTypes, cancellationToken);
        }

        logger.LogInformation(
            "数据库初始化[结构层]：完成，已执行 {Count} 个幂等迁移脚本", orderedMigrations.Count);
        state.MarkSchema(DatabaseInitializationStage.Ready);
    }

    /// <summary>缺表路径：非生产或显式授权时按 CodeFirst 一次性建表并修正引擎；生产默认拒绝。</summary>
    /// <param name="entityTypes">模型实体类型。</param>
    /// <param name="modelTableNames">模型表名清单。</param>
    /// <param name="missingCount">缺失表数量（写入日志）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task InitializeTablesAsync(
        IReadOnlyList<Type> entityTypes,
        IReadOnlyList<string> modelTableNames,
        int missingCount,
        CancellationToken cancellationToken)
    {
        var canInitialize = environment?.IsProduction() != true || options.AllowSchemaInitialization;
        if (!canInitialize)
        {
            throw new InvalidOperationException(
                "数据库结构未初始化（缺表），Production 禁止由应用自动改表。"
                + $"缺失表数量：{missingCount}。"
                + "请先由运维建立结构，或显式配置 Database:AllowSchemaInitialization=true 授权一次性初始化。");
        }

        if (!options.ShouldInitializeStructure)
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
            entityTypes.Count, missingCount);

        // 事务支持要求 InnoDB：MySQL 默认引擎可能为 MyISAM，逐表幂等修正
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

    /// <summary>判断类型是否为具体实体类型：类、非抽象、非泛型定义，且基类链中存在实体体系基类（或为显式纳入的基础设施消息实体）。</summary>
    /// <param name="type">待判断类型。</param>
    /// <returns>是具体实体类型时返回 true。</returns>
    private static bool IsConcreteEntityType(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
        {
            return false;
        }

        // 基础设施消息实体：OutboxMessage 不继承 DomainBase 体系，但仍须参与 CodeFirst 建表
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
