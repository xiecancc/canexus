# CRUD 样板收敛路线（P2-1）

> 对应代码评审报告 E1 / B5：20 个控制器逐字重复 CRUD 六件套、62 个写命令两套实现并行、
> 版本常量未联动。本报告为「排期优化」项，按 P3「不为规范化而规范化」原则，**不强行全量重构**，
> 采取「低风险联动 + 明确路线 + 分批验证」的务实路径。

## 一、现状（核实于 2026-11-12）

| 项 | 数量 | 说明 |
| --- | --- | --- |
| 控制器 | 20 个 / 约 1884 行 | 分页 / 详情 / 创建 / 更新 / 删除 / 恢复六件套高度同构，另含下拉、导出等模块特有端点 |
| 写命令 | 62 个 | 52 个 `Request<bool>`、5 个无返回值、4 个返回 DTO；约半数模块直接继承 `RequestHandler` 手工快照 / 版本 / 事件，其余用 `AggregateCreate/Update/Delete/RestoreCommandHandler` 基类 |
| 验证器 | 每模块一组 | 启动期 `WriteCommandValidatorStartupValidator` 校验覆盖 |
| API 版本 | 20 个控制器硬编码 `[ApiVersion("1.0")]` | 与 `ApiVersioningExtensions.CurrentVersion` 未联动 |

## 二、已完成（本批）

**版本常量联动**：20 个控制器统一改为 `[ApiVersion(ApiVersioningExtensions.CurrentVersion)]`
（`public const string`，编译期内联为 `"1.0"`，运行时行为完全不变），并补 `using API.Extensions;`。
破坏性变更发布 v2 时只需改一处常量，避免 20 处漏改。

## 三、为何暂不做全量泛型基类 / Source Generator

1. **特性参数必须是编译期常量**：`[Route]`、`[RequirePermission]`、`[HttpGet]` 等在不同模块取值不同，
   泛型基类无法用类型参数表达；派生类仍需逐个声明特性与 Action，能省下的主要是 `sender.Send(...)` 的
   一行样板，收益有限。
2. **命令返回类型三套并存**（B6）：52 个 `bool`、5 个无返回值、4 个 DTO，
   泛型基类必须引入更多类型参数与适配器，复杂度反而上升。
3. **已有基类是有效资产**：`AggregateCreate/Update/Delete/RestoreCommandHandler` 已封装快照 / 版本 / 事件，
   约半数模块使用；把另一半迁移过去才是真正的收敛点，但涉及写命令语义与审计事件，
   需逐模块回归（乐观锁、软删除版本轮换、Outbox 事件），风险高于「控制器泛型化」。
4. **启动期门禁已兜住漏项**：`CommandPermissionStartupValidator`（漏标命令策略）、
   `WriteCommandValidatorStartupValidator`（缺验证器）、`PermissionsStartupValidator`（权限注册）
   已覆盖新增命令最容易出错的部分；样板重复属可读性成本，不是正确性风险。
5. **Source Generator 成本高**：需新增生成器项目、接入 11 个 csproj 的构建、处理增量生成与调试，
   且与 `TreatWarningsAsErrors` + 强制 XML 注释组合容易产生难诊断的构建错误，当前规模不划算。

## 四、后续路线（按模块分批，每批走完整交付门禁）

1. **第二批**：把仍手工实现快照 / 版本 / 事件的写命令（如 UserCommands、DeptCommands、MenuCommands 等）
   逐个迁移到 `Aggregate*CommandHandler` 基类，保持命令契约与领域事件不变；
   每迁移一个模块跑该模块的单测 + API 集成测试，确认乐观锁与事件发布等价。
2. **第三批**：对 11 个几乎逐字相同的 Restore Handler，抽一个泛型 `RestoreCommandHandler<TEntity>`
   （命令携带 `Id` + `Version`），用启动期扫描确保无遗漏。
3. **第四批**：若届时确有必要，再评估 `CrudControllerBase` 只收敛「公共受保护辅助方法」
   （统一 `Ok(Result.Ok(...))` 包装、取消令牌透传），**不**强行统一特性与路由。
4. 每批都必须同步 README 的目录结构 / 变更记录，并通过 README 类型校验与格式校验。

## 五、明确不做

- 不为「看起来更 DDD」重写为充血聚合（P3-2）。
- 不为移除 Domain 层的 SqlSugar 特性大改实体标注（P3-3）。
- 不统一文件下载 / 健康检查为 `Result` 包装（P3-4，属合理例外）。
- 不引入 ABP / EF Core / Auto API（P3-1 / P3-6）。
- 不为领域实体变更引入迁移框架或列同步（P3-8）；开发态继续走删库重建。
