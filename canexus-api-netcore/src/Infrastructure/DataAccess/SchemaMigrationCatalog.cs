
namespace Infrastructure.DataAccess;

/// <summary>
/// 迁移清单目录：解析 vNNN 语义序号并按版本排序，同时拒绝重复 / 非法 ID。
/// <para>本目录只负责「执行顺序」，不读写任何数据库状态。</para>
/// </summary>
public static class SchemaMigrationCatalog
{
    /// <summary>解析迁移 ID 对应的序号。</summary>
    /// <param name="migrationId">迁移 ID（vNNN）。</param>
    /// <returns>序号；无法解析时返回 null（视为非法 ID，由 <see cref="Order"/> 拒绝）。</returns>
    public static int? VersionOf(string migrationId)
    {
        if (migrationId.Length >= 2
            && (migrationId[0] == 'v' || migrationId[0] == 'V')
            && int.TryParse(migrationId.AsSpan(1), out var version)
            && version > 0)
        {
            return version;
        }

        return null;
    }

    /// <summary>按序号返回迁移清单；重复 ID 或不符合 vNNN 命名的 ID 立即终止启动（避免歧义）。</summary>
    /// <param name="migrations">注册的全部迁移。</param>
    /// <returns>按序号排序的迁移清单。</returns>
    /// <exception cref="InvalidOperationException">存在重复 ID 或非法 ID 时抛出。</exception>
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
            throw new InvalidOperationException($"迁移 ID 必须为 vNNN 语义序号：{string.Join(", ", invalid)}");
        }

        return list
            .OrderBy(migration => VersionOf(migration.MigrationId), Comparer<int?>.Default)
            .ThenBy(migration => migration.MigrationId, StringComparer.Ordinal)
            .ToArray();
    }
}
