using MiniExcelLibs.Attributes;

namespace Application.Contracts.Exports;
/// <summary>Excel 导出文件响应（导出查询统一返回：xlsx 字节 + 建议文件名 + MIME；控制器直接转 FileResult）。</summary>
public class ExportFileResponse
{
    /// <summary>Excel 文件内容（xlsx 字节）。</summary>
    public byte[] Content { get; set; } = [];

    /// <summary>建议下载文件名（含扩展名，如 用户列表_20261104090000.xlsx）。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>内容类型（固定 xlsx MIME）。</summary>
    public string ContentType { get; set; } = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>用户导出行（GET /api/system/user/export；列名与用户管理列表口径一致）。</summary>
public class UserExportRow
{
    /// <summary>用户名。</summary>
    [ExcelColumnName("用户名")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>用户昵称。</summary>
    [ExcelColumnName("昵称")]
    public string NickName { get; set; } = string.Empty;

    /// <summary>所属部门名称（未分配为空）。</summary>
    [ExcelColumnName("部门")]
    public string DeptName { get; set; } = string.Empty;

    /// <summary>邮箱。</summary>
    [ExcelColumnName("邮箱")]
    public string? Email { get; set; }

    /// <summary>手机号。</summary>
    [ExcelColumnName("手机号")]
    public string? Phone { get; set; }

    /// <summary>拥有角色（角色名顿号连接）。</summary>
    [ExcelColumnName("角色")]
    public string Roles { get; set; } = string.Empty;

    /// <summary>状态（启用 / 停用）。</summary>
    [ExcelColumnName("状态")]
    public string Status { get; set; } = string.Empty;

    /// <summary>创建时间（UTC，yyyy-MM-dd HH:mm:ss）。</summary>
    [ExcelColumnName("创建时间")]
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>角色导出行（GET /api/system/role/export）。</summary>
public class RoleExportRow
{
    /// <summary>角色名称。</summary>
    [ExcelColumnName("角色名称")]
    public string RoleName { get; set; } = string.Empty;

    /// <summary>角色权限字符。</summary>
    [ExcelColumnName("权限字符")]
    public string RoleKey { get; set; } = string.Empty;

    /// <summary>数据范围（全部 / 自定义 / 本部门 / 本部门及以下 / 仅本人）。</summary>
    [ExcelColumnName("数据范围")]
    public string DataScope { get; set; } = string.Empty;

    /// <summary>状态（启用 / 停用）。</summary>
    [ExcelColumnName("状态")]
    public string Status { get; set; } = string.Empty;

    /// <summary>创建时间（UTC，yyyy-MM-dd HH:mm:ss）。</summary>
    [ExcelColumnName("创建时间")]
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>操作日志导出行（GET /api/monitor/oper-log/export）。</summary>
public class OperLogExportRow
{
    /// <summary>操作模块。</summary>
    [ExcelColumnName("操作模块")]
    public string Title { get; set; } = string.Empty;

    /// <summary>业务类型描述（如 新增 / 修改 / 删除）。</summary>
    [ExcelColumnName("业务类型")]
    public string BusinessType { get; set; } = string.Empty;

    /// <summary>方法名称（类全名.方法名）。</summary>
    [ExcelColumnName("方法")]
    public string Method { get; set; } = string.Empty;

    /// <summary>请求方式（GET / POST 等）。</summary>
    [ExcelColumnName("请求方式")]
    public string RequestMethod { get; set; } = string.Empty;

    /// <summary>操作人（未认证时为空）。</summary>
    [ExcelColumnName("操作人")]
    public string OperUserName { get; set; } = string.Empty;

    /// <summary>操作 IP 地址。</summary>
    [ExcelColumnName("操作 IP")]
    public string? OperIp { get; set; }

    /// <summary>请求 URL（含查询串）。</summary>
    [ExcelColumnName("请求 URL")]
    public string RequestUrl { get; set; } = string.Empty;

    /// <summary>操作状态（成功 / 失败）。</summary>
    [ExcelColumnName("操作状态")]
    public string Status { get; set; } = string.Empty;

    /// <summary>耗时（毫秒）。</summary>
    [ExcelColumnName("耗时(ms)")]
    public long CostMs { get; set; }

    /// <summary>操作时间（UTC，yyyy-MM-dd HH:mm:ss）。</summary>
    [ExcelColumnName("操作时间")]
    public string OperTime { get; set; } = string.Empty;
}

/// <summary>登录日志导出行（GET /api/monitor/login-log/export）。</summary>
public class LoginLogExportRow
{
    /// <summary>登录账号。</summary>
    [ExcelColumnName("登录账号")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>登录 IP 地址。</summary>
    [ExcelColumnName("IP 地址")]
    public string? Ip { get; set; }

    /// <summary>登录地点。</summary>
    [ExcelColumnName("登录地点")]
    public string? LoginLocation { get; set; }

    /// <summary>登录状态（成功 / 失败）。</summary>
    [ExcelColumnName("登录状态")]
    public string Status { get; set; } = string.Empty;

    /// <summary>提示消息（如 登录成功 / 密码错误）。</summary>
    [ExcelColumnName("提示消息")]
    public string Msg { get; set; } = string.Empty;

    /// <summary>登录时间（UTC，yyyy-MM-dd HH:mm:ss）。</summary>
    [ExcelColumnName("登录时间")]
    public string LoginTime { get; set; } = string.Empty;
}
