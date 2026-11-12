
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

namespace Application.Queries;
/// <summary>分页查询登录日志处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="loginLogRepository">登录日志仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class LoginLogQueryPagedHandler(ILoginLogRepository loginLogRepository, IMapper mapper) : QueryHandler<LoginLogQueryPaged, PagedResponse<LoginLogPagedResponse>>
{
    /// <inheritdoc />
    protected override async Task<PagedResponse<LoginLogPagedResponse>> HandleAsync(LoginLogQueryPaged query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var userName = NormalizeKeyword(request.UserName);
        var ip = NormalizeKeyword(request.Ip);
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

        // 开放泛型映射闭合 PagedResponse<SysLoginLog> → PagedResponse<LoginLogPagedResponse>：
        // 分页元数据同名复制，Items 元素运行期按模块映射解析
        return mapper.Map<PagedResponse<LoginLogPagedResponse>>(page)!;
    }
}

/// <summary>分页查询操作日志处理器（标准分页流程：标题 / 操作人 / 业务类型 / 状态 / 时间段过滤）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="operLogRepository">操作日志仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class OperLogQueryPagedHandler(IOperLogRepository operLogRepository, IMapper mapper) : AggregateQueryPagedHandler<OperLogQueryPaged, OperLogPageRequest, SysOperLog, OperLogPagedResponse>(operLogRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysOperLog, bool>>? BuildPredicate(OperLogPageRequest request)
    {
        var title = NormalizeKeyword(request.Title);
        var operUserName = NormalizeKeyword(request.OperUserName);
        var businessType = request.BusinessType;
        var status = request.Status;
        var beginTime = request.BeginTime;
        var endTime = LogQueryBounds.NormalizeEndTime(request.EndTime);

        return l =>
            (title == null || l.Title.Contains(title))
            && (businessType == null || l.BusinessType == businessType.Value)
            && (status == null || l.Status == status.Value)
            && (operUserName == null || (l.OperUserName != null && l.OperUserName.Contains(operUserName)))
            && (beginTime == null || (l.OperTime != null && l.OperTime >= beginTime.Value))
            && (endTime == null || (l.OperTime != null && l.OperTime < endTime.Value));
    }
}
