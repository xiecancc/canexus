using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Infrastructure.DataAccess;

/// <summary>
/// 模型结构校验器（T5 #13 / RC-02）：用 SqlSugar ORM 元数据生成「模型快照」，
/// 与 MySQL <c>information_schema.COLUMNS / STATISTICS</c> 的实际结构比对。
/// <para>不手写列名/索引清单——模型侧全部取自 <c>client.EntityMaintenance.GetEntityInfo(type)</c>
/// （与 <see cref="DbInitializer"/> 取表名用的是同一入口），新增实体或字段自动纳入校验。</para>
/// <para>比对项：模型表是否存在、模型列是否齐全、非空约束是否一致、主键索引是否存在，
/// 以及（P2-6）列类型 / 长度与模型声明的唯一 / 普通索引是否存在。列类型 / 长度比对刻意只做确定性规则，
/// 索引比对按「列是否被索引覆盖」判定，取舍见 <see cref="Compare"/> 内的注释。</para>
/// <para>纯比对逻辑 <see cref="Compare"/> 不依赖数据库，可单元测试；<see cref="ValidateAsync"/>
/// 由 <see cref="DbInitializer"/> 在 <c>Database:ValidateModelStructure=true</c> 时调用，
/// 任一差异即在启动期抛异常阻止启动，用于发现绕过 CodeFirst / 迁移通道直接改表的结构漂移。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="logger">日志器（可选：由 DbInitializer 就地构造时不注入，不影响纯比对逻辑）。</param>
public sealed class ModelStructureValidator(ISqlSugarClient client, ILogger<ModelStructureValidator>? logger = null)
{
    /// <summary>模型列快照。</summary>
    /// <param name="Name">列名。</param>
    /// <param name="IsNullable">模型是否声明可空。</param>
    /// <param name="Length">模型声明的列长度（SugarColumn.Length：字符串列为字符长度，decimal 为精度），0 表示未声明。</param>
    /// <param name="DecimalDigits">模型声明的小数位（SugarColumn.DecimalDigits），0 表示未声明。</param>
    /// <param name="IsText">模型是否为字符串列（EntityColumnInfo.UnderType == string）。</param>
    /// <param name="IsNumeric">模型是否为数值 / 枚举 / 布尔列（用于类型族明显不一致判定）。</param>
    public sealed record ModelColumn(
        string Name, bool IsNullable, int Length = 0, int DecimalDigits = 0, bool IsText = false, bool IsNumeric = false);

    /// <summary>模型表快照。</summary>
    /// <param name="Name">表名。</param>
    /// <param name="Columns">模型列清单。</param>
    /// <param name="Indexes">模型声明的索引清单（唯一 / 普通），全部取自 ORM 元数据。</param>
    public sealed record ModelTable(string Name, IReadOnlyList<ModelColumn> Columns, IReadOnlyList<ModelIndex>? Indexes = null);

    /// <summary>模型声明的索引快照（按「列是否被索引覆盖」判定存在性，不依赖 SqlSugar 生成的实际索引名）。</summary>
    /// <param name="ColumnName">索引涉及的列名（DbColumnName）。</param>
    /// <param name="Name">索引名 / 索引组名（仅用于差异描述）。</param>
    /// <param name="IsUnique">模型是否声明唯一索引。</param>
    public sealed record ModelIndex(string ColumnName, string Name, bool IsUnique);

    /// <summary>information_schema.COLUMNS 行快照。</summary>
    /// <param name="TableName">表名。</param>
    /// <param name="ColumnName">列名。</param>
    /// <param name="IsNullable">数据库是否允许 NULL。</param>
    /// <param name="DataType">DATA_TYPE（如 varchar / int / decimal）。</param>
    /// <param name="ColumnType">COLUMN_TYPE（如 varchar(50) / int(11)）。</param>
    /// <param name="CharacterMaximumLength">CHARACTER_MAXIMUM_LENGTH（字符列长度；数值列为 null）。</param>
    /// <param name="NumericPrecision">NUMERIC_PRECISION（数值列精度；字符列为 null）。</param>
    /// <param name="NumericScale">NUMERIC_SCALE（数值列小数位；字符列为 null）。</param>
    public sealed record DatabaseColumn(
        string TableName, string ColumnName, bool IsNullable,
        string DataType = "", string ColumnType = "",
        long? CharacterMaximumLength = null, long? NumericPrecision = null, long? NumericScale = null);

