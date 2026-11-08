using SqlSugar;

namespace Infrastructure.DataAccess.Migrations;

/// <summary>
/// v001：岗位管理模块建表（T-13）——以幂等 SQL 脚本声明结构变更，脚本正文即审计指纹来源。
/// <para><c>CREATE TABLE IF NOT EXISTS</c> 保证重跑不产生半套结构；列名与 SqlSugar CodeFirst
/// 对实体 <c>SysPost</c> / <c>SysUserPost</c> 的生成口径一致（属性名列名）。</para>
/// </summary>
public sealed class AddPostTablesMigration : ISchemaMigration
{
    /// <inheritdoc />
    public string MigrationId => "v001";

    /// <inheritdoc />
    /// <remarks>说明文字保持不变：存量库记录的旧口径指纹（ID + 说明）需据此识别并一次性升级为正文指纹。</remarks>
    public string Description => "岗位管理模块建表：sys_post（岗位）与 sys_user_post（用户-岗位关联），结构由 CodeFirst 同步";

    /// <inheritdoc />
    public IReadOnlyList<string> CreatedTableNames { get; } = ["sys_post", "sys_user_post"];

    /// <inheritdoc />
    public IReadOnlyList<string> Scripts { get; } =
    [
        """
        CREATE TABLE IF NOT EXISTS sys_post (
          Id VARCHAR(36) NOT NULL PRIMARY KEY,
          Version VARCHAR(36) NOT NULL,
          UpdatedAt DATETIME NULL,
          UpdatedBy VARCHAR(36) NULL,
          Description VARCHAR(500) NULL,
          IsDeleted TINYINT(1) NOT NULL DEFAULT 0,
          TenantId VARCHAR(36) NULL,
          CreatedAt DATETIME NOT NULL,
          CreatedBy VARCHAR(36) NOT NULL,
          PostName VARCHAR(50) NULL,
          PostCode VARCHAR(64) NULL,
          OrderNum INT NOT NULL DEFAULT 0,
          Status INT NOT NULL DEFAULT 1
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,
        """
        CREATE TABLE IF NOT EXISTS sys_user_post (
          UserId VARCHAR(36) NOT NULL,
          PostId VARCHAR(36) NOT NULL,
          TenantId VARCHAR(36) NULL,
          CreatedAt DATETIME NOT NULL,
          CreatedBy VARCHAR(36) NOT NULL,
          PRIMARY KEY (UserId, PostId)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """
    ];

    /// <inheritdoc />
    /// <remarks>结构变更全部由 <see cref="Scripts"/> 在事务内执行；此处不再调用 CodeFirst.InitTables（生产禁止应用自动改表）。</remarks>
    public Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
