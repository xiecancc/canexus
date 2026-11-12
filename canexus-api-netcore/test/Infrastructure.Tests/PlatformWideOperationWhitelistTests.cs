
using Infrastructure.Scheduling;

namespace Infrastructure.Tests;

/// <summary>
/// P1-2 平台级跨租户全表操作白名单回归：清单必须恰好登记 3 处已知操作、不含未知项，
/// 且每项都明确标注「业务语义待核实」，防止清单腐化或悄悄新增未评审的全表操作。
/// </summary>
[TestFixture]
public class PlatformWideOperationWhitelistTests
{
    /// <summary>白名单恰好包含 3 处已登记操作，键集合与常量一致且不含未知项。</summary>
    [Test]
    public void Operations_ContainsExactlyTheThreeRegisteredOperations()
    {
        var expected = new[]
        {
            PlatformWideOperationWhitelist.LogRetention,
            PlatformWideOperationWhitelist.RefreshTokenCleanup,
            PlatformWideOperationWhitelist.TokenVersionRevokeAll
        };

        Assert.Multiple(() =>
        {
            Assert.That(PlatformWideOperationWhitelist.Operations, Has.Count.EqualTo(3),
                "平台级全表操作白名单应恰好 3 项");
            Assert.That(PlatformWideOperationWhitelist.Operations.Keys, Is.EquivalentTo(expected),
                "白名单不得缺少已登记操作，也不得含未知项");
            Assert.That(PlatformWideOperationWhitelist.LogRetention, Is.EqualTo(JobTypeResolver.LogRetentionJobKey),
                "日志保留作业须按 JobKey 关联");
            Assert.That(PlatformWideOperationWhitelist.RefreshTokenCleanup,
                Is.EqualTo(JobTypeResolver.RefreshTokenCleanupJobKey), "刷新令牌回收作业须按 JobKey 关联");
        });
    }

    /// <summary>每项登记理由都明确标注业务语义待核实（避免把「已允许」误读为「已确认」）。</summary>
    [Test]
    public void Operations_EachRationaleMarksBusinessSemanticsPendingVerification()
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in PlatformWideOperationWhitelist.Operations)
            {
                Assert.That(entry.Value, Does.Contain("业务语义待核实"),
                    $"操作 {entry.Key} 的允许理由须标注业务语义待核实");
            }
        });
    }
}
