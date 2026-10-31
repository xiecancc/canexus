
using Domain.Shared.Enums;

namespace API.Logging;
/// <summary>
/// 操作日志特性：标注在需要记录操作日志的 Action 上，声明模块名与业务类型。
/// <para>由全局操作日志过滤器（OperLogActionFilter）解析：请求前后记录参数（脱敏）、
/// 成功失败与异常消息、耗时、IP 与操作人，写入 sys_oper_log。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="module">操作模块名。</param>
/// <param name="businessType">业务类型。</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class OperLogAttribute(string module, OperBusinessType businessType) : Attribute
{
    /// <summary>操作模块名（如“用户管理”）。</summary>
    public string Module { get; } = module;

    /// <summary>业务类型（同字典 sys_oper_type）。</summary>
    public OperBusinessType BusinessType { get; } = businessType;
}
