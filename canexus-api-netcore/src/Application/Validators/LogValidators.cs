
using Application.Contracts.Commands;
using Application.Validators.Common;

namespace Application.Validators;
/// <summary>删除登录日志命令验证器（乐观锁版本号入口兜底；日志为物理删除，版本号不参与比对但拒绝空令牌）。</summary>
public sealed class LoginLogDeleteCommandValidator() : DeleteCommandValidator<LoginLogDeleteCommand>;

/// <summary>删除操作日志命令验证器（乐观锁版本号入口兜底；日志为物理删除，版本号不参与比对但拒绝空令牌）。</summary>
public sealed class OperLogDeleteCommandValidator() : DeleteCommandValidator<OperLogDeleteCommand>;
