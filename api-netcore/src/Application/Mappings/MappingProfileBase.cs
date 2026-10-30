namespace Application.Mappings;

using AutoMapper;

/// <summary>
/// 映射配置基类：统一注册跨模块的映射约定。
/// <para>字符串值转换器：DTO → 实体 / 实体 → VO 的全部字符串属性自动去首尾空格
/// （实体 setter 内有同样的归一化，双保险）；null 值原样透传不做处理。</para>
/// </summary>
public abstract class MappingProfileBase : Profile
{
    /// <summary>构造函数。</summary>
    protected MappingProfileBase()
    {
        // 表达式树内不可使用 ?. 运算符，改用条件表达式
        ValueTransformers.Add<string>(value => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim());
    }
}
