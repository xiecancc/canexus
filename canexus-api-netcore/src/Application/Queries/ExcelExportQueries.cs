
using Application.Abstractions;
using Application.Contracts.Exports;
using Application.Contracts.Logs;
using Application.Contracts.Queries;
using Application.Contracts.Roles;
using Application.Contracts.Users;
using Domain.Shared.Enums;
using Infrastructure.Shared.Options;
using MediatR;
using Microsoft.Extensions.Options;
using MiniExcelLibs;

namespace Application.Queries;
/// <summary>分页循环拉取结果：已导出行与截断标记。</summary>
/// <typeparam name="TItem">分页行类型。</typeparam>
/// <param name="Rows">已导出行（不超过 Export:MaxRows 配置上限）。</param>
/// <param name="Truncated">是否仍有数据未导出（达到行数上限或上游分页口径差异）。</param>
internal sealed record ExportCollectResult<TItem>(IReadOnlyList<TItem> Rows, bool Truncated);

/// <summary>Excel 导出共享支撑：分页循环全量拉取（每页 500、总上限 10000，硬裁剪）与 xlsx 文件构建。</summary>
internal static class ExcelExportSupport
{
    /// <summary>每页拉取行数（与分页请求的 PageSize 上限一致）。</summary>
    public const int PageRows = 500;

    /// <summary>导出总行数上限（防止单次导出拖垮内存；超出部分截断并在文件名处如实导出已有行）。</summary>
    public const int MaxRows = 10000;

    /// <summary>按页循环拉取全量行（首页前检查取消；总行数硬裁剪至 maxRows，含单页超额场景）。</summary>
    /// <typeparam name="TItem">分页行类型。</typeparam>
    /// <param name="fetchPage">按页拉取委托（入参页码与取消令牌，返回页行与总数）。</param>
    /// <param name="maxRows">本次导出允许的最大行数（来自 Export:MaxRows；未注入配置时用 <see cref="MaxRows"/>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已导出行（不超过 maxRows）与是否发生截断。</returns>
    public static async Task<ExportCollectResult<TItem>> CollectAsync<TItem>(
        Func<int, CancellationToken, Task<(IReadOnlyList<TItem> Items, long Total)>> fetchPage,
        int maxRows,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = maxRows > 0 ? maxRows : MaxRows;
        var (firstPage, total) = await fetchPage(1, cancellationToken);
        var rows = new List<TItem>(Math.Min(limit, Math.Max(firstPage.Count, 0)));
        AppendWithinLimit(rows, firstPage, limit);

        var pageCount = (int)Math.Ceiling(Math.Min(total, limit) / (double)PageRows);
        for (var pageIndex = 2; pageIndex <= pageCount && rows.Count < limit; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (items, _) = await fetchPage(pageIndex, cancellationToken);
            AppendWithinLimit(rows, items, limit);
        }

        // 如实反映「还有数据未导出」：总数大于实际导出行数即为截断（无论因上限还是上游分页口径差异）
        return new ExportCollectResult<TItem>(rows, total > rows.Count);
    }

    /// <summary>按剩余额度追加当页行，绝不越过行数上限。</summary>
    /// <typeparam name="TItem">分页行类型。</typeparam>
    /// <param name="rows">累计行集合。</param>
    /// <param name="items">当页拉取的行。</param>
    /// <param name="limit">本次导出的行数上限。</param>
    private static void AppendWithinLimit<TItem>(List<TItem> rows, IReadOnlyList<TItem> items, int limit)
    {
        var remaining = limit - rows.Count;
        if (remaining <= 0 || items.Count == 0)
        {
            return;
        }

        rows.AddRange(items.Take(remaining));
    }

