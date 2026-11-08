using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Domain.Shared.Localization;

/// <summary>
/// 错误消息本地化器（轻量 JSON 资源实现，替代 resx + WinForms 资源工具链）。
/// <para>资源约定：<c>Localization/ErrorMessages.{culture}.json</c>（嵌入资源），
/// 键为 <c>Error_{错误码}</c>（如 Error_40005）。
/// 查找顺序：默认文化（zh）直接返回调用方原始文案（保留动态诊断细节）；
/// 非默认文化按 请求文化 → 语言回退 → zh-CN 默认资源 → 原始文案兜底。</para>
/// </summary>
public static class ErrorMessageLocalizer
{
    /// <summary>各文化的资源字典缓存（culture 名 → 键值表），首次访问加载后常驻。</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>缓存加载锁（双检锁：多请求并发首次访问只加载一次）。</summary>
    private static readonly object LoadLock = new();

    /// <summary>本地化指定错误码的消息。</summary>
    /// <param name="errorCode">业务错误码。</param>
    /// <param name="fallback">未收录错误码时的兜底消息（通常是异常携带的原始文案）。</param>
    /// <returns>本地化后的消息；无可用翻译时返回兜底消息。</returns>
    public static string Localize(int errorCode, string fallback)
    {
        return Localize(CultureInfo.CurrentUICulture, errorCode, fallback);
    }

    /// <summary>按指定文化本地化错误码消息（供测试与特殊场景显式指定文化）。</summary>
    /// <remarks>
    /// 默认文化（zh）直接返回调用方原始文案而非资源表文本：部分错误码的消息携带动态细节
    /// （如「不支持的文件类型：'.exe'（允许的扩展名：…）」「内容签名与声明扩展名不符」），
    /// 资源表是静态文案，覆盖后会丢失排查所需的上下文；原始文案本就以中文书写，
    /// 即为默认语言的正确呈现。非默认文化按 资源 → zh 资源 → 原始文案 回退，
    /// 保证 en-US 等语言始终有英文呈现（EX-01 本地化覆盖缺口的目标即在此）。
    /// </remarks>
    /// <param name="culture">目标文化（取 CurrentUICulture，由请求本地化中间件按 Accept-Language 设置）。</param>
    /// <param name="errorCode">业务错误码。</param>
    /// <param name="fallback">兜底消息。</param>
    /// <returns>本地化后的消息。</returns>
    public static string Localize(CultureInfo culture, int errorCode, string fallback)
    {
        if (string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        var key = $"Error_{errorCode}";

        // 精确文化 → 语言回退（zh-CN 匹配 zh 资源不存在时）→ 默认资源 → 兜底
        foreach (var name in new[] { culture.Name, culture.TwoLetterISOLanguageName })
        {
            if (TryGetMessage(name, key, out var message))
            {
                return message;
            }
        }

        return TryGetMessage("zh-CN", key, out var default_) ? default_ : fallback;
    }

    /// <summary>从缓存 / 嵌入资源加载指定文化的键值表并查询。</summary>
    /// <param name="cultureName">文化名（如 zh-CN / en-US）。</param>
    /// <param name="key">资源键。</param>
    /// <param name="message">命中的消息。</param>
    /// <returns>命中返回 true。</returns>
    private static bool TryGetMessage(string cultureName, string key, out string message)
    {
        lock (LoadLock)
        {
            if (!Cache.TryGetValue(cultureName, out var table))
            {
                table = LoadResource(cultureName);
                Cache[cultureName] = table!;
            }

            if (table is not null && table.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                message = value;
                return true;
            }

            message = string.Empty;
            return false;
        }
    }

    /// <summary>从程序集嵌入资源加载指定文化的 JSON 键值表。</summary>
    /// <param name="cultureName">文化名。</param>
    /// <returns>键值表；资源不存在返回 null（保留默认资源回退机会）。</returns>
    private static Dictionary<string, string>? LoadResource(string cultureName)
    {
        // 嵌入资源逻辑名：RootNamespace + 目录 + 文件名（下划线分隔文化，避免被 MSBuild 判定为卫星资源文化后缀）
        var resourceName = $"Domain.Shared.Localization.ErrorMessages_{cultureName}.json";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
    }
}
