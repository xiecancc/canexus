# DTO 基类家族与乐观锁版本（Version）改造清单

> 日期：2026-10-31（第二轮按用户新定向重整后的最终形态） ｜ 累计未提交批次
> 原则：DTO / Query / Command / Handler 的继承体系镜像领域与仓储继承（DomainBase → AggregateBase；IDomainRepository → IAggregateRepository → IAggregateTreeRepository）。

## 一、基类家族（最终形态，含用户手动分层的 Aggregate 级）

### Application.Contracts/Common/DomainDtos.cs（Domain 级，镜像 DomainBase）

| 类型 | 说明 |
| --- | --- |
| `DomainRequest` | 请求载荷根（新增 / 修改公共属性挂载点，仅作 `AggregateRequest` 的父根） |
| `DomainListRequest` | 非分页列表查询根（预留） |
| `DomainPagedRequest : PagedRequest` | 分页查询根，继承领域分页契约 |
| `DomainListResponse` | 轻量读模型根（下拉简项 / 日志 VO） |
| `DomainPagedResponse` | 镜像 DomainBase：`TenantId?` + `CreatedAt` + `CreatedBy` |

### Application.Contracts/Common/AggregateDtos.cs（Aggregate 级，镜像 AggregateBase）

| 类型 | 说明 |
| --- | --- |
| `AggregateRequest : DomainRequest` | 聚合请求载荷根：新增 / 修改公共属性挂载点（Id/Version/审计由插入时生成） |
| `AggregateListRequest : DomainListRequest` | 聚合列表查询根（预留） |
| `AggregatePagedRequest : DomainPagedRequest` | 聚合分页查询根 |
| `AggregateDeleteRequest` | 删除载荷：`Id` + `Version`（乐观锁；Id 以路由为准） |
| `AggregateRestoreRequest` | 恢复载荷：`Id` + `Version`（预留） |
| `AggregateListResponse : DomainListResponse` | 聚合轻量读模型根 |
| `AggregatePagedResponse : DomainPagedResponse` | 镜像 AggregateBase：`Id` + `Version` + `UpdatedAt?` + `UpdatedBy?` + `Description?`（IsDeleted 不外泄，软删语义由查询过滤与恢复请求承载）；派生 VO 作为 `PagedResponse<T>` 泛型参数返回 |

### CommonDtos.cs 已移除（第四轮：全模块 DTO 按实体属性重挂）

`StatusRequest` / `StatusDescriptionRequest` 删除，其职责由「镜像实体属性」取代（写入基类现挂接 `AggregateRequest`，原 `AggregateCreateRequest` 已更名）：

- 8 个聚合模块写入基类（User/Role/Dept/Menu/Notice/DictData/Job/Tenant WriteRequest）改挂 `AggregateRequest`，并**按实体属性自声明** `Status`（CommonStatus，缺省 Enabled）与 `Description`
- SysConfig / SysDictType 无状态字段：ConfigWriteRequest / DictTypeWriteRequest 仅换挂 `AggregateRequest`，不声明 Status
- Dept/Menu 写入 DTO 此前缺失 Description（实体具备），本次补齐并接线处理器（DeptCreate/DeptUpdate 显式赋值；Menu 走 AutoMapper 同名映射）
- `UserStatusRequest` / `JobStatusRequest` 轻量状态请求改为独立类自声明 Status（保留 Version）
- `DomainCreateRequest` 不再被模块直接继承，仅作 `AggregateCreateRequest` 的父根

### 已删除类型

`DomainRequest`、`DomainResponse`、`DomainStatusResponse`、`DomainEntityResponse`（更名为 `DomainPagedResponse`）、`IVersionedRequest`、原 `EntityVO`/`StatusVO`。

## 二、乐观锁闭环（Version）

- 14 个更新类 DTO 携带 `Guid Version`（User/Role/Dept/Menu/Notice/Config/DictType/DictData/Tenant/Job 的 Update + UserStatus/UserPassword/UserRoles/JobStatus）；不再声明接口，版本属性直接声明在 DTO 上。
- 10 个 `XxxDeleteRequest : AggregateDeleteRequest`；10 个删除命令 `(Guid Id, Guid Version)` 实现 `IDeleteCommand`（含 Version）。
- `CommandGuards.EnsureVersionMatches(entity, expectedVersion)`：版本不一致抛 40005；置于业务守卫之后、写操作之前。调用点：显式处理器 14 处 + 5 个基础更新模块（Notice/Config/Tenant/DictType/DictData）的 `ValidateAsync` 钩子末尾（基类 `UpdateCommandHandler` 不再强约束接口，版本比对由钩子负责）。

## 三、VO 变更

- 挂接 `AggregatePagedResponse`（自动获得 Id/Version/UpdatedAt/UpdatedBy/Description + 基类 TenantId/CreatedAt/CreatedBy）：`UserBaseVO`、`RoleBaseVO`、`DeptBaseVO`、`MenuBaseVO`、`NoticeBaseVO`、`ConfigListVO`、`DictTypeListVO`、`DictDataListVO`、`JobListVO`、`TenantListVO`、`JobLogListVO`、`NoticeLatestVO`；8 处与基类重复的 `Description` 声明已移除（请求侧保留）
- 挂接 `AggregatePagedResponse` + 自声明 `Status`：`DictDataListVO`、`JobListVO`、`TenantListVO`、`JobLogListVO`
- 挂接 `DomainListResponse`：`RoleSimpleVO`、`TenantSimpleVO`、`DictDataSimpleVO`、`OperLogListVO`、`LoginLogListVO`（日志不参与乐观锁）

