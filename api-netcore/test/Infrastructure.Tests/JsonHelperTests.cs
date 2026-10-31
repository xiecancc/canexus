
using Domain.Shared.Json;
using NUnit.Framework;

namespace Infrastructure.Tests;
/// <summary>JsonHelper 单元测试：统一配置行为（camelCase、中文不转义、忽略大小写反序列化）与容错解析。</summary>
[TestFixture]
public class JsonHelperTests
{
    private sealed class Sample
    {
        public string UserName { get; set; } = string.Empty;
        public int Age { get; set; }
        public string? Remark { get; set; }
    }

    [Test]
    public void Serialize_UsesCamelCaseAndKeepsChineseReadable()
    {
        var json = JsonHelper.Serialize(new Sample { UserName = "管理员", Age = 20 });

        Assert.That(json, Does.Contain("\"userName\""), "属性名应为 camelCase");
        Assert.That(json, Does.Contain("管理员"), "中文应保持原文不转义为 \\uXXXX");
        Assert.That(json, Does.Not.Contain("\\u"));
    }

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

    [Test]
    public void Serialize_ObjectTypeOverload_SerializesByRuntimeShape()
    {
        var json = JsonHelper.Serialize(new Sample { UserName = "typed" }, typeof(Sample));

        Assert.That(json, Does.Contain("\"userName\":\"typed\""));
    }

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
