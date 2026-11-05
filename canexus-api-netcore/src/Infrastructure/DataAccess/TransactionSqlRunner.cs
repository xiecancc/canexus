
using System.Data.Common;

namespace Infrastructure.DataAccess;

/// <summary>
/// 事务内 SQL 执行器（P1-A3）：在迁移当前事务连接上执行原生 SQL，命令显式挂接事务，
/// 保证与迁移记录写入同事务（失败整体回滚）。
/// <para>背景：SqlSugar 的 ORM 路径（Insertable/Updateable/Queryable）会挂接 Ado 事务，
/// 但裸 <c>Ado.ExecuteCommand</c> 不挂接（本版本实测自动提交），迁移 SQL 须经本执行器运行。</para>
/// </summary>
public interface ITransactionSqlRunner
{
    /// <summary>在当前事务内执行一条 SQL（DDL/DML），返回受影响行数。</summary>
    /// <param name="sql">SQL 文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数。</returns>
    public Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}

/// <summary><see cref="ITransactionSqlRunner"/> 的默认实现：命令显式挂接事务连接。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="connection">当前事务所在连接。</param>
/// <param name="transaction">当前事务。</param>
public sealed class TransactionSqlRunner(DbConnection connection, DbTransaction transaction) : ITransactionSqlRunner
{
    /// <inheritdoc />
    public async Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
