namespace Infrastructure.DataAccess;

using System.Collections.Concurrent;
using System.Reflection;
using Domain.Entities;
using Domain.Services.Users;
using Infrastructure.Shared.Filtering;
using Infrastructure.Shared.Options;
using Infrastructure.Shared.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;

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

        // 单例服务被闭包捕获是安全的：TenantContext 基于 AsyncLocal、其余为无状态开关
        var tenantContext = serviceProvider.GetRequiredService<ITenantContext>();
        var dataFilter = serviceProvider.GetRequiredService<IDataFilter>();
        var currentUser = serviceProvider.GetRequiredService<UserContext>();

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
                    if (property.PropertyType == typeof(string) || Nullable.GetUnderlyingType(property.PropertyType) != null)
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
            ConfigureGlobalQueryFilters(client, options, dataFilter, tenantContext);

            // SQL 执行日志：输出执行的 SQL 与参数（调试用）；开关读取监视器最新值，运行期改配置即可生效
            if (monitor.CurrentValue.SqlLogEnabled)
            {
                client.Aop.OnLogExecuting = (sql, parameters) => Console.WriteLine(
                    "[SqlSugar] {0}{1}",
                    sql,
                    parameters.Length == 0
                        ? string.Empty
                        : "\n    参数: " + string.Join(", ", parameters.Select(p => $"{p.ParameterName}={p.Value}")));
            }
        });
    }

    /// <summary>配置 SqlSugar 写入审计 Aop：创建 / 更新审计字段自动填充、空主键生成与软删除标记。</summary>
    /// <param name="client">SqlSugar 客户端（每异步上下文实例）。</param>
    /// <param name="currentUser">当前用户上下文（审计人取值来源）。</param>
    /// <param name="tenantContext">租户上下文（租户归属填充来源）。</param>
    private static void ConfigureExecutingAop(
        ISqlSugarClient client,
        UserContext currentUser,
        ITenantContext tenantContext)
    {
        // 审计自动填充 + 主键/软删除初始化：直接修改实体实例属性，SqlSugar 构建 SQL 时会取到新值
        // 创建审计覆盖 DomainBase 全部实体（聚合根与关联表）；主键与软删除标记、更新审计仅聚合根具备
        client.Aop.DataExecuting = (_, filterModel) =>
        {
            switch (filterModel.OperationType)
            {
                case DataFilterType.InsertByObject when filterModel.EntityValue is DomainBase insertEntity:
                    insertEntity.CreatedAt = DateTime.Now;
                    insertEntity.CreatedBy = currentUser.UserName ?? "system";
                    // 租户归属自动填充：实体未显式指定时取当前租户上下文（null 表示平台级数据）
                    insertEntity.TenantId ??= tenantContext.TenantId;
                    if (insertEntity is AggregateBase insertAggregate)
                    {
                        // 空主键统一由框架生成 36 位 GUID，业务代码无需显式赋值；关联表为联合主键，无 Id 列
                        if (insertAggregate.Id == Guid.Empty)
                        {
                            insertAggregate.Id = Guid.NewGuid();
                        }

                        insertAggregate.IsDeleted = false;
                    }

                    break;

                case DataFilterType.UpdateByObject when filterModel.EntityValue is AggregateBase updateEntity:
                    updateEntity.UpdatedAt = DateTime.Now;
                    updateEntity.UpdatedBy = currentUser.UserName ?? "system";
                    break;
            }
        };
    }

    /// <summary>配置全局查询过滤器：按具体实体类型逐个反射注册租户隔离与软删除过滤（AND 组合）。</summary>
    /// <param name="client">SqlSugar 客户端（每异步上下文实例）。</param>
    /// <param name="options">数据访问配置（提供参与扫描的实体程序集）。</param>
    /// <param name="dataFilter">过滤开关。</param>
    /// <param name="tenantContext">租户上下文。</param>
    private static void ConfigureGlobalQueryFilters(
        ISqlSugarClient client,
        DatabaseOption options,
        IDataFilter dataFilter,
        ITenantContext tenantContext)
    {
        // 全局过滤器（按具体实体类型逐个注册，AND 组合）：
        // SqlSugar 的 AddTableFilter 对抽象基类注册不会向派生实体传播，故扫描实体程序集中
        // 全部具体实体类型，经反射逐类型注册（扫描结果静态缓存，避免重复反射）。
        // 1. 租户隔离（全部实体，TenantId 定义于 DomainBase）：平台用户（无租户）可见全部数据，
        //    租户用户仅可见本租户数据（租户为空的平台级数据对租户用户不可见）
        // 2. 软删除（仅聚合根，IsDeleted 定义于 AggregateBase；关联表为物理删除无墓碑）：
        //    开关关闭或未删除数据可见，表达式在每次查询时求值，支持运行时切换
        foreach (var entityType in GetAggregateEntityTypes(options.EntityAssemblies))
        {
            AddAggregateFilterMethod.MakeGenericMethod(entityType)
                .Invoke(null, [client, dataFilter, tenantContext]);
        }

        foreach (var entityType in GetPlainEntityTypes(options.EntityAssemblies))
        {
            AddTenantFilterMethod.MakeGenericMethod(entityType)
                .Invoke(null, [client, dataFilter, tenantContext]);
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
    private static void AddAggregateFilter<TEntity>(ISqlSugarClient client, IDataFilter dataFilter, ITenantContext tenantContext)
        where TEntity : AggregateBase
    {
        client.QueryFilter.AddTableFilter<TEntity>(it =>
            (dataFilter.SoftDeleteEnabled == false || it.IsDeleted == false)
            && (dataFilter.TenantFilterEnabled == false
                || tenantContext.TenantId == null
                || it.TenantId == tenantContext.TenantId));
    }

    /// <summary>为单个非聚合实体类型注册租户隔离全局过滤器（无软删除字段，供反射按具体类型调用）。</summary>
    /// <typeparam name="TEntity">具体非聚合实体类型。</typeparam>
    /// <param name="client">SqlSugar 客户端（每异步上下文实例）。</param>
    /// <param name="dataFilter">过滤开关。</param>
    /// <param name="tenantContext">租户上下文。</param>
    private static void AddTenantFilter<TEntity>(ISqlSugarClient client, IDataFilter dataFilter, ITenantContext tenantContext)
        where TEntity : DomainBase
    {
        client.QueryFilter.AddTableFilter<TEntity>(it =>
            dataFilter.TenantFilterEnabled == false
            || tenantContext.TenantId == null
            || it.TenantId == tenantContext.TenantId);
    }
}
