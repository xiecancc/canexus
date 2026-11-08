using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Domain.Shared.Results;

namespace Domain.Shared.Localization;

/// <summary>
/// 错误消息本地化资源的启动期完整性校验器（与 <see cref="ErrorMessageLocalizer"/> 共用同一套嵌入资源）。
/// <para>以 <see cref="ErrorCodes"/> 中全部 <c>public const int</c> 常量为基准，逐一核对
/// <c>zh-CN</c> / <c>en-US</c> 两份 JSON 资源：每个错误码都须有非空文案、不得出现无对应常量的多余键、
/// 不得出现重复键或重复的错误码数值。任何缺口都在宿主启动前汇总为一条
/// <see cref="InvalidOperationException"/>，避免 en-US 请求静默回退到中文默认资源文案。</para>
/// <para>嵌入资源逻辑名与 <see cref="ErrorMessageLocalizer"/> 一致：
/// <c>Domain.Shared.Localization.ErrorMessages_{culture}.json</c>，故读取时固定使用本类型所在程序集
/// <c>typeof(ErrorMessageResourceValidator).Assembly</c>，不依赖 <c>GetExecutingAssembly</c> 的调用方语义。</para>
/// </summary>
public static class ErrorMessageResourceValidator
{
    private const string ResourceNamePrefix = "Domain.Shared.Localization.ErrorMessages_";
    private const string KeyPrefix = "Error_";

    // 受检文化集合：zh-CN 为默认资源，en-US 为英文资源
    private static readonly string[] Cultures = ["zh-CN", "en-US"];

    /// <summary>
    /// 启动期校验：资源存在缺失、空值、多余键或重复键时抛出 <see cref="InvalidOperationException"/> 阻止宿主启动。
    /// </summary>
    /// <exception cref="InvalidOperationException">资源未通过完整性校验时抛出，消息按错误码数值升序列出全部问题。</exception>
    public static void Validate()
    {
        var problems = FindProblems();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "错误消息本地化资源完整性校验失败：\n" + string.Join("\n", problems));
        }
    }

    /// <summary>
    /// 返回当前两份嵌入资源相对 <see cref="ErrorCodes"/> 常量集合的全部问题（按错误码数值升序）。
    /// </summary>
    /// <returns>问题描述列表；资源完整时为空集合（供启动期校验与单元测试复用）。</returns>
    public static IReadOnlyList<string> FindProblems()
    {
        var issues = new List<(int Order, string Text)>();

        var expected = CollectExpectedCodes(issues);

        foreach (var culture in Cultures)
        {
            var table = LoadResource(culture, issues);
            if (table is null)
            {
                continue;
            }

            // 正向：每个错误码常量都须有非空文案
            foreach (var (code, name) in expected.OrderBy(pair => pair.Key))
            {
                var key = $"{KeyPrefix}{code}";
                if (!table.TryGetValue(key, out var value))
                {
                    issues.Add((code, $"[{culture}] 缺失键 {key}（{name}）"));
                }
                else if (string.IsNullOrWhiteSpace(value))
                {
                    issues.Add((code, $"[{culture}] 键 {key}（{name}）值为空"));
                }
            }

            // 反向：资源中不得出现无对应常量的多余键、非法键或同一数值重复
            var seenCodes = new Dictionary<int, string>();
            foreach (var key in table.Keys.OrderBy(key => key, StringComparer.Ordinal))
            {
                if (!TryReadCode(key, out var code))
                {
                    issues.Add((int.MaxValue, $"[{culture}] 非法键 {key}"));
                    continue;
                }

                if (!expected.ContainsKey(code))
                {
                    issues.Add((code, $"[{culture}] 多余键 {key}（无对应错误码常量）"));
                    continue;
                }

                if (seenCodes.TryGetValue(code, out var first))
                {
                    issues.Add((code, $"[{culture}] 同一错误码数值重复：{first} 与 {key}"));
                }
                else
                {
                    seenCodes[code] = key;
                }
            }
        }

        return issues
            .OrderBy(issue => issue.Order)
            .ThenBy(issue => issue.Text, StringComparer.Ordinal)
            .Select(issue => issue.Text)
            .ToArray();
    }

    // 反射取出 ErrorCodes 中全部 public const int 常量（数值 → 常量名）；同时报告重复数值
    private static Dictionary<int, string> CollectExpectedCodes(List<(int Order, string Text)> issues)
    {
        var expected = new Dictionary<int, string>();
        foreach (var field in typeof(ErrorCodes)
                     .GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(field => field.FieldType == typeof(int) && field.IsLiteral))
        {
            var code = (int)field.GetValue(null)!;
            if (!expected.TryAdd(code, field.Name))
            {
                issues.Add((code, $"错误码数值重复：{code}（{expected[code]} 与 {field.Name}）"));
            }
        }

        return expected;
    }

    // 读取指定文化的嵌入资源；用 JsonDocument 逐条枚举以精确识别重复键与非字符串值
    private static Dictionary<string, string>? LoadResource(string cultureName, List<(int Order, string Text)> issues)
    {
        var resourceName = $"{ResourceNamePrefix}{cultureName}.json";
        using var stream = typeof(ErrorMessageResourceValidator).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            issues.Add((int.MinValue, $"[{cultureName}] 嵌入资源缺失：{resourceName}"));
            return null;
        }

        using var reader = new StreamReader(stream!);
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add((int.MinValue, $"[{cultureName}] 资源根节点须为 JSON 对象"));
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var key = property.Name;
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : string.Empty;

                if (!table.TryAdd(key, value))
                {
                    issues.Add((ReadOrder(key), $"[{cultureName}] 重复键 {key}"));
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    issues.Add((ReadOrder(key), $"[{cultureName}] 键 {key} 的值不是字符串"));
                }
            }
        }
        catch (JsonException exception)
        {
            issues.Add((int.MinValue, $"[{cultureName}] 资源 JSON 解析失败：{exception.Message}"));
            return null;
        }

        return table;
    }

    // 将 Error_{code} 解析为错误码数值；非法格式返回 false
    private static bool TryReadCode(string key, out int code)
    {
        code = 0;
        return key.StartsWith(KeyPrefix, StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(KeyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out code);
    }

    // 供问题排序：合法键取错误码数值，非法键排到末尾
    private static int ReadOrder(string key) => TryReadCode(key, out var code) ? code : int.MaxValue;
}
