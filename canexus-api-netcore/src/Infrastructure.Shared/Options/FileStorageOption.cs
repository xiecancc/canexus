namespace Infrastructure.Shared.Options;

/// <summary>
/// 文件存储配置选项
/// <para>控制上传白名单 / 大小上限与本地磁盘根目录；Provider 预留对象存储扩展位。</para>
/// </summary>
public class FileStorageOption : OptionBase
{
    /// <summary>存储提供方：local（本地磁盘，当前唯一实现；预留 oss 等扩展值）。</summary>
    public string Provider { get; set; } = "local";

    /// <summary>本地存储根目录：绝对路径或相对应用运行目录的相对路径（相对路径按运行目录解析）。</summary>
    public string RootPath { get; set; } = "uploads";

    /// <summary>单文件大小上限（MB，含边界）。</summary>
    public int MaxSizeMB { get; set; } = 10;

    /// <summary>允许上传的扩展名白名单（不含点、大小写不敏感；空集合表示拒绝一切上传）。</summary>
    public List<string> AllowedExtensions { get; set; } =
    [
        "jpg", "jpeg", "png", "gif", "webp", "bmp",
        "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "md",
        "zip", "rar", "7z"
    ];

    /// <inheritdoc />
    public override void Validate()
    {
        EnsureNotNullOrWhiteSpace(Provider, nameof(Provider), "FileStorage:Provider 不能为空");
        EnsureNotNullOrWhiteSpace(RootPath, nameof(RootPath), "FileStorage:RootPath 不能为空");
        EnsureInRange(MaxSizeMB, 1, 1024, nameof(MaxSizeMB), "FileStorage:MaxSizeMB 必须在 1-1024 之间");

        // 白名单元素归一化检查：不得包含点 / 空白 / 路径分隔符（防白名单绕过）
        foreach (var ext in AllowedExtensions)
        {
            Ensure(
                !string.IsNullOrWhiteSpace(ext)
                && !ext.Contains('.')
                && !ext.Contains('/')
                && !ext.Contains('\\')
                && ext.Trim().Length == ext.Length,
                nameof(AllowedExtensions),
                $"FileStorage:AllowedExtensions 中存在非法扩展名：'{ext}'（不得包含点、路径分隔符或首尾空白）");
        }
    }
}
