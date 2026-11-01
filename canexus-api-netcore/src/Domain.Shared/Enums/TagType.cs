namespace Domain.Shared.Enums;

/// <summary>字典数据标签样式（对应前端 el-tag 的 type：primary / success / info / warning / danger）。</summary>
public enum TagType
{
    /// <summary>主要（primary）。</summary>
    Primary = 1,

    /// <summary>成功（success）。</summary>
    Success = 2,

    /// <summary>信息（info）。</summary>
    Info = 3,

    /// <summary>警告（warning）。</summary>
    Warning = 4,

    /// <summary>危险（danger）。</summary>
    Danger = 5
}
