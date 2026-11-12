using Domain.Shared.Results;
using Microsoft.AspNetCore.Mvc;

namespace API.Extensions;

/// <summary>
/// 统一响应包装扩展（P2-A4）：收敛控制器层 <c>Ok(Result.Ok(...))</c> 双层包装样板。
/// <para>控制器侧一行 <c>this.OkData(...)</c> 即完成「HTTP 200 + 统一 Result 信封」包装，
/// 成功文案可省略（默认「操作成功」）；语义与逐处手写完全一致，仅消除样板。</para>
/// </summary>
public static class ResultControllerExtensions
{
    /// <summary>以 HTTP 200 返回统一成功响应：业务码 200，携带业务数据与成功文案。</summary>
    /// <typeparam name="T">业务数据类型。</typeparam>
    /// <param name="controller">控制器实例（扩展入口）。</param>
    /// <param name="data">业务数据（写入统一响应 Data 字段）。</param>
    /// <param name="message">成功文案（缺省「操作成功」）。</param>
    /// <returns>HTTP 200 统一响应。</returns>
    public static IActionResult OkData<T>(this ControllerBase controller, T data, string message = "操作成功")
        => controller.Ok(Result.Ok(data, message));
}
