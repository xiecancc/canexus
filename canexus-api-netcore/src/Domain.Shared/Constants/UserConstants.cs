namespace Domain.Shared.Constants;
/// <summary>内置用户保留值：种子数据与业务校验共用的账号约定，全库唯一来源。</summary>
public static class UserConstants
{
    /// <summary>内置管理员账号用户名（保留值：禁止删除，禁止修改用户名）。</summary>
    public const string AdminUserName = "admin";

    /// <summary>创建用户时的缺省密码（与种子参数 sys.user.initPassword 的缺省值一致）。</summary>
    public const string DefaultPassword = "123456";
}
