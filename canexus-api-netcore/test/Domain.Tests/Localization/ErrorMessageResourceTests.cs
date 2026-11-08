using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Domain.Shared.Localization;
using Domain.Shared.Results;

namespace Domain.Tests.Localization;

/// <summary>
/// 错误消息本地化资源（zh-CN / en-US）完整性与启动期校验器回归测试。
/// </summary>
[TestFixture]
public class ErrorMessageResourceTests
{
    private const string ResourceNamePrefix = "Domain.Shared.Localization.ErrorMessages_";
    private const string KeyPrefix = "Error_";
    private const int UnregisteredCode = 999999;

    private static readonly string[] Cultures = ["zh-CN", "en-US"];

    /// <summary>全部错误码常量在 zh-CN / en-US 两份资源中都应有非空文案。</summary>
    [Test]
    public void AllErrorCodes_HaveNonEmptyMessage_InBothCultures()
    {
        var codes = GetExpectedCodes();

        Assert.Multiple(() =>
        {
            foreach (var culture in Cultures)
            {
                var table = LoadResource(culture);
                foreach (var code in codes)
                {
                    var key = $"{KeyPrefix}{code}";
                    Assert.That(table.TryGetValue(key, out var message), Is.True,
                        $"[{culture}] 缺少键 {key}");
                    Assert.That(string.IsNullOrWhiteSpace(message), Is.False,
                        $"[{culture}] 键 {key} 的文案为空");
                }
            }
        });
    }

    /// <summary>当前资源应通过启动期校验：FindProblems 无问题且 Validate 不抛异常。</summary>
    [Test]
    public void Validate_CurrentResources_AreComplete()
    {
        Assert.That(ErrorMessageResourceValidator.FindProblems(), Is.Empty,
            "当前资源相对 ErrorCodes 常量集合不应有任何问题");
        Assert.DoesNotThrow(() => ErrorMessageResourceValidator.Validate());
    }

    /// <summary>两份资源不得出现无对应错误码常量的多余键、非法键或重复键。</summary>
    [Test]
    public void Resources_ContainNoExtraInvalidOrDuplicateKeys()
    {
        var known = GetExpectedCodes().ToHashSet();

        Assert.Multiple(() =>
        {
            foreach (var culture in Cultures)
            {
                var table = LoadResource(culture);
                var extras = table.Keys
                    .Where(key => !IsKnownKey(key, known))
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToArray();
                Assert.That(extras, Is.Empty,
                    $"[{culture}] 存在多余或非法键：{string.Join(", ", extras)}");
                Assert.That(table.Count, Is.EqualTo(known.Count),
                    $"[{culture}] 键数量应与错误码常量数量一致");
            }
        });
    }

    /// <summary>新收录的错误码在 en-US 文化下经 Localize 返回英文文案，而非中文默认资源或调用方兜底。</summary>
    [Test]
    public void Localize_NewlyAddedCode_ReturnsEnglishUnderEnUs()
    {
        const string fallback = "未翻译的原始兜底文案";
        var message = ErrorMessageLocalizer.Localize(
            new CultureInfo("en-US"), ErrorCodes.AuthForbidden, fallback);

        Assert.Multiple(() =>
        {
            Assert.That(message, Is.Not.EqualTo(fallback), "en-US 不应回退到调用方兜底文案");
            Assert.That(message, Does.Contain("permission"), "en-US 应返回英文文案");
            Assert.That(message, Does.Not.Contain("无权限"), "en-US 不应返回中文默认资源文案");
        });
    }

    /// <summary>未收录的错误码仍返回调用方兜底文案，渐进式收录的回退语义不被破坏。</summary>
    [Test]
    public void Localize_UnregisteredCode_ReturnsFallback()
    {
        const string fallback = "调用方原始文案";

        Assert.Multiple(() =>
        {
            foreach (var culture in Cultures)
            {
                Assert.That(
                    ErrorMessageLocalizer.Localize(new CultureInfo(culture), UnregisteredCode, fallback),
                    Is.EqualTo(fallback),
                    $"[{culture}] 未收录错误码应原样返回兜底文案");
            }
        });
    }

    private static IReadOnlyList<int> GetExpectedCodes() =>
        typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(int) && field.IsLiteral)
            .Select(field => (int)field.GetValue(null)!)
            .OrderBy(code => code)
            .ToArray();

    private static Dictionary<string, string> LoadResource(string cultureName)
    {
        var resourceName = $"{ResourceNamePrefix}{cultureName}.json";
        using var stream = typeof(ErrorMessageLocalizer).Assembly.GetManifestResourceStream(resourceName);
        Assert.That(stream, Is.Not.Null, $"嵌入资源 {resourceName} 应存在");
        using var reader = new StreamReader(stream!);
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        Assert.That(table, Is.Not.Null, $"嵌入资源 {resourceName} 应可反序列化为键值表");
        return table!;
    }

    private static bool IsKnownKey(string key, HashSet<int> known) =>
        key.StartsWith(KeyPrefix, StringComparison.Ordinal)
        && int.TryParse(key.AsSpan(KeyPrefix.Length), NumberStyles.None,
            CultureInfo.InvariantCulture, out var code)
        && known.Contains(code);
}
