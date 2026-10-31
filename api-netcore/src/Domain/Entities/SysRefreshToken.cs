
using SqlSugar;

namespace Domain.Entities;
/// <summary>刷新令牌实体：持久化 refreshToken，支持轮换（旧令牌吊销）与校验。
/// <para>实体不保留验证逻辑与数据归一化；令牌串由认证服务内部生成（恒非空）。</para></summary>
[SugarTable("sys_refresh_token")]
public class SysRefreshToken : AggregateBase
{
    /// <summary>刷新令牌随机串（64 位十六进制）。</summary>
    [SugarColumn(Length = 64)]
    public virtual string Token { get; set; } = string.Empty;

    /// <summary>所属用户 Id。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual Guid UserId { get; set; }

    /// <summary>过期时间。</summary>
    [SugarColumn]
    public virtual DateTime ExpiresAt { get; set; }

    /// <summary>吊销时间；null 表示未吊销。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual DateTime? RevokedAt { get; set; }
}
