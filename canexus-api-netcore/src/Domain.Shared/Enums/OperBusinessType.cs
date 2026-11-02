namespace Domain.Shared.Enums;

/// <summary>操作日志业务类型（与字典 sys_oper_type 取值一致，取值同若依）。</summary>
public enum OperBusinessType
{
    /// <summary>新增。</summary>
    Add = 1,

    /// <summary>修改。</summary>
    Update = 2,

    /// <summary>删除。</summary>
    Delete = 3,

    /// <summary>授权。</summary>
    Grant = 4,

    /// <summary>导出。</summary>
    Export = 5,

    /// <summary>导入。</summary>
    Import = 6,

    /// <summary>恢复。</summary>
    Restore = 7,

    /// <summary>清空。</summary>
    Clean = 8
}
