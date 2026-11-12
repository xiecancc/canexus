
using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;
/// <summary>通知公告实体（站内通知与系统公告，供首页公告卡片与公告管理页使用）。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成。</para></summary>
[SugarIndex("ix_sys_notice_scope", nameof(TenantId), OrderByType.Asc, nameof(DeletedAt), OrderByType.Asc, nameof(CreatedAt), OrderByType.Asc)]
[SugarTable("sys_notice", TableDescription = "通知公告实体（站内通知与系统公告，供首页公告卡片与公告管理页使用）。")]
public class SysNotice : AggregateBase
{
    /// <summary>公告标题。</summary>
    [SugarColumn(ColumnDescription = "公告标题。", Length = 200)]
    public virtual string Title { get; set; } = string.Empty;

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>：1 通知，2 公告）。</summary>
    [SugarColumn(ColumnDescription = "公告类型（见 NoticeType：1 通知，2 公告）。")]
    public virtual NoticeType NoticeType { get; set; } = NoticeType.Notification;

    /// <summary>公告内容（富文本正文，存储为长文本）。</summary>
    [SugarColumn(ColumnDescription = "公告内容（富文本正文，存储为长文本）。", ColumnDataType = "longtext")]
    public virtual string Content { get; set; } = string.Empty;

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 正常，0 关闭——关闭的公告不在首页最新公告中展示）。</summary>
    [SugarColumn(ColumnDescription = "状态（见 CommonStatus：1 正常，0 关闭——关闭的公告不在首页最新公告中展示）。")]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;
}
