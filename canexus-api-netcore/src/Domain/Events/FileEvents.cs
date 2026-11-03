using Domain.Entities;

namespace Domain.Events;
/// <summary>文件上传成功领域事件：文件元数据落库后发布（缩略图生成、病毒扫描等响应动作可由监听器扩展处理）。</summary>
/// <param name="entity">创建后的文件元数据。</param>
public sealed class FileUploadedEvent(SysFile entity) : AggregateCreatedEvent<SysFile>(entity);

/// <summary>文件删除领域事件：文件元数据软删除成功后发布，Domain 为删除前的状态（物理文件保留，可由监听器决定清理策略）。</summary>
/// <param name="entity">删除前的文件元数据。</param>
public sealed class FileDeletedEvent(SysFile entity) : AggregateDeletedEvent<SysFile>(entity);
