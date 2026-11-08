namespace Domain.Shared.Constants;
/// <summary>内置用户保留值：种子数据与业务校验共用的账号约定，全库唯一来源。</summary>
public static class UserConstants
{
    /// <summary>内置管理员账号用户名（保留值：禁止删除，禁止修改用户名）。</summary>
    public const string AdminUserName = "admin";

    /// <summary>
    /// 用户初始密码的种子缺省值：仅用于首次写入「参数配置」sys.user.initPassword，
    /// 创建 / 导入用户一律读取该配置项而非本常量；满足密码强度规则（6-20 位含字母与数字）。
    /// </summary>
    public const string DefaultPassword = "Aa123456";

    /// <summary>
    /// 历史版本内置超管的初始口令：仅用于识别「从未修改过口令」的存量 admin 并触发一次性轮换，
    /// 严禁用于创建账号、配置回退或任何新口令赋值。
    /// </summary>
    public const string LegacySeedPassword = "admin123";
}
