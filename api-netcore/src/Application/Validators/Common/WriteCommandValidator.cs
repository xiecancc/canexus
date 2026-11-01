
using Application.Contracts.Abstractions;
using FluentValidation;

namespace Application.Validators.Common;
/// <summary>
/// 写命令验证器桥接基类：将对命令 Request 载荷的字段校验委托给请求验证器，
/// 创建 / 更新命令的共用字段规则只需在请求验证器中定义一次。
/// <para>命令 record 继承 <see cref="Command{TRequest}"/>（或其派生
/// CreateCommand / UpdateCommand），子类构造时传入对应请求类型的验证器实例；
/// 仅创建方可用的字段规则在子类构造函数中补充。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型。</typeparam>
/// <typeparam name="TRequest">请求载荷类型。</typeparam>
public abstract class WriteCommandValidator<TCommand, TRequest> : AbstractValidator<TCommand>
    where TCommand : Command<TRequest>
    where TRequest : class
{
    /// <summary>构造函数：装配「命令 → 请求载荷」的委托校验规则。</summary>
    /// <param name="requestValidator">请求载荷验证器（承接共用字段规则）。</param>
    protected WriteCommandValidator(AbstractValidator<TRequest> requestValidator)
    {
        RuleFor(command => command.Request).SetValidator(requestValidator);
    }
}
