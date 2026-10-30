namespace Application.Contracts.Abstractions;

/// <summary>
/// 请求根接口：一切无返回值 MediatR 请求的公共标记，<see cref="ICommand"/> 的父接口。
/// <para>说明：C# 的 record 只能继承 record，而请求定义为承载不可变数据的 record，
/// 因此请求侧契约采用接口层次（处理器侧采用类继承层次）；
/// 带返回值的泛型变体（ICommand&lt;TResponse&gt; / IQuery&lt;TResponse&gt;）必须直接继承
/// MediatR.IRequest&lt;TResponse&gt;（MediatR.Send 重载决议的硬性要求），无法纳入本层次。</para>
/// </summary>
public interface IRequest : MediatR.IRequest
{
}
