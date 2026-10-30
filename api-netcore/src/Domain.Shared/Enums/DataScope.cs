namespace Domain.Shared.Enums;

/// <summary>
/// 数据范围枚举：约束角色可见的数据边界（对应若依 sys_role.data_scope 字段语义）。
/// <para>数据过滤的具体实现由应用层 IDataScopeFilter 在查询时使用（B5 模块接入）。</para>
/// </summary>
public enum DataScope
{
    /// <summary>全部数据权限。</summary>
    All = 1,

    /// <summary>自定义数据权限（按配置的部门集合）。</summary>
    Custom = 2,

    /// <summary>本部门数据权限。</summary>
    Dept = 3,

    /// <summary>本部门及以下数据权限。</summary>
    DeptAndBelow = 4,

    /// <summary>仅本人数据权限。</summary>
    Self = 5
}
