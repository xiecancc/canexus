namespace Infrastructure.Shared.Options;

using System.Reflection;
using Domain.Entities;
using SqlSugar;

/// <summary>
/// 数据访问配置项（绑定配置节 "Database"，由 AddDataAccess 读取或配置委托填充）。
/// </summary>
public sealed class DatabaseOption : OptionBase
{
    /// <summary>数据库连接串。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>数据库类型，默认 MySQL。</summary>
    public DbType DbType { get; set; } = DbType.MySql;

    /// <summary>是否自动建库建表（数据库 / 表不存在时自动创建），默认 true。</summary>
    public bool AutoCreateTable { get; set; } = true;

    /// <summary>是否自动同步表结构（按实体定义同步列、索引差异），默认 true。</summary>
    public bool AutoSyncStructure { get; set; } = true;

    /// <summary>是否自动释放连接（每次操作后归还连接池），默认 true。</summary>
    public bool IsAutoCloseConnection { get; set; } = true;

    /// <summary>SQL 命令执行超时时间（秒），默认 30。</summary>
    public int CommandTimeOut { get; set; } = 30;

    /// <summary>是否启用 SQL 执行日志（输出到控制台，调试用），默认 false。</summary>
    public bool SqlLogEnabled { get; set; }

    /// <summary>参与 CodeFirst 建表的实体所在程序集，默认为 Domain 程序集；测试等场景可追加其他程序集。</summary>
    public List<Assembly> EntityAssemblies { get; } = [typeof(AggregateBase).Assembly];

    /// <inheritdoc />
    public override void Validate()
    {
        EnsureNotNullOrWhiteSpace(ConnectionString, nameof(ConnectionString), "Database:ConnectionString 不能为空");
        EnsureValidEnum(DbType, nameof(DbType));
        EnsureInRange(CommandTimeOut, 1, 3_600, nameof(CommandTimeOut), "CommandTimeOut 必须在 1-3600 秒之间");
        EnsureNotEmpty(EntityAssemblies, nameof(EntityAssemblies), "参与 CodeFirst 建表的实体程序集不能为空");
    }
}
