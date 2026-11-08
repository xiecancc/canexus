namespace Infrastructure.Shared.DataAccess;

/// <summary>数据库初始化阶段状态。</summary>
public enum DatabaseInitializationStage
{
    /// <summary>尚未执行。</summary>
    Pending = 0,

    /// <summary>已就绪。</summary>
    Ready = 1,

    /// <summary>失败（宿主启动时即 fail-fast，运行期一般不会停留在此状态）。</summary>
    Failed = 2
}

/// <summary>初始化结论的匿名快照（供 /health/ready 输出，不含连接串、表名、异常堆栈等内部信息）。</summary>
/// <param name="Database">库级判定结论。</param>
/// <param name="Schema">结构判定结论。</param>
/// <param name="Seed">种子判定结论。</param>
/// <param name="AppliedVersion">库中已应用的结构版本。</param>
/// <param name="ExpectedVersion">代码期望的结构版本。</param>
public sealed record DatabaseInitializationSnapshot(
    string Database,
    string Schema,
    string Seed,
    int AppliedVersion,
    int ExpectedVersion);

/// <summary>
/// 数据库初始化结论（单例）：分别记录「库 / 结构 / 种子」三层的判定结果与结构版本。
/// <para>启动期由 <c>DbInitializer</c> 与宿主写入，运行期只读；供启动日志汇总与
/// <c>/health/ready</c> 输出脱敏状态（不暴露连接串、表名与异常细节）。</para>
/// </summary>
public sealed class DatabaseInitializationState
{
    private readonly object _sync = new();
    private DatabaseInitializationStage _database = DatabaseInitializationStage.Pending;
    private DatabaseInitializationStage _schema = DatabaseInitializationStage.Pending;
    private DatabaseInitializationStage _seed = DatabaseInitializationStage.Pending;
    private int _appliedVersion;
    private int _expectedVersion;

    /// <summary>三层是否全部就绪。</summary>
    public bool IsReady
    {
        get
        {
            lock (_sync)
            {
                return _database == DatabaseInitializationStage.Ready
                    && _schema == DatabaseInitializationStage.Ready
                    && _seed == DatabaseInitializationStage.Ready;
            }
        }
    }

    /// <summary>记录库级判定结论。</summary>
    /// <param name="status">阶段状态。</param>
    public void MarkDatabase(DatabaseInitializationStage status)
    {
        lock (_sync)
        {
            _database = status;
        }
    }

    /// <summary>记录结构判定结论与版本。</summary>
    /// <param name="status">阶段状态。</param>
    /// <param name="appliedVersion">库中已应用的结构版本。</param>
    /// <param name="expectedVersion">代码期望的结构版本。</param>
    public void MarkSchema(DatabaseInitializationStage status, int appliedVersion, int expectedVersion)
    {
        lock (_sync)
        {
            _schema = status;
            _appliedVersion = appliedVersion;
            _expectedVersion = expectedVersion;
        }
    }

    /// <summary>记录种子判定结论。</summary>
    /// <param name="status">阶段状态。</param>
    public void MarkSeed(DatabaseInitializationStage status)
    {
        lock (_sync)
        {
            _seed = status;
        }
    }

    /// <summary>生成匿名快照（状态名与版本号，不含任何内部描述）。</summary>
    /// <returns>快照。</returns>
    public DatabaseInitializationSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new DatabaseInitializationSnapshot(
                _database.ToString(),
                _schema.ToString(),
                _seed.ToString(),
                _appliedVersion,
                _expectedVersion);
        }
    }
}
