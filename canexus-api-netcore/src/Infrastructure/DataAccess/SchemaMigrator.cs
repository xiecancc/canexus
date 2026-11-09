
using System.Data.Common;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Infrastructure.DataAccess;

/// <summary>
/// 数据库迁移执行器：按 <see cref="SchemaMigrationCatalog.Order"/> 的顺序，逐个在事务内执行
/// 注册的全部 <see cref="ISchemaMigration"/>。
/// <para>框架不记录迁移历史：数据库为开发期临时库，可直接删除重建，结构由 CodeFirst 依实体模型生成；
/// 因此每次启动都会重跑迁移清单，所有脚本必须幂等（<c>IF NOT EXISTS</c> / <c>INSERT ... ON DUPLICATE KEY</c> 等）。
/// 单个迁移的脚本与扩展点在同一事务内执行，DML 失败可整体回滚；MySQL DDL 存在隐式提交，
/// 涉及建表 / 改列的脚本请自行保证幂等。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="logger">日志器。</param>
public sealed class SchemaMigrator(ISqlSugarClient client, ILogger<SchemaMigrator> logger)
{
    /// <summary>按序执行全部迁移（每个迁移独占一个事务；不读写任何历史记录）。</summary>
    /// <param name="migrations">已按序号排序的迁移清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    /// <exception cref="InvalidOperationException">迁移执行失败且未抛出具体异常时抛出。</exception>
    public async Task ApplyAsync(
        IReadOnlyList<ISchemaMigration> migrations,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var migration in migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await client.Ado.UseTranAsync(async () =>
            {
                // 原生 SQL 经事务执行器运行（裸 Ado.ExecuteCommand 不挂接事务，会自动提交）
                var runner = new TransactionSqlRunner(
                    (DbConnection)client.Ado.Connection,
                    (DbTransaction)client.Ado.Transaction);

                foreach (var script in migration.Scripts)
                {
                    await runner.ExecuteAsync(script, cancellationToken);
                }

                await migration.ApplyAsync(client, runner, cancellationToken);
                return true;
            });

            if (!result.IsSuccess)
            {
                throw result.ErrorException
                    ?? new InvalidOperationException($"迁移 {migration.MigrationId} 执行失败，已回滚");
            }

            logger.LogInformation("已执行结构迁移 {MigrationId}：{Description}", migration.MigrationId, migration.Description);
        }
    }
}