    /// <summary>information_schema.STATISTICS 行快照。</summary>
    /// <param name="TableName">表名。</param>
    /// <param name="IndexName">索引名。</param>
    /// <param name="ColumnName">索引列名。</param>
    /// <param name="NonUnique">NON_UNIQUE（true=非唯一，false=唯一，含主键）。</param>
    public sealed record DatabaseIndex(string TableName, string IndexName, string ColumnName, bool NonUnique = true);

    /// <summary>由实体类型生成模型快照（表名、列名、可空性、长度、类型族、索引均取 ORM 元数据，不手写清单）。</summary>
    /// <param name="entityTypes">实体类型清单。</param>
    /// <returns>按表名排序的模型快照。</returns>
    public IReadOnlyList<ModelTable> CollectModel(IReadOnlyList<Type> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);
        return entityTypes
            .Select(type =>
            {
                var info = client.EntityMaintenance.GetEntityInfo(type);
                var propertyToColumn = info.Columns.ToDictionary(
                    column => column.PropertyName, column => column.DbColumnName, StringComparer.OrdinalIgnoreCase);
                return new ModelTable(
                    info.DbTableName,
                    info.Columns
                        .Select(column => new ModelColumn(
                            column.DbColumnName,
                            column.IsNullable,
                            column.Length,
                            column.DecimalDigits,
                            column.UnderType == typeof(string),
                            IsNumericType(column.UnderType)))
                        .ToArray(),
                    CollectModelIndexes(info, propertyToColumn));
            })
            .OrderBy(table => table.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>判断 CLR 类型是否属于数值 / 枚举 / 布尔族（可空类型取其底层类型）。</summary>
    /// <param name="type">EntityColumnInfo.UnderType。</param>
    /// <returns>属于数值族返回 true。</returns>
    private static bool IsNumericType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsEnum
            || underlying == typeof(byte) || underlying == typeof(sbyte)
            || underlying == typeof(short) || underlying == typeof(ushort)
            || underlying == typeof(int) || underlying == typeof(uint)
            || underlying == typeof(long) || underlying == typeof(ulong)
            || underlying == typeof(float) || underlying == typeof(double)
            || underlying == typeof(decimal) || underlying == typeof(bool);
    }

    /// <summary>从 ORM 元数据收集模型声明的唯一 / 普通索引（列级索引组名 + 类级 SugarIndex）。</summary>
    /// <param name="info">实体元数据。</param>
    /// <param name="propertyToColumn">属性名到 DbColumnName 的映射（SugarIndex.IndexFields 的键为属性名）。</param>
    /// <returns>按列名展开的模型索引清单。</returns>
    private static IReadOnlyList<ModelIndex> CollectModelIndexes(
        EntityInfo info, IReadOnlyDictionary<string, string> propertyToColumn)
    {
        var result = new List<ModelIndex>();
        foreach (var column in info.Columns)
        {
            foreach (var group in column.IndexGroupNameList ?? [])
            {
                result.Add(new ModelIndex(column.DbColumnName, group, IsUnique: false));
            }
            foreach (var group in column.UIndexGroupNameList ?? [])
            {
                result.Add(new ModelIndex(column.DbColumnName, group, IsUnique: true));
            }
        }

        foreach (var index in info.Indexs ?? [])
        {
            if (index.IndexFields is not System.Collections.IDictionary fields)
            {
                continue;
            }
            foreach (var key in fields.Keys)
            {
                var propertyName = key?.ToString();
                if (propertyName is null)
                {
                    continue;
                }
                var columnName = propertyToColumn.TryGetValue(propertyName, out var mapped) ? mapped : propertyName;
                result.Add(new ModelIndex(columnName, index.IndexName, index.IsUnique));
            }
        }

        return result;
    }

