
using System.Collections.Concurrent;
using System.Reflection;
using Domain.Entities;
using Domain.Services.Users;
using Domain.Shared.Logging;
using Infrastructure.Shared.Filtering;
using Infrastructure.Shared.Options;
using Infrastructure.Shared.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Infrastructure.DataAccess;
/// <summary>
/// SqlSugar 客户端工厂：构建单例 SqlSugarScope（线程安全，内部按异步上下文隔离连接与过滤器）。
/// <para>启动快照（连接串 / DbType / IsAutoCloseConnection / 实体程序集）取自首次创建客户端时的
/// IOptionsMonitor 当前值，不支持热加载；CommandTimeOut / SqlLogEnabled 在每个异步上下文
/// 创建客户端时读取监视器最新值（热加载）。</para>
/// </summary>
internal static class SqlSugarClientFactory
{
    /// <summary>
    /// 创建 SqlSugar 客户端：连接配置 + 审计填充 Aop + 全局查询过滤器 + 命令超时 + 可选 SQL 日志。
    /// </summary>
    /// <param name="monitor">数据访问配置监视器（启动快照与热加载值的统一来源）。</param>
    /// <param name="serviceProvider">服务提供者（用于解析租户上下文、过滤开关与当前用户上下文）。</param>
    /// <returns>SqlSugar 客户端。</returns>
    public static SqlSugarScope Create(IOptionsMonitor<DatabaseOption> monitor, IServiceProvider serviceProvider)
    {
        // 启动快照：连接串 / DbType / IsAutoCloseConnection / 实体程序集由首次创建客户端时的配置决定
        //（不支持热加载，连接与过滤器构造一次性定型）
        var options = monitor.CurrentValue;

        // 单例服务被闭包捕获是安全的：TenantContext 与 DataFilter 的状态均基于 AsyncLocal（按请求隔离），其余为无状态服务
        var tenantContext = serviceProvider.GetRequiredService<ITenantContext>();
        var dataFilter = serviceProvider.GetRequiredService<IDataFilter>();
        var currentUser = serviceProvider.GetRequiredService<IUserContext>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SqlSugar");

        var connectionConfig = new ConnectionConfig
        {
            ConnectionString = options.ConnectionString,
            DbType = options.DbType,
            IsAutoCloseConnection = options.IsAutoCloseConnection,
            InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                // 字符串与可空值类型属性默认生成可空列，兼容审计 / 租户等可空字段，避免 CodeFirst 生成过严约束
                EntityService = (property, column) =>
                {
                    if (property.PropertyType == typeof(string) || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                    {
                        column.IsNullable = true;
                    }
                }
            }
        };

        // 第二个参数在每个异步上下文首次创建客户端时执行：审计填充 Aop + 全局查询过滤器 + 命令超时 + 可选 SQL 日志
        return new SqlSugarScope(connectionConfig, client =>
        {
            // 命令超时（秒）：AdoProvider 级别配置，读取监视器最新值（热加载）
            client.Ado.CommandTimeOut = monitor.CurrentValue.CommandTimeOut;

            ConfigureExecutingAop(client, currentUser, tenantContext);
            ConfigureGlobalQueryFilters(client, options, dataFilter, tenantContext, currentUser);

            // SQL 执行日志 + 慢查询日志（P1-R5）统一在 OnLogExecuting / OnLogExecuted 中组合：
            // SqlSugar Aop 为单委托槽位，两类逻辑必须合并赋值（后者赋值会覆盖前者）。
            // 1. 全量 SQL 日志：Database:SqlLogEnabled 开关（调试用，结构化日志）；
            // 2. 慢查询日志：执行耗时超过 Database:SlowQueryLogMilliseconds（默认 500ms，0 = 关闭）的 SQL
            //    以 Warning 记录（含耗时与 TraceId），生产环境无需开启全量日志即可定位慢 SQL。
            // 两者均附带请求 TraceId（P2-B3）——慢接口可经响应头 X-Trace-Id 定位到本请求的全部 SQL 链路。
            // 参数值经 SensitiveDataMasker 按参数名关键字脱敏（P0-S6）：密码哈希 / 令牌哈希等敏感列
            // 不落明文日志，与操作日志（OperLogSanitizer）共用共享内核规则。
            // 计时经 AsyncLocal 起止配对：OnLogExecuting 记录起始时间戳，OnLogExecuted 计算耗时
            //（SqlSugarScope 按异步上下文隔离客户端，同一上下文内执行/完成一一对应，AsyncLocal 跨 Aop 天然隔离）
            var slowThresholdMs = monitor.CurrentValue.SlowQueryLogMilliseconds;
            var executingAt = slowThresholdMs > 0 ? new AsyncLocal<DateTimeOffset>() : null;

            client.Aop.OnLogExecuting = (sql, parameters) =>
            {
                executingAt?.Value = DateTimeOffset.UtcNow;

                if (!monitor.CurrentValue.SqlLogEnabled)
                {
                    return;
                }

                var parameterText = parameters.Length == 0
                    ? string.Empty
                    : $"\n    参数: {string.Join(", ", parameters.Select(p =>
                        $"{p.ParameterName}={(SensitiveDataMasker.IsSensitiveParameter(p.ParameterName) ? SensitiveDataMasker.MaskText : p.Value)}"))}";
                var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "-";
                logger.LogInformation("[SqlSugar] {Sql}{Parameters} TraceId={TraceId}", sql, parameterText, traceId);
            };

