
using Domain.Shared.Enums;

namespace Application.Contracts.Common;
/// <summary>
/// 状态请求基类：对齐领域实体的启停语义（Status：启用 / 停用）。
/// <para>创建请求继承后缺省为启用；仅状态的轻量请求（如 UserStatusRequest）可直接继承本类。</para>
/// </summary>
public abstract class StatusRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;
}

/// <summary>
/// 状态 + 备注请求基类：对齐 <c>AggregateBase</c> 的 Status 启停语义与 Description 备注字段。
/// <para>各模块 Create / Update 请求的共同字段收拢于此，消除逐模块重复定义。</para>
/// </summary>
public abstract class StatusDescriptionRequest : StatusRequest
{
    /// <summary>备注。</summary>
    public string? Description { get; set; }
}

/// <summary>
/// 实体 VO 基类：对齐领域实体的标识（Id）与创建审计（CreatedAt）。
/// </summary>
public abstract class EntityVO
{
    /// <summary>实体 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 状态 VO 基类：在实体 VO 之上追加启停语义（对齐各实体的 CommonStatus）。
/// </summary>
public abstract class StatusVO : EntityVO
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }
}
