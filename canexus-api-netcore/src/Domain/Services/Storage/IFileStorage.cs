namespace Domain.Services.Storage;

/// <summary>文件保存结果：落盘后的物理文件信息与由内容签名推导的类型（业务元数据由调用方落库）。</summary>
/// <param name="StoredName">存储文件名（随机化重命名，防路径穿越与覆盖）。</param>
/// <param name="RelativePath">相对存储根目录的路径（供下载时定位，不含根目录本身）。</param>
/// <param name="DetectedContentType">由文件内容签名 + MIME 白名单推导的规范 MIME（忽略客户端声明）。</param>
/// <param name="SizeBytes">实际落盘字节数（不可 seek 的输入流也准确）。</param>
public sealed record FileSaveResult(
    string StoredName,
    string RelativePath,
    string DetectedContentType,
    long SizeBytes);

/// <summary>
/// 文件存储端口：抽象 BLOB 的写入 / 读取 / 删除，屏蔽底层介质（本地磁盘 / 对象存储）差异。
/// <para>实现方职责：扩展名白名单、大小上限与内容签名校验（违规抛业务异常）、随机化存储文件名、
/// 按相对路径定位文件；返回类型必须由文件内容签名推导，不得采信客户端声明的 MIME。
/// 端口不关心目录组织策略。</para>
/// </summary>
public interface IFileStorage
{
    /// <summary>保存文件流：校验通过后写入存储介质，返回物理存储信息。</summary>
    /// <param name="content">文件内容流（调用方负责生命周期，实现方读完即止）。</param>
    /// <param name="originalFileName">原始文件名（仅用于提取扩展名做白名单校验，不作为存储名）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存储结果（存储文件名与相对路径）。</returns>
    public Task<FileSaveResult> SaveAsync(Stream content, string originalFileName, CancellationToken cancellationToken = default);

    /// <summary>打开已存储文件的读取流（调用方负责释放）。</summary>
    /// <param name="relativePath">相对存储根目录的路径（由保存结果提供）。</param>
    /// <returns>只读文件流；文件不存在时抛出业务异常。</returns>
    public Stream OpenRead(string relativePath);

    /// <summary>删除已存储的物理文件（文件不存在时静默忽略，保证幂等）。</summary>
    /// <param name="relativePath">相对存储根目录的路径。</param>
    public void Delete(string relativePath);
}