            client.Aop.OnLogExecuted = (sql, _) =>
            {
                if (executingAt is null)
                {
                    return;
                }

                var startedAt = executingAt.Value;
                if (startedAt == default)
                {
                    return;
                }

                var threshold = monitor.CurrentValue.SlowQueryLogMilliseconds;
                var elapsed = DateTimeOffset.UtcNow - startedAt;
                if (threshold <= 0 || elapsed.TotalMilliseconds < threshold)
                {
                    return;
                }

                var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "-";
                logger.LogWarning(
                    "[SqlSugar] 慢查询：{ElapsedMs:F0}ms（阈值 {ThresholdMs}ms）{Sql} TraceId={TraceId}",
                    elapsed.TotalMilliseconds, threshold, sql, traceId);
            };
        });
    }

    /// <summary>配置 SqlSugar 写入审计 Aop：创建 / 更新审计字段自动填充、空主键生成与软删除标记。</summary>
    /// <param name="client">SqlSugar 客户端（每异步上下文实例）。</param>
    /// <param name="currentUser">当前用户上下文（审计人取值来源）。</param>
    /// <param name="tenantContext">租户上下文（租户归属填充来源）。</param>
    private static void ConfigureExecutingAop(
        ISqlSugarClient client,
        IUserContext currentUser,
        ITenantContext tenantContext)
    {
        // 审计自动填充 + 主键/软删除初始化：直接修改实体实例属性，SqlSugar 构建 SQL 时会取到新值
        // 创建审计覆盖 DomainBase 全部实体（聚合根与关联表）；主键与软删除标记、更新审计仅聚合根具备
        client.Aop.DataExecuting = (_, filterModel) =>
        {
            switch (filterModel.OperationType)
            {
                case DataFilterType.InsertByObject when filterModel.EntityValue is DomainBase insertEntity:
                    insertEntity.CreatedAt = DateTime.UtcNow;
                    insertEntity.CreatedBy = currentUser.UserId ?? Guid.Empty;
                    // 租户归属自动填充：实体未显式指定时取当前租户上下文（null 表示平台级数据）
                    insertEntity.TenantId ??= tenantContext.TenantId;
                    if (insertEntity is AggregateBase insertAggregate)
                    {
                        // 空主键统一由框架生成 36 位 GUID，业务代码无需显式赋值；关联表为联合主键，无 Id 列
                        if (insertAggregate.Id == Guid.Empty)
                        {
                            insertAggregate.Id = Guid.NewGuid();
                        }

                        // 软删除时间戳：新插入聚合一律为「未删除」（null）
                        insertAggregate.DeletedAt = null;
                    }

                    break;

                case DataFilterType.UpdateByObject when filterModel.EntityValue is AggregateBase updateEntity:
                    // 更新人可空语义：无登录上下文（系统任务 / 集成测试）时保持 null 不记录操作人，
                    // 与按列更新路径（ExecDeleteOrRestore）口径一致；更新时间总是填充
                    updateEntity.UpdatedAt = DateTime.UtcNow;
                    updateEntity.UpdatedBy = currentUser.UserId;
                    break;
            }
        };
    }

    /// <summary>配置全局查询过滤器：按具体实体类型逐个反射注册租户隔离与软删除过滤（AND 组合）。</summary>
    /// <param name="client">SqlSugar 客户端（每异步上下文实例）。</param>
    /// <param name="options">数据访问配置（提供参与扫描的实体程序集）。</param>
    /// <param name="dataFilter">过滤开关。</param>
    /// <param name="tenantContext">租户上下文。</param>
    /// <param name="currentUser">当前用户上下文（区分平台用户 / 租户用户，决定平台级行是否可见）。</param>
    private static void ConfigureGlobalQueryFilters(
        ISqlSugarClient client,
        DatabaseOption options,
        IDataFilter dataFilter,
        ITenantContext tenantContext,
        IUserContext currentUser)
    {
        // 全局过滤器（按具体实体类型逐个注册，AND 组合）：
        // SqlSugar 的 AddTableFilter 对抽象基类注册不会向派生实体传播，故扫描实体程序集中
        // 全部具体实体类型，经反射逐类型注册（扫描结果静态缓存，避免重复反射）。
        // 1. 租户隔离（全部实体，TenantId 定义于 DomainBase）：平台用户（JWT 无 tenantId 声明）
        //    除按 X-Tenant-Id 过滤出的业务数据外，仍可见 TenantId IS NULL 的平台级数据
        //    （sys_tenant 定义表 / 平台管理员 / 平台角色菜单等）；租户用户仅可见本租户数据，
        //    平台级行对其不可见。区分依据为 currentUser.TenantId 而非请求头，避免租户用户越权。
        // 2. 软删除（仅聚合根，DeletedAt 定义于 AggregateBase；关联表为物理删除无墓碑）：
        //    DeletedAt IS NULL 为未删除，开关关闭时墓碑也可见；表达式在每次查询时求值，支持运行时切换
        foreach (var entityType in GetAggregateEntityTypes(options.EntityAssemblies))
        {
            AddAggregateFilterMethod.MakeGenericMethod(entityType)
                .Invoke(null, [client, dataFilter, tenantContext, currentUser]);
        }

        foreach (var entityType in GetPlainEntityTypes(options.EntityAssemblies))
        {
            AddTenantFilterMethod.MakeGenericMethod(entityType)
                .Invoke(null, [client, dataFilter, tenantContext, currentUser]);
        }
    }

    /// <summary>AddAggregateFilter 泛型方法的反射句柄（按具体实体类型注册过滤器时复用）。</summary>
    private static readonly MethodInfo AddAggregateFilterMethod = typeof(SqlSugarClientFactory)
        .GetMethod(nameof(AddAggregateFilter), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"缺少方法 {nameof(AddAggregateFilter)}");

    /// <summary>AddTenantFilter 泛型方法的反射句柄（按具体实体类型注册过滤器时复用）。</summary>
    private static readonly MethodInfo AddTenantFilterMethod = typeof(SqlSugarClientFactory)
        .GetMethod(nameof(AddTenantFilter), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"缺少方法 {nameof(AddTenantFilter)}");

    /// <summary>聚合实体类型缓存（键为实体所在程序集；扫描仅在程序集首次出现时执行一次）。</summary>
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<Type>> AggregateEntityTypeCache = new();

    /// <summary>非聚合实体类型缓存（键为实体所在程序集；扫描仅在程序集首次出现时执行一次）。</summary>
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<Type>> PlainEntityTypeCache = new();

    /// <summary>扫描实体程序集，返回全部 AggregateBase 的非抽象子类（去重，按程序集静态缓存）。</summary>
    /// <param name="assemblies">参与 CodeFirst 建表的实体程序集。</param>
    /// <returns>聚合实体类型列表。</returns>
    private static IReadOnlyList<Type> GetAggregateEntityTypes(IEnumerable<Assembly> assemblies)
    {
        return assemblies
            .SelectMany(assembly => AggregateEntityTypeCache.GetOrAdd(assembly, static asm => asm.GetTypes()
                .Where(type => type.IsClass
                    && !type.IsAbstract
                    && !type.IsGenericTypeDefinition
                    && typeof(AggregateBase).IsAssignableFrom(type))
                .ToList()))
            .Distinct()
            .ToList();
    }

    /// <summary>扫描实体程序集，返回全部非聚合实体类型（DomainBase 的非抽象子类且非聚合根，去重，按程序集静态缓存）。</summary>
    /// <param name="assemblies">参与 CodeFirst 建表的实体程序集。</param>
    /// <returns>关联表等非聚合实体类型列表。</returns>
    private static IReadOnlyList<Type> GetPlainEntityTypes(IEnumerable<Assembly> assemblies)
    {
        return assemblies
            .SelectMany(assembly => PlainEntityTypeCache.GetOrAdd(assembly, static asm => asm.GetTypes()
                .Where(type => type.IsClass
                    && !type.IsAbstract
                    && !type.IsGenericTypeDefinition
                    && typeof(DomainBase).IsAssignableFrom(type)
                    && !typeof(AggregateBase).IsAssignableFrom(type))
                .ToList()))
            .Distinct()
            .ToList();
    }

    /// <summary>为单个聚合根实体类型注册软删除与租户隔离全局过滤器（供反射按具体类型调用）。</summary>
    /// <typeparam name="TEntity">具体聚合根实体类型。</typeparam>
    /// <param name="client">SqlSugar 客户端（每异步上下文实例）。</param>
    /// <param name="dataFilter">过滤开关。</param>
    /// <param name="tenantContext">租户上下文。</param>
    /// <param name="currentUser">当前用户上下文（平台用户可见 TenantId IS NULL 的平台级行）。</param>
    private static void AddAggregateFilter<TEntity>(
        ISqlSugarClient client, IDataFilter dataFilter, ITenantContext tenantContext, IUserContext currentUser)
        where TEntity : AggregateBase
    {
        client.QueryFilter.AddTableFilter<TEntity>(it =>
            (dataFilter.SoftDeleteEnabled == false || it.DeletedAt == null)
            && (dataFilter.TenantFilterEnabled == false
                || tenantContext.TenantId == null
                || it.TenantId == tenantContext.TenantId
                || (currentUser.TenantId == null && it.TenantId == null)));
    }

    /// <summary>为单个非聚合实体类型注册租户隔离全局过滤器（无软删除字段，供反射按具体类型调用）。</summary>
    /// <typeparam name="TEntity">具体非聚合实体类型。</typeparam>
    /// <param name="client">SqlSugar 客户端（每异步上下文实例）。</param>
    /// <param name="dataFilter">过滤开关。</param>
    /// <param name="tenantContext">租户上下文。</param>
    /// <param name="currentUser">当前用户上下文（平台用户可见 TenantId IS NULL 的平台级行）。</param>
    private static void AddTenantFilter<TEntity>(
        ISqlSugarClient client, IDataFilter dataFilter, ITenantContext tenantContext, IUserContext currentUser)
        where TEntity : DomainBase
    {
        client.QueryFilter.AddTableFilter<TEntity>(it =>
            dataFilter.TenantFilterEnabled == false
            || tenantContext.TenantId == null
            || it.TenantId == tenantContext.TenantId
            || (currentUser.TenantId == null && it.TenantId == null));
    }
}
