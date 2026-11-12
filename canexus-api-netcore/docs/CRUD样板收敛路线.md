# CRUD 样板收敛路线（P2-1）

> 对应代码评审报告 E1 / B5：20 个控制器逐字重复 CRUD 六件套、62 个写命令两套实现并行、
> 版本常量未联动。本报告为「排期优化」项，按 P3「不为规范化而规范化」原则，**不强行全量重构**，
> 采取「低风险联动 + 明确路线 + 分批验证」的务实路径。

## 一、现状（核实于 2026-11-12）

| 项 | 数量 | 说明 |
| --- | --- | --- |
| 控制器 | 20 个 / 约 1884 行 | 分页 / 详情 / 创建 / 更新 / 删除 / 恢复六件套高度同构，另含下拉、导出等模块特有端点 |
| 写命令 | 62 个 | 52 个 `Request<bool>`、5 个无返回值、4 个返回 DTO；标准流程处理器用 `AggregateCreate/Update/Delete/RestoreCommandHandler` 基类，复杂编排（关联表替换 / 树形移动 / 口令哈希 / 物理先行上传）保持显式实现（见第三节结论） |
| 验证器 | 每模块一组 | 启动期 `WriteCommandValidatorStartupValidator` 校验覆盖 |
| API 版本 | 20 个控制器统一引用 `ApiVersioningExtensions.CurrentVersion`（已联动，见第二节） | 破坏性版本发布只需改一处常量 |

## 二、已完成

**版本常量联动（2026-11-12）**：20 个控制器统一改为 `[ApiVersion(ApiVersioningExtensions.CurrentVersion)]`
（`public const string`，编译期内联为 `"1.0"`，运行时行为完全不变），并补 `using API.Extensions;`。
破坏性变更发布 v2 时只需改一处常量，避免 20 处漏改。

**第二批：Job 模块迁移（2026-11-13）**：JobCommands 的 5 个标准流程处理器
（创建 / 删除 / 恢复 / 更新 / 状态更新）迁移到 `Aggregate*CommandHandler` 基类，
命令契约、错误码文案、领域事件与调度同步时序完全不变（调度同步仍由
JobScheduleSynchronizationHandler 在 Outbox 提交后投递）；`JobRunOnceCommandHandler`
无返回值、非 CRUD，保持显式实现。验证：Application.Tests Job 过滤 18 项、
API.Tests JobApiTests 6 项（创建 / 启停 / 立即执行 / 删除 / 更新重调度 / 404 契约）全绿。

## 二之一、其余手工处理器的保留结论（核实于 2026-11-13）

原路线把 UserCommands、DeptCommands、MenuCommands 列为迁移候选，逐个核实后确认它们
**正是基类文档明确要求显式实现的复杂编排**，强行迁移需改基类或改变事件时序，收益低于风险：

| 处理器 | 保留原因 |
| --- | --- |
| RoleCreate / RoleUpdate / RoleDelete | 角色-菜单授权关联全删全插 / 级联物理清理（基类文档：涉及关联表替换或事务编排应显式实现） |
| UserCreate / UserUpdate / UserDelete | 口令哈希旁路 + 用户-角色关联全删全插 / 级联清理 |
| DeptUpdate / MenuUpdate | 更新与发布事件之间存在树形 MoveAsync（环检测 / 改父），基类无此间插钩子，迁移会改变事件携带的聚合状态 |
| FileUploadCommandHandler | 物理文件先行落盘 + 元数据失败孤儿补偿 + 返回 DTO（非 bool） |
| JobRunOnceCommandHandler | 无返回值、触发执行不写聚合，非 CRUD 流程 |
| LogCommands 各清理 / 删除 | 日志实体非聚合基（无乐观锁 / 软删除），`Aggregate*CommandHandler` 约束不适用 |

结论：后续批次不再迁移上述处理器；剩余可收敛点仅第三节第 3 点的恢复泛型化（待评估）。

## 三、为何暂不做全量泛型基类 / Source Generator

1. **特性参数必须是编译期常量**：`[Route]`、`[RequirePermission]`、`[HttpGet]` 等在不同模块取值不同，
   泛型基类无法用类型参数表达；派生类仍需逐个声明特性与 Action，能省下的主要是 `sender.Send(...)` 的
   一行样板，收益有限。
2. **命令返回类型三套并存**（B6）：52 个 `bool`、5 个无返回值、4 个 DTO，
   泛型基类必须引入更多类型参数与适配器，复杂度反而上升。
3. **已有基类是有效资产**：`AggregateCreate/Update/Delete/RestoreCommandHandler` 已封装快照 / 版本 / 事件，
   标准流程处理器已全部迁移使用（第二批完成）；剩余手工实现均为合法复杂编排（见「二之一」结论），
   迁移需引入新钩子或改变事件时序，收益低于风险。
4. **启动期门禁已兜住漏项**：`CommandPermissionStartupValidator`（漏标命令策略）、
   `WriteCommandValidatorStartupValidator`（缺验证器）、`PermissionsStartupValidator`（权限注册）
   已覆盖新增命令最容易出错的部分；样板重复属可读性成本，不是正确性风险。
5. **Source Generator 成本高**：需新增生成器项目、接入 11 个 csproj 的构建、处理增量生成与调试，
   且与 `TreatWarningsAsErrors` + 强制 XML 注释组合容易产生难诊断的构建错误，当前规模不划算。

## 四、后续路线（按模块分批，每批走完整交付门禁）

1. ~~**第二批**：把仍手工实现快照 / 版本 / 事件的写命令逐个迁移到基类~~
   **已完成（2026-11-13）**：可迁移的标准流程处理器（Job 模块 5 个）已全部迁移；
   其余手工处理器经逐个核实均为合法复杂编排，保留结论见「二之一」章节，不再迁移。
2. **第三批**：对 11 个几乎逐字相同的 Restore Handler，抽一个泛型 `RestoreCommandHandler<TEntity>`
   （命令携带 `Id` + `Version`），用启动期扫描确保无遗漏。注：各模块 Restore 处理器已统一继承
   `AggregateRestoreCommandHandler` 基类，逐字重复仅剩「类声明 + CreateEvent 一行」；
   再抽一层泛型的收益已趋近于零，除非后续新增大量聚合，否则**暂不做**。
3. **第四批**：若届时确有必要，再评估 `CrudControllerBase` 只收敛「公共受保护辅助方法」
   （统一 `Ok(Result.Ok(...))` 包装、取消令牌透传），**不**强行统一特性与路由。
4. 每批都必须同步 README 的目录结构 / 变更记录，并通过 README 类型校验与格式校验。

## 五、明确不做

- 不为「看起来更 DDD」重写为充血聚合（P3-2）。
- 不为移除 Domain 层的 SqlSugar 特性大改实体标注（P3-3）。
- 不统一文件下载 / 健康检查为 `Result` 包装（P3-4，属合理例外）。
- 不引入 ABP / EF Core / Auto API（P3-1 / P3-6）。
- 不为领域实体变更引入迁移框架或列同步（P3-8）；开发态继续走删库重建。
