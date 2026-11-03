
using Domain.Services.Rules;
using NUnit.Framework;

namespace Domain.Tests.Rules;
/// <summary>日志查询时间边界纯逻辑单元测试（B8）。</summary>
[TestFixture]
public class LogQueryBoundsTests
{
    /// <summary>验证结束时间为 null 时归一化结果仍为 null。</summary>
    [Test]
    public void NormalizeEndTime_Null_ReturnsNull()
    {
        Assert.That(LogQueryBounds.NormalizeEndTime(null), Is.Null);
    }

    /// <summary>验证纯日期（yyyy-MM-dd）的结束时间外推至次日零点以包含当日。</summary>
    [Test]
    public void NormalizeEndTime_DateOnly_ExtendsToNextDayMidnight()
    {
        var result = LogQueryBounds.NormalizeEndTime(new DateTime(2026, 10, 27));

        Assert.That(result, Is.EqualTo(new DateTime(2026, 10, 28)), "yyyy-MM-dd 结束时间应含当日（外推至次日零点）");
    }

    /// <summary>验证带具体时刻的结束时间保持原值不外推。</summary>
    [Test]
    public void NormalizeEndTime_WithTime_KeepsAsIs()
    {
        var value = new DateTime(2026, 10, 27, 12, 30, 0);

        Assert.That(LogQueryBounds.NormalizeEndTime(value), Is.EqualTo(value), "带时刻的结束时间不外推");
    }
}
