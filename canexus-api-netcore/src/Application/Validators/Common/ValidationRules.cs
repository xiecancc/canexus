
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using FluentValidation;
using Infrastructure.Shared.Options;

namespace Application.Validators.Common;
/// <summary>
/// 通用验证规则扩展（Must 风格，失败聚合为 CommonValidationFailed 40001）：
/// 供各模块验证器复用，消除逐模块重复定义；需要模块错误码的规则在模块内以 Custom 规则抛 BusinessException。
/// </summary>
public static class ValidationRules
{
    /// <summary>非空约束（去首尾空格后判断）。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    /// <param name="rule">属性规则构建器。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>规则选项。</returns>
    public static IRuleBuilderOptions<T, string?> NotBlankRule<T>(this IRuleBuilder<T, string?> rule, string message)
        => rule.Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(message);

    /// <summary>trim 后最大长度约束。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    /// <param name="rule">属性规则构建器。</param>
    /// <param name="maxLength">最大长度（含）。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>规则选项。</returns>
    public static IRuleBuilderOptions<T, string?> TrimmedMaxLengthRule<T>(this IRuleBuilder<T, string?> rule, int maxLength, string message)
        => rule.Must(value => (value?.Trim() ?? string.Empty).Length <= maxLength).WithMessage(message);

    /// <summary>状态取值约束（启用 / 停用，含未定义枚举值拦截）。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    /// <param name="rule">属性规则构建器。</param>
    /// <param name="message">失败消息（缺省为通用启停文案）。</param>
    /// <returns>规则选项。</returns>
    public static IRuleBuilderOptions<T, CommonStatus> StatusRule<T>(this IRuleBuilder<T, CommonStatus> rule, string message = "状态取值不合法：仅支持 1 启用 / 0 停用")
        => rule.Must(status => status is CommonStatus.Enabled or CommonStatus.Disabled).WithMessage(message);

    /// <summary>枚举定义域约束（拦截越界数值强转出的未定义枚举值）。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    /// <typeparam name="TEnum">枚举类型。</typeparam>
    /// <param name="rule">属性规则构建器。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>规则选项。</returns>
    public static IRuleBuilderOptions<T, TEnum> DefinedRule<T, TEnum>(this IRuleBuilder<T, TEnum> rule, string message)
        where TEnum : struct, Enum
        => rule.Must(value => Enum.IsDefined(value)).WithMessage(message);

    /// <summary>按部署方配置的口令策略校验强度（默认口径与历史规则一致：6-20 位、含字母与数字、不含空白）。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    /// <param name="rule">属性规则构建器。</param>
    /// <param name="policy">口令策略（由 IOptionsMonitor 注入，支持热加载）。</param>
    /// <returns>规则选项。</returns>
    public static IRuleBuilderOptions<T, string?> PasswordRule<T>(this IRuleBuilder<T, string?> rule, PasswordPolicyOption policy)
        => rule.Must(value => IsPasswordValid(value, policy)).WithMessage(BuildPasswordMessage(policy));

    /// <summary>按策略判定口令是否合规（长度、禁止空白、各类字符要求）。</summary>
    /// <param name="value">待校验口令。</param>
    /// <param name="policy">口令策略。</param>
    /// <returns>合规返回 true。</returns>
    private static bool IsPasswordValid(string? value, PasswordPolicyOption policy)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length < policy.MinLength
            || value.Length > policy.MaxLength
            || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return (!policy.RequireLetter || value.Any(char.IsLetter))
            && (!policy.RequireDigit || value.Any(char.IsDigit))
            && (!policy.RequireUppercase || value.Any(char.IsUpper))
            && (!policy.RequireLowercase || value.Any(char.IsLower))
            && (!policy.RequireSpecialCharacter || value.Any(ch => !char.IsLetterOrDigit(ch)));
    }

    /// <summary>按策略生成可读的校验失败提示（默认策略输出与历史文案完全一致）。</summary>
    /// <param name="policy">口令策略。</param>
    /// <returns>失败消息。</returns>
    private static string BuildPasswordMessage(PasswordPolicyOption policy)
    {
        List<string> requirements = [];
        if (policy.RequireLetter)
        {
            requirements.Add("字母");
        }

        if (policy.RequireDigit)
        {
            requirements.Add("数字");
        }

        if (policy.RequireUppercase)
        {
            requirements.Add("大写字母");
        }

        if (policy.RequireLowercase)
        {
            requirements.Add("小写字母");
        }

        if (policy.RequireSpecialCharacter)
        {
            requirements.Add("特殊字符");
        }

        var suffix = requirements.Count == 0
            ? string.Empty
            : $"，且需同时包含{(requirements.Count == 2 ? $"{requirements[0]}和{requirements[1]}" : string.Join("、", requirements))}";
        return $"密码为 {policy.MinLength}-{policy.MaxLength} 位{suffix}";
    }

    /// <summary>参数键名格式约束（非空白且去空白后不超过 100 字符，与实体列宽一致）。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    /// <param name="rule">属性规则构建器。</param>
    /// <returns>规则选项。</returns>
    public static IRuleBuilderOptions<T, string?> ConfigKeyRule<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(key => !string.IsNullOrWhiteSpace(key) && key.Trim().Length <= 100)
            .WithMessage("参数键名不合法：不能为空且不超过 100 个字符");

    /// <summary>单个 Guid 值约束：拒绝空值 <see cref="Guid.Empty"/>（值类型不存在 null；用于乐观锁版本号等
    /// 不允许缺省的令牌字段）。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    /// <param name="rule">属性规则构建器。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>规则选项。</returns>
    public static IRuleBuilderOptions<T, Guid> NoEmptyRule<T>(this IRuleBuilder<T, Guid> rule, string message)
        => rule.NotEmpty().WithMessage(message);

    /// <summary>Guid 集合约束：集合允许为空（清空全部关联），但不允许包含空 <see cref="Guid.Empty"/> 元素
    /// （关联存在性由处理器校验）。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    /// <param name="rule">属性规则构建器。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>规则选项。</returns>
    public static IRuleBuilderOptions<T, List<Guid>> NoEmptyRule<T>(this IRuleBuilder<T, List<Guid>> rule, string message)
        => rule.Must(ids => ids.All(id => id != Guid.Empty)).WithMessage(message);
}
