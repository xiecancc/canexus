using Domain.Services.Storage;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Storage;

/// <summary>
/// 本地磁盘文件存储实现。
/// <para>安全策略：扩展名白名单 + 大小上限校验（违规抛业务异常，不落盘）；
/// 存储文件名随机化（GUID + 原扩展名）杜绝路径穿越与同名覆盖；
/// 相对路径固定为「日期分桶 / 存储文件名」两级结构，且定位时拒绝包含「..」片段的路径。</para>
/// </summary>
/// <param name="options">文件存储配置选项。</param>
public class LocalFileStorage(IOptions<FileStorageOption> options) : IFileStorage
{
    /// <summary>存储提供方标识（落库用）。</summary>
    public const string ProviderName = "local";

    /// <summary>配置快照（IOptions 足够：热加载对已保存文件路径无回溯意义）。</summary>
    private FileStorageOption Option { get; } = options.Value;

    /// <summary>存储根目录绝对路径（相对路径按当前运行目录解析）。</summary>
    private string RootFullPath => Path.IsPathRooted(Option.RootPath)
        ? Option.RootPath
        : Path.GetFullPath(Option.RootPath, Directory.GetCurrentDirectory());

    /// <inheritdoc />
    public async Task<FileSaveResult> SaveAsync(Stream content, string originalFileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ThrowIfContentNull(content);

        var extension = Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();
        if (extension.Length == 0 || !Option.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessException(
                ErrorCodes.FileExtensionNotAllowed,
                $"不支持的文件类型：'{extension}'（允许的扩展名：{string.Join("、", Option.AllowedExtensions)}）");
        }

        if (content.CanSeek)
        {
            if (content.Length > (long)Option.MaxSizeMB * 1024 * 1024)
            {
                throw new BusinessException(
                    ErrorCodes.FileSizeExceeded,
                    $"文件大小超出上限（最大 {Option.MaxSizeMB} MB）");
            }
        }

        var storedName = $"{Guid.NewGuid():N}.{extension}";
        var bucket = DateTime.UtcNow.ToString("yyyyMM/dd");
        var relativePath = $"{bucket}/{storedName}";
        var fullDirectory = Path.Combine(RootFullPath, bucket.Replace('/', Path.DirectorySeparatorChar));

        // 目录逐级创建（幂等），写失败时清理半成品文件
        Directory.CreateDirectory(fullDirectory);
        var fullPath = Path.Combine(fullDirectory, storedName);

        try
        {
            await using var target = File.Create(fullPath);
            if (content.CanSeek)
            {
                content.Position = 0;
            }

            await content.CopyToAsync(target, cancellationToken);
        }
        catch
        {
            TryCleanup(fullPath);
            throw;
        }

        // 写后复核实际大小（流不可 seek 时唯一可靠的大小校验点）
        var written = new FileInfo(fullPath).Length;
        if (written > (long)Option.MaxSizeMB * 1024 * 1024)
        {
            TryCleanup(fullPath);
            throw new BusinessException(
                ErrorCodes.FileSizeExceeded,
                $"文件大小超出上限（最大 {Option.MaxSizeMB} MB）");
        }

        return new FileSaveResult(storedName, relativePath);
    }

    /// <inheritdoc />
    public Stream OpenRead(string relativePath)
    {
        var fullPath = ResolveSafeFullPath(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new BusinessException(ErrorCodes.FileNotFound, "文件不存在或已被清理");
        }

        return File.OpenRead(fullPath);
    }

    /// <inheritdoc />
    public void Delete(string relativePath)
    {
        var fullPath = ResolveSafeFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    /// <summary>解析相对路径为根目录内绝对路径；拒绝越界（防路径穿越）。</summary>
    /// <param name="relativePath">相对存储根目录的路径。</param>
    /// <returns>根目录内的绝对路径。</returns>
    private string ResolveSafeFullPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var full = Path.GetFullPath(Path.Combine(RootFullPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = RootFullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(ErrorCodes.FileNotFound, "非法的文件路径");
        }

        return full;
    }

    /// <summary>静默清理写失败的半成品文件（清理失败不抛出，避免掩盖原始异常）。</summary>
    /// <param name="fullPath">文件绝对路径。</param>
    private static void TryCleanup(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // 清理失败不阻断主流程：半成品文件由运维清理
        }
    }

    /// <summary>校验内容流非空。</summary>
    /// <param name="content">内容流。</param>
    private static void ThrowIfContentNull(Stream content) => ArgumentNullException.ThrowIfNull(content);
}
