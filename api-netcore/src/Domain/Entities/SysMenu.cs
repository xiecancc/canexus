namespace Domain.Entities;

using SqlSugar;
using Domain.Shared.Enums;

/// <summary>系统菜单实体：目录 / 菜单 / 按钮统一存储，树形结构。
/// <para>实体不保留验证逻辑与数据归一化：字段约束（含类型与字段耦合的跨属性规则）
/// 由 Application 层 FluentValidation Validators 承接，trim 在字段映射阶段自动完成。</para></summary>
[SugarTable("sys_menu")]
public class SysMenu : AggregateTreeBase<SysMenu>
{
    /// <summary>菜单类型（见 <see cref="Domain.Shared.Enums.MenuType"/>）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual MenuType MenuType { get; set; } = MenuType.Directory;

    /// <summary>菜单名称。</summary>
    [SugarColumn(Length = 50)]
    public virtual string MenuName { get; set; } = string.Empty;

    /// <summary>路由地址（顶层带 / 前缀，子级为相对路径）。</summary>
    [SugarColumn(Length = 200)]
    public virtual string Path { get; set; } = string.Empty;

    /// <summary>组件路径（相对前端 views 目录，如 system/user/index）；目录与按钮为空。</summary>
    [SugarColumn(IsNullable = true, Length = 255)]
    public virtual string? Component { get; set; }

    /// <summary>权限标识（如 system:user:list）；非按钮可为空。</summary>
    [SugarColumn(IsNullable = true, Length = 100)]
    public virtual string? Perms { get; set; }

    /// <summary>菜单图标。</summary>
    [SugarColumn(IsNullable = true, Length = 100)]
    public virtual string? Icon { get; set; }

    /// <summary>显示顺序（升序，树形同级排序依据）。</summary>
    [SugarColumn(IsNullable = true)]
    public override int OrderNum { get; set; }

    /// <summary>是否显示（见 <see cref="Domain.Shared.Enums.MenuVisible"/>）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual MenuVisible Visible { get; set; } = MenuVisible.Show;

    /// <summary>路由页面是否缓存（KeepAlive）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual bool KeepAlive { get; set; }

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;
}
