
using Application.Contracts.Tenants;
using Application.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Shared.Enums;
using Domain.Shared.Paging;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Application.Tests.Mappings;
/// <summary>
/// 分页结果映射单元测试：覆盖 CommonProfile 开放泛型映射与 MapToPagedResult 扩展两条路径，
/// 验证分页元数据（总条数 / 页码 / 页大小）自动复制与元素列表按实体 → VO 映射转换。
/// </summary>
[TestFixture]
public class PagedResultMappingTests
{
    /// <summary>实体 → VO 映射器（CommonProfile + SysTenantProfile，与应用同一 AddAutoMapper 构建方式）。</summary>
    private readonly IMapper _mapper = CreateMapper();

    /// <summary>构建 AutoMapper 映射器。</summary>
    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(expression =>
        {
            expression.AddProfile<CommonProfile>();
            expression.AddProfile<SysTenantProfile>();
        });
        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }

    /// <summary>构造两行实体分页结果（页码 2 / 页大小 10 / 总数 15）。</summary>
    private static PagedResult<SysTenant> CreateEntityPage() => PagedResult<SysTenant>.Create(
        [
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), TenantName = "租户A", TenantKey = "tenant-a", Status = CommonStatus.Enabled },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), TenantName = "租户B", TenantKey = "tenant-b", Status = CommonStatus.Enabled }
        ],
        total: 15,
        pageIndex: 2,
        pageSize: 10);

    [Test]
    public void OpenGenericProfile_MapsMetadataAndItems()
    {
        // 开放泛型映射：mapper.Map<PagedResult<TDto>>(PagedResult<实体>) 直接可用
        var vo = _mapper.Map<PagedResult<TenantListVO>>(CreateEntityPage());

        Assert.Multiple(() =>
        {
            Assert.That(vo.Total, Is.EqualTo(15), "总条数应自动复制");
            Assert.That(vo.PageIndex, Is.EqualTo(2), "页码应自动复制");
            Assert.That(vo.PageSize, Is.EqualTo(10), "每页大小应自动复制");
            Assert.That(vo.Items, Has.Count.EqualTo(2), "元素数量应与源一致");
            Assert.That(vo.Items[0].TenantName, Is.EqualTo("租户A"), "元素应按实体 → VO 映射转换");
            Assert.That(vo.Items[1].TenantKey, Is.EqualTo("tenant-b"), "元素应按实体 → VO 映射转换");
        });
    }

    [Test]
    public void MapToPagedResult_Extension_MapsMetadataAndItems()
    {
        // 扩展方法路径：显式复制元数据 + mapper.Map 元素列表；
        // 注意：扩展有两个泛型参数，显式指定时必须同时给出（Source, Destination）
        var vo = CreateEntityPage().MapToPagedResult<SysTenant, TenantListVO>(_mapper);

        Assert.Multiple(() =>
        {
            Assert.That(vo.Total, Is.EqualTo(15));
            Assert.That(vo.PageIndex, Is.EqualTo(2));
            Assert.That(vo.PageSize, Is.EqualTo(10));
            Assert.That(vo.Items, Has.Count.EqualTo(2));
            Assert.That(vo.Items[0].TenantKey, Is.EqualTo("tenant-a"));
        });
    }

    [Test]
    public void OpenGenericProfile_MissingElementMap_Throws()
    {
        // 缺少元素映射（未注册 SysTenant → 本测试类的映射）时应显式抛错，而非静默产出空对象
        var page = PagedResult<SysTenant>.Create(
            [new() { TenantName = "租户A", TenantKey = "tenant-a" }],
            total: 1, pageIndex: 1, pageSize: 10);

        Assert.Throws<AutoMapperMappingException>(
            () => _mapper.Map<PagedResult<PagedResultMappingTests>>(page));
    }
}
