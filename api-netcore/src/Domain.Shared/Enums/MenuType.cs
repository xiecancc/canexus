namespace Domain.Shared.Enums;

/// <summary>菜单类型：目录 / 菜单 / 按钮统一存储于 sys_menu（取值同若依）。</summary>
public enum MenuType
{
    /// <summary>目录（仅路由分组，无组件）。</summary>
    Directory = 1,

    /// <summary>菜单（路由页面，有组件路径）。</summary>
    Menu = 2,

    /// <summary>按钮（页面内操作权限点，仅有权限标识）。</summary>
    Button = 3
}
