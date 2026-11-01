
using Application.Contracts.Common;
using AutoMapper;
using Domain.Entities;
using Domain.Shared.Paging;

namespace Application.Mappings;
/// <summary>
/// 通用映射 Profile：承载全局共享的映射约定、「领域基类 → DTO 读模型基类」映射关系，
/// 以及分页结果的开放泛型映射。
/// <para>字符串值转换器：写入实体的字符串统一 trim，空白归一为空串
/// （表达式树内不可使用 ?. 运算符，改用条件表达式）。</para>
/// <para>分页结果开放泛型映射：全局注册一次即可支持任意「实体 → DTO」组合的分页映射
/// （<c>mapper.Map&lt;PagedResponse&lt;TDto&gt;&gt;(pagedEntities)</c>）：
/// Total / PageIndex / PageSize 按同名属性自动复制，
/// Items 列表元素在运行期按已注册的元素映射解析（各模块 Profile 中的 CreateMap），
/// 缺少对应元素映射时抛 AutoMapperMappingException。
/// 查询处理器的常规路径仍由 <c>PageQueryHandler</c> 基类统一承担
/// （逐项投影 + <c>PagedResponse.Create</c>）。</para>
/// </summary>
public sealed class CommonProfile : Profile
{
    /// <summary>构造函数。</summary>
    public CommonProfile()
    {
        ValueTransformers.Add<string>(value => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim());

        // 领域基类 → DTO 读模型基类映射：租户归属 / 创建审计 / 更新审计 / 乐观锁版本 / 备注
        // 均按同名属性自动映射（DTO 基类属性镜像 DomainBase / AggregateBase）；
        // 各模块「实体 → Response」的派生映射由模块 Profile 注册，
        // 并通过 IncludeBase 挂接基类映射链，同名约定即可补齐基类承载的审计字段。
        // —— 列表向 ——
        CreateMap<DomainBase, DomainListResponse>();
        CreateMap<AggregateBase, AggregateListResponse>()
            .IncludeBase<DomainBase, DomainListResponse>();
        // —— 分页向 ——
        CreateMap<DomainBase, DomainPagedResponse>();
        CreateMap<AggregateBase, AggregatePagedResponse>()
            .IncludeBase<DomainBase, DomainPagedResponse>();

        // DTO 写模型基类 → 领域基类映射（模板链）：为各模块「请求 → 实体」的
        // IncludeBase 派生映射提供基类承载，审计字段（CreatedAt / CreatedBy 等）
        // 由仓储 / 应用层在持久化时统一填充，此处仅承担同名业务属性的对齐。
        // —— 领域层 ——
        CreateMap<DomainChangeRequest, DomainBase>();
        // —— 聚合根层 ——
        CreateMap<AggregateChangeRequest, AggregateBase>()
            .IncludeBase<DomainChangeRequest, DomainBase>();

        // 开放泛型映射：闭合 PagedResponse<实体> → PagedResponse<DTO> 时按已注册的元素映射解析 Items
        CreateMap(typeof(PagedResponse<>), typeof(PagedResponse<>));
    }
}

/// <summary>分页结果映射扩展：PagedResponse&lt;实体&gt; → PagedResponse&lt;DTO&gt; 的可复用入口。</summary>
public static class PagedResponseMappingExtensions
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
    public static PagedResponse<TDestination> MapToPagedResponse<TSource, TDestination>(
        this PagedResponse<TSource> source, IMapper mapper) =>
        PagedResponse<TDestination>.Create(
            mapper.Map<List<TDestination>>(source.Items),
            source.Total,
            new PagedRequest { PageIndex = source.PageIndex, PageSize = source.PageSize });
}
