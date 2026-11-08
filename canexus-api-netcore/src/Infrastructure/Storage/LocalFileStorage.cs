using Domain.Services.Storage;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Storage;

/// <summary>
/// 本地磁盘文件存储实现。
/// <para>安全策略：扩展名白名单 + 内容签名 / MIME 白名单双向校验（忽略客户端声明的 ContentType）
/// + 三道大小上限校验（违规抛业务异常，不落盘）；
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

        var maxBytes = (long)Option.MaxSizeMB * 1024 * 1024;
        // 第一道：可 seek 的流在读取前即按声明长度拒绝（Kestrel 线级上限之外 earliest 的业务拦截）
        if (content.CanSeek && content.Length > maxBytes)
        {
            throw FileTooLarge();
        }

        // 流式落盘（SEC-07）：内存占用恒定为一个块（64 KiB），不再把整个文件缓冲进 MemoryStream。
        // 大小判定仍优先于内容类型判定（避免为识别类型而处理超大载荷）；类型检测在落盘后
        // 回读文件头完成，判定依据仍是真实内容而非客户端声明。
        var chunk = new byte[64 * 1024];
        cancellationToken.ThrowIfCancellationRequested();
        var firstRead = await content.ReadAsync(chunk.AsMemory(), cancellationToken);
        if (firstRead == 0)
        {
            throw new BusinessException(ErrorCodes.FileExtensionNotAllowed, "文件内容为空或类型无法识别");
        }

        long total = firstRead;
        if (total > maxBytes)
        {
            throw FileTooLarge();
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
            await target.WriteAsync(chunk.AsMemory(0, firstRead), cancellationToken);

            // 第三道（流式）：边读边写边累计，超限立即中断并清理半成品，不占用整文件内存
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await content.ReadAsync(chunk.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    throw FileTooLarge();
                }

                await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            TryCleanup(fullPath);
            throw;
        }

        // 写后复核实际落盘大小（防止流式写入与累计计数不一致）
        var written = new FileInfo(fullPath).Length;
        if (written > maxBytes)
        {
            TryCleanup(fullPath);
            throw FileTooLarge();
        }

        // 类型判定只认文件头：签名族必须与扩展名同族，且 MIME 只取内置白名单推导值；
        // 回读落盘文件的头部（≤512 字节），不占用整文件内存
        byte[] header;
        await using (var probe = File.OpenRead(fullPath))
        {
            var headerBuffer = new byte[512];
            var headerLength = await probe.ReadAsync(headerBuffer, cancellationToken);
            header = headerBuffer.AsSpan(0, headerLength).ToArray();
        }

        var family = FileContentInspector.Detect(header);
        if (!FileContentInspector.TryResolveMime(extension, family, out var detectedContentType))
        {
            TryCleanup(fullPath);
            throw new BusinessException(
                ErrorCodes.FileExtensionNotAllowed,
                $"不支持的文件类型：'.{extension}' 与文件内容不符（实测内容格式 {family}）");
        }

        return new FileSaveResult(storedName, relativePath, detectedContentType, written);
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

    /// <summary>构造统一的文件大小超限业务异常。</summary>
    /// <returns>业务异常。</returns>
    private BusinessException FileTooLarge()
    {
        return new BusinessException(
            ErrorCodes.FileSizeExceeded,
            $"文件大小超出上限（最大 {Option.MaxSizeMB} MB）");
    }

    /// <summary>校验内容流非空。</summary>
    /// <param name="content">内容流。</param>
    private static void ThrowIfContentNull(Stream content) => ArgumentNullException.ThrowIfNull(content);
}
