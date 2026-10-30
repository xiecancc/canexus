namespace Application.Contracts.Abstractions;

/// <summary>
/// 查询标记接口：封装一切不改变系统状态的读操作请求（列表 / 详情 / 统计等）。
/// <para>直接继承 MediatR.IRequest&lt;TResponse&gt;：MediatR.Send 的重载决议要求请求接口
/// 直接继承其标记接口，任何自定义中间泛型接口都会导致类型推断失败。</para>
/// </summary>
/// <typeparam name="TResponse">查询返回的数据类型。</typeparam>
public interface IQuery<TResponse> : MediatR.IRequest<TResponse>
{
}
