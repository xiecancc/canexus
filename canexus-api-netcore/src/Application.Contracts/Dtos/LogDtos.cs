using Application.Contracts.Dtos.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Logs;
/// <summary>操作日志分页查询请求（GET /api/system/oper-log/page；beginTime / endTime 格式 yyyy-MM-dd，可空）。</summary>
public class OperLogPageRequest : AggregatePagedRequest
{
    /// <summary>操作模块：模糊匹配。</summary>
    public string? Title { get; set; }

    /// <summary>业务类型（见 <see cref="Domain.Shared.Enums.OperBusinessType"/>）；为空不过滤。</summary>
    public OperBusinessType? BusinessType { get; set; }

    /// <summary>操作状态：成功（1）/ 失败（0）；为空不过滤。</summary>
    public ExecutionStatus? Status { get; set; }

    /// <summary>操作人：模糊匹配。</summary>
    public string? OperUserName { get; set; }

    /// <summary>操作时间范围-开始（含，格式 yyyy-MM-dd）。</summary>
    public DateTime? BeginTime { get; set; }

    /// <summary>操作时间范围-结束（含当日，格式 yyyy-MM-dd）。</summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>操作日志列表行（分页数据项）。</summary>
public class OperLogListResponse : AggregatePagedResponse
{
    /// <summary>操作模块。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>业务类型（见 <see cref="Domain.Shared.Enums.OperBusinessType"/>）。</summary>
    public OperBusinessType BusinessType { get; set; }

    /// <summary>方法名称（类全名.方法名）。</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>请求方式（GET / POST / PUT / DELETE）。</summary>
    public string RequestMethod { get; set; } = string.Empty;

    /// <summary>请求 URL（含查询串）。</summary>
    public string RequestUrl { get; set; } = string.Empty;

    /// <summary>请求参数（敏感字段脱敏后的 JSON）。</summary>
    public string? OperParam { get; set; }

    /// <summary>操作 IP 地址。</summary>
    public string? OperIp { get; set; }

    /// <summary>操作人用户名。</summary>
    public string? OperUserName { get; set; }

    /// <summary>操作状态：成功（1），失败（0）。</summary>
    public ExecutionStatus Status { get; set; }

    /// <summary>错误消息（失败时记录）。</summary>
    public string? ErrorMsg { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    public long CostMs { get; set; }

    /// <summary>操作时间。</summary>
    public DateTime? OperTime { get; set; }
}

/// <summary>登录日志分页查询请求（GET /api/system/login-log/page；beginTime / endTime 格式 yyyy-MM-dd，可空）。</summary>
public class LoginLogPageRequest : AggregatePagedRequest
{
    /// <summary>登录账号：模糊匹配。</summary>
    public string? UserName { get; set; }

    /// <summary>登录 IP：模糊匹配。</summary>
    public string? Ip { get; set; }

    /// <summary>登录状态：成功（1）/ 失败（0）；为空不过滤。</summary>
    public ExecutionStatus? Status { get; set; }

    /// <summary>登录时间范围-开始（含，格式 yyyy-MM-dd）。</summary>
    public DateTime? BeginTime { get; set; }

    /// <summary>登录时间范围-结束（含当日，格式 yyyy-MM-dd）。</summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>登录日志列表行（分页数据项）。</summary>
public class LoginLogListResponse : AggregatePagedResponse
{
    /// <summary>登录账号。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>登录 IP 地址。</summary>
    public string? Ip { get; set; }

    /// <summary>登录地点。</summary>
    public string? LoginLocation { get; set; }

    /// <summary>原始 User-Agent。</summary>
    public string? UserAgent { get; set; }

    /// <summary>登录状态：成功（1），失败（0）。</summary>
    public ExecutionStatus Status { get; set; }

    /// <summary>提示消息。</summary>
    public string Msg { get; set; } = string.Empty;

    /// <summary>登录时间。</summary>
    public DateTime? LoginTime { get; set; }
}

/// <summary>删除登录日志请求（DELETE /api/system/login-log/{id}；物理删除不做版本校验，Version 仅保持删除调用形态统一）。</summary>
public class LoginLogDeleteRequest
{
    /// <summary>乐观锁版本号（物理删除不校验，仅为删除调用形态统一）。</summary>
    public Guid Version { get; set; }
}

/// <summary>删除操作日志请求（DELETE /api/system/oper-log/{id}；物理删除不做版本校验，Version 仅保持删除调用形态统一）。</summary>
public class OperLogDeleteRequest
{
    /// <summary>乐观锁版本号（物理删除不校验，仅为删除调用形态统一）。</summary>
    public Guid Version { get; set; }
}
