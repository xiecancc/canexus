using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Files;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Services.Storage;
using Domain.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Application.Commands;
/// <summary>上传文件命令处理器：白名单 / 大小校验委托 IFileStorage（违规抛业务异常且不落库），成功后写入元数据档案并发布领域事件；
/// 元数据写入失败时补偿删除已落盘物理文件，避免孤儿文件残留。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="fileRepository">文件元数据仓储。</param>
/// <param name="fileStorage">文件存储端口（写入物理文件）。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
/// <param name="logger">日志器（孤儿文件补偿删除失败时告警）。</param>
public sealed class FileUploadCommandHandler(
    IAggregateRepository<SysFile> fileRepository,
    IFileStorage fileStorage,
    IDomainEventPublisher domainEventPublisher,
    ILogger<FileUploadCommandHandler> logger) : RequestHandler<FileUploadCommand, FileUploadedResponse>
{
    /// <inheritdoc />
    protected override async Task<FileUploadedResponse> HandleAsync(FileUploadCommand command, CancellationToken cancellationToken)
    {
        // 物理文件先行落盘（实现方承担白名单 / 大小校验），成功后再写元数据；
        // 元数据写入失败时补偿删除已落盘物理文件，避免孤儿文件残留（补偿删除失败仅告警，业务异常照常向外传播）
        var saved = await fileStorage.SaveAsync(command.Content, command.FileName, cancellationToken);

        var entity = new SysFile
        {
            FileName = command.FileName,
            StoredName = saved.StoredName,
            RelativePath = saved.RelativePath,
            // 落库 MIME 只取内容签名推导值，忽略客户端声明（T-14）；大小取实际落盘字节数
            ContentType = saved.DetectedContentType,
            SizeBytes = saved.SizeBytes,
            StorageProvider = LocalFileStorageProvider,
            BizCategory = command.BizCategory
        };
        try
        {
            await fileRepository.InsertAsync(entity, cancellationToken);
        }
        catch
        {
            // 仅在元数据写入失败路径补偿：删除失败的异常吞掉并告警，不得顶替或掩盖业务异常
            try
            {
                fileStorage.Delete(saved.RelativePath);
            }
            catch (Exception deleteException)
            {
                logger.LogWarning(deleteException, "上传元数据写入失败后的孤儿文件补偿删除失败，路径：{RelativePath}", saved.RelativePath);
            }

            throw;
        }

        await domainEventPublisher.PublishAsync(new FileUploadedEvent(entity), cancellationToken);

        return new FileUploadedResponse
        {
            Id = entity.Id,
            FileName = entity.FileName,
            SizeBytes = entity.SizeBytes
        };
    }

    /// <summary>存储提供方标识（与本地磁盘实现常量保持一致，避免 Application 反向依赖 Infrastructure）。</summary>
    private const string LocalFileStorageProvider = "local";
}

/// <summary>删除文件命令处理器（标准软删除流程：仅摘除元数据可见性，物理文件保留供恢复与审计）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="fileRepository">文件元数据仓储。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
public sealed class FileDeleteCommandHandler(
    IAggregateRepository<SysFile> fileRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateDeleteCommandHandler<FileDeleteCommand, SysFile>(fileRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.FileNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "文件";

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysFile deleted)
    {
        return new FileDeletedEvent(deleted);
    }
}
