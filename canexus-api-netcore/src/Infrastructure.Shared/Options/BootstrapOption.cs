
using System.Text.RegularExpressions;

namespace Infrastructure.Shared.Options;

/// <summary>
/// 启动引导配置（配置节 "Bootstrap"）：内置超管账号的一次性初始口令与演示数据写入开关。
/// <para>口令不落代码、不落仓库配置：非 Development 环境必须由部署配置 / 环境变量显式提供，
/// 宿主启动期未提供即失败；非生产环境未提供时由种子初始化器随机生成，仅经控制台一次性披露，
/// 不写入 Serilog 文件日志。</para>
/// </summary>
public sealed partial class BootstrapOption : OptionBase
{
    /// <summary>内置超管账号（admin）的首次登录口令；留空时按环境处理（生产启动失败 / 非生产随机生成）。</summary>
    public string AdminInitialPassword { get; set; } = string.Empty;

    /// <summary>是否写入示例公告、默认部门树等演示数据；Production 环境恒定跳过（由种子初始化器结合宿主环境判定）。</summary>
    public bool SeedDemoData { get; set; } = true;

    /// <summary>
    /// 种子 admin 是否标记「首登须改密」：默认 true，Production 环境必须为 true（由启动期生产基线校验保证）；
    /// 仅隔离测试宿主可显式设为 false，以复用固定测试凭据跑通既有集成测试。
    /// </summary>
    public bool RequirePasswordChangeOnSeed { get; set; } = true;

    /// <inheritdoc />
    public override void Validate()
    {
        // 留空是合法配置：生产由宿主启动期强校验拒绝，非生产由种子初始化器生成一次性口令
        if (string.IsNullOrWhiteSpace(AdminInitialPassword))
        {
            return;
        }

        // 初始口令的长度口径统一取自 PasswordPolicyOption 的安全基线默认值（8-20 位），
        // 不再在两处硬编码 6 / 8 位下限导致漂移（P2-4 / E5）。此处走 Options 管道 ValidateOnStart，
        // 无法注入可热加载的策略实例，故以代码基线为准：即便部署方下调 Security:PasswordPolicy，
        // 内置超管初始口令也不得低于生产基线，避免口令强度被间接降低。
        // 一次性初始口令由 RequirePasswordChangeOnSeed（默认 true）在首登强制改为满足完整复杂度策略的口令，
        // 故此处只校验长度 + 字母数字 + 禁止空白；大写 / 小写 / 特殊字符等完整复杂度由改密时的
        // ValidationRules.PasswordRule 按部署方配置的 PasswordPolicyOption 强制执行。
        var baseline = new PasswordPolicyOption();
        EnsureInRange(AdminInitialPassword.Length, baseline.MinLength, baseline.MaxLength, nameof(AdminInitialPassword),
            $"Bootstrap:AdminInitialPassword 长度必须在 {baseline.MinLength}-{baseline.MaxLength} 位之间（与口令策略基线一致）");
        Ensure(!AdminInitialPassword.Any(char.IsWhiteSpace), nameof(AdminInitialPassword),
            "Bootstrap:AdminInitialPassword 不允许包含空白字符");
        Ensure(PasswordPattern().IsMatch(AdminInitialPassword), nameof(AdminInitialPassword),
            "Bootstrap:AdminInitialPassword 必须同时包含字母与数字");
    }

    /// <summary>口令强度模式：至少一个字母且至少一个数字。</summary>
    /// <returns>已编译的正则表达式。</returns>
    [GeneratedRegex(@"^(?=.*[A-Za-z])(?=.*\d).+$")]
    private static partial Regex PasswordPattern();
}
