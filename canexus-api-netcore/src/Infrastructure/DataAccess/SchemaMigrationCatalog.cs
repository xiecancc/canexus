using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.DataAccess;

/// <summary>
/// 结构版本与脚本指纹目录（T-13）：版本取自迁移 ID 的语义序号（baseline-v1 = 0，v001 = 1 …），
/// 指纹取迁移脚本正文的 SHA-256，而非 ID 与说明文字。
/// </summary>
public static class SchemaMigrationCatalog
{
    /// <summary>基线版本号。</summary>
    public const int BaselineVersion = 0;

    /// <summary>解析迁移 ID 对应的结构版本。</summary>
    /// <param name="migrationId">迁移 ID。</param>
    /// <returns>版本号；无法解析时返回 null（按「未知更高版本」处理，避免把未来版本误判为落后）。</returns>
    public static int? VersionOf(string migrationId)
    {
        if (string.Equals(migrationId, SchemaMigrator.BaselineMigrationId, StringComparison.Ordinal))
        {
            return BaselineVersion;
        }

        if (migrationId.Length >= 2
            && (migrationId[0] == 'v' || migrationId[0] == 'V')
            && int.TryParse(migrationId.AsSpan(1), out var version)
            && version > 0)
        {
            return version;
        }

        return null;
    }

    /// <summary>按版本顺序返回迁移清单，并检测重复 ID。</summary>
    /// <param name="migrations">注册的全部迁移。</param>
    /// <returns>按版本排序的迁移清单。</returns>
    /// <exception cref="InvalidOperationException">存在重复 ID 或不可解析版本时抛出。</exception>
    public static IReadOnlyList<ISchemaMigration> Order(IEnumerable<ISchemaMigration> migrations)
    {
        var list = migrations.ToArray();
        var duplicates = list
            .GroupBy(migration => migration.MigrationId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"迁移清单存在重复 MigrationId：{string.Join(", ", duplicates)}");
        }

        var invalid = list
            .Where(migration => VersionOf(migration.MigrationId) is null)
            .Select(migration => migration.MigrationId)
            .ToArray();
        if (invalid.Length > 0)
        {
            throw new InvalidOperationException(
                $"迁移 ID 必须为 baseline-v1 或 vNNN 语义序号：{string.Join(", ", invalid)}");
        }

        return list
            .OrderBy(migration => VersionOf(migration.MigrationId), Comparer<int?>.Default)
            .ThenBy(migration => migration.MigrationId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>代码期望的结构版本（清单中最大版本；空清单为基线）。</summary>
    /// <param name="orderedMigrations">已排序的迁移清单。</param>
    /// <returns>期望版本号。</returns>
    public static int ExpectedVersion(IReadOnlyList<ISchemaMigration> orderedMigrations)
    {
        return orderedMigrations.Count == 0
            ? BaselineVersion
            : orderedMigrations.Max(migration => VersionOf(migration.MigrationId) ?? BaselineVersion);
    }

    /// <summary>脚本正文指纹（SHA-256 十六进制）：无脚本时回退到 ID + 说明，兼容纯登记型迁移。</summary>
    /// <param name="migration">迁移。</param>
    /// <returns>指纹。</returns>
    public static string ScriptHash(ISchemaMigration migration)
    {
        var content = migration.Scripts.Count > 0
            ? string.Join("\n", migration.Scripts)
            : $"{migration.MigrationId}|{migration.Description}";
        return ComputeHash(content);
    }

    /// <summary>升级前的旧指纹口径（ID + 说明），用于识别存量库记录并做一次性自愈。</summary>
    /// <param name="migration">迁移。</param>
    /// <returns>旧口径指纹。</returns>
    public static string LegacyHash(ISchemaMigration migration)
    {
        return ComputeHash($"{migration.MigrationId}|{migration.Description}");
    }

    /// <summary>计算 SHA-256 十六进制字符串。</summary>
    /// <param name="content">正文。</param>
    /// <returns>指纹。</returns>
    public static string ComputeHash(string content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
