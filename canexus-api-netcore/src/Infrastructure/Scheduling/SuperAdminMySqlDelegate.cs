using Quartz.Impl.AdoJobStore;

namespace Infrastructure.Scheduling;

/// <summary>
/// Quartz MySQL 布尔列适配 delegate：Quartz 3.22.9 中 <c>BooleanToString</c> 已从基类移除
/// （写入路径由基类 / 驱动处理），但读取路径仍可能收到驱动返回的字符串 "1"/"0"，
/// 基类 <see cref="StdAdoDelegate.GetBooleanFromDbValue"/> 对该形态的转换有限，故仅覆写读取侧做对称适配。
/// <para>注意：当前调度器配置（DependencyExtensions）使用 StdAdoDelegate（"Y"/"N" 语义，MySQL 兼容），
/// 本类为备用适配器，未被 DI / 配置引用，保留供 MySQLDelegate 方案切换时使用。</para>
/// </summary>
public sealed class SuperAdminMySqlDelegate : StdAdoDelegate
{
    /// <inheritdoc />
    public override bool GetBooleanFromDbValue(object columnValue)
    {
        if (columnValue is bool booleanValue)
        {
            return booleanValue;
        }

        if (columnValue is string stringValue)
        {
            if (stringValue == "1" || stringValue == "Y")
            {
                return true;
            }

            if (stringValue == "0" || stringValue == "N")
            {
                return false;
            }
        }

        return base.GetBooleanFromDbValue(columnValue);
    }
}