    /// <summary>读取当前库 information_schema 的列与索引快照。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>列与索引快照。</returns>
    public async Task<(IReadOnlyList<DatabaseColumn> Columns, IReadOnlyList<DatabaseIndex> Indexes)> CollectDatabaseAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var columns = await client.Ado.SqlQueryAsync<ColumnRow>(
            "SELECT TABLE_NAME AS TableName, COLUMN_NAME AS ColumnName, IS_NULLABLE AS IsNullable, "
            + "DATA_TYPE AS DataType, COLUMN_TYPE AS ColumnType, "
            + "CHARACTER_MAXIMUM_LENGTH AS CharacterMaximumLength, "
            + "NUMERIC_PRECISION AS NumericPrecision, NUMERIC_SCALE AS NumericScale "
            + "FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()");
        var indexes = await client.Ado.SqlQueryAsync<IndexRow>(
            "SELECT TABLE_NAME AS TableName, INDEX_NAME AS IndexName, COLUMN_NAME AS ColumnName, "
            + "NON_UNIQUE AS NonUnique "
            + "FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE()");

        return (
            [.. columns.Select(row => new DatabaseColumn(
                row.TableName, row.ColumnName,
                string.Equals(row.IsNullable, "YES", StringComparison.OrdinalIgnoreCase),
                row.DataType, row.ColumnType,
                row.CharacterMaximumLength, row.NumericPrecision, row.NumericScale))],
            [.. indexes.Select(row => new DatabaseIndex(
                row.TableName, row.IndexName, row.ColumnName, row.NonUnique != 0))]);
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
                group => group.ToDictionary(column => column.ColumnName, column => column, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var tablesWithPrimary = indexes
            .Where(index => string.Equals(index.IndexName, "PRIMARY", StringComparison.OrdinalIgnoreCase))
            .Select(index => index.TableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 索引行按 (表名, 列名) 归组：索引比对按「列是否被索引覆盖」判定，不依赖 SqlSugar 生成的实际索引名
        var indexRowsByTableColumn = indexes
            .GroupBy(index => $"{index.TableName}\u001F{index.ColumnName}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

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
                if (!actual.TryGetValue(column.Name, out var dbColumn))
                {
                    problems.Add($"表 {table.Name} 缺少模型列 {column.Name}（迁移脚本未落地或被绕过）");
                    continue;
                }

                if (dbColumn.IsNullable != column.IsNullable)
                {
                    problems.Add(
                        $"表 {table.Name} 列 {column.Name} 非空约束不一致：模型 {(column.IsNullable ? "可空" : "非空")}，"
                        + $"数据库 {(dbColumn.IsNullable ? "可空" : "非空")}");
                }

                CompareColumnShape(table.Name, column, dbColumn, problems);
            }

            if (table.Indexes is { Count: > 0 })
            {
                CompareIndexes(table.Name, table.Indexes, indexRowsByTableColumn, problems);
            }
        }

        return problems;
    }

    /// <summary>
    /// P2-6 列类型 / 长度的保守比对。
    /// <para><b>取舍</b>：SqlSugar 5.1.4 的 <c>EntityColumnInfo.DataType</c> 对未显式声明
    /// <c>ColumnDataType</c> 的列恒为空字符串（实测探针确认），且 MySQL 类型别名 / 映射众多
    /// （<c>int(11)</c> vs <c>int</c>、<c>tinyint(1)</c> vs <c>bool</c>、enum→int、Guid→char(36)），
    /// 精确比对类型名极易误报。故只做两类确定性规则：</para>
    /// <para>① 模型显式声明长度（<c>Length &gt; 0</c>）且数据库列「实际长度」更小才报——
    /// 字符列取 <c>CHARACTER_MAXIMUM_LENGTH</c>，数值列取 <c>NUMERIC_PRECISION</c>；
    /// <c>Length=0</c> 的列（如 int / bool / datetime）一律跳过，从根源规避 <c>int(11) vs int</c> 误报。</para>
    /// <para>② 类型族明显不一致：仅用 information_schema 的确定性标记（字符列有 CharacterMaximumLength、
    /// 数值列有 NumericPrecision）判定「模型字符串列 ↔ 数据库数值列」「模型数值列 ↔ 数据库字符列」，
    /// 不解析 COLUMN_TYPE 文本，不受类型别名影响；标记均缺失的类型（datetime / json 等）保守跳过。</para>
    /// <para>模型声明的小数位（<c>DecimalDigits</c>）大于数据库 <c>NUMERIC_SCALE</c> 时一并报告精度损失。</para>
    /// </summary>
    /// <param name="tableName">表名。</param>
    /// <param name="column">模型列快照。</param>
    /// <param name="db">数据库列快照。</param>
    /// <param name="problems">差异收集列表。</param>
    private static void CompareColumnShape(string tableName, ModelColumn column, DatabaseColumn db, List<string> problems)
    {
        // ② 类型族明显不一致（不依赖 Length，覆盖 int vs varchar 双向）
        if (column.IsText && db.CharacterMaximumLength is null && db.NumericPrecision is not null)
        {
            problems.Add($"表 {tableName} 列 {column.Name} 类型族不一致：模型声明为字符串列，"
                + $"数据库为数值类型 {db.DataType}（{db.ColumnType}）");
        }
        else if (column.IsNumeric && db.NumericPrecision is null && db.CharacterMaximumLength is not null)
        {
            problems.Add($"表 {tableName} 列 {column.Name} 类型族不一致：模型声明为数值列，"
                + $"数据库为字符类型 {db.DataType}（{db.ColumnType}）");
        }

        if (column.Length <= 0)
        {
            return;
        }

        // ① 模型声明长度大于数据库实际长度
        var dbLength = db.CharacterMaximumLength ?? db.NumericPrecision;
        if (dbLength is { } actualLength && actualLength < column.Length)
        {
            problems.Add($"表 {tableName} 列 {column.Name} 长度不足：模型声明 {column.Length}，"
                + $"数据库实际 {actualLength}（{db.ColumnType}）");
        }

        if (column.DecimalDigits > 0 && db.NumericScale is { } actualScale && actualScale < column.DecimalDigits)
        {
            problems.Add($"表 {tableName} 列 {column.Name} 小数位不足：模型声明 {column.DecimalDigits}，"
                + $"数据库实际 {actualScale}（{db.ColumnType}）");
        }
    }

