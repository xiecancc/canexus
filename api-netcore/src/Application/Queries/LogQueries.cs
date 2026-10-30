namespace Application.Queries;

using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Logs;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Rules;
using Domain.Shared.Paging;

/// <summary>分页查询登录日志处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="loginLogRepository">登录日志仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class GetLoginLogPageQueryHandler(ILoginLogRepository loginLogRepository, IMapper mapper) : QueryHandler<GetLoginLogPageQuery, PagedResult<LoginLogListVO>>
{
    /// <inheritdoc />
    protected override async Task<PagedResult<LoginLogListVO>> HandleAsync(GetLoginLogPageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var userName = string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName.Trim();
        var ip = string.IsNullOrWhiteSpace(request.Ip) ? null : request.Ip.Trim();
        var status = request.Status;
        var beginTime = request.BeginTime;
        var endTime = LogQueryBounds.NormalizeEndTime(request.EndTime);

        Expression<Func<SysLoginLog, bool>> predicate = l =>
            (userName == null || l.UserName.Contains(userName))
            && (ip == null || (l.Ip != null && l.Ip.Contains(ip)))
            && (status == null || l.Status == status.Value)
            && (beginTime == null || (l.LoginTime != null && l.LoginTime >= beginTime.Value))
            && (endTime == null || (l.LoginTime != null && l.LoginTime < endTime.Value));

        // 按登录时间倒序：日志列表最新记录在前（Guid 主键无时序语义，不能按 Id 排序）
        var page = await loginLogRepository.GetPagedByTimeDescAsync(request, predicate, cancellationToken);
        var items = page.Items.Select(l => mapper.Map<LoginLogListVO>(l)).ToList();
        return PagedResult<LoginLogListVO>.Create(items, page.Total, request);
    }
}

/// <summary>分页查询操作日志处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="operLogRepository">操作日志仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class GetOperLogPageQueryHandler(IOperLogRepository operLogRepository, IMapper mapper) : QueryHandler<GetOperLogPageQuery, PagedResult<OperLogListVO>>
{
    /// <inheritdoc />
    protected override async Task<PagedResult<OperLogListVO>> HandleAsync(GetOperLogPageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        var operUserName = string.IsNullOrWhiteSpace(request.OperUserName) ? null : request.OperUserName.Trim();
        var businessType = request.BusinessType;
        var status = request.Status;
        var beginTime = request.BeginTime;
        var endTime = LogQueryBounds.NormalizeEndTime(request.EndTime);

        Expression<Func<SysOperLog, bool>> predicate = l =>
            (title == null || l.Title.Contains(title))
            && (businessType == null || l.BusinessType == businessType.Value)
            && (status == null || l.Status == status.Value)
            && (operUserName == null || (l.OperUserName != null && l.OperUserName.Contains(operUserName)))
            && (beginTime == null || (l.OperTime != null && l.OperTime >= beginTime.Value))
            && (endTime == null || (l.OperTime != null && l.OperTime < endTime.Value));

        var page = await operLogRepository.GetPagedAsync(request.PageIndex, request.PageSize, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(l => mapper.Map<OperLogListVO>(l)).ToList();
        return PagedResult<OperLogListVO>.Create(items, page.Total, request);
    }
}
