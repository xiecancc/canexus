namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Depts;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建部门命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class CreateDeptCommandHandler(IAggregateTreeRepository<SysDept> deptRepository, IDomainEventPublisher domainEventPublisher) : CommandHandler<CreateDeptCommand, Guid>
{
    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateDeptCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // 父部门存在性校验（根部门 parentId=Guid.Empty 跳过）
        if (request.ParentId != SysDept.RootParentId
            && await deptRepository.GetAsync(request.ParentId, cancellationToken) is null)
        {
            throw new BusinessException(ErrorCodes.DeptNotFound, $"部门 {request.ParentId} 不存在");
        }

        var dept = new SysDept
        {
            ParentId = request.ParentId,
            DeptName = request.DeptName.Trim(),
            OrderNum = request.OrderNum,
            Leader = request.Leader,
            Phone = request.Phone,
            Email = request.Email,
            Status = request.Status
        };
        var inserted = await deptRepository.InsertAsync(dept, cancellationToken);

        // 关键节点发布部门创建领域事件（清理等响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new DeptChangedEvent(inserted.Id, AggregateChangeType.Created), cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除部门命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeleteDeptCommandHandler(
    IAggregateTreeRepository<SysDept> deptRepository,
    IAggregateRepository<SysUser> userRepository,
    IDomainEventPublisher domainEventPublisher) : CommandHandler<DeleteDeptCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteDeptCommand command, CancellationToken cancellationToken)
    {
        var dept = await deptRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DeptNotFound, $"部门 {command.Id} 不存在");

        if (await deptRepository.CountAsync(d => d.ParentId == command.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DeptHasChildren, $"部门「{dept.DeptName}」下存在子部门，不允许删除");
        }

        if (await userRepository.CountAsync(u => u.DeptId == command.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DeptHasUsers, $"部门「{dept.DeptName}」下存在用户，不允许删除");
        }

        await deptRepository.DeleteAsync(dept, cancellationToken);

        // 关键节点发布部门删除领域事件（清理等响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new DeptChangedEvent(command.Id, AggregateChangeType.Deleted), cancellationToken);
    }
}

/// <summary>更新部门命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateDeptCommandHandler(
    IAggregateTreeRepository<SysDept> deptRepository,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork) : CommandHandler<UpdateDeptCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateDeptCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var dept = await deptRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DeptNotFound, $"部门 {command.Id} 不存在");

        var parentChanged = dept.ParentId != request.ParentId;

        await unitOfWork.RunInTransactionAsync(async () =>
        {
            // 仅更新业务字段；父级变更统一交由树形仓储 MoveAsync 处理（环检测 / 改父）
            dept.DeptName = request.DeptName.Trim();
            dept.OrderNum = request.OrderNum;
            dept.Leader = request.Leader;
            dept.Phone = request.Phone;
            dept.Email = request.Email;
            dept.Status = request.Status;
            await deptRepository.UpdateAsync(dept, cancellationToken);

            if (parentChanged)
            {
                // 传入部门模块分段错误码，保持既有 API 契约稳定
                await deptRepository.MoveAsync(command.Id, request.ParentId, new TreeMoveCodes(
                    ParentInvalid: ErrorCodes.DeptParentInvalid,
                    NodeNotFound: ErrorCodes.DeptNotFound,
                    ParentNotFound: ErrorCodes.DeptParentNotFound,
                    EntityName: "部门"), cancellationToken);
            }
        }, cancellationToken);

        // 关键节点发布部门更新领域事件（含挂载点变化，清理等响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new DeptChangedEvent(command.Id, AggregateChangeType.Updated), cancellationToken);
    }
}