## 四、API 契约（前端 admin-vue 待跟进）

1. 列表 / 详情响应新增 `version` 字段；2. 所有 PUT（含 status/password/roles）须回传 `version`；3. DELETE 端点须携带请求体 `{ "version": "..." }`；4. 版本不匹配返回 40005。

## 五、测试

- `test/API.Tests/ApiTestSupport.cs`：`GetVersionAsync<TEntity>(db, id)` 直读测试库聚合版本，模拟前端「先查后写」。
- 6 个集成测试文件的成功写操作先回读 version；负向用例以 `new { version = Guid.Empty }` 满足绑定（守卫先于版本校验）。
- 结果：Domain 45 / Infrastructure 37 / Application 37 / API 56，共 175 全绿。

## 六、读模型命名规范统一（去 VO 化）

全部 VO 标识符按父类命名规范（`*Response`）重命名，处理器泛型参数 `TVo` → `TResponse`，文件 `RouteVO.cs` → `RouteResponse.cs`：

| 原名 | 新名 |
| --- | --- |
| `UserBaseVO` / `UserListVO` / `UserDetailVO` / `UserRoleItemVO` | `UserBaseResponse` / `UserListResponse` / `UserDetailResponse` / `UserRoleItemResponse` |
| `RoleBaseVO` / `RoleListVO` / `RoleSimpleVO` / `RoleDetailVO` | `RoleBaseResponse` / `RoleListResponse` / `RoleSimpleResponse` / `RoleDetailResponse` |
| `DeptBaseVO` / `DeptTreeVO` / `DeptDetailVO` | `DeptBaseResponse` / `DeptTreeResponse` / `DeptDetailResponse` |
| `MenuBaseVO` / `MenuTreeVO` / `MenuDetailVO` | `MenuBaseResponse` / `MenuTreeResponse` / `MenuDetailResponse` |
| `NoticeBaseVO` / `NoticeListVO` / `NoticeLatestVO` | `NoticeBaseResponse` / `NoticeListResponse` / `NoticeLatestResponse` |
| `ConfigListVO` / `DictTypeListVO` / `DictDataListVO` / `DictDataSimpleVO` | `ConfigListResponse` / `DictTypeListResponse` / `DictDataListResponse` / `DictDataSimpleResponse` |
| `JobListVO` / `JobLogListVO` / `TenantListVO` / `TenantSimpleVO` | `JobListResponse` / `JobLogListResponse` / `TenantListResponse` / `TenantSimpleResponse` |
| `OperLogListVO` / `LoginLogListVO` / `RouteVO` | `OperLogListResponse` / `LoginLogListResponse` / `RouteResponse` |

JSON 字段名（camelCase 属性序列化）不变，API 契约兼容。

## 七、CommonProfile 基类映射（第 8 轮：完整读 / 写双向基类链）

```csharp
// 读方向（实体基类 → DTO 响应基类）
CreateMap<DomainBase, DomainListResponse>();
CreateMap<AggregateBase, AggregateListResponse>().IncludeBase<DomainBase, DomainListResponse>();
CreateMap<DomainBase, DomainPagedResponse>();
CreateMap<AggregateBase, AggregatePagedResponse>().IncludeBase<DomainBase, DomainPagedResponse>();

// 写方向（DTO 请求基类 → 实体基类，模板链）
CreateMap<DomainRequest, DomainBase>();
CreateMap<AggregateRequest, AggregateBase>().IncludeBase<DomainRequest, DomainBase>();
```

读模型审计字段（TenantId/CreatedAt/CreatedBy/Id/Version/UpdatedAt/UpdatedBy/Description）按同名约定自动映射；
写方向为模板链——AggregateRequest 当前为空挂载点，未来新增公共属性时各模块 IncludeBase 派生映射自动继承。

## 八、模块 Profile 挂接 IncludeBase（第 8 轮）

10 个模块 Profile（SysUser/Role/Dept/Menu/Notice/Config/Dict/Job/Log/Tenant）统一：

- 「实体 → Response」派生映射追加 `.IncludeBase<AggregateBase, AggregatePagedResponse>()`（Simple 下拉项为 `.IncludeBase<AggregateBase, AggregateListResponse>()`）；
- 「请求 → 实体」派生映射追加 `.IncludeBase<AggregateRequest, AggregateBase>()`；
- 注释「实体 → 视图对象」更名为「实体 → 响应模型」；统一引入 `Application.Contracts.Common`。

注意：TreeMappingTests 的自定义 MapperConfiguration 需同步注册 `CommonProfile`（IncludeBase 依赖同配置内的基类映射），已修复。
验证：`dotnet build` 0 警告 0 错误；测试 175 全绿（Domain 45 / Infrastructure 37 / Application 37 / API 56）。
