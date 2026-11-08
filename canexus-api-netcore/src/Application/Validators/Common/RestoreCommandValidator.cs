
using Application.Contracts.Abstractions;
using FluentValidation;

namespace Application.Validators.Common;
/// <summary>
/// 恢复命令验证器基类：恢复不携带请求载荷，校验目标 Id 与乐观锁版本号非空，
/// 防止空令牌绕过仓储层的 Version CAS 语义（与 <see cref="DeleteCommandValidator{TCommand}"/> 对称）。
/// </summary>
/// <typeparam name="TCommand">恢复命令类型（须继承 <see cref="AggregateRestoreCommand"/>）。</typeparam>
public abstract class RestoreCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : AggregateRestoreCommand
{
    /// <summary>构造函数：装配目标 Id 与乐观锁版本号非空校验。</summary>
    protected RestoreCommandValidator()
    {
        RuleFor(command => command.Id).NoEmptyRule("恢复目标 Id 不能为空");
        RuleFor(command => command.Version).NoEmptyRule("乐观锁版本号不能为空，请回传已删除列表中的 Version");
    }
}
