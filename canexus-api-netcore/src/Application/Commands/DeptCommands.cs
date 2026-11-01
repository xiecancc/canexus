
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Depts;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>创建部门命令处理器（标准创建流程：父部门校验 → 构造 → 插入 → 发布创建事件）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="deptRepository">部门树形仓储。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
public sealed class DeptCreateCommandHandler(
    IAggregateTreeRepository<SysDept> deptRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateCreateCommandHandler<DeptCreateCommand, DeptCreateRequest, SysDept>(deptRepository, domainEventPublisher)
{
    /// <summary>部门树形仓储（父部门存在性校验）。</summary>
    private IAggregateTreeRepository<SysDept> DeptRepository { get; } = deptRepository;

    /// <inheritdoc />
    protected override async Task ValidateAsync(DeptCreateRequest request, CancellationToken cancellationToken)
    {
        // 父部门存在性校验（根部门 parentId=Guid.Empty 跳过）
        if (request.ParentId != SysDept.RootParentId
            && await DeptRepository.GetAsync([it => it.Id == request.ParentId], cancellationToken) is null)
        {
            throw new BusinessException(ErrorCodes.DeptNotFound, $"部门 {request.ParentId} 不存在");
        }
    }

    /// <inheritdoc />
    protected override SysDept MapToEntity(DeptCreateRequest request)
    {
        return new SysDept
        {
            ParentId = request.ParentId,
            DeptName = request.DeptName.Trim(),
            OrderNum = request.OrderNum,
            Leader = request.Leader,
            Phone = request.Phone,
            Email = request.Email,
            Status = request.Status,
            Description = request.Description
        };
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDept inserted)
    {
        // 关键节点发布部门创建领域事件（主键与审计字段已就地填充到传入实体，携带创建后的部门状态）
        return new DeptCreatedEvent(inserted);
    }
}

/// <summary>删除部门命令处理器（标准删除流程：子部门 / 部门占用守卫 → 乐观锁校验 → 软删除 → 发布删除事件）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="deptRepository">部门树形仓储。</param>
/// <param name="userRepository">用户仓储（部门占用检查）。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
public sealed class DeptDeleteCommandHandler(
    IAggregateTreeRepository<SysDept> deptRepository,
    IAggregateRepository<SysUser> userRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateDeleteCommandHandler<DeptDeleteCommand, SysDept>(deptRepository, domainEventPublisher)
{
    /// <summary>用户仓储（部门占用检查）。</summary>
    private IAggregateRepository<SysUser> UserRepository { get; } = userRepository;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.DeptNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "部门";

    /// <inheritdoc />
    protected override async Task ValidateBeforeDeleteAsync(SysDept entity, CancellationToken cancellationToken)
    {
        if (await Repository.CountAsync([d => d.ParentId == entity.Id], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DeptHasChildren, $"部门「{entity.DeptName}」下存在子部门，不允许删除");
        }

        if (await UserRepository.CountAsync([u => u.DeptId == entity.Id], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DeptHasUsers, $"部门「{entity.DeptName}」下存在用户，不允许删除");
        }
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDept deleted)
    {
        // 关键节点发布部门删除领域事件（携带删除前的部门状态）
        return new DeptDeletedEvent(deleted);
    }
}

/// <summary>更新部门命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeptUpdateCommandHandler(
    IAggregateTreeRepository<SysDept> deptRepository,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork) : RequestHandler<DeptUpdateCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(DeptUpdateCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var dept = await deptRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DeptNotFound, $"部门 {command.Id} 不存在");

        var parentChanged = dept.ParentId != request.ParentId;

        // 变更前捕获部门状态快照（实体在事务内被原位修改，挂载点变化亦可由监听器比对）
        var before = dept.Snapshot<SysDept>();

        await unitOfWork.RunInTransactionAsync(async () =>
        {
            // 仅更新业务字段；父级变更统一交由树形仓储 MoveAsync 处理（环检测 / 改父）
            dept.DeptName = request.DeptName.Trim();
            dept.OrderNum = request.OrderNum;
            dept.Leader = request.Leader;
            dept.Phone = request.Phone;
            dept.Email = request.Email;
            dept.Status = request.Status;
            dept.Description = request.Description;
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

        // 关键节点发布部门修改领域事件（携带变更前后部门状态）
        await domainEventPublisher.PublishAsync(new DeptUpdatedEvent(before, dept), cancellationToken);
        return true;
    }
}
