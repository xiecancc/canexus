namespace Infrastructure.Shared.Options;

/// <summary>
/// 口令强度策略（appsettings.json 的 "Security:PasswordPolicy" 节）：把原先硬编码在
/// <c>ValidationRules.PasswordRule</c> 正则里的 6-20 位「字母 + 数字」规则改为部署方可配置。
/// <para>默认值与历史口径完全一致（6-20 位、必须含字母与数字、不允许空白字符），
/// 因此不配置该节时行为不变；配置后自助改密与管理员重置密码共用同一策略。</para>
/// </summary>
public sealed class PasswordPolicyOption : OptionBase
{
    /// <summary>最小长度（默认 6）。</summary>
    public int MinLength { get; set; } = 6;

    /// <summary>最大长度（默认 20）。</summary>
    public int MaxLength { get; set; } = 20;

    /// <summary>是否必须包含字母（默认 true）。</summary>
    public bool RequireLetter { get; set; } = true;

    /// <summary>是否必须包含数字（默认 true）。</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>是否必须包含大写字母（默认 false）。</summary>
    public bool RequireUppercase { get; set; }

    /// <summary>是否必须包含小写字母（默认 false）。</summary>
    public bool RequireLowercase { get; set; }

    /// <summary>是否必须包含字母/数字之外的字符（默认 false）。</summary>
    public bool RequireSpecialCharacter { get; set; }

    /// <inheritdoc />
    public override void Validate()
    {
        EnsureInRange(MinLength, 1, 128, nameof(MinLength), "PasswordPolicy:MinLength 必须在 1-128 之间");
        EnsureInRange(MaxLength, 1, 128, nameof(MaxLength), "PasswordPolicy:MaxLength 必须在 1-128 之间");
        Ensure(MinLength <= MaxLength, nameof(MaxLength),
            "PasswordPolicy:MaxLength 不得小于 MinLength");
    }
}
