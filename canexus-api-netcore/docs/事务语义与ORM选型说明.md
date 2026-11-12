# 事务语义与 ORM 选型说明

本文是 `test/Infrastructure.Tests/Architecture/TransactionBoundaryArchitectureTests.cs`
白名单的**语义依据**，随仓库提交（避免理由链依赖本地 `.docs/` 分析报告）。
白名单条目必须与真实代码一致：新增越界写法要么改走抽象，要么在本文第二节登记理由；
删除实现而不删白名单同样会被架构测试判失败。

## 一、选型结论

- ORM：SqlSugar（CodeFirst 建表、Queryable / 仓储抽象、工作单元）。
- 不引入 `IAmbientSession` 一类的自定义会话抽象：SqlSugar 的 `CopyNew()` 本身就是
  「独立连接 / 独立事务作用域」的标准表达，额外抽象只会掩盖真实边界。
- 事务边界由 `IUnitOfWork` + `TransactionBehavior` 统一收敛；业务命令处理器不应直接
  开启连接或执行原生 SQL。
- 结构初始化、迁移、健康检查等基础设施场景没有实体语义（或必须绕开 ORM 的 SQL 生成），
  允许使用裸 `Ado`，但限定在 `BareAdoWhitelist` 列举的文件内。

## 二、CopyNew() 白名单语义依据

| 文件 | 用途 | 为何必须独立于业务事务 |
| --- | --- | --- |
| `src/Infrastructure/Auth/TokenVersionService.cs` | 全量令牌版本递增 | 吊销 / 版本递增必须在业务事务回滚后仍然存活，否则回滚会「撤销」吊销动作 |
| `src/Infrastructure/Repositories/LoginLogRepository.cs` | 登录日志落库 | 登录失败等场景业务事务回滚，审计日志仍须保留 |
| `src/Infrastructure/Repositories/OutboxMessageRepository.cs` | Outbox 消息认领 | 认领状态独立于业务事务，避免回滚导致消息重复投递 |
| `src/Infrastructure/Repositories/SysRefreshTokenRepository.cs` | 刷新令牌吊销 / 轮换 | 同上，吊销结果不得随业务回滚消失 |

新增 `CopyNew()` 前先确认是否属于「审计 / 认领 / 吊销必须存活于业务事务回滚之外」；
若只是想绕开当前连接，应改走仓储 / 工作单元抽象，而不是扩大白名单。

## 三、裸 Ado 白名单语义依据

仅限数据访问基础设施执行原生 SQL：结构探测、迁移、初始化、模型校验、种子、
健康检查、工作单元、过滤器注册、聚合仓储的批量写入。这些场景要么查询的是
`information_schema` 等无实体映射的系统视图，要么需要 ORM 无法表达的数据库方言 DDL。
业务查询一律走 Queryable / 仓储；应用层与 API 层出现 `.Ado.` 即测试失败。

## 四、与开发 / 生产工作流的关系

- 开发态：数据库可直接 `DROP DATABASE` 后重启，由缺表路径的 CodeFirst 全量重建，
  **不保留迁移历史、不需要列同步**。
- 生产态：禁止应用自动改表，结构由外部脚本初始化；`ISchemaMigration` 通道用于
  CodeFirst 无法表达的结构变更（索引 / 约束 / 数据订正），当前零实现属有意预留。
- 本说明只约束事务与会话边界，不改变上述工作流。
