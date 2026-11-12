
using System.Collections.Concurrent;
using System.Reflection;
using Domain.Entities;
using Domain.Services.Users;
using Domain.Shared.Logging;
using Infrastructure.Shared.Filtering;
using Infrastructure.Shared.Options;
using Infrastructure.Shared.Tenants;
using Microsoft.AspNetCore.Http;
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
        // 可选解析：数据访问可独立装配（如集成测试），未注册 HttpContextAccessor 时平台态审计仅少一项路径信息
        var httpContextAccessor = serviceProvider.GetService<IHttpContextAccessor>();

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
            AuditPlatformWideTenantBypass(logger, dataFilter, tenantContext, currentUser, httpContextAccessor);

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

    /// <summary>平台态短路后门的一次性 Warning 是否已发出（进程级，避免每个请求刷屏）。</summary>
    private static int PlatformWideAuditWarned;

    /// <summary>
    /// 平台态租户过滤短路的显式审计（P1-2）：仅当「租户过滤开关启用且请求租户上下文为 null」时记录，
    /// 这正是 <c>AddAggregateFilter</c> / <c>AddTenantFilter</c> 中
    /// <c>tenantContext.TenantId == null</c> 令租户条件整体短路为 true、平台上下文不过滤任何行的设计内后门。
    /// <para><b>注入点</b>：全局过滤器是表达式树（由 SqlSugar 在每次查询时求值），无法在 lambda 内写日志或注入服务；
    /// 故在 SqlSugarScope 每异步上下文注册过滤器处（Create 的配置委托，即该上下文首次建连 / 首次查询时）
    /// 按当时的租户态判断一次，不在表达式树内注入任何副作用，过滤器语义与性能均不变。
    /// 过滤器短路本身按每次查询时的 tenantContext 求值，本审计取首次建连时的值作为该上下文的代表；
    /// 若中间件在首次建连后才收敛租户，本条留痕仅对应首次查询，属表达式树约束下的可接受近似。</para>
    /// <para><b>级别与降噪</b>：该配置委托每个异步上下文仅执行一次，天然不随查询次数放大；
    /// 但平台态（未携带 X-Tenant-Id）是平台管理员、匿名登录、后台作业等合法且高频路径，
    /// 若每个上下文都发 Warning 会在正常运维下持续刷屏并制造误报警。故：
    /// ① 进程内仅发一次 LogWarning，显式宣告该后门已启用；
    /// ② 每个平台态上下文发一条 LogInformation（P2-F3：由 Debug 升级，默认级别下可见），
    /// 携带操作人 / TraceId / 路径与明确行为说明，使平台态跨租户数据访问无需调整日志级别即可审计；
    /// 结构化参数延迟求值、热路径零开销。
    /// 过滤开关被显式关闭（TenantFilterEnabled=false）属于另一条路径，不在此记录。</para>
    /// </summary>
    /// <param name="logger">日志器（category 为 SqlSugar）。</param>
    /// <param name="dataFilter">数据过滤开关。</param>
    /// <param name="tenantContext">请求租户上下文。</param>
    /// <param name="currentUser">当前用户上下文（操作人取值来源）。</param>
    /// <param name="httpContextAccessor">HTTP 上下文访问器（请求路径，可选）。</param>
    private static void AuditPlatformWideTenantBypass(
        ILogger logger,
        IDataFilter dataFilter,
        ITenantContext tenantContext,
        IUserContext currentUser,
        IHttpContextAccessor? httpContextAccessor)
    {
        if (!dataFilter.TenantFilterEnabled || tenantContext.TenantId is not null)
        {
            return;
        }

        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "-";
        var path = httpContextAccessor?.HttpContext?.Request.Path.Value ?? "-";

        if (Interlocked.Exchange(ref PlatformWideAuditWarned, 1) == 0)
        {
            logger.LogWarning(
                "[多租户审计] 平台态租户过滤整体短路后门启用：TenantId 上下文为 null 时不过滤任何租户行（设计内后门）。"
                + "此后逐次留痕以 Information 级别记录。"
                + "OperatorUserId={OperatorUserId} TraceId={TraceId} Path={Path}",
                currentUser.UserId, traceId, path);
        }

        logger.LogInformation(
            "[多租户审计] 平台态租户过滤整体短路：TenantId 上下文为 null，本次上下文所有租户数据均可见（设计内平台后门）。"
            + "OperatorUserId={OperatorUserId} TraceId={TraceId} Path={Path}",
            currentUser.UserId, traceId, path);
    }
}
