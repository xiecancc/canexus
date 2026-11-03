namespace Application.Contracts.Users;
/// <summary>用户导入行（Excel 模板行模型，列名中文表头，经 MiniExcel 映射）。</summary>
public class UserImportRow
{
    /// <summary>用户名（必填，唯一，创建后不可改）。</summary>
    [MiniExcelLibs.Attributes.ExcelColumnName("用户名")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>用户昵称（必填）。</summary>
    [MiniExcelLibs.Attributes.ExcelColumnName("昵称")]
    public string NickName { get; set; } = string.Empty;

    /// <summary>邮箱（可空）。</summary>
    [MiniExcelLibs.Attributes.ExcelColumnName("邮箱")]
    public string? Email { get; set; }

    /// <summary>手机号（可空）。</summary>
    [MiniExcelLibs.Attributes.ExcelColumnName("手机号")]
    public string? Phone { get; set; }

    /// <summary>部门 Id（可空 Guid；不存在时报行级错误）。</summary>
    [MiniExcelLibs.Attributes.ExcelColumnName("部门Id")]
    public string? DeptId { get; set; }

    /// <summary>初始密码（可空，缺省系统默认密码）。</summary>
    [MiniExcelLibs.Attributes.ExcelColumnName("初始密码")]
    public string? Password { get; set; }
}

/// <summary>用户批量导入结果回执（HTTP 200 返回；存在行级错误时全部不入库）。</summary>
public class UserImportResultDto
{
    /// <summary>模板总行数（不含表头）。</summary>
    public int TotalCount { get; set; }

    /// <summary>校验通过并入库的行数。</summary>
    public int SuccessCount { get; set; }

    /// <summary>校验失败的行数。</summary>
    public int FailureCount { get; set; }

    /// <summary>行级错误明细（行号从 2 开始，1 为表头）。</summary>
    public List<UserImportErrorDto> Errors { get; set; } = [];
}

/// <summary>用户导入行级错误明细。</summary>
/// <param name="Row">Excel 行号（2 起）。</param>
/// <param name="UserName">该行用户名（便于定位）。</param>
/// <param name="Reason">失败原因。</param>
public record UserImportErrorDto(int Row, string UserName, string Reason);
