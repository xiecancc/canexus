namespace Application.Behaviors;

using Application.Contracts.Abstractions;
using FluentValidation;
using MediatR;

/// <summary>
/// 验证管道行为：在请求进入处理器前执行注册的全部 <c>IValidator&lt;TRequest&gt;</c>。
/// <para>校验失败时抛出 <see cref="ValidationException"/>，由全局异常处理中间件
/// 转换为 HTTP 400 + 业务错误码 CommonValidationFailed 的统一响应。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>构造函数。</summary>
    /// <param name="validators">当前请求匹配的全部验证器（来自 Validators 目录的程序集扫描注册）。</param>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
