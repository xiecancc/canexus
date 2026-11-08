namespace Infrastructure.DataAccess;

/// <summary>结构就绪判定结果。</summary>
public enum SchemaReadiness
{
    /// <summary>已应用版本与代码期望一致、模型表齐全：启动不执行任何 DDL。</summary>
    Ready = 0,

    /// <summary>结构已初始化但版本落后：按序应用缺失的迁移脚本。</summary>
    Pending = 1,

    /// <summary>结构未初始化（缺表或无迁移记录）：非生产可初始化，生产须先经迁移通道建表。</summary>
    Uninitialized = 2,

    /// <summary>库中版本高于代码期望（部署了更旧的代码）：拒绝启动。</summary>
    CodeBehind = 3
}

/// <summary>结构判定快照。</summary>
/// <param name="Readiness">就绪分类。</param>
/// <param name="AppliedVersion">库中已应用的结构版本。</param>
/// <param name="ExpectedVersion">代码期望的结构版本。</param>
/// <param name="MissingTables">缺失的模型表。</param>
/// <param name="UnknownRecords">无法被当前代码识别的已应用迁移 ID（视为更高版本）。</param>
public sealed record SchemaStatus(
    SchemaReadiness Readiness,
    int AppliedVersion,
    int ExpectedVersion,
    IReadOnlyList<string> MissingTables,
    IReadOnlyList<string> UnknownRecords);
