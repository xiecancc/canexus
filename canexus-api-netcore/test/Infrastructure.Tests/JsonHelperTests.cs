
using Domain.Shared.Json;
using NUnit.Framework;

namespace Infrastructure.Tests;
/// <summary>JsonHelper 单元测试：统一配置行为（camelCase、中文不转义、忽略大小写反序列化、省略 null、循环引用保护、紧凑输出）与容错解析。</summary>
[TestFixture]
public class JsonHelperTests
{
    private sealed class Sample
    {
        public string UserName { get; set; } = string.Empty;
        public int Age { get; set; }
        public string? Remark { get; set; }
    }

    private sealed class Node
    {
        public string Name { get; set; } = string.Empty;
        public Node? Next { get; set; }
    }

    /// <summary>验证序列化使用 camelCase 且中文保持原文不转义。</summary>
    [Test]
    public void Serialize_UsesCamelCaseAndKeepsChineseReadable()
    {
        var json = JsonHelper.Serialize(new Sample { UserName = "管理员", Age = 20 });

        Assert.That(json, Does.Contain("\"userName\""), "属性名应为 camelCase");
        Assert.That(json, Does.Contain("管理员"), "中文应保持原文不转义为 \\uXXXX");
        Assert.That(json, Does.Not.Contain("\\u"));
    }

    /// <summary>验证 null 属性被省略（减小载荷，反序列化缺省仍为 null）。</summary>
    [Test]
    public void Serialize_OmitsNullProperties()
    {
        var json = JsonHelper.Serialize(new Sample { UserName = "admin", Age = 20, Remark = null });

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("\"remark\""), "null 属性不应写出");
            Assert.That(JsonHelper.Deserialize<Sample>(json)!.Remark, Is.Null, "省略后读回仍为 null");
        });
    }

    /// <summary>验证循环引用被安全断开（回边写 null）而不抛异常。</summary>
    [Test]
    public void Serialize_HandlesCircularReferences()
    {
        var first = new Node { Name = "first" };
        var second = new Node { Name = "second" };
        first.Next = second;
        second.Next = first;

        var json = JsonHelper.Serialize(first);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"first\""));
            Assert.That(json, Does.Contain("\"second\""));
        });
    }

    /// <summary>验证输出为紧凑格式（无缩进与换行）。</summary>
    [Test]
    public void Serialize_ProducesCompactOutput()
    {
        var json = JsonHelper.Serialize(new Sample { UserName = "compact", Age = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("\n"), "不应包含换行");
            Assert.That(json, Does.Not.Contain("\r"), "不应包含回车");
        });
    }

    /// <summary>验证反序列化忽略属性名大小写并兼容字符串数字。</summary>
    [Test]
    public void Deserialize_IsCaseInsensitiveAndSupportsStringNumber()
    {
        var sample = JsonHelper.Deserialize<Sample>("""{"USERNAME":"admin","AGE":"20"}""");

        Assert.Multiple(() =>
        {
            Assert.That(sample, Is.Not.Null);
            Assert.That(sample!.UserName, Is.EqualTo("admin"));
            Assert.That(sample.Age, Is.EqualTo(20), "字符串数字应兼容读取（Web 默认 NumberHandling）");
        });
    }

    /// <summary>验证序列化与反序列化往返后数据保持一致。</summary>
    [Test]
    public void SerializeDeserialize_RoundTripPreservesData()
    {
        var original = new Sample { UserName = "round-trip", Age = 30, Remark = "备注" };

        var restored = JsonHelper.Deserialize<Sample>(JsonHelper.Serialize(original));

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.UserName, Is.EqualTo(original.UserName));
            Assert.That(restored.Age, Is.EqualTo(original.Age));
            Assert.That(restored.Remark, Is.EqualTo(original.Remark));
        });
    }

    /// <summary>验证 object 类型重载按运行时形状序列化。</summary>
    [Test]
    public void Serialize_ObjectTypeOverload_SerializesByRuntimeShape()
    {
        var json = JsonHelper.Serialize(new Sample { UserName = "typed" }, typeof(Sample));

        Assert.That(json, Does.Contain("\"userName\":\"typed\""));
    }

    /// <summary>验证容错反序列化对非法、null 或空白输入返回 false 不抛异常。</summary>
    [Test]
    public void TryDeserialize_ReturnsFalseOnInvalidOrEmptyInput()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JsonHelper.TryDeserialize<Sample>("{invalid", out _), Is.False, "非法 JSON 不抛异常返回 false");
            Assert.That(JsonHelper.TryDeserialize<Sample>(null, out _), Is.False, "null 输入返回 false");
            Assert.That(JsonHelper.TryDeserialize<Sample>("   ", out _), Is.False, "空白输入返回 false");
            Assert.That(JsonHelper.TryDeserialize<Sample>("""{"userName":"ok"}""", out var value), Is.True);
            Assert.That(value!.UserName, Is.EqualTo("ok"));
        });
    }
}
