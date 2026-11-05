
using MySqlConnector;
using Quartz.Impl.AdoJobStore.Common;

namespace Infrastructure.Scheduling;

/// <summary>
/// Quartz MySQL 数据源 provider 注册（P1-A4）：Quartz 3.22 内置 "MySql" provider 的元数据（指向老版 MySql.Data）
/// 在本项目 MySqlConnector 环境下不可用（构造即抛 ArgumentException），
/// 这里自注册一个指向 MySqlConnector 的 provider（SuperAdminMySql），注册幂等。
/// </summary>
public static class QuartzMySqlProvider
{
    /// <summary>自注册 provider 名（AddScheduling 中 Quartz dataSource.provider 取值）。</summary>
    public const string ProviderName = "SuperAdminMySql";

    private static readonly object SyncRoot = new();

    private static bool _registered;

    /// <summary>幂等注册 MySqlConnector provider 元数据（多宿主并发启动安全）。</summary>
    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_registered)
            {
                return;
            }

            DbProvider.RegisterDbMetadata(ProviderName, new DbMetadata
            {
                AssemblyName = typeof(MySqlConnection).Assembly.GetName().Name ?? "MySqlConnector",
                ProductName = "MySqlConnector",
                ConnectionType = typeof(MySqlConnection),
                CommandType = typeof(MySqlCommand),
                ParameterType = typeof(MySqlParameter),
                ExceptionType = typeof(MySqlException),
                ParameterNamePrefix = "@",
                BindByName = true,
                ParameterDbType = typeof(MySqlDbType),
                ParameterDbTypePropertyName = nameof(MySqlParameter.MySqlDbType),
                DbBinaryTypeName = "LongBlob",
                ParameterDbTypeProperty = typeof(MySqlParameter).GetProperty(nameof(MySqlParameter.MySqlDbType))!,
                ParameterIsNullableProperty = typeof(MySqlParameter).GetProperty(nameof(MySqlParameter.IsNullable))!
            });

            _registered = true;
        }
    }
}
