
using System.Reflection;
using Domain.Entities;
using SqlSugar;

namespace Infrastructure.Shared.Options;
/// <summary>
/// 数据访问配置项（绑定配置节 "Database"，由 AddDataAccess 读取或配置委托填充）。
/// </summary>
public sealed class DatabaseOption : OptionBase
{
    /// <summary>数据库连接串。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>数据库类型，默认 MySQL。</summary>
    public DbType DbType { get; set; } = DbType.MySql;

    /// <summary>是否允许用剥离库名的管理连接自动建库（库不存在时），默认 true；生产建议显式关闭。</summary>
    public bool AutoCreateDatabase { get; set; } = true;

    /// <summary>是否自动建库建表（表不存在时自动创建），默认 true；Production 基线要求显式配置为 false。</summary>
    public bool AutoCreateTable { get; set; } = true;

    /// <summary>是否自动同步表结构（按实体定义同步列、索引差异），默认 true；Production 基线要求显式配置为 false。</summary>
    public bool AutoSyncStructure { get; set; } = true;

    /// <summary>
    /// 是否允许在 Production 由应用执行「结构初始化」（首次建表 / 引擎修正），默认 false。
    /// <para>结构已就绪时无论此开关为何都不执行 DDL；仅结构未初始化的全新生产库，需运维显式授权一次。</para>
    /// </summary>
    public bool AllowSchemaInitialization { get; set; }

    /// <summary>是否启用数据库迁移机制（P1-A3：基线登记 + 增量迁移 + 启动校验），默认 true。</summary>
    public bool MigrationsEnabled { get; set; } = true;

    /// <summary>
    /// 是否在结构就绪校验阶段比对「CodeFirst 模型快照」与 information_schema 实际列/索引，默认 false。
    /// <para>T5 #13：快照由 <c>client.EntityMaintenance.GetEntityInfo</c> 生成（不手写列名清单），
    /// 比对列存在性与非空约束，并核对主键索引。默认关闭以免存量库的历史结构漂移阻断启动；
    /// 运维完成一次结构对齐后可显式开启，此后任何绕过迁移通道改表 / 篡改迁移脚本都会在启动期被拦截。</para>
    /// </summary>
    public bool ValidateModelStructure { get; set; }

    /// <summary>是否自动释放连接（每次操作后归还连接池），默认 true。</summary>
    public bool IsAutoCloseConnection { get; set; } = true;

    /// <summary>SQL 命令执行超时时间（秒），默认 30。</summary>
    public int CommandTimeOut { get; set; } = 30;

    /// <summary>是否启用 SQL 执行日志（输出到控制台，调试用），默认 false。</summary>
    public bool SqlLogEnabled { get; set; }

    /// <summary>
    /// 慢查询日志阈值（毫秒），执行耗时超过该值的 SQL 以 Warning 记录（含耗时与 TraceId，生产排查慢 SQL 用），默认 500。
    /// </summary>
    public int SlowQueryLogMilliseconds { get; set; } = 500;

    /// <summary>参与 CodeFirst 建表的实体所在程序集，默认为 Domain 程序集；测试等场景可追加其他程序集。</summary>
    public List<Assembly> EntityAssemblies { get; } = [typeof(AggregateBase).Assembly];

    /// <inheritdoc />
    public override void Validate()
    {
        EnsureNotNullOrWhiteSpace(ConnectionString, nameof(ConnectionString), "Database:ConnectionString 不能为空");
        EnsureValidEnum(DbType, nameof(DbType));
        EnsureInRange(CommandTimeOut, 1, 3_600, nameof(CommandTimeOut), "CommandTimeOut 必须在 1-3600 秒之间");
        EnsureInRange(SlowQueryLogMilliseconds, 0, 3_600_000, nameof(SlowQueryLogMilliseconds), "SlowQueryLogMilliseconds 必须在 0-3600000 毫秒之间（0 = 关闭慢查询日志）");
        EnsureNotEmpty(EntityAssemblies, nameof(EntityAssemblies), "参与 CodeFirst 建表的实体程序集不能为空");
    }
}
