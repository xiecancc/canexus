
using System.Text.RegularExpressions;
using Domain.Entities;
using Domain.Shared.Enums;
using Infrastructure.Repositories;
using SqlSugar;

namespace Infrastructure.Tests;

/// <summary>
/// Outbox 多副本认领测试（P1-B）：真实 MySQL 临时库验证并发认领唯一性、租约超时重新认领、
/// 退避重试与死信路径、迟到更新不覆盖新认领者。
/// <para>临时库（super_admin_outbox_test_&lt;guid&gt;）测试结束后 DROP；表显式 ENGINE=InnoDB，
/// 与生产行锁语义对齐（本服务器默认引擎为 MyISAM）。</para>
/// </summary>
[TestFixture]
public class OutboxClaimTests
{
    private string _tempDatabaseName = null!;
    private string _tempConnection = null!;
    private SqlSugarScope _client = null!;

    /// <summary>一次性初始化：建独立临时库并创建 Outbox 表（含 P1-B 新增认领字段）。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = TestDatabaseSupport.TestConnectionString;
        _tempDatabaseName = $"super_admin_outbox_test_{Guid.NewGuid():N}";
        _tempConnection = Regex.Replace(connectionString, "(?i)database=[^;]+", $"database={_tempDatabaseName}");

        var adminConnection = Regex.Replace(connectionString, "(?i)database=[^;]+", "database=super_admin_test");
        using (var adminClient = new SqlSugarScope(CreateConnectionConfig(adminConnection)))
        {
            await adminClient.Ado.ExecuteCommandAsync("CREATE DATABASE IF NOT EXISTS `" + _tempDatabaseName + "`");
        }

