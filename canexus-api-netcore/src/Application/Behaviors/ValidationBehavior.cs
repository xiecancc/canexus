
using Application.Contracts.Abstractions;
using FluentValidation;
using MediatR;

namespace Application.Behaviors;
/// <summary>
/// 验证管道行为：在请求进入处理器前执行注册的全部 <c>IValidator&lt;TRequest&gt;</c>。
/// <para>校验失败时抛出 <see cref="ValidationException"/>，由全局异常处理中间件
/// 转换为 HTTP 400 + 业务错误码 CommonValidationFailed 的统一响应。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="validators">当前请求匹配的全部验证器（来自 Validators 目录的程序集扫描注册）。</param>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // MediatR 12.4.1 的 RequestHandlerDelegate 不带令牌参数，令牌由 Send 在构建管道时捕获；
        // 此处显式检查，保证取消在进入后续行为 / 处理器前即中止（不白跑校验与事务）
        cancellationToken.ThrowIfCancellationRequested();
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken))))
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
