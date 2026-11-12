using Infrastructure.Scheduling;

namespace Infrastructure.Tests.Scheduling;

/// <summary>
/// P2-F2 作业参数契约回归：按白名单作业类型校验「该作业认识的参数键」——未知键 / 值不可解析
/// 返回可直接展示的问题清单；全部白名单作业都必须登记契约（防止新增作业遗漏契约声明）。
/// </summary>
[TestFixture]
public class JobParameterContractsTests
{
    /// <summary>合法参数（保留作业的正确键与可解析值）返回空问题清单。</summary>
    [Test]
    public void ValidParameters_ReturnNoProblems()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                JobParameterContracts.Validate(JobTypeResolver.LogRetentionJobKey,
                    new Dictionary<string, string> { [LogRetentionJob.RetentionDaysParameter] = "90" }),
                Is.Empty);
            Assert.That(JobParameterContracts.Validate(JobTypeResolver.NoOpJobKey, new Dictionary<string, string>()), Is.Empty);
            Assert.That(
                JobParameterContracts.Validate(JobTypeResolver.RefreshTokenCleanupJobKey, new Dictionary<string, string>()),
                Is.Empty);
        });
    }

    /// <summary>保留作业的未知键与不可解析值分别产生对应问题说明。</summary>
    [Test]
    public void UnknownKey_AndUnparsableValue_AreReported()
    {
        var problems = JobParameterContracts.Validate(
            JobTypeResolver.LogRetentionJobKey,
            new Dictionary<string, string> { ["Foo"] = "1", [LogRetentionJob.RetentionDaysParameter] = "abc" });

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Count.EqualTo(2));
            Assert.That(problems[0], Does.Contain("不认识参数键「Foo」"));
            Assert.That(problems[1], Does.Contain("须为整数"));
        });
    }

    /// <summary>无参作业（noop / refresh-token-cleanup）的任何参数键都视为未知键。</summary>
    [Test]
    public void ParameterlessJobs_RejectAnyKey()
    {
        var problems = JobParameterContracts.Validate(
            JobTypeResolver.RefreshTokenCleanupJobKey,
            new Dictionary<string, string> { ["RetentionDays"] = "30" });

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("不认识参数键「RetentionDays」"));
    }

    /// <summary>架构守卫：全部白名单作业都必须登记参数契约，防止新增作业时遗漏声明。</summary>
    [Test]
    public void AllAllowedJobTypes_AreCoveredByContracts()
    {
        var allowed = JobTypeResolver.AllowedKeys;
        var unregistered = allowed.Where(key => !IsContractRegistered(key)).ToList();

        Assert.That(unregistered, Is.Empty,
            $"白名单作业 {string.Join(" / ", unregistered)} 未登记参数契约（JobParameterContracts.Contracts），新增作业须同步登记");
    }

    /// <summary>判定作业类型是否已登记契约（经 Validate 对未知键的报错文案间接探测：已登记的作业会报「不认识参数键」）。</summary>
    /// <param name="jobType">作业类型稳定标识。</param>
    /// <returns>已登记返回 true。</returns>
    private static bool IsContractRegistered(string jobType)
    {
        var problems = JobParameterContracts.Validate(jobType, new Dictionary<string, string> { ["__probe__"] = "1" });
        return problems.Count == 1 && problems[0].Contains("不认识参数键「__probe__」");
    }
}
