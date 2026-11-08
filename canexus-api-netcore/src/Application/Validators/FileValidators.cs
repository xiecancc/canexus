
using Application.Contracts.Commands;
using Application.Validators.Common;
using FluentValidation;

namespace Application.Validators;
/// <summary>
/// 文件上传命令验证器（写命令入口覆盖）：文件名非空、内容流可读。
/// 后缀白名单 / 内容签名 / 大小限制仍由文件存储与控制器按既有链路校验。
/// </summary>
/// <remarks>构造函数。</remarks>
public sealed class FileUploadCommandValidator : AbstractValidator<FileUploadCommand>
{
    /// <summary>构造函数：装配文件名与内容流规则。</summary>
    public FileUploadCommandValidator()
    {
        RuleFor(command => command.FileName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("文件名不能为空");
        RuleFor(command => command.Content)
            .NotNull()
            .WithMessage("文件内容不能为空");
        RuleFor(command => command.Content)
            .Must(stream => stream is null || stream.CanRead)
            .WithMessage("上传文件流不可读");
    }
}

/// <summary>文件删除命令验证器（乐观锁版本号入口兜底）。</summary>
public sealed class FileDeleteCommandValidator() : DeleteCommandValidator<FileDeleteCommand>;
