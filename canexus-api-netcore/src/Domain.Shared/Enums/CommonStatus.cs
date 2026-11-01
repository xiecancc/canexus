namespace Domain.Shared.Enums;

/// <summary>
/// 通用启停状态：1 启用 / 0 停用（同字典 sys_normal_disable，取值与各业务表 status 列一致）。
/// <para>领域含义随宿主实体有所别名：定时任务域中 1 = 运行（入调度）、0 = 暂停（不入调度）；
/// 公告域中 1 = 正常（展示）、0 = 关闭（隐藏）——底层数值语义一致，均为「启用 / 停用」。</para>
/// </summary>
public enum CommonStatus
{
    /// <summary>停用（任务域：暂停；公告域：关闭）。</summary>
    Disabled = 0,

    /// <summary>启用（任务域：运行；公告域：正常）。</summary>
    Enabled = 1
}