    /// <summary>
    /// P2-6 索引存在性比对：按「模型声明的列是否被索引覆盖」判定，不比对索引名
    /// （SqlSugar 由 IndexGroupNameList / SugarIndex 生成的实际索引名可能与组名不一致，比对名字会误报）。
    /// 唯一索引要求该列被 NonUnique=false 的索引覆盖；普通索引只要求存在任意覆盖索引（含主键）。
    /// <para>ORM 元数据可稳定取到索引声明（EntityInfo.Indexs 与列级 IndexGroupNameList / UIndexGroupNameList），
    /// 但当前代码库实体未声明任何索引，真实库上该规则为空集；主要用于未来声明索引后的漂移防护。</para>
    /// </summary>
    /// <param name="tableName">表名。</param>
    /// <param name="declared">模型声明的索引清单。</param>
    /// <param name="indexRows">按 (表名, 列名) 归组的数据库索引行。</param>
    /// <param name="problems">差异收集列表。</param>
    private static void CompareIndexes(
        string tableName,
        IReadOnlyList<ModelIndex> declared,
        IReadOnlyDictionary<string, DatabaseIndex[]> indexRows,
        List<string> problems)
    {
        foreach (var index in declared
                     .OrderBy(item => item.ColumnName, StringComparer.Ordinal)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            var covering = indexRows.TryGetValue($"{tableName}\u001F{index.ColumnName}", out var rows)
                ? rows
                : [];
            var satisfied = index.IsUnique
                ? covering.Any(row => !row.NonUnique)
                : covering.Length > 0;
            if (!satisfied)
            {
                problems.Add(index.IsUnique
                    ? $"表 {tableName} 列 {index.ColumnName} 缺少唯一索引（模型声明 {index.Name}）"
                    : $"表 {tableName} 列 {index.ColumnName} 缺少普通索引（模型声明 {index.Name}）");
            }
        }
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

        /// <summary>DATA_TYPE（如 varchar / int / decimal）。</summary>
        public string DataType { get; set; } = string.Empty;

        /// <summary>COLUMN_TYPE（如 varchar(50) / int(11)）。</summary>
        public string ColumnType { get; set; } = string.Empty;

        /// <summary>CHARACTER_MAXIMUM_LENGTH（字符列长度；数值列为 null）。</summary>
        public long? CharacterMaximumLength { get; set; }

        /// <summary>NUMERIC_PRECISION（数值列精度；字符列为 null）。</summary>
        public long? NumericPrecision { get; set; }

        /// <summary>NUMERIC_SCALE（数值列小数位；字符列为 null）。</summary>
        public long? NumericScale { get; set; }
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

        /// <summary>NON_UNIQUE（1=非唯一，0=唯一）。</summary>
        public int NonUnique { get; set; }
    }
}
