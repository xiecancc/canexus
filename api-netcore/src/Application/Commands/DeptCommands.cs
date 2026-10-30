namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Depts;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建部门命令处理器。</summary>
public sealed class CreateDeptCommandHandler : CommandHandler<CreateDeptCommand, Guid>
{
    private readonly IAggregateTreeRepository<SysDept> _deptRepository;

    /// <summary>构造函数。</summary>
    public CreateDeptCommandHandler(IAggregateTreeRepository<SysDept> deptRepository)
    {
        _deptRepository = deptRepository;
    }

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateDeptCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // 父部门存在性校验（根部门 parentId=Guid.Empty 跳过）
        if (request.ParentId != SysDept.RootParentId
            && await _deptRepository.GetAsync(request.ParentId, cancellationToken) is null)
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
        var inserted = await _deptRepository.InsertAsync(dept, cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除部门命令处理器。</summary>
public sealed class DeleteDeptCommandHandler : CommandHandler<DeleteDeptCommand>
{
    private readonly IAggregateTreeRepository<SysDept> _deptRepository;
    private readonly IAggregateRepository<SysUser> _userRepository;

    /// <summary>构造函数。</summary>
    public DeleteDeptCommandHandler(
        IAggregateTreeRepository<SysDept> deptRepository,
        IAggregateRepository<SysUser> userRepository)
    {
        _deptRepository = deptRepository;
        _userRepository = userRepository;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteDeptCommand command, CancellationToken cancellationToken)
    {
        var dept = await _deptRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DeptNotFound, $"部门 {command.Id} 不存在");

        if (await _deptRepository.CountAsync(d => d.ParentId == command.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DeptHasChildren, $"部门「{dept.DeptName}」下存在子部门，不允许删除");
        }

        if (await _userRepository.CountAsync(u => u.DeptId == command.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DeptHasUsers, $"部门「{dept.DeptName}」下存在用户，不允许删除");
        }

        await _deptRepository.DeleteAsync(dept, cancellationToken);
    }
}

/// <summary>更新部门命令处理器。</summary>
public sealed class UpdateDeptCommandHandler : CommandHandler<UpdateDeptCommand>
{
    private readonly IAggregateTreeRepository<SysDept> _deptRepository;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>构造函数。</summary>
    public UpdateDeptCommandHandler(
        IAggregateTreeRepository<SysDept> deptRepository,
        IUnitOfWork unitOfWork)
    {
        _deptRepository = deptRepository;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateDeptCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var dept = await _deptRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DeptNotFound, $"部门 {command.Id} 不存在");

        var parentChanged = dept.ParentId != request.ParentId;

        await _unitOfWork.RunInTransactionAsync(async () =>
        {
            // 仅更新业务字段；父级变更统一交由树形仓储 MoveAsync 处理（环检测 / 改父）
            dept.DeptName = request.DeptName.Trim();
            dept.OrderNum = request.OrderNum;
            dept.Leader = request.Leader;
            dept.Phone = request.Phone;
            dept.Email = request.Email;
            dept.Status = request.Status;
            await _deptRepository.UpdateAsync(dept, cancellationToken);

            if (parentChanged)
            {
                // 传入部门模块分段错误码，保持既有 API 契约稳定
                await _deptRepository.MoveAsync(command.Id, request.ParentId, new TreeMoveCodes(
                    ParentInvalid: ErrorCodes.DeptParentInvalid,
                    NodeNotFound: ErrorCodes.DeptNotFound,
                    ParentNotFound: ErrorCodes.DeptParentNotFound,
                    EntityName: "部门"), cancellationToken);
            }
        }, cancellationToken);
    }
}