        _client = new SqlSugarScope(CreateConnectionConfig(_tempConnection));
        _client.CodeFirst.InitTables<OutboxMessage>();
        await _client.Ado.ExecuteCommandAsync("ALTER TABLE sys_outbox_message ENGINE=InnoDB");
    }

    /// <summary>每个测试前清空消息表：同一夹具内共享临时库，避免前一用例残留记录干扰批次断言。</summary>
    [SetUp]
    public async Task ResetMessagesAsync()
    {
        await _client.Ado.ExecuteCommandAsync("TRUNCATE TABLE sys_outbox_message");
    }

    /// <summary>并发两个 Worker 同时认领同一批消息：每条消息只被一个 Worker 认领（不会重复投递）。</summary>
    [Test]
    public async Task TwoWorkers_ClaimSameBatch_EachMessageClaimedOnce()
    {
        const int total = 30;
        var messages = Enumerable.Range(0, total)
            .Select(_ => OutboxMessage.Create("Test.Event", "{}"))
            .ToList();
        await _client.Insertable(messages).ExecuteCommandAsync();

        var workerA = CreateRepository();
        var workerB = CreateRepository();
        var results = await Task.WhenAll(
            workerA.ClaimBatchAsync(total, "worker-a", TimeSpan.FromMinutes(1)),
            workerB.ClaimBatchAsync(total, "worker-b", TimeSpan.FromMinutes(1)));

        var claimed = results.SelectMany(batch => batch).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(claimed, Has.Count.EqualTo(total), "两个 Worker 的认领结果之合并应覆盖全部消息且无重复");
            Assert.That(claimed.Select(message => message.Id).Distinct().Count(), Is.EqualTo(total), "每条消息只能被一个 Worker 认领");
        });

        var ids = messages.Select(message => message.Id).ToList();
        var persisted = await _client.Queryable<OutboxMessage>().Where(message => ids.Contains(message.Id)).ToListAsync();
        Assert.That(persisted.All(message => message.Status == OutboxMessageStatus.Processing), Is.True, "认领成功后状态应为处理中");
        Assert.That(persisted.All(message => message.ClaimOwner is "worker-a" or "worker-b"), Is.True, "每条消息应记录唯一认领者");
        Assert.That(persisted.All(message => message.LeaseUntil is not null), Is.True, "认领时应写入租约截止时间");
    }

    /// <summary>租约超时后可被其他 Worker 重新认领；旧认领者迟到的完成更新不得覆盖新认领者结果。</summary>
    [Test]
    public async Task ExpiredLease_CanBeReclaimed_AndStaleUpdateIgnored()
    {
        var message = OutboxMessage.Create("Test.Event", "{}");
        await _client.Insertable(message).ExecuteCommandAsync();

        var workerA = CreateRepository();
        var first = await workerA.ClaimBatchAsync(1, "worker-a", TimeSpan.FromSeconds(1));
        Assert.That(first, Has.Count.EqualTo(1), "首次认领应成功");

        await Task.Delay(TimeSpan.FromSeconds(2));

        var workerB = CreateRepository();
        var second = await workerB.ClaimBatchAsync(1, "worker-b", TimeSpan.FromMinutes(1));
        Assert.That(second, Has.Count.EqualTo(1));
        Assert.That(second[0].ClaimOwner, Is.EqualTo("worker-b"), "租约到期后应由新 Worker 重新认领");

        // 旧认领者迟到更新：带 ClaimOwner 条件，应被忽略
        await workerA.MarkProcessedAsync(message.Id, "worker-a", DateTime.UtcNow);

        var row = await _client.Queryable<OutboxMessage>().Where(it => it.Id == message.Id).FirstAsync();
        Assert.Multiple(() =>
        {
            Assert.That(row.Status, Is.EqualTo(OutboxMessageStatus.Processing), "旧认领者不应把新认领中的消息标记为已处理");
            Assert.That(row.ClaimOwner, Is.EqualTo("worker-b"));
            Assert.That(row.ProcessedAt, Is.Null);
        });
    }

    /// <summary>重复认领同一消息：同 owner 幂等成功，其他 owner 直接跳过（事件 Id 即幂等键）。</summary>
    [Test]
    public async Task TryClaim_DuplicateOwnerIsIdempotent_OtherOwnerSkips()
    {
        var message = OutboxMessage.Create("Test.Event", "{}");
        await _client.Insertable(message).ExecuteCommandAsync();

        var repository = CreateRepository();
        Assert.That(await repository.TryClaimAsync(message.Id, "worker-a", TimeSpan.FromMinutes(1)), Is.True, "首次认领应成功");
        Assert.That(await repository.TryClaimAsync(message.Id, "worker-a", TimeSpan.FromMinutes(1)), Is.True, "同 owner 重复认领应幂等成功");
        Assert.That(await repository.TryClaimAsync(message.Id, "worker-b", TimeSpan.FromMinutes(1)), Is.False, "其他 owner 在租约内应跳过");
    }

    /// <summary>失败按退避重试：未到退避时间不会被认领，到期后可再次认领；达到上限进入死信（Failed）。</summary>
    [Test]
    public async Task Failure_BackoffThenRetry_AndReachDeadLetter()
    {
        var message = OutboxMessage.Create("Test.Event", "{}");
        await _client.Insertable(message).ExecuteCommandAsync();

        var workerA = CreateRepository();
        var claimed = await workerA.ClaimBatchAsync(1, "worker-a", TimeSpan.FromMinutes(1));
        Assert.That(claimed, Has.Count.EqualTo(1));

        await workerA.RecordFailureAsync(message.Id, "worker-a", "投递失败", 1, 3, DateTime.UtcNow.AddSeconds(1));

        var row = await _client.Queryable<OutboxMessage>().Where(it => it.Id == message.Id).FirstAsync();
        Assert.Multiple(() =>
        {
            Assert.That(row.Status, Is.EqualTo(OutboxMessageStatus.Pending), "未达上限应回到待重试");
            Assert.That(row.AttemptCount, Is.EqualTo(1));
        });

        // 退避未到期：其他 Worker 不应认领
        var workerB = CreateRepository();
        Assert.That(await workerB.ClaimBatchAsync(1, "worker-b", TimeSpan.FromMinutes(1)), Has.Count.EqualTo(0), "退避期内不应被认领");

        await Task.Delay(TimeSpan.FromSeconds(2));
        var retried = await workerB.ClaimBatchAsync(1, "worker-b", TimeSpan.FromMinutes(1));
        Assert.That(retried, Has.Count.EqualTo(1), "退避到期后应可重新认领");

        // 第 3 次失败达到上限（3）：进入死信
        await workerB.RecordFailureAsync(message.Id, "worker-b", "投递失败", 3, 3, DateTime.UtcNow.AddMinutes(1));
        row = await _client.Queryable<OutboxMessage>().Where(it => it.Id == message.Id).FirstAsync();
        Assert.Multiple(() =>
        {
            Assert.That(row.Status, Is.EqualTo(OutboxMessageStatus.Failed), "达到重试上限应进入死信");
            Assert.That(row.NextRetryAt, Is.Null, "死信不应再有下次重试时间");
        });
    }

    /// <summary>一次性清理：删除临时库。</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        try
        {
            _client.Dispose();
        }
        catch
        {
            // 初始化失败时客户端可能未创建，清理失败不影响测试结果
        }

        try
        {
            var adminConnection = Regex.Replace(
                TestDatabaseSupport.TestConnectionString, "(?i)database=[^;]+", "database=super_admin_test");
            using var adminClient = new SqlSugarScope(CreateConnectionConfig(adminConnection));
            await adminClient.Ado.ExecuteCommandAsync("DROP DATABASE IF EXISTS `" + _tempDatabaseName + "`");
        }
        catch
        {
            // 初始化失败时临时库可能不存在，清理失败不影响测试结果
        }
    }

    /// <summary>创建独立仓储实例（模拟不同副本）。</summary>
    /// <returns>Outbox 仓储。</returns>
    private OutboxMessageRepository CreateRepository() => new(new SqlSugarScope(CreateConnectionConfig(_tempConnection)));

    /// <summary>构造与生产一致（SqlSugarClientFactory 口径）的连接配置。</summary>
    /// <param name="connectionString">连接串。</param>
    /// <returns>连接配置。</returns>
    private static ConnectionConfig CreateConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (property.PropertyType == typeof(string) || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                    {
                        column.IsNullable = true;
                    }
                }
            }
        };
    }
}
