using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Files;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>分页查询文件元数据处理器（标准分页流程：条件构造 → 通用仓储分页 → 开放泛型映射）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="fileRepository">文件元数据仓储。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class FileQueryPagedHandler(
    IAggregateRepository<SysFile> fileRepository,
    IMapper mapper) : AggregateQueryPagedHandler<FileQueryPaged, FilePageRequest, SysFile, FilePagedResponse>(fileRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysFile, bool>>? BuildPredicate(FilePageRequest request)
    {
        var fileName = NormalizeKeyword(request.FileName);
        var bizCategory = NormalizeFilter(request.BizCategory);

        return f =>
            (fileName == null || f.FileName.Contains(fileName))
            && (bizCategory == null || (f.BizCategory != null && f.BizCategory == bizCategory));
    }
}

/// <summary>文件详情查询处理器（返回元数据；物理流读取由控制器经 IFileStorage 承接）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="fileRepository">文件元数据仓储。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class FileQueryDetailHandler(
    IAggregateRepository<SysFile> fileRepository,
    IMapper mapper) : AggregateQueryDetailHandler<FileQueryDetail, SysFile, FileDetailResponse>(fileRepository, mapper)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.FileNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "文件";
}
