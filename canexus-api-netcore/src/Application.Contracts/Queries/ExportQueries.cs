using Application.Contracts.Abstractions;
using Application.Contracts.Exports;
using Application.Contracts.Logs;
using Application.Contracts.Roles;
using Application.Contracts.Users;

namespace Application.Contracts.Queries;
/// <summary>导出用户列表查询（复用用户分页同口径筛选与数据范围过滤；行数上限 10000，超出截断）。</summary>
/// <param name="Request">用户分页筛选条件（分页参数被忽略，全量按筛选拉取）。</param>
public sealed record UserExportQuery(UserPageRequest Request) : Request<ExportFileResponse>;

/// <summary>导出角色列表查询（复用角色分页同口径筛选；行数上限 10000，超出截断）。</summary>
/// <param name="Request">角色分页筛选条件（分页参数被忽略，全量按筛选拉取）。</param>
public sealed record RoleExportQuery(RolePageRequest Request) : Request<ExportFileResponse>;

/// <summary>导出操作日志查询（复用操作日志分页同口径筛选；行数上限 10000，超出截断）。</summary>
/// <param name="Request">操作日志分页筛选条件（分页参数被忽略，全量按筛选拉取）。</param>
public sealed record OperLogExportQuery(OperLogPageRequest Request) : Request<ExportFileResponse>;

/// <summary>导出登录日志查询（复用登录日志分页同口径筛选；行数上限 10000，超出截断）。</summary>
/// <param name="Request">登录日志分页筛选条件（分页参数被忽略，全量按筛选拉取）。</param>
public sealed record LoginLogExportQuery(LoginLogPageRequest Request) : Request<ExportFileResponse>;
