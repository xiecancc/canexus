namespace Infrastructure.Tests.Architecture;

/// <summary>
/// T6 #14 事务边界架构测试：白名单约束「独立会话」的两类逃生口。
/// <para>本项目不引入 <c>IAmbientSession</c> 抽象（SqlSugar 的 <c>CopyNew()</c> 本身就是
/// 表达「独立连接 / 独立事务作用域」的标准方式），改为用白名单测试约束使用面：
/// <list type="bullet">
/// <item><b>CopyNew()</b>：仅供「审计 / 认领 / 吊销必须存活于业务事务回滚之外」的仓储与令牌服务使用；</item>
/// <item><b>裸 Ado</b>：仅供数据访问基础设施（结构探测 / 迁移 / 初始化 / 健康检查 / 工作单元）执行
/// 原生 SQL 使用——这些场景要么没有实体语义，要么必须绕开 ORM 的 SQL 生成。</item>
/// </list>
/// 应用层 / API 层出现这两类写法即测试失败：要么改走仓储与工作单元抽象，要么把文件加入白名单并在
/// 事务语义文档中说明理由（白名单条目必须与真实代码一致，删除实现而不删白名单同样会失败）。
/// 该文档位于仓库内 docs/事务语义与ORM选型说明.md，随源码提交。</para>
/// </summary>
[TestFixture]
public class TransactionBoundaryArchitectureTests
{
    /// <summary>仓库根目录（test/Infrastructure.Tests/bin/... 向上五级到 canexus-api-netcore）。</summary>
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>src 源码目录。</summary>
    private static readonly string SourceRoot = Path.Combine(RepositoryRoot, "src");

    /// <summary>
    /// 允许使用 <c>CopyNew()</c> 的文件白名单（相对 canexus-api-netcore，正斜杠书写）。
    /// 每一项的语义依据见仓库内 docs/事务语义与ORM选型说明.md 第二节（随仓库提交，不依赖本地分析文档）。
    /// </summary>
    private static readonly string[] CopyNewWhitelist =
    [
        "src/Infrastructure/Auth/TokenVersionService.cs",
        "src/Infrastructure/Repositories/LoginLogRepository.cs",
        "src/Infrastructure/Repositories/OutboxMessageRepository.cs",
        "src/Infrastructure/Repositories/SysRefreshTokenRepository.cs"
    ];

    /// <summary>
    /// 允许使用裸 <c>Ado</c> 的文件白名单（相对 canexus-api-netcore，正斜杠书写）。
    /// </summary>
    private static readonly string[] BareAdoWhitelist =
    [
        "src/API/HealthChecks/DatabaseHealthCheck.cs",
        "src/Infrastructure/DataAccess/DatabaseSchemaProbe.cs",
        "src/Infrastructure/DataAccess/DbInitializer.cs",
        "src/Infrastructure/DataAccess/ModelStructureValidator.cs",
        "src/Infrastructure/DataAccess/SchemaMigrator.cs",
        "src/Infrastructure/DataAccess/SeedDataInitializer.cs",
        "src/Infrastructure/DataAccess/SqlSugarClientFactory.cs",
        "src/Infrastructure/Repositories/AggregateRepository.cs",
        "src/Infrastructure/Repositories/UnitOfWork.cs"
    ];

    /// <summary>src 下出现 CopyNew / 裸 Ado 的文件必须全部在白名单内。</summary>
    [Test]
    public void IndependentSessionUsage_IsLimitedToWhitelist()
    {
        var copyNewFiles = FindFilesContaining("CopyNew()");
        var bareAdoFiles = FindFilesContaining(".Ado.");

        var copyNewViolations = copyNewFiles
            .Where(file => !CopyNewWhitelist.Contains(file, StringComparer.Ordinal))
            .ToArray();
        var bareAdoViolations = bareAdoFiles
            .Where(file => !BareAdoWhitelist.Contains(file, StringComparer.Ordinal))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(copyNewViolations, Is.Empty,
                "CopyNew() 只允许用于「审计 / 认领 / 吊销须存活于事务回滚之外」的白名单文件；"
                + "新增使用请改走仓储抽象，或把文件加入 TransactionBoundaryArchitectureTests.CopyNewWhitelist"
                + "并在 docs/事务语义与ORM选型说明.md 说明理由。越界文件：\n" + string.Join("\n", copyNewViolations));
            Assert.That(bareAdoViolations, Is.Empty,
                "裸 Ado 只允许数据访问基础设施执行原生 SQL；业务查询请走 Queryable / 仓储抽象。"
                + "越界文件：\n" + string.Join("\n", bareAdoViolations));
        });
    }

    /// <summary>白名单不得包含已经不再使用对应写法的条目（防止白名单腐化成摆设）。</summary>
    [Test]
    public void Whitelist_Entries_AreStillInUse()
    {
        var copyNewFiles = FindFilesContaining("CopyNew()");
        var bareAdoFiles = FindFilesContaining(".Ado.");

        var staleCopyNew = CopyNewWhitelist.Where(file => !copyNewFiles.Contains(file, StringComparer.Ordinal)).ToArray();
        var staleBareAdo = BareAdoWhitelist.Where(file => !bareAdoFiles.Contains(file, StringComparer.Ordinal)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(staleCopyNew, Is.Empty,
                "白名单条目已不再使用 CopyNew()，应从白名单移除：\n" + string.Join("\n", staleCopyNew));
            Assert.That(staleBareAdo, Is.Empty,
                "白名单条目已不再使用裸 Ado，应从白名单移除：\n" + string.Join("\n", staleBareAdo));
        });
    }

    /// <summary>扫描 src 下全部 .cs 源文件（排除 bin / obj），返回命中关键字的仓库相对路径（正斜杠）。</summary>
    /// <param name="marker">文本标记。</param>
    /// <returns>命中文件的相对路径集合。</returns>
    private static string[] FindFilesContaining(string marker)
    {
        var directory = new DirectoryInfo(SourceRoot);
        if (!directory.Exists)
        {
            Assert.Fail($"源码目录不存在：{SourceRoot}（架构测试须在仓库内运行）");
        }

        return [.. directory
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file.FullName).Contains(marker, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file.FullName).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)];
    }
}
