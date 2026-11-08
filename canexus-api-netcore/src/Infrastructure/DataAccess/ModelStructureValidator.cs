using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Infrastructure.DataAccess;

/// <summary>
/// 模型结构校验器（T5 #13 / RC-02）：用 SqlSugar ORM 元数据生成「模型快照」，
/// 与 MySQL <c>information_schema.COLUMNS / STATISTICS</c> 的实际结构比对。
/// <para>不手写列名/索引清单——模型侧全部取自 <c>client.EntityMaintenance.GetEntityInfo(type)</c>
/// （与 <see cref="DbInitializer"/> 取表名用的是同一入口），新增实体或字段自动纳入校验。</para>
/// <para>比对项：模型表是否存在、模型列是否齐全、非空约束是否一致、主键索引是否存在。
/// 纯比对逻辑 <see cref="Compare"/> 不依赖数据库，可单元测试；<see cref="ValidateAsync"/>
/// 由 <see cref="DbInitializer"/> 在 <c>Database:ValidateModelStructure=true</c> 时调用，
/// 任一差异即在启动期抛异常阻止启动（与已应用迁移脚本指纹校验形成「脚本正文 + 落地结构」双保险）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="logger">日志器（可选：由 DbInitializer 就地构造时不注入，不影响纯比对逻辑）。</param>
public sealed class ModelStructureValidator(ISqlSugarClient client, ILogger<ModelStructureValidator>? logger = null)
{
    /// <summary>模型列快照。</summary>
    /// <param name="Name">列名。</param>
    /// <param name="IsNullable">模型是否声明可空。</param>
    public sealed record ModelColumn(string Name, bool IsNullable);

    /// <summary>模型表快照。</summary>
    /// <param name="Name">表名。</param>
    /// <param name="Columns">模型列清单。</param>
    public sealed record ModelTable(string Name, IReadOnlyList<ModelColumn> Columns);

    /// <summary>information_schema.COLUMNS 行快照。</summary>
    /// <param name="TableName">表名。</param>
    /// <param name="ColumnName">列名。</param>
    /// <param name="IsNullable">数据库是否允许 NULL。</param>
    public sealed record DatabaseColumn(string TableName, string ColumnName, bool IsNullable);

    /// <summary>information_schema.STATISTICS 行快照。</summary>
    /// <param name="TableName">表名。</param>
    /// <param name="IndexName">索引名。</param>
    /// <param name="ColumnName">索引列名。</param>
    public sealed record DatabaseIndex(string TableName, string IndexName, string ColumnName);

    /// <summary>由实体类型生成模型快照（表名、列名、可空性均取 ORM 元数据）。</summary>
    /// <param name="entityTypes">实体类型清单。</param>
    /// <returns>按表名排序的模型快照。</returns>
    public IReadOnlyList<ModelTable> CollectModel(IReadOnlyList<Type> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);
        return entityTypes
            .Select(type =>
            {
                var info = client.EntityMaintenance.GetEntityInfo(type);
                return new ModelTable(
                    info.DbTableName,
                    info.Columns
                        .Select(column => new ModelColumn(column.DbColumnName, column.IsNullable))
                        .ToArray());
            })
            .OrderBy(table => table.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>读取当前库 information_schema 的列与索引快照。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>列与索引快照。</returns>
    public async Task<(IReadOnlyList<DatabaseColumn> Columns, IReadOnlyList<DatabaseIndex> Indexes)> CollectDatabaseAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var columns = await client.Ado.SqlQueryAsync<ColumnRow>(
            "SELECT TABLE_NAME AS TableName, COLUMN_NAME AS ColumnName, IS_NULLABLE AS IsNullable "
            + "FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()");
        var indexes = await client.Ado.SqlQueryAsync<IndexRow>(
            "SELECT TABLE_NAME AS TableName, INDEX_NAME AS IndexName, COLUMN_NAME AS ColumnName "
            + "FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE()");

        return (
            [.. columns.Select(row => new DatabaseColumn(
                row.TableName, row.ColumnName,
                string.Equals(row.IsNullable, "YES", StringComparison.OrdinalIgnoreCase)))],
            [.. indexes.Select(row => new DatabaseIndex(row.TableName, row.IndexName, row.ColumnName))]);
    }

    /// <summary>
    /// 纯比对：返回全部结构差异（按表名、列名排序）。
    /// </summary>
    /// <param name="model">模型快照。</param>
    /// <param name="columns">数据库列快照。</param>
    /// <param name="indexes">数据库索引快照。</param>
    /// <returns>差异描述列表；完全一致时为空。</returns>
    public static IReadOnlyList<string> Compare(
        IReadOnlyList<ModelTable> model,
        IReadOnlyList<DatabaseColumn> columns,
        IReadOnlyList<DatabaseIndex> indexes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(indexes);

        var problems = new List<string>();
        var columnsByTable = columns
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(column => column.ColumnName, column => column.IsNullable, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var tablesWithPrimary = indexes
            .Where(index => string.Equals(index.IndexName, "PRIMARY", StringComparison.OrdinalIgnoreCase))
            .Select(index => index.TableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var table in model.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (!columnsByTable.TryGetValue(table.Name, out var actual))
            {
                problems.Add($"表 {table.Name} 在数据库中不存在（模型快照有定义，information_schema 无列记录）");
                continue;
            }

            if (!tablesWithPrimary.Contains(table.Name) && table.Columns.Count > 0)
            {
                problems.Add($"表 {table.Name} 缺少 PRIMARY 索引（information_schema.STATISTICS 未记录主键）");
            }

            foreach (var column in table.Columns.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if (!actual.TryGetValue(column.Name, out var isNullable))
                {
                    problems.Add($"表 {table.Name} 缺少模型列 {column.Name}（迁移脚本未落地或被绕过）");
                    continue;
                }

                if (isNullable != column.IsNullable)
                {
                    problems.Add(
                        $"表 {table.Name} 列 {column.Name} 非空约束不一致：模型 {(column.IsNullable ? "可空" : "非空")}，"
                        + $"数据库 {(isNullable ? "可空" : "非空")}");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// 启动期校验：模型快照与 information_schema 不一致即抛出 <see cref="InvalidOperationException"/> 阻止启动。
    /// </summary>
    /// <param name="entityTypes">实体类型清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    /// <exception cref="InvalidOperationException">存在结构差异时抛出。</exception>
    public async Task ValidateAsync(IReadOnlyList<Type> entityTypes, CancellationToken cancellationToken = default)
    {
        var (columns, indexes) = await CollectDatabaseAsync(cancellationToken);
        var problems = Compare(CollectModel(entityTypes), columns, indexes);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "模型结构与 information_schema 不一致（模型快照取自 SqlSugar EntityMaintenance）：\n"
                + string.Join("\n", problems));
        }

        logger?.LogInformation("模型结构校验通过：{TableCount} 张表 / {ColumnCount} 列与 information_schema 一致",
            entityTypes.Count, columns.Count);
    }

    /// <summary>information_schema.COLUMNS 行映射。</summary>
    private sealed class ColumnRow
    {
        /// <summary>表名。</summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>列名。</summary>
        public string ColumnName { get; set; } = string.Empty;

        /// <summary>是否允许 NULL（YES / NO）。</summary>
        public string IsNullable { get; set; } = string.Empty;
    }

    /// <summary>information_schema.STATISTICS 行映射。</summary>
    private sealed class IndexRow
    {
        /// <summary>表名。</summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>索引名。</summary>
        public string IndexName { get; set; } = string.Empty;

        /// <summary>索引列名。</summary>
        public string ColumnName { get; set; } = string.Empty;
    }
}
