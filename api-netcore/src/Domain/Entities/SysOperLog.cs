namespace Domain.Entities;

using SqlSugar;
using Domain.Shared.Enums;

/// <summary>操作日志实体：记录带 [OperLog] 特性的写操作审计信息。</summary>
[SugarTable("sys_oper_log")]
public class SysOperLog : AggregateBase
{
    /// <summary>操作模块（如“用户管理”）。</summary>
    [SugarColumn(Length = 50)]
    public string Title { get; set; } = string.Empty;

    /// <summary>业务类型（见 <see cref="Domain.Shared.Enums.OperBusinessType"/>：1 新增，2 修改，3 删除，4 授权，5 导出，6 导入，8 清空）。</summary>
    [SugarColumn(IsNullable = true)]
    public OperBusinessType BusinessType { get; set; }

    /// <summary>方法名称（类全名.方法名）。</summary>
    [SugarColumn(Length = 200)]
    public string Method { get; set; } = string.Empty;

    /// <summary>请求方式（GET / POST / PUT / DELETE）。</summary>
    [SugarColumn(Length = 10)]
    public string RequestMethod { get; set; } = string.Empty;

    /// <summary>请求 URL（含查询串）。</summary>
    [SugarColumn(Length = 255)]
    public string RequestUrl { get; set; } = string.Empty;

    /// <summary>请求参数（敏感字段脱敏后的 JSON，截断 2000 字符）。</summary>
    [SugarColumn(IsNullable = true, Length = 2000)]
    public string? OperParam { get; set; }

    /// <summary>操作 IP 地址。</summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string? OperIp { get; set; }

    /// <summary>操作人用户名（未认证时为空）。</summary>
    [SugarColumn(IsNullable = true, Length = 30)]
    public string? OperUserName { get; set; }

    /// <summary>操作状态（见 <see cref="Domain.Shared.Enums.ExecutionStatus"/>：1 成功，0 失败）。</summary>
    [SugarColumn(IsNullable = true)]
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Success;

    /// <summary>错误消息（失败时记录，截断 2000 字符）。</summary>
    [SugarColumn(IsNullable = true, Length = 2000)]
    public string? ErrorMsg { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    [SugarColumn(IsNullable = true)]
    public long CostMs { get; set; }

    /// <summary>操作时间。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? OperTime { get; set; }
}
