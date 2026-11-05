
using SqlSugar;

namespace Domain.Entities;
/// <summary>刷新令牌实体：持久化 refreshToken，支持轮换（旧令牌吊销）与校验。
/// <para>实体不保留验证逻辑与数据归一化；令牌串由认证服务内部生成（恒非空）。</para></summary>
[SugarTable("sys_refresh_token", TableDescription = "刷新令牌实体：持久化 refreshToken，支持轮换（旧令牌吊销）与校验。")]
public class SysRefreshToken : AggregateBase
{
    /// <summary>刷新令牌随机串（64 位十六进制）。</summary>
    [SugarColumn(ColumnDescription = "刷新令牌随机串（64 位十六进制）。", Length = 64)]
    public virtual string Token { get; set; } = string.Empty;

    /// <summary>所属用户 Id。</summary>
    [SugarColumn(ColumnDescription = "所属用户 Id。")]
    public virtual Guid UserId { get; set; }

    /// <summary>过期时间。</summary>
    [SugarColumn(ColumnDescription = "过期时间。")]
    public virtual DateTime ExpiresAt { get; set; }

    /// <summary>吊销时间；null 表示未吊销。</summary>
    [SugarColumn(ColumnDescription = "吊销时间；null 表示未吊销。", IsNullable = true)]
    public virtual DateTime? RevokedAt { get; set; }

    /// <summary>签发来源 IP（在线会话列表展示用；历史数据为 null）。</summary>
    [SugarColumn(ColumnDescription = "签发来源 IP（在线会话列表展示用；历史数据为 null）。", IsNullable = true, Length = 64)]
    public virtual string? Ip { get; set; }

    /// <summary>签发来源 User-Agent 原文（在线会话列表展示用；历史数据为 null）。</summary>
    [SugarColumn(ColumnDescription = "签发来源 User-Agent 原文（在线会话列表展示用；历史数据为 null）。", IsNullable = true, Length = 512)]
    public virtual string? UserAgent { get; set; }

    /// <summary>
    /// 会话标识：同一次登录派生出的 accessToken 与轮换后的 refreshToken 共享同一值；
    /// 单会话登出 / 管理员强制下线时按该值写入 accessToken 吊销集合，仅使目标设备失效。
    /// <para>历史记录（升级前签发）为 null，表示无会话标识，此类会话只能按用户级版本整体失效。</para>
    /// </summary>
    [SugarColumn(ColumnDescription = "会话标识：同一次登录及其 refreshToken 轮换共享，用于单会话 accessToken 吊销。", IsNullable = true)]
    public virtual Guid? SessionId { get; set; }
}
