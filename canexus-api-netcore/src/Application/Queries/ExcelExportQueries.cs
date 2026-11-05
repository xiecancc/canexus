
using Application.Abstractions;
using Application.Contracts.Exports;
using Application.Contracts.Logs;
using Application.Contracts.Queries;
using Application.Contracts.Roles;
using Application.Contracts.Users;
using Domain.Shared.Enums;
using MediatR;
using MiniExcelLibs;

namespace Application.Queries;
/// <summary>Excel 导出共享支撑：分页循环全量拉取（每页 500、总上限 10000）与 xlsx 文件构建。</summary>
internal static class ExcelExportSupport
{
    /// <summary>每页拉取行数（与分页请求的 PageSize 上限一致）。</summary>
    public const int PageRows = 500;

    /// <summary>导出总行数上限（防止单次导出拖垮内存；超出部分截断并在文件名处如实导出已有行）。</summary>
    public const int MaxRows = 10000;

    /// <summary>按页循环拉取全量行（首次拉取后以 Total 收敛循环次数）。</summary>
    /// <typeparam name="TItem">分页行类型。</typeparam>
    /// <param name="fetchPage">按页拉取委托（入参页码，返回页行与总数）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全量行（不超过 <see cref="MaxRows"/>）。</returns>
    public static async Task<List<TItem>> CollectAsync<TItem>(
        Func<int, Task<(IReadOnlyList<TItem> Items, long Total)>> fetchPage,
        CancellationToken cancellationToken)
    {
        var (firstPage, total) = await fetchPage(1);
        var rows = new List<TItem>(firstPage);
        var pageCount = (int)Math.Ceiling(Math.Min(total, MaxRows) / (double)PageRows);
        for (var pageIndex = 2; pageIndex <= pageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (items, _) = await fetchPage(pageIndex);
            rows.AddRange(items);
        }

        return rows;
    }

    /// <summary>将导出行写入 xlsx 内存流并构建文件响应。</summary>
    /// <typeparam name="TRow">Excel 行类型（MiniExcel POCO，列名经 ExcelColumnName 标注）。</typeparam>
    /// <param name="sheetName">工作表名。</param>
    /// <param name="rows">导出行集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导出文件响应（字节 + 建议文件名 + MIME）。</returns>
    public static async Task<ExportFileResponse> BuildExcelAsync<TRow>(
        string sheetName, IEnumerable<TRow> rows, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await stream.SaveAsAsync(rows, sheetName: sheetName, cancellationToken: cancellationToken);
        return new ExportFileResponse
        {
            Content = stream.ToArray(),
            FileName = $"{sheetName}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx"
        };
    }

    /// <summary>状态枚举转中文文本。</summary>
    public static string ToText(CommonStatus status) => status switch
    {
        CommonStatus.Enabled => "启用",
        CommonStatus.Disabled => "停用",
        _ => status.ToString()
    };

    /// <summary>执行状态枚举转中文文本。</summary>
    public static string ToText(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Success => "成功",
        ExecutionStatus.Failure => "失败",
        _ => status.ToString()
    };

    /// <summary>数据范围枚举转中文文本。</summary>
    public static string ToText(DataScope scope) => scope switch
    {
        DataScope.All => "全部",
        DataScope.Custom => "自定义",
        DataScope.Dept => "本部门",
        DataScope.DeptAndBelow => "本部门及以下",
        DataScope.Self => "仅本人",
        _ => scope.ToString()
    };

    /// <summary>操作业务类型枚举转中文文本。</summary>
    public static string ToText(OperBusinessType type) => type switch
    {
        OperBusinessType.Add => "新增",
        OperBusinessType.Update => "修改",
        OperBusinessType.Delete => "删除",
        OperBusinessType.Grant => "授权",
        OperBusinessType.Export => "导出",
        OperBusinessType.Import => "导入",
        OperBusinessType.Restore => "恢复",
        OperBusinessType.Clean => "清空",
        OperBusinessType.Force => "强退",
        _ => type.ToString()
    };
}

