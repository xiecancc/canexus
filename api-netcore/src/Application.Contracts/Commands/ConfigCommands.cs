
using Application.Contracts.Abstractions;
using Application.Contracts.Configs;

namespace Application.Contracts.Commands;
/// <summary>创建参数配置命令（configKey 唯一，创建项一律非内置）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record CreateConfigCommand(ConfigCreateRequest Request) : ICommand<Guid>;

/// <summary>删除参数配置命令（内置参数拒绝删除）。</summary>
/// <param name="Id">参数 Id。</param>
public sealed record DeleteConfigCommand(Guid Id) : ICommand;

/// <summary>更新参数配置命令（键名可变更，新旧键缓存均失效；内置参数可编辑）。</summary>
/// <param name="Id">参数 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UpdateConfigCommand(Guid Id, ConfigUpdateRequest Request) : ICommand;
