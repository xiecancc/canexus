namespace Application.Contracts.Commands;

using Application.Contracts.Abstractions;
using Application.Contracts.Depts;

/// <summary>创建部门命令（父部门存在性由处理器校验）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record CreateDeptCommand(DeptCreateRequest Request) : ICommand<Guid>;

/// <summary>删除部门命令（存在子部门或部门下存在用户时拒绝）。</summary>
/// <param name="Id">部门 Id。</param>
public sealed record DeleteDeptCommand(Guid Id) : ICommand;

/// <summary>更新部门命令（父级不可选自身或自己的后代）。</summary>
/// <param name="Id">部门 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UpdateDeptCommand(Guid Id, DeptUpdateRequest Request) : ICommand;
