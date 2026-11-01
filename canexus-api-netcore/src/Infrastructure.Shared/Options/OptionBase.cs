/*
 * 文件名称: OptionBase.cs
 * 功能描述: 配置选项基类，定义配置选项的公共属性和方法
 * 作者信息: 谢灿软件 <492384481@qq.com>
 * 最近修订: 2026-04-16
 */

namespace Infrastructure.Shared.Options;

/// <summary>
/// 配置选项基类
/// <para>定义配置选项的公共属性和方法，所有配置选项应继承此类</para>
/// </summary>
public abstract class OptionBase
{
    /// <summary>
    /// 校验配置选项
    /// <para>对配置属性进行数据校验，确保属性值符合预设的业务规则和数据规范</para>
    /// </summary>
    /// <exception cref="ArgumentException">校验失败时抛出异常</exception>
    public abstract void Validate();

    /// <summary>
    /// 验证字符串不为空或空白
    /// </summary>
    /// <param name="value">要验证的字符串值</param>
    /// <param name="parameterName">参数名称</param>
    /// <param name="message">错误消息</param>
    /// <exception cref="ArgumentException">验证失败时抛出异常</exception>
    protected static void EnsureNotNullOrWhiteSpace(string? value, string parameterName, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message ?? $"{parameterName} 不能为空或仅包含空白字符", parameterName);
        }
    }

    /// <summary>
    /// 验证字符串不为空
    /// </summary>
    /// <param name="value">要验证的字符串值</param>
    /// <param name="parameterName">参数名称</param>
    /// <param name="message">错误消息</param>
    /// <exception cref="ArgumentException">验证失败时抛出异常</exception>
    protected static void EnsureNotNull(string? value, string parameterName, string? message = null)
    {
        if (value is null)
        {
            throw new ArgumentException(message ?? $"{parameterName} 不能为 null", parameterName);
        }
    }

    /// <summary>
    /// 验证字符串长度
    /// </summary>
    /// <param name="value">要验证的字符串值</param>
    /// <param name="minLength">最小长度</param>
    /// <param name="parameterName">参数名称</param>
    /// <param name="message">错误消息</param>
    /// <exception cref="ArgumentException">验证失败时抛出异常</exception>
    protected static void EnsureMinLength(string value, int minLength, string parameterName, string? message = null)
    {
        if (value.Length < minLength)
        {
            throw new ArgumentException(message ?? $"{parameterName} 长度至少为 {minLength} 个字符", parameterName);
        }
    }

    /// <summary>
    /// 验证数值范围
    /// </summary>
    /// <param name="value">要验证的数值</param>
    /// <param name="minValue">最小值</param>
    /// <param name="maxValue">最大值</param>
    /// <param name="parameterName">参数名称</param>
    /// <param name="message">错误消息</param>
    /// <exception cref="ArgumentOutOfRangeException">验证失败时抛出异常</exception>
    protected static void EnsureInRange(int value, int minValue, int maxValue, string parameterName, string? message = null)
    {
        if (value < minValue || value > maxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, message ?? $"{parameterName} 必须在 {minValue}-{maxValue} 之间");
        }
    }

    /// <summary>
    /// 验证数值大于 0
    /// </summary>
    /// <param name="value">要验证的数值</param>
    /// <param name="parameterName">参数名称</param>
    /// <param name="message">错误消息</param>
    /// <exception cref="ArgumentException">验证失败时抛出异常</exception>
    protected static void EnsureGreaterThanZero(int value, string parameterName, string? message = null)
    {
        if (value <= 0)
        {
            throw new ArgumentException(message ?? $"{parameterName} 必须大于 0", parameterName);
        }
    }

    /// <summary>
    /// 验证枚举值有效
    /// </summary>
    /// <typeparam name="T">枚举类型</typeparam>
    /// <param name="value">要验证的枚举值</param>
    /// <param name="parameterName">参数名称</param>
    /// <param name="message">错误消息</param>
    /// <exception cref="ArgumentException">验证失败时抛出异常</exception>
    protected static void EnsureValidEnum<T>(T value, string parameterName, string? message = null) where T : Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
        {
            throw new ArgumentException(message ?? $"不支持/格式错误的 {typeof(T).Name} 值：{value}", parameterName);
        }
    }

    /// <summary>
    /// 验证集合不为空
    /// </summary>
    /// <typeparam name="T">集合元素类型</typeparam>
    /// <param name="collection">要验证的集合</param>
    /// <param name="parameterName">参数名称</param>
    /// <param name="message">错误消息</param>
    /// <exception cref="ArgumentException">验证失败时抛出异常</exception>
    protected static void EnsureNotEmpty<T>(ICollection<T>? collection, string parameterName, string? message = null)
    {
        if (collection is null || collection.Count == 0)
        {
            throw new ArgumentException(message ?? $"{parameterName} 不能为空集合", parameterName);
        }
    }

    /// <summary>
    /// 验证布尔条件
    /// </summary>
    /// <param name="condition">条件</param>
    /// <param name="parameterName">参数名称</param>
    /// <param name="message">错误消息</param>
    /// <exception cref="ArgumentException">验证失败时抛出异常</exception>
    protected static void Ensure(bool condition, string parameterName, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message, parameterName);
        }
    }
}
