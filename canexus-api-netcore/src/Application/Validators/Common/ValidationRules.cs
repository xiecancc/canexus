
using System.Text.RegularExpressions;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using FluentValidation;

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

    /// <summary>密码强度正则：6-20 位且同时包含字母与数字，不允许空白字符（与前端重置密码对话框口径一致）。</summary>
    private static readonly Regex PasswordPattern = new(@"^(?=.*[A-Za-z])(?=.*\d)\S{6,20}$", RegexOptions.Compiled);

    /// <summary>密码强度约束：6-20 位且同时包含字母与数字（与前端重置密码对话框口径一致）。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    /// <param name="rule">属性规则构建器。</param>
    /// <returns>规则选项。</returns>
    public static IRuleBuilderOptions<T, string?> PasswordRule<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(value => value is not null && PasswordPattern.IsMatch(value))
            .WithMessage("密码为 6-20 位，且需同时包含字母和数字");

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
