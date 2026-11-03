using System.Linq.Expressions;
using Domain.Entities;
using NUnit.Framework;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 集成测试支撑：从测试数据库直读聚合当前乐观锁版本号。
/// <para>模拟真实前端「先查列表 / 详情取 version，再随写请求回传」的乐观锁交互时序。</para>
/// </summary>
internal static class ApiTestSupport
{
    /// <summary>测试专用 JWT 签名密钥（appsettings.json 已脱敏，集成测试经内存配置覆盖注入）。</summary>
    public const string TestJwtSecret = "TestOnly@Jwt#Secret-Key-0123456789-abcdef-2026";

    /// <summary>读取聚合当前乐观锁版本号。</summary>
    /// <typeparam name="TEntity">聚合实体类型。</typeparam>
    /// <param name="db">测试库 SqlSugar 客户端（与被测 API 同一 DI 容器 / 同一数据库）。</param>
    /// <param name="id">聚合 Id。</param>
    /// <returns>当前版本号。</returns>
    public static async Task<Guid> GetVersionAsync<TEntity>(ISqlSugarClient db, Guid id)
        where TEntity : AggregateBase, new()
    {
        return (await db.Queryable<TEntity>().Where(it => it.Id == id).FirstAsync()).Version;
    }

    /// <summary>按唯一键条件反查实体 Id（创建接口不返回 Id，测试经数据库直查）。</summary>
    /// <typeparam name="TEntity">聚合实体类型。</typeparam>
    /// <param name="db">测试库 SqlSugar 客户端（与被测 API 同一 DI 容器 / 同一数据库）。</param>
    /// <param name="predicate">唯一键条件。</param>
    /// <returns>实体 Id。</returns>
    public static async Task<Guid> GetEntityIdAsync<TEntity>(ISqlSugarClient db, Expression<Func<TEntity, bool>> predicate)
        where TEntity : AggregateBase, new()
    {
        var entity = await db.Queryable<TEntity>().Where(predicate).FirstAsync();
        Assert.That(entity, Is.Not.Null, "应能按唯一键反查到新建实体");
        return entity!.Id;
    }
}
