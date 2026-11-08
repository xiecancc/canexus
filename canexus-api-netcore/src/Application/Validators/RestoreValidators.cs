
using Application.Contracts.Commands;
using Application.Validators.Common;

namespace Application.Validators;
/// <summary>参数配置恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class ConfigRestoreCommandValidator() : RestoreCommandValidator<ConfigRestoreCommand>;

/// <summary>部门恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class DeptRestoreCommandValidator() : RestoreCommandValidator<DeptRestoreCommand>;

/// <summary>字典数据恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class DictDataRestoreCommandValidator() : RestoreCommandValidator<DictDataRestoreCommand>;

/// <summary>字典类型恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class DictTypeRestoreCommandValidator() : RestoreCommandValidator<DictTypeRestoreCommand>;

/// <summary>定时任务恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class JobRestoreCommandValidator() : RestoreCommandValidator<JobRestoreCommand>;

/// <summary>菜单恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class MenuRestoreCommandValidator() : RestoreCommandValidator<MenuRestoreCommand>;

/// <summary>公告通知恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class NoticeRestoreCommandValidator() : RestoreCommandValidator<NoticeRestoreCommand>;

/// <summary>岗位恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class PostRestoreCommandValidator() : RestoreCommandValidator<PostRestoreCommand>;

/// <summary>角色恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class RoleRestoreCommandValidator() : RestoreCommandValidator<RoleRestoreCommand>;

/// <summary>租户恢复命令验证器（目标 Id 与乐观锁版本号入口兜底）。</summary>
public sealed class TenantRestoreCommandValidator() : RestoreCommandValidator<TenantRestoreCommand>;

/// <summary>用户恢复命令验证器（目标 Id 与乐观锁版本号入口兜底；数据范围守卫另行校验可见性）。</summary>
public sealed class UserRestoreCommandValidator() : RestoreCommandValidator<UserRestoreCommand>;
