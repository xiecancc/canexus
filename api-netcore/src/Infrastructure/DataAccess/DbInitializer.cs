
using Domain.Entities;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Infrastructure.DataAccess;
/// <summary>
/// 数据库初始化器（CodeFirst）：自动建库 + 同步表结构。
/// <para>扫描配置程序集中继承 <see cref="DomainBase"/> 体系（含聚合基类与树形聚合基类）的具体实体类型（排除抽象类与泛型定义）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="options">数据访问配置。</param>
/// <param name="logger">日志器（记录初始化过程）。</param>
public sealed class DbInitializer(ISqlSugarClient client, DatabaseOption options, ILogger<DbInitializer> logger)
{
    /// <summary>初始化数据库：按配置自动建库（AutoCreateTable）、同步表结构（AutoSyncStructure）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 两个开关均关闭时跳过初始化（由外部自行管理数据库结构）
        if (!options.AutoCreateTable && !options.AutoSyncStructure)
        {
            logger.LogInformation("数据库初始化已跳过：AutoCreateTable 与 AutoSyncStructure 均为关闭");
            return;
        }

        var entityTypes = options.EntityAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsConcreteEntityType)
            .Distinct()
            .ToArray();

        if (entityTypes.Length == 0)
        {
            return;
        }

        // 建库：连接串中的库不存在时自动创建（MySQL 需要 CREATE DATABASE 权限）
        if (options.AutoCreateTable)
        {
            client.DbMaintenance.CreateDatabase();
            logger.LogInformation("数据库创建检查完成（不存在时已自动建库）");
        }

        // CodeFirst 建表 / 同步表结构（SqlSugar 的 InitTables 同时承担缺失表的创建与已有表的结构同步；
        // 仅关闭 AutoSyncStructure 而保留 AutoCreateTable 时，新建表仍会按实体定义生成完整结构）
        if (options.AutoCreateTable || options.AutoSyncStructure)
        {
            client.CodeFirst.InitTables(entityTypes);
            logger.LogInformation("CodeFirst 表结构同步完成：共 {EntityCount} 个实体类型", entityTypes.Length);
        }

        // 事务支持要求 InnoDB：若服务器默认存储引擎为 MyISAM（不支持事务），将框架管理的表统一修正为 InnoDB
        if (options.AutoSyncStructure)
        {
            List<string> alteredTables = [];
            foreach (var entityType in entityTypes)
            {
                var tableName = client.EntityMaintenance.GetEntityInfo(entityType).DbTableName;
                var engine = await client.Ado.GetScalarAsync(
                    "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName",
                    new SugarParameter("@tableName", tableName));

                if (!string.Equals(engine?.ToString(), "InnoDB", StringComparison.OrdinalIgnoreCase))
                {
                    await client.Ado.ExecuteCommandAsync($"ALTER TABLE `{tableName}` ENGINE = InnoDB");
                    alteredTables.Add(tableName);
                }
            }

            if (alteredTables.Count > 0)
            {
                logger.LogWarning("已将 {Count} 个非 InnoDB 表修正为 InnoDB：{Tables}",
                    alteredTables.Count, string.Join(", ", alteredTables));
            }
        }
    }

    /// <summary>判断类型是否为具体实体类型：类、非抽象、非泛型定义，且基类链中存在实体体系基类。</summary>
    /// <param name="type">待判断类型。</param>
    /// <returns>是具体实体类型时返回 true。</returns>
    private static bool IsConcreteEntityType(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
        {
            return false;
        }

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current == typeof(DomainBase))
            {
                return true;
            }
        }

        return false;
    }
}
