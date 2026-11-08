using MySqlConnector;

namespace Infrastructure.DataAccess;

/// <summary>
/// 库级与表级结构探针（T-13）：全部判定走 <c>information_schema</c>，使用剥离库名的管理连接。
/// <para>库不存在时，应用连接串本身无法打开（MySQL 报 Unknown database），因此库级判定必须先于
/// 任何业务连接；管理连接只连到实例，不指定默认库。</para>
/// </summary>
public static class DatabaseSchemaProbe
{
    /// <summary>从业务连接串拆出「管理连接串」与目标库名。</summary>
    /// <param name="connectionString">业务连接串（必须显式声明 database=）。</param>
    /// <returns>管理连接串与目标库名。</returns>
    /// <exception cref="InvalidOperationException">连接串未声明库名时抛出。</exception>
    public static (string ManagementConnectionString, string DatabaseName) Split(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException(
                "Database:ConnectionString 必须显式声明 database=...，库级判定需要确定的库名");
        }

        var databaseName = builder.Database;
        // 真正移除 database 键（赋值空串会残留 "Database=;"），避免驱动把空库名当作默认库
        builder.Remove("Database");
        return (builder.ConnectionString, databaseName);
    }

    /// <summary>用管理连接查 information_schema.SCHEMATA 判定库是否存在。</summary>
    /// <param name="managementConnectionString">剥离库名的管理连接串。</param>
    /// <param name="databaseName">目标库名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在返回 true。</returns>
    /// <exception cref="InvalidOperationException">连接实例失败时抛出明确异常（不伪装成「库不存在」）。</exception>
    public static async Task<bool> DatabaseExistsAsync(
        string managementConnectionString,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new MySqlConnection(managementConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @schema";
            command.Parameters.AddWithValue("@schema", databaseName);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        }
        catch (MySqlException exception)
        {
            throw new InvalidOperationException(
                $"数据库实例不可达，无法完成库级判定（目标库 {databaseName}）：{exception.Message}", exception);
        }
    }

    /// <summary>用管理连接创建库（仅在配置允许且环境许可时调用）。</summary>
    /// <param name="managementConnectionString">剥离库名的管理连接串。</param>
    /// <param name="databaseName">目标库名（由连接串解析，非外部输入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public static async Task CreateDatabaseAsync(
        string managementConnectionString,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(managementConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 库名来自连接串解析并经反引号转义，不接受请求侧输入
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName.Replace("`", "``")}` "
            + "CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>在业务连接上按 information_schema.TABLES 返回缺失的模型表。</summary>
    /// <param name="client">SqlSugar 客户端。</param>
    /// <param name="tableNames">模型表名清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缺失表名（按名称排序）。</returns>
    public static async Task<IReadOnlyList<string>> FindMissingTablesAsync(
        SqlSugar.ISqlSugarClient client,
        IReadOnlyList<string> tableNames,
        CancellationToken cancellationToken = default)
    {
        if (tableNames.Count == 0)
        {
            return Array.Empty<string>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = await client.Ado.SqlQueryAsync<TableNameRow>(
            "SELECT TABLE_NAME AS TableName FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()");
        foreach (var row in rows)
        {
            existing.Add(row.TableName);
        }

        return tableNames
            .Where(name => !existing.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>information_schema.TABLES 行映射。</summary>
    private sealed class TableNameRow
    {
        /// <summary>表名。</summary>
        public string TableName { get; set; } = string.Empty;
    }
}
