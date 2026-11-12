using SqlSugar;

namespace Domain.Entities;

/// <summary>
/// 系统文件实体：上传文件的元数据档案（物理内容由 IFileStorage 管理于此表之外）。
/// <para>FileName / ContentType / SizeBytes 描述原始上传物；StorageProvider + RelativePath 定位物理存储；
/// 软删除（继承自聚合基类）只摘除元数据可见性，物理文件保留（恢复可用，物理清理属运维职责）。</para>
/// </summary>
[SugarIndex("ix_sys_file_scope", nameof(TenantId), OrderByType.Asc, nameof(DeletedAt), OrderByType.Asc, nameof(CreatedAt), OrderByType.Asc)]
[SugarTable("sys_file", TableDescription = "系统文件表：上传文件元数据档案")]
public class SysFile : AggregateBase
{
    /// <summary>原始文件名（含扩展名，展示用）。</summary>
    [SugarColumn(Length = 256, ColumnDescription = "原始文件名（含扩展名）")]
    public virtual string FileName { get; set; } = string.Empty;

    /// <summary>存储文件名（随机化重命名后的物理名，含扩展名）。</summary>
    [SugarColumn(Length = 128, ColumnDescription = "存储文件名（随机化重命名）")]
    public virtual string StoredName { get; set; } = string.Empty;

    /// <summary>相对存储根目录的路径（定位物理文件，不含根目录）。</summary>
    [SugarColumn(Length = 512, ColumnDescription = "相对存储根目录的路径")]
    public virtual string RelativePath { get; set; } = string.Empty;

    /// <summary>内容类型（MIME，由文件内容签名 + 内置白名单推导，不采信上传方声明）。</summary>
    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "内容类型 MIME")]
    public virtual string? ContentType { get; set; }

    /// <summary>文件大小（字节）。</summary>
    [SugarColumn(ColumnDescription = "文件大小（字节）")]
    public virtual long SizeBytes { get; set; }

    /// <summary>存储提供方标识（local / oss 等，与 IFileStorage 实现对应）。</summary>
    [SugarColumn(Length = 32, ColumnDescription = "存储提供方标识")]
    public virtual string StorageProvider { get; set; } = "local";

    /// <summary>业务用途分类（avatar / attachment / import 等，预留扩展）。</summary>
    [SugarColumn(Length = 32, IsNullable = true, ColumnDescription = "业务用途分类")]
    public virtual string? BizCategory { get; set; }
}
