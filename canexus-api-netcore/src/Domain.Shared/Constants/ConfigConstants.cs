
namespace Domain.Shared.Constants;
/// <summary>
/// 系统参数键常量（sys_config 表 configKey）：种子初始化（SeedDataInitializer）与业务消费方（如登录验证码开关）共用，
/// 避免键名以魔法字符串散布（键名拼写漂移会导致读取静默失败，参数配置页的内置保护亦依赖精确键名）。
/// </summary>
public static class ConfigConstants
{
    /// <summary>用户初始密码参数键：新增用户时的缺省初始密码。</summary>
    public const string InitPasswordKey = "sys.user.initPassword";

    /// <summary>登录验证码开关参数键：是否开启登录验证码校验（true 开启 / false 关闭）。</summary>
    public const string CaptchaEnabledKey = "sys.account.captchaEnabled";
}