    /// <summary>将导出行写入 xlsx 内存流并构建文件响应（截断时在文件名标注实际导出行数）。</summary>
    /// <typeparam name="TRow">Excel 行类型（MiniExcel POCO，列名经 ExcelColumnName 标注）。</typeparam>
    /// <param name="sheetName">工作表名。</param>
    /// <param name="rows">导出行集合。</param>
    /// <param name="truncated">是否因行数上限发生截断。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导出文件响应（字节 + 建议文件名 + MIME）。</returns>
    public static async Task<ExportFileResponse> BuildExcelAsync<TRow>(
        string sheetName, IEnumerable<TRow> rows, bool truncated, CancellationToken cancellationToken)
    {
        var rowList = rows as IReadOnlyList<TRow> ?? rows.ToList();
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        await stream.SaveAsAsync(rowList, sheetName: sheetName, cancellationToken: cancellationToken);
        var suffix = truncated ? $"_已截断{rowList.Count}行" : string.Empty;
        return new ExportFileResponse
        {
            Content = stream.ToArray(),
            FileName = $"{sheetName}_{DateTime.UtcNow:yyyyMMddHHmmss}{suffix}.xlsx"
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
/// <param name="exportOptions">导出阈值（Export:MaxRows，EX-05 阈值 Option 化）。</param>
public sealed class UserExportQueryHandler(ISender sender, IOptionsMonitor<ExportOption> exportOptions) : QueryHandler<UserExportQuery, ExportFileResponse>
{
    /// <inheritdoc />
    protected override async Task<ExportFileResponse> HandleAsync(UserExportQuery query, CancellationToken cancellationToken)
    {
        var collected = await ExcelExportSupport.CollectAsync<UserPagedResponse>(async (pageIndex, token) =>
        {
            var pageRequest = query.Request.CloneForPage<UserPageRequest>(pageIndex, ExcelExportSupport.PageRows);
            var page = await sender.Send(new UserQueryPaged(pageRequest), token);
            return (page.Items, page.Total);
        }, exportOptions.CurrentValue.MaxRows, cancellationToken);

        var excelRows = collected.Rows.Select(row => new UserExportRow
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

        return await ExcelExportSupport.BuildExcelAsync("用户列表", excelRows, collected.Truncated, cancellationToken);
    }
}

/// <summary>角色列表导出查询处理器（复用角色分页查询同口径筛选）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 发送器。</param>
/// <param name="exportOptions">导出阈值（Export:MaxRows，EX-05 阈值 Option 化）。</param>
public sealed class RoleExportQueryHandler(ISender sender, IOptionsMonitor<ExportOption> exportOptions) : QueryHandler<RoleExportQuery, ExportFileResponse>
{
    /// <inheritdoc />
    protected override async Task<ExportFileResponse> HandleAsync(RoleExportQuery query, CancellationToken cancellationToken)
    {
        var collected = await ExcelExportSupport.CollectAsync<RolePagedResponse>(async (pageIndex, token) =>
        {
            var pageRequest = query.Request.CloneForPage<RolePageRequest>(pageIndex, ExcelExportSupport.PageRows);
            var page = await sender.Send(new RoleQueryPaged(pageRequest), token);
            return (page.Items, page.Total);
        }, exportOptions.CurrentValue.MaxRows, cancellationToken);

        var excelRows = collected.Rows.Select(row => new RoleExportRow
        {
            RoleName = row.RoleName,
            RoleKey = row.RoleKey,
            DataScope = ExcelExportSupport.ToText(row.DataScope),
            Status = ExcelExportSupport.ToText(row.Status),
            CreatedAt = row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        });

        return await ExcelExportSupport.BuildExcelAsync("角色列表", excelRows, collected.Truncated, cancellationToken);
    }
}

/// <summary>操作日志导出查询处理器（复用操作日志分页查询同口径筛选）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 发送器。</param>
/// <param name="exportOptions">导出阈值（Export:MaxRows，EX-05 阈值 Option 化）。</param>
public sealed class OperLogExportQueryHandler(ISender sender, IOptionsMonitor<ExportOption> exportOptions) : QueryHandler<OperLogExportQuery, ExportFileResponse>
{
    /// <inheritdoc />
    protected override async Task<ExportFileResponse> HandleAsync(OperLogExportQuery query, CancellationToken cancellationToken)
    {
        var collected = await ExcelExportSupport.CollectAsync<OperLogPagedResponse>(async (pageIndex, token) =>
        {
            var pageRequest = query.Request.CloneForPage<OperLogPageRequest>(pageIndex, ExcelExportSupport.PageRows);
            var page = await sender.Send(new OperLogQueryPaged(pageRequest), token);
            return (page.Items, page.Total);
        }, exportOptions.CurrentValue.MaxRows, cancellationToken);

        var excelRows = collected.Rows.Select(row => new OperLogExportRow
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

        return await ExcelExportSupport.BuildExcelAsync("操作日志", excelRows, collected.Truncated, cancellationToken);
    }
}

/// <summary>登录日志导出查询处理器（复用登录日志分页查询同口径筛选）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 发送器。</param>
/// <param name="exportOptions">导出阈值（Export:MaxRows，EX-05 阈值 Option 化）。</param>
public sealed class LoginLogExportQueryHandler(ISender sender, IOptionsMonitor<ExportOption> exportOptions) : QueryHandler<LoginLogExportQuery, ExportFileResponse>
{
    /// <inheritdoc />
    protected override async Task<ExportFileResponse> HandleAsync(LoginLogExportQuery query, CancellationToken cancellationToken)
    {
        var collected = await ExcelExportSupport.CollectAsync<LoginLogPagedResponse>(async (pageIndex, token) =>
        {
            var pageRequest = query.Request.CloneForPage<LoginLogPageRequest>(pageIndex, ExcelExportSupport.PageRows);
            var page = await sender.Send(new LoginLogQueryPaged(pageRequest), token);
            return (page.Items, page.Total);
        }, exportOptions.CurrentValue.MaxRows, cancellationToken);

        var excelRows = collected.Rows.Select(row => new LoginLogExportRow
        {
            UserName = row.UserName,
            Ip = row.Ip,
            LoginLocation = row.LoginLocation,
            Status = ExcelExportSupport.ToText(row.Status),
            Msg = row.Msg,
            LoginTime = row.LoginTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty
        });

        return await ExcelExportSupport.BuildExcelAsync("登录日志", excelRows, collected.Truncated, cancellationToken);
    }
}
