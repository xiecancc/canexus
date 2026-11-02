
using Application.Contracts.Abstractions;
using Application.Contracts.Dtos.Common;
using FluentValidation;

namespace Application.Validators.Common;
/// <summary>
/// 写命令验证器桥接基类：将对命令 Request 载荷的字段校验委托给请求验证器，
/// 创建 / 更新命令的共用字段规则只需在请求验证器中定义一次。
/// <para>命令 record 继承 <see cref="AggregateCreateCommand{TRequest}"/>（或
/// <see cref="AggregateUpdateCommand{TRequest}"/>），两者均继承 <see cref="AggregateCommand{TRequest}"/>
/// 命令根（载荷经 <see cref="AggregateCommand{TRequest}.Request"/> 暴露）；
/// 仅创建方可用的字段规则在子类构造函数中补充。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型。</typeparam>
/// <typeparam name="TRequest">请求载荷类型。</typeparam>
public abstract class WriteCommandValidator<TCommand, TRequest> : AbstractValidator<TCommand>
    where TCommand : AggregateCommand<TRequest>
    where TRequest : AggregateInputRequest
{
    /// <summary>构造函数：装配「命令 → 请求载荷」的委托校验规则。</summary>
    /// <param name="requestValidator">请求载荷验证器（承接共用字段规则）。</param>
    protected WriteCommandValidator(AbstractValidator<TRequest> requestValidator)
    {
        RuleFor(command => command.Request).SetValidator(requestValidator);
    }
}

/// <summary>
/// 更新命令验证器桥接基类：在 <see cref="WriteCommandValidator{TCommand, TRequest}"/> 的载荷字段校验之上，
/// 补充乐观锁令牌的入口兜底校验——版本号由仓储层做框架级比对，但空令牌（<see cref="Guid.Empty"/>）
/// 会让比对语义失效，故在管道入口直接拒绝。
/// </summary>
/// <typeparam name="TCommand">更新命令类型（须继承 <see cref="AggregateUpdateCommand{TRequest}"/>）。</typeparam>
/// <typeparam name="TRequest">请求载荷类型。</typeparam>
public abstract class UpdateCommandValidator<TCommand, TRequest> : WriteCommandValidator<TCommand, TRequest>
    where TCommand : AggregateUpdateCommand<TRequest>
    where TRequest : AggregateInputRequest
{
    /// <summary>构造函数：装配载荷校验与乐观锁版本号非空校验。</summary>
    /// <param name="requestValidator">请求载荷验证器（承接共用字段规则）。</param>
    protected UpdateCommandValidator(AbstractValidator<TRequest> requestValidator) : base(requestValidator)
    {
        // 乐观锁令牌入口兜底：Version 为值类型（不存在 null），仅需拒绝默认空值 Guid.Empty
        RuleFor(command => command.Version).NoEmptyRule("乐观锁版本号不能为空，请回传详情 / 列表接口返回的 Version");
    }
}

/// <summary>
/// 删除命令验证器基类：删除命令不携带请求载荷，仅校验删除令牌（聚合 id 与乐观锁版本号）中的版本号非空，
/// 防止空令牌绕过仓储层的版本比对语义。
/// </summary>
/// <typeparam name="TCommand">删除命令类型（须继承 <see cref="AggregateDeleteCommand"/>）。</typeparam>
public abstract class DeleteCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : AggregateDeleteCommand
{
    /// <summary>构造函数：装配乐观锁版本号非空校验。</summary>
    protected DeleteCommandValidator()
    {
        RuleFor(command => command.Version).NoEmptyRule("乐观锁版本号不能为空，请回传详情 / 列表接口返回的 Version");
    }
}
