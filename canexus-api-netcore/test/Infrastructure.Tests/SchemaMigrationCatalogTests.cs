using Infrastructure.DataAccess;
using Infrastructure.Shared.DataAccess;
using SqlSugar;

namespace Infrastructure.Tests;

/// <summary>T-13 纯单测：版本目录、脚本正文指纹、管理连接拆分、初始化状态快照与生产配置基线。</summary>
[TestFixture]
public class SchemaMigrationCatalogTests
{
    /// <summary>版本号按 vNNN 语义解析，基线为 0，未知 ID 返回 null。</summary>
    [Test]
    public void VersionOf_ParsesSemanticIds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SchemaMigrationCatalog.VersionOf("baseline-v1"), Is.EqualTo(0));
            Assert.That(SchemaMigrationCatalog.VersionOf("v001"), Is.EqualTo(1));
            Assert.That(SchemaMigrationCatalog.VersionOf("v012"), Is.EqualTo(12));
            Assert.That(SchemaMigrationCatalog.VersionOf("future-x"), Is.Null);
        });
    }

    /// <summary>清单按版本排序，重复 ID 与非法 ID 均被拒绝。</summary>
    [Test]
    public void Order_SortsAndRejectsConflicts()
    {
        ISchemaMigration[] migrations =
        [
            new StubMigration("v002", ["SELECT 1"]),
            new StubMigration("v001", ["SELECT 2"])
        ];
        var ordered = SchemaMigrationCatalog.Order(migrations);
        Assert.Multiple(() =>
        {
            Assert.That(ordered.Select(item => item.MigrationId), Is.EqualTo(new[] { "v001", "v002" }));
            Assert.That(SchemaMigrationCatalog.ExpectedVersion(ordered), Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => SchemaMigrationCatalog.Order(
                [new StubMigration("v001", []), new StubMigration("v001", [])]));
            Assert.Throws<InvalidOperationException>(() => SchemaMigrationCatalog.Order(
                [new StubMigration("unknown", [])]));
        });
    }

    /// <summary>ScriptHash 反映脚本正文（而非 ID + 说明），正文变更即改变指纹。</summary>
    [Test]
    public void ScriptHash_ReflectsScriptBody()
    {
        var migration = new StubMigration("v001", ["CREATE TABLE a (Id INT)", "CREATE TABLE b (Id INT)"]);
        var changed = new StubMigration("v001", ["CREATE TABLE a (Id INT)", "CREATE TABLE b (Id BIGINT)"]);
        Assert.Multiple(() =>
        {
            Assert.That(SchemaMigrationCatalog.ScriptHash(migration),
                Is.EqualTo(SchemaMigrationCatalog.ComputeHash("CREATE TABLE a (Id INT)\nCREATE TABLE b (Id INT)")));
            Assert.That(SchemaMigrationCatalog.ScriptHash(migration),
                Is.Not.EqualTo(SchemaMigrationCatalog.LegacyHash(migration)));
            Assert.That(SchemaMigrationCatalog.ScriptHash(migration),
                Is.Not.EqualTo(SchemaMigrationCatalog.ScriptHash(changed)));
        });
    }

    /// <summary>纯登记型迁移（无脚本）指纹回退为 ID + 说明，与旧口径一致。</summary>
    [Test]
    public void ScriptHash_EmptyScripts_FallsBackToLegacy()
    {
        var migration = new StubMigration("v002", []);
        Assert.That(SchemaMigrationCatalog.ScriptHash(migration),
            Is.EqualTo(SchemaMigrationCatalog.LegacyHash(migration)));
    }

    /// <summary>管理连接必须剥离库名，且目标库名可确定。</summary>
    [Test]
    public void Split_RemovesDatabaseFromManagementConnection()
    {
        const string source = "server=localhost;port=3306;database=super_admin_test;user id=root;password=test;CharSet=utf8mb4";
        var (management, database) = DatabaseSchemaProbe.Split(source);
        Assert.Multiple(() =>
        {
            Assert.That(database, Is.EqualTo("super_admin_test"));
            Assert.That(management, Does.Not.Contain("Database=").IgnoreCase);
            Assert.That(management, Does.Contain("localhost").IgnoreCase);
            Assert.That(management, Does.Contain("Password=test"));
        });
    }

    /// <summary>初始化状态三层全部就绪才 IsReady，快照不含任何内部描述。</summary>
    [Test]
    public void InitializationState_RequiresAllThreeStages()
    {
        var state = new DatabaseInitializationState();
        Assert.That(state.IsReady, Is.False);

        state.MarkDatabase(DatabaseInitializationStage.Ready);
        state.MarkSchema(DatabaseInitializationStage.Ready, 2, 2);
        Assert.That(state.IsReady, Is.False, "种子未完成时不应就绪");

        state.MarkSeed(DatabaseInitializationStage.Ready);
        var snapshot = state.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(state.IsReady, Is.True);
            Assert.That(snapshot.Database, Is.EqualTo("Ready"));
            Assert.That(snapshot.Schema, Is.EqualTo("Ready"));
            Assert.That(snapshot.Seed, Is.EqualTo("Ready"));
            Assert.That(snapshot.AppliedVersion, Is.EqualTo(2));
            Assert.That(snapshot.ExpectedVersion, Is.EqualTo(2));
        });
    }

    /// <summary>测试用迁移桩。</summary>
    private sealed class StubMigration(string id, IReadOnlyList<string> scripts) : ISchemaMigration
    {
        /// <inheritdoc />
        public string MigrationId { get; } = id;

        /// <inheritdoc />
        public string Description { get; } = $"桩迁移 {id}";

        /// <inheritdoc />
        public IReadOnlyList<string> Scripts { get; } = scripts;

        /// <inheritdoc />
        public Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
