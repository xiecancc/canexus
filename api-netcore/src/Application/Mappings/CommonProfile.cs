
using AutoMapper;
using Domain.Shared.Paging;

namespace Application.Mappings;
/// <summary>
/// 分页结果开放泛型映射：PagedResult&lt;TSource&gt; → PagedResult&lt;TDestination&gt;。
/// <para>全局注册一次即可支持任意「实体 → DTO」组合的直接映射
/// （<c>mapper.Map&lt;PagedResult&lt;TDto&gt;&gt;(pagedEntities)</c>）：
/// Total / PageIndex / PageSize 按同名属性自动复制；
/// Items 列表元素在运行期按已注册的元素映射解析（各模块 Profile 中的 CreateMap&lt;实体, VO&gt;），
/// 缺少对应元素映射时抛 AutoMapperMappingException。</para>
/// </summary>
public sealed class CommonProfile : Profile
{
    /// <summary>构造函数。</summary>
    public CommonProfile()
    {
        // 表达式树内不可使用 ?. 运算符，改用条件表达式
        ValueTransformers.Add<string>(value => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim());

        // 开放泛型映射：闭合 PagedResult<实体> → PagedResult<DTO> 时按已注册的元素映射解析 Items
        CreateMap(typeof(PagedResult<>), typeof(PagedResult<>));
    }
}

/// <summary>分页结果映射扩展：PagedResult&lt;实体&gt; → PagedResult&lt;DTO&gt; 的可复用入口。</summary>
public static class PagedResultMappingExtensions
{
    /// <summary>
    /// 将实体分页结果映射为 DTO 分页结果：分页元数据（总条数 / 页码 / 每页大小）直接复制，
    /// 元素列表逐项经 <paramref name="mapper"/> 按已注册的元素映射转换。
    /// </summary>
    /// <typeparam name="TSource">实体类型。</typeparam>
    /// <typeparam name="TDestination">DTO 类型（需已有 CreateMap&lt;TSource, TDestination&gt;）。</typeparam>
    /// <param name="source">实体分页结果。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    /// <returns>DTO 分页结果（元数据与源一致）。</returns>
    public static PagedResult<TDestination> MapToPagedResult<TSource, TDestination>(
        this PagedResult<TSource> source, IMapper mapper) =>
        PagedResult<TDestination>.Create(
            mapper.Map<List<TDestination>>(source.Items),
            source.Total,
            source.PageIndex,
            source.PageSize);
}
