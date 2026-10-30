namespace Domain.Entities;

using SqlSugar;
using Domain.Shared.Enums;

/// <summary>系统登录日志实体：记录每次登录成功 / 失败的审计信息。</summary>
[SugarTable("sys_login_log")]
public class SysLoginLog : AggregateBase
{
    /// <summary>登录账号。</summary>
    [SugarColumn(Length = 30)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>登录 IP 地址。</summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string? Ip { get; set; }

    /// <summary>登录地点。</summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string? LoginLocation { get; set; }

    /// <summary>原始 User-Agent（原样存储，解析留给展示层，避免后端维护 UA 规则）。</summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string? UserAgent { get; set; }

    /// <summary>登录状态（见 <see cref="Domain.Shared.Enums.ExecutionStatus"/>：1 成功，0 失败）。</summary>
    [SugarColumn(IsNullable = true)]
    public ExecutionStatus Status { get; set; }

    /// <summary>提示消息（如“登录成功”“密码错误”）。</summary>
    [SugarColumn(Length = 255)]
    public string Msg { get; set; } = string.Empty;

    /// <summary>登录时间（由审计机制自动填充为 CreatedAt，此处冗余存储便于统计查询）。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? LoginTime { get; set; }
}
