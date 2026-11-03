using Application.Contracts.Abstractions;
using Application.Contracts.Files;

namespace Application.Contracts.Commands;
/// <summary>上传文件命令（白名单与大小校验由 IFileStorage 实现方承接，违规抛业务异常且不落库）。</summary>
/// <param name="FileName">原始文件名（含扩展名）。</param>
/// <param name="ContentType">内容类型 MIME（可空）。</param>
/// <param name="BizCategory">业务用途分类（可空，预留）。</param>
/// <param name="Content">文件内容流（请求期内有效）。</param>
public sealed record FileUploadCommand(string FileName, string? ContentType, string? BizCategory, Stream Content) : Request<FileUploadedResponse>, ICommand;

/// <summary>删除文件命令（软删除元数据；物理文件保留，供恢复与审计回溯）。</summary>
/// <param name="Id">文件 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record FileDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);
