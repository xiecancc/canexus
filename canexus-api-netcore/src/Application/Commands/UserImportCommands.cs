using System.Text.RegularExpressions;
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Users;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Auth;
using Domain.Services.Events;
using Domain.Shared.Constants;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using MiniExcelLibs;

namespace Application.Commands;
/// <summary>
/// 批量导入用户命令处理器：MiniExcel 流式解析（列名中文表头）→ 行级校验 → 全部合法才批量入库。
/// <para>校验语义：用户名 / 昵称必填、用户名格式与文件内唯一、邮箱 / 手机号格式、部门 Id 存在性、
/// 用户名库内唯一；任一行失败即整体不入库（全量成功语义，杜绝「部分成功」引发的业务歧义），
/// 行级错误随 200 响应回执返回（行号 + 原因），供前端生成错误报告。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="userRepository">用户仓储。</param>
/// <param name="deptRepository">部门仓储（部门 Id 存在性校验）。</param>
/// <param name="passwordHasher">密码哈希器（初始密码 BCrypt 存储）。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
public sealed partial class UserImportCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateTreeRepository<SysDept> deptRepository,
    IPasswordHasher passwordHasher,
    IDomainEventPublisher domainEventPublisher) : RequestHandler<UserImportCommand, UserImportResultDto>
{
    /// <summary>单次导入行数上限（防大文件拖垮请求；超限整体拒绝）。</summary>
    public const int MaxImportRows = 1000;

    /// <summary>用户名格式：字母 / 数字 / 下划线 / 连字符，2-64 位。</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_-]{2,64}$")]
    private static partial Regex UserNameRegex();

    /// <summary>邮箱格式（宽松校验：x@y.z）。</summary>
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();

    /// <summary>手机号格式（中国大陆 11 位）。</summary>
    [GeneratedRegex(@"^1[3-9]\d{9}$")]
    private static partial Regex PhoneRegex();

    /// <inheritdoc />
    protected override async Task<UserImportResultDto> HandleAsync(UserImportCommand command, CancellationToken cancellationToken)
    {
        List<UserImportRow> rows;
        try
        {
            rows = [.. MiniExcel.Query<UserImportRow>(command.Content)];
        }
        // 解析失败类型因文件损坏形态而异（非 xlsx 容器 / 空流 / 非法 sheet 等），统一按模板错误兜底；
        // 仅取消令牌不拦截（正常取消语义）
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new BusinessException(ErrorCodes.UserImportFileInvalid, "导入文件无法解析，请使用系统提供的导入模板（xlsx）");
        }

        if (rows.Count == 0)
        {
            throw new BusinessException(ErrorCodes.UserImportFileInvalid, "导入文件没有数据行，请填写后再导入");
        }

        if (rows.Count > MaxImportRows)
        {
            throw new BusinessException(ErrorCodes.UserImportFileInvalid, $"单次导入不能超过 {MaxImportRows} 行，请分批导入");
        }

        // —— 行级校验：逐行收集错误，任一错误整体不入库 ——
        var errors = new List<UserImportErrorDto>();
        var seenUserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++)
        {
            // Excel 行号从 1 起，第 1 行为表头
            var rowNumber = i + 2;
            var row = rows[i];
            var userName = (row.UserName ?? string.Empty).Trim();

            var rowErrors = ValidateRow(row, userName, seenUserNames);
            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors.Select(reason => new UserImportErrorDto(rowNumber, userName, reason)));
                continue;
            }

            seenUserNames.Add(userName);
        }

        // 库内唯一性：批量比对（有行级错误时跳过，错误回执优先）
        if (errors.Count == 0)
        {
            var candidateNames = seenUserNames.ToList();
            var existing = await userRepository.GetListAsync(
                [u => candidateNames.Contains(u.UserName)], cancellationToken: cancellationToken);
            foreach (var existed in existing)
            {
                var rowNumber = rows.FindIndex(r => string.Equals((r.UserName ?? string.Empty).Trim(), existed.UserName, StringComparison.OrdinalIgnoreCase)) + 2;
                errors.Add(new UserImportErrorDto(rowNumber, existed.UserName, $"用户名「{existed.UserName}」已存在于系统中"));
            }
        }

        // 部门存在性：批量比对（存在性属行级错误的一部分）
        var deptIds = rows
            .Select(r => TryParseGuid(r.DeptId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var existingDeptIds = deptIds.Count == 0
            ? []
            : (await deptRepository.GetListAsync([d => deptIds.Contains(d.Id)], cancellationToken: cancellationToken))
                .Select(d => d.Id)
                .ToHashSet();
        foreach (var deptId in deptIds.Where(id => !existingDeptIds.Contains(id)))
        {
            var rowNumber = rows.FindIndex(r => TryParseGuid(r.DeptId) == deptId) + 2;
            errors.Add(new UserImportErrorDto(rowNumber, (rows[rowNumber - 2].UserName ?? string.Empty).Trim(), $"部门 Id「{deptId}」不存在"));
        }

        var result = new UserImportResultDto
        {
            TotalCount = rows.Count,
            FailureCount = errors.Count,
            SuccessCount = errors.Count == 0 ? rows.Count : 0,
            Errors = errors
        };

        // 全量成功语义：存在行级错误时不写任何数据
        if (errors.Count > 0)
        {
            return result;
        }

        // —— 批量入库：初始密码哈希 + 逐行发布领域事件（部门存在性已在前面校验，此处直接取库内确认集）——
        var users = rows.Select(row => new SysUser
        {
            UserName = row.UserName.Trim(),
            NickName = row.NickName.Trim(),
            PasswordHash = passwordHasher.Hash(
                string.IsNullOrWhiteSpace(row.Password) ? UserConstants.DefaultPassword : row.Password.Trim()),
            DeptId = TryParseGuid(row.DeptId) is { } deptId && existingDeptIds.Contains(deptId) ? deptId : null,
            Email = string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
            Status = Domain.Shared.Enums.CommonStatus.Enabled
        }).ToList();

        await userRepository.InsertAsync(users, cancellationToken);
        foreach (var user in users)
        {
            await domainEventPublisher.PublishAsync(new UserCreatedEvent(user), cancellationToken);
        }

        return result;
    }

    /// <summary>单行校验（返回该行全部失败原因；通过时返回空集合）。</summary>
    /// <param name="row">导入行。</param>
    /// <param name="userName">规整后的用户名。</param>
    /// <param name="seenUserNames">文件内已通过校验的用户名集合（文件内唯一性）。</param>
    /// <returns>失败原因列表。</returns>
    private static List<string> ValidateRow(UserImportRow row, string userName, HashSet<string> seenUserNames)
    {
        var reasons = new List<string>();

        if (userName.Length == 0)
        {
            reasons.Add("用户名不能为空");
        }
        else if (!UserNameRegex().IsMatch(userName))
        {
            reasons.Add($"用户名「{userName}」格式非法（仅限字母 / 数字 / 下划线 / 连字符，2-64 位）");
        }
        else if (!seenUserNames.Add(userName))
        {
            reasons.Add($"用户名「{userName}」在文件内重复");
        }

        if (string.IsNullOrWhiteSpace(row.NickName))
        {
            reasons.Add("昵称不能为空");
        }

        var email = (row.Email ?? string.Empty).Trim();
        if (email.Length > 0 && !EmailRegex().IsMatch(email))
        {
            reasons.Add($"邮箱「{email}」格式非法");
        }

        var phone = (row.Phone ?? string.Empty).Trim();
        if (phone.Length > 0 && !PhoneRegex().IsMatch(phone))
        {
            reasons.Add($"手机号「{phone}」格式非法（须为 11 位大陆手机号）");
        }

        if (!string.IsNullOrWhiteSpace(row.DeptId) && TryParseGuid(row.DeptId) is null)
        {
            reasons.Add($"部门 Id「{row.DeptId}」不是合法的 Guid");
        }

        return reasons;
    }

    /// <summary>尝试解析 Guid（空 / 非法返回 null）。</summary>
    /// <param name="text">原始文本。</param>
    /// <returns>解析结果。</returns>
    private static Guid? TryParseGuid(string? text)
    {
        return Guid.TryParse(text?.Trim(), out var id) ? id : null;
    }
}
