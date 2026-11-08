using Domain.Shared.Jobs;
using NUnit.Framework;

namespace Infrastructure.Tests.Scheduling;

/// <summary>
/// T5 #4 作业参数模型回归：校验口径必须在「写入侧验证器」与「入调度侧 JobDataMap 装配」一致，
/// 且参数签名不受键顺序影响（否则每次读取数据库都会误判 JobDetail 已变更而反复替换）。
/// </summary>
[TestFixture]
public class JobParametersTests
{
    /// <summary>空参数合法且解析为空表，签名为空串。</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Empty_IsValid_AndParsesToEmpty(string? json)
    {
        Assert.Multiple(() =>
        {
            Assert.That(JobParameters.IsValid(json), Is.True);
            Assert.That(JobParameters.Validate(json), Is.Null);
            Assert.That(JobParameters.Parse(json), Is.Empty);
            Assert.That(JobParameters.Signature(JobParameters.Parse(json)), Is.Empty);
        });
    }

    /// <summary>标量值统一转字符串，数字 / 布尔 / null 不丢失。</summary>
    [Test]
    public void Scalars_AreNormalizedToStrings()
    {
        var parameters = JobParameters.Parse("{\"RetentionDays\":30,\"Enabled\":true,\"Note\":null,\"Mode\":\"safe\"}");

        Assert.Multiple(() =>
        {
            Assert.That(parameters["RetentionDays"], Is.EqualTo("30"));
            Assert.That(parameters["Enabled"], Is.EqualTo("true"));
            Assert.That(parameters["Note"], Is.Empty);
            Assert.That(parameters["Mode"], Is.EqualTo("safe"));
        });
    }

    /// <summary>签名按键名排序，与 JSON 中的写入顺序无关。</summary>
    [Test]
    public void Signature_IsOrderIndependent()
    {
        var first = JobParameters.Signature(JobParameters.Parse("{\"A\":\"1\",\"B\":\"2\"}"));
        var second = JobParameters.Signature(JobParameters.Parse("{\"B\":\"2\",\"A\":\"1\"}"));

        Assert.That(first, Is.EqualTo(second));
    }

    /// <summary>非法参数：非 JSON、非对象、数组/嵌套、保留键、超长键、超长值、键过多。</summary>
    [Test]
    public void InvalidPayloads_AreRejected()
    {
        var invalid = new (string Name, string Json)[]
        {
            ("非 JSON", "not-json"),
            ("顶层数组", "[1,2]"),
            ("嵌套对象", "{\"a\":{\"b\":1}}"),
            ("嵌套数组", "{\"a\":[1]}"),
            ("保留键 jobId", "{\"jobId\":\"x\"}"),
            ("保留键签名", $"{{\"{JobParameters.SignatureKey}\":\"x\"}}"),
            ("空键", "{\"\":\"x\"}"),
            ("超长键", "{\"" + new string('k', JobParameters.MaxKeyLength + 1) + "\":\"x\"}"),
            ("超长值", "{\"a\":\"" + new string('v', JobParameters.MaxValueLength + 1) + "\"}"),
            ("键过多", "{" + string.Join(",", Enumerable.Range(0, JobParameters.MaxKeyCount + 1).Select(i => $"\"k{i}\":1")) + "}")
        };

        foreach (var (name, json) in invalid)
        {
            Assert.Multiple(() =>
            {
                Assert.That(JobParameters.Validate(json), Is.Not.Null, $"{name} 应被拒绝");
                Assert.That(JobParameters.IsValid(json), Is.False, $"{name} 应被拒绝");
                Assert.Throws<InvalidOperationException>(() => JobParameters.Parse(json), $"{name} 入调度前须 fail-fast");
            });
        }
    }

    /// <summary>超长 JSON 文本在解析前即被长度规则拦截。</summary>
    [Test]
    public void OversizedJson_IsRejected()
    {
        var oversized = "{\"a\":\"" + new string('x', JobParameters.MaxJsonLength) + "\"}";

        Assert.That(JobParameters.Validate(oversized), Does.Contain("长度"));
    }

    /// <summary>整型参数：缺失、非数字、越界一律回退到默认值，合法值原样返回。</summary>
    [Test]
    public void GetInt_FallsBackOnMissingMalformedOrOutOfRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JobParameters.GetInt(JobParameters.Parse("{\"Days\":15}"), "Days", 30, 1, 365), Is.EqualTo(15));
            Assert.That(JobParameters.GetInt(JobParameters.Parse("{}"), "Days", 30, 1, 365), Is.EqualTo(30));
            Assert.That(JobParameters.GetInt(JobParameters.Parse("{\"Days\":\"abc\"}"), "Days", 30, 1, 365), Is.EqualTo(30));
            Assert.That(JobParameters.GetInt(JobParameters.Parse("{\"Days\":0}"), "Days", 30, 1, 365), Is.EqualTo(30),
                "0 天会清空全部日志，必须回退到默认值");
            Assert.That(JobParameters.GetInt(JobParameters.Parse("{\"Days\":99999}"), "Days", 30, 1, 365), Is.EqualTo(30));
        });
    }
}