/// <summary>用户列表导出查询处理器（复用用户分页查询同口径筛选与数据范围过滤，经 ISender 分页循环拉取）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 发送器（复用 UserQueryPaged 的筛选 / 数据范围 / 关联组装逻辑）。</param>
public sealed class UserExportQueryHandler(ISender sender) : QueryHandler<UserExportQuery, ExportFileResponse>
{
    /// <inheritdoc />
    protected override async Task<ExportFileResponse> HandleAsync(UserExportQuery query, CancellationToken cancellationToken)
    {
        var rows = await ExcelExportSupport.CollectAsync<UserPagedResponse>(async pageIndex =>
        {
            query.Request.PageIndex = pageIndex;
            query.Request.PageSize = ExcelExportSupport.PageRows;
            var page = await sender.Send(new UserQueryPaged(query.Request), cancellationToken);
            return (page.Items, page.Total);
        }, cancellationToken);

        var excelRows = rows.Select(row => new UserExportRow
        {
            UserName = row.UserName,
            NickName = row.NickName,
            DeptName = row.DeptName ?? string.Empty,
            Email = row.Email,
            Phone = row.Phone,
            Roles = string.Join("、", row.Roles.Select(role => role.RoleName)),
            Status = ExcelExportSupport.ToText(row.Status),
            CreatedAt = row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        });

        return await ExcelExportSupport.BuildExcelAsync("用户列表", excelRows, cancellationToken);
    }
}

/// <summary>角色列表导出查询处理器（复用角色分页查询同口径筛选）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 发送器。</param>
public sealed class RoleExportQueryHandler(ISender sender) : QueryHandler<RoleExportQuery, ExportFileResponse>
{
    /// <inheritdoc />
    protected override async Task<ExportFileResponse> HandleAsync(RoleExportQuery query, CancellationToken cancellationToken)
    {
        var rows = await ExcelExportSupport.CollectAsync<RolePagedResponse>(async pageIndex =>
        {
            query.Request.PageIndex = pageIndex;
            query.Request.PageSize = ExcelExportSupport.PageRows;
            var page = await sender.Send(new RoleQueryPaged(query.Request), cancellationToken);
            return (page.Items, page.Total);
        }, cancellationToken);

        var excelRows = rows.Select(row => new RoleExportRow
        {
            RoleName = row.RoleName,
            RoleKey = row.RoleKey,
            DataScope = ExcelExportSupport.ToText(row.DataScope),
            Status = ExcelExportSupport.ToText(row.Status),
            CreatedAt = row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        });

        return await ExcelExportSupport.BuildExcelAsync("角色列表", excelRows, cancellationToken);
    }
}

/// <summary>操作日志导出查询处理器（复用操作日志分页查询同口径筛选）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 发送器。</param>
public sealed class OperLogExportQueryHandler(ISender sender) : QueryHandler<OperLogExportQuery, ExportFileResponse>
{
    /// <inheritdoc />
    protected override async Task<ExportFileResponse> HandleAsync(OperLogExportQuery query, CancellationToken cancellationToken)
    {
        var rows = await ExcelExportSupport.CollectAsync<OperLogPagedResponse>(async pageIndex =>
        {
            query.Request.PageIndex = pageIndex;
            query.Request.PageSize = ExcelExportSupport.PageRows;
            var page = await sender.Send(new OperLogQueryPaged(query.Request), cancellationToken);
            return (page.Items, page.Total);
        }, cancellationToken);

        var excelRows = rows.Select(row => new OperLogExportRow
        {
            Title = row.Title,
            BusinessType = ExcelExportSupport.ToText(row.BusinessType),
            Method = row.Method,
            RequestMethod = row.RequestMethod,
            OperUserName = row.OperUserName ?? string.Empty,
            OperIp = row.OperIp,
            RequestUrl = row.RequestUrl,
            Status = ExcelExportSupport.ToText(row.Status),
            CostMs = row.CostMs,
            OperTime = row.OperTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty
        });

        return await ExcelExportSupport.BuildExcelAsync("操作日志", excelRows, cancellationToken);
    }
}

/// <summary>登录日志导出查询处理器（复用登录日志分页查询同口径筛选）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 发送器。</param>
public sealed class LoginLogExportQueryHandler(ISender sender) : QueryHandler<LoginLogExportQuery, ExportFileResponse>
{
    /// <inheritdoc />
    protected override async Task<ExportFileResponse> HandleAsync(LoginLogExportQuery query, CancellationToken cancellationToken)
    {
        var rows = await ExcelExportSupport.CollectAsync<LoginLogPagedResponse>(async pageIndex =>
        {
            query.Request.PageIndex = pageIndex;
            query.Request.PageSize = ExcelExportSupport.PageRows;
            var page = await sender.Send(new LoginLogQueryPaged(query.Request), cancellationToken);
            return (page.Items, page.Total);
        }, cancellationToken);

        var excelRows = rows.Select(row => new LoginLogExportRow
        {
            UserName = row.UserName,
            Ip = row.Ip,
            LoginLocation = row.LoginLocation,
            Status = ExcelExportSupport.ToText(row.Status),
            Msg = row.Msg,
            LoginTime = row.LoginTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty
        });

        return await ExcelExportSupport.BuildExcelAsync("登录日志", excelRows, cancellationToken);
    }
}
