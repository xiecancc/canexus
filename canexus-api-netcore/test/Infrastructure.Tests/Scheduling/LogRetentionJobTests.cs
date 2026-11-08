using System.Text.RegularExpressions;
using Domain.Entities;
using Domain.Shared.Enums;
using Infrastructure.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using NUnit.Framework;
using SqlSugar;

namespace Infrastructure.Tests.Scheduling;

/// <summary>
/// T5 #5 回归：日志保留作业必须让四类日志表的行数受控（只删过期行、保留期内行不动），
/// 且刷新令牌回收不得删掉「已吊销但尚未过期」的记录（T4 令牌家族重用检测的证据）。
/// <para>在独立临时库上实测，结束 DROP，不污染 super_admin_test。</para>
/// </summary>
[TestFixture]
public class LogRetentionJobTests
{
    private string _database = null!;
    private string _connection = null!;
    private string _adminConnection = null!;
    private SqlSugarScope _client = null!;

    /// <summary>建独立临时库与四类日志表 + 刷新令牌表。</summary>
    [SetUp]
    public async Task SetUpAsync()
    {
        var source = TestDatabaseSupport.TestConnectionString;
        _database = $"super_admin_retention_{Guid.NewGuid():N}";
        _connection = Regex.Replace(source, "(?i)database=[^;]+", $"database={_database}");
        _adminConnection = Regex.Replace(source, "(?i)database=[^;]+", "database=super_admin_test");

        await using (var connection = new MySqlConnection(_adminConnection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_database}`";
            await command.ExecuteNonQueryAsync();
        }

        _client = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = _connection,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
        _client.CodeFirst.InitTables(
            typeof(SysLoginLog),
            typeof(SysOperLog),
            typeof(SysEntityChangeLog),
            typeof(SysJobLog),
            typeof(SysRefreshToken));
    }

    /// <summary>释放临时库。</summary>
    [TearDown]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        await using var connection = new MySqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{_database}`";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>四表各写一条 40 天前与一条 2 小时前的记录，30 天保留策略应只删前者。</summary>
    [Test]
    public async Task CleanAsync_RemovesOnlyExpiredRows_FromAllFourLogTables()
    {
        var old = DateTime.UtcNow.AddDays(-40);
        var recent = DateTime.UtcNow.AddHours(-2);
        SeedLogs(old, recent);

        var job = new LogRetentionJob(_client, NullLogger<LogRetentionJob>.Instance);
        var summary = await job.CleanAsync(30);

        Assert.Multiple(() =>
        {
            Assert.That(summary[nameof(SysLoginLog)], Is.EqualTo(1), "登录日志应只删除过期行");
            Assert.That(summary[nameof(SysOperLog)], Is.EqualTo(1), "操作日志应只删除过期行");
            Assert.That(summary[nameof(SysEntityChangeLog)], Is.EqualTo(1), "字段级变更日志应只删除过期行");
            Assert.That(summary[nameof(SysJobLog)], Is.EqualTo(1), "任务执行日志应只删除过期行");
            Assert.That(_client.Queryable<SysLoginLog>().Count(), Is.EqualTo(1));
            Assert.That(_client.Queryable<SysOperLog>().Count(), Is.EqualTo(1));
            Assert.That(_client.Queryable<SysEntityChangeLog>().Count(), Is.EqualTo(1));
            Assert.That(_client.Queryable<SysJobLog>().Count(), Is.EqualTo(1));
        });
    }

    /// <summary>RetentionDays 越界收敛：0 天不得清空全部日志。</summary>
    [Test]
    public async Task CleanAsync_ClampsInvalidRetentionDays()
    {
        var old = DateTime.UtcNow.AddDays(-40);
        var recent = DateTime.UtcNow.AddHours(-2);
        SeedLogs(old, recent);

        var job = new LogRetentionJob(_client, NullLogger<LogRetentionJob>.Instance);
        await job.CleanAsync(0);

        Assert.That(_client.Queryable<SysLoginLog>().Count(), Is.EqualTo(1),
            "0 天应收敛到最小 1 天，保留期内的行不得被删除");
    }

    /// <summary>刷新令牌回收只删早已过期的行，已吊销但未过期的记录必须保留以供重用检测。</summary>
    [Test]
    public async Task RefreshTokenCleanup_PreservesRevokedButUnexpiredTokens()
    {
        var now = DateTime.UtcNow;
        _client.Insertable(new List<SysRefreshToken>
        {
            new()
            {
                Id = Guid.NewGuid(), Token = "expired-long-ago", UserId = Guid.NewGuid(),
                ExpiresAt = now.AddDays(-40), CreatedAt = now.AddDays(-50)
            },
            new()
            {
                Id = Guid.NewGuid(), Token = "revoked-but-unexpired", UserId = Guid.NewGuid(),
                ExpiresAt = now.AddDays(7), RevokedAt = now.AddDays(-1), CreatedAt = now.AddDays(-8)
            },
            new()
            {
                Id = Guid.NewGuid(), Token = "active", UserId = Guid.NewGuid(),
                ExpiresAt = now.AddDays(7), CreatedAt = now.AddDays(-1)
            }
        }).ExecuteCommand();

        var job = new RefreshTokenCleanupJob(_client, NullLogger<RefreshTokenCleanupJob>.Instance);
        var removed = await job.CleanAsync(7);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(1), "只应回收过期时间早于宽限期截止点的记录");
            Assert.That(_client.Queryable<SysRefreshToken>().Count(), Is.EqualTo(2));
            Assert.That(_client.Queryable<SysRefreshToken>().Any(it => it.Token == "revoked-but-unexpired"), Is.True,
                "已吊销但未过期的记录是令牌家族重用检测的证据，不得回收");
            Assert.That(_client.Queryable<SysRefreshToken>().Any(it => it.Token == "active"), Is.True);
        });
    }

    /// <summary>向四表各插入一条过期与一条保留期内的记录。</summary>
    /// <param name="old">过期时间（40 天前）。</param>
    /// <param name="recent">保留期内时间（2 小时前）。</param>
    private void SeedLogs(DateTime old, DateTime recent)
    {
        _client.Insertable(new SysLoginLog
        {
            Id = Guid.NewGuid(),
            UserName = "old",
            Msg = "old",
            Status = ExecutionStatus.Success,
            CreatedAt = old,
            LoginTime = old
        }).ExecuteCommand();
        _client.Insertable(new SysLoginLog
        {
            Id = Guid.NewGuid(),
            UserName = "recent",
            Msg = "recent",
            Status = ExecutionStatus.Success,
            CreatedAt = recent,
            LoginTime = recent
        }).ExecuteCommand();

        _client.Insertable(new SysOperLog
        {
            Id = Guid.NewGuid(),
            Title = "old",
            Method = "M",
            RequestMethod = "GET",
            RequestUrl = "/x",
            Status = ExecutionStatus.Success,
            CreatedAt = old,
            OperTime = old
        }).ExecuteCommand();
        _client.Insertable(new SysOperLog
        {
            Id = Guid.NewGuid(),
            Title = "recent",
            Method = "M",
            RequestMethod = "GET",
            RequestUrl = "/x",
            Status = ExecutionStatus.Success,
            CreatedAt = recent,
            OperTime = recent
        }).ExecuteCommand();

        _client.Insertable(new SysEntityChangeLog
        {
            Id = Guid.NewGuid(),
            EntityName = "old",
            EntityId = Guid.NewGuid(),
            FieldName = "F",
            CreatedAt = old,
            ChangedAt = old
        }).ExecuteCommand();
        _client.Insertable(new SysEntityChangeLog
        {
            Id = Guid.NewGuid(),
            EntityName = "recent",
            EntityId = Guid.NewGuid(),
            FieldName = "F",
            CreatedAt = recent,
            ChangedAt = recent
        }).ExecuteCommand();

        _client.Insertable(new SysJobLog
        {
            Id = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            JobName = "old",
            Status = ExecutionStatus.Success,
            TriggerTime = old,
            CreatedAt = old
        }).ExecuteCommand();
        _client.Insertable(new SysJobLog
        {
            Id = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            JobName = "recent",
            Status = ExecutionStatus.Success,
            TriggerTime = recent,
            CreatedAt = recent
        }).ExecuteCommand();
    }
}
