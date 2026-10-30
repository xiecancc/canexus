namespace Domain.Entities;

using SqlSugar;
using Domain.Shared.Enums;

/// <summary>通知公告实体（站内通知与系统公告，供首页公告卡片与公告管理页使用）。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成。</para></summary>
[SugarTable("sys_notice")]
public class SysNotice : AggregateBase
{
    /// <summary>公告标题。</summary>
    [SugarColumn(Length = 200)]
    public virtual string Title { get; set; } = string.Empty;

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>：1 通知，2 公告）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual NoticeType NoticeType { get; set; } = NoticeType.Notification;

    /// <summary>公告内容（富文本正文，存储为长文本）。</summary>
    [SugarColumn(ColumnDataType = "longtext")]
    public virtual string Content { get; set; } = string.Empty;

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 正常，0 关闭——关闭的公告不在首页最新公告中展示）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;
}
