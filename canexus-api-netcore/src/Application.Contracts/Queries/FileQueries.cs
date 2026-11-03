using Application.Contracts.Abstractions;
using Application.Contracts.Files;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>分页查询文件元数据（文件名模糊 / 用途分类过滤，按创建时间倒序）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record FileQueryPaged(FilePageRequest Request) : AggregateQueryPaged<FilePageRequest, FilePagedResponse>(Request);

/// <summary>文件详情查询（返回元数据；物理流由控制器经 IFileStorage 配套读取）。</summary>
/// <param name="Id">文件 Id。</param>
public sealed record FileQueryDetail(Guid Id) : AggregateQueryDetail<FileDetailResponse>(Id);
