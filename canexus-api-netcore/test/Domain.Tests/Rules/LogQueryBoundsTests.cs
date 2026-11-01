
using Domain.Services.Rules;
using NUnit.Framework;

namespace Domain.Tests.Rules;
/// <summary>日志查询时间边界纯逻辑单元测试（B8）。</summary>
[TestFixture]
public class LogQueryBoundsTests
{
    [Test]
    public void NormalizeEndTime_Null_ReturnsNull()
    {
        Assert.That(LogQueryBounds.NormalizeEndTime(null), Is.Null);
    }

    [Test]
    public void NormalizeEndTime_DateOnly_ExtendsToNextDayMidnight()
    {
        var result = LogQueryBounds.NormalizeEndTime(new DateTime(2026, 10, 27));

        Assert.That(result, Is.EqualTo(new DateTime(2026, 10, 28)), "yyyy-MM-dd 结束时间应含当日（外推至次日零点）");
    }

    [Test]
    public void NormalizeEndTime_WithTime_KeepsAsIs()
    {
        var value = new DateTime(2026, 10, 27, 12, 30, 0);

        Assert.That(LogQueryBounds.NormalizeEndTime(value), Is.EqualTo(value), "带时刻的结束时间不外推");
    }
}
