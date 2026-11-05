namespace Domain.Shared.Results;

/// <summary>
/// 全局业务错误码体系（5~6 位数字，分段规则如下）：
/// <list type="bullet">
/// <item><c>400xx</c>：通用/公共段，对应 HTTP 400；</item>
/// <item><c>401xx</c>：认证段，对应 HTTP 401；</item>
/// <item><c>403xx</c>：权限段，对应 HTTP 403；</item>
/// <item><c>404xx</c>：资源不存在段，对应 HTTP 404；</item>
/// <item><c>4002xx、4003xx……</c>：业务模块段（第 4 位起为模块编号，末两位段内自增），对应 HTTP 400：
/// 用户 = 4002xx、角色 = 4003xx、菜单 = 4004xx、部门 = 4005xx……后续模块按序追加；</item>
/// <item><c>50000</c>：系统内部错误，对应 HTTP 500。</item>
/// </list>
/// </summary>
public static class ErrorCodes
{
    // —— 通用段（400xx，对应 HTTP 400）——

    /// <summary>通用请求错误。</summary>
    public const int CommonInvalidRequest = 40000;

    /// <summary>参数校验失败（模型校验 / FluentValidation）。</summary>
    public const int CommonValidationFailed = 40001;

    /// <summary>数据不存在。</summary>
    public const int CommonDataNotFound = 40002;

    /// <summary>数据重复（唯一性冲突）。</summary>
    public const int CommonDataDuplicate = 40003;

    /// <summary>通用操作失败。</summary>
    public const int CommonOperationFailed = 40004;

    /// <summary>并发冲突：数据已被他人修改（乐观锁版本号不一致），请刷新后重试。</summary>
    public const int CommonConcurrencyConflict = 40005;

    /// <summary>树形节点父级不合法（不可选自身或自己的后代）。</summary>
    public const int CommonTreeParentInvalid = 40006;

    /// <summary>请求过于频繁（触发接口限流），请稍后重试。</summary>
    public const int CommonTooManyRequests = 40007;

    // —— 认证段（401xx，对应 HTTP 401）——

    /// <summary>未登录 / 未认证。</summary>
    public const int AuthUnauthorized = 40100;

    /// <summary>令牌已过期。</summary>
    public const int AuthTokenExpired = 40102;

    /// <summary>账号或密码错误。</summary>
    public const int AuthAccountOrPasswordError = 40103;

    /// <summary>验证码错误。</summary>
    public const int AuthCaptchaError = 40104;

    /// <summary>账号已锁定（密码连续错误次数过多）。</summary>
    public const int AuthAccountLocked = 40105;

    /// <summary>刷新令牌无效或已过期。</summary>
    public const int AuthRefreshTokenInvalid = 40106;

    /// <summary>登录状态已失效（令牌版本变更 / 会话被吊销 / 账号已停用或删除），需重新登录。</summary>
    public const int AuthTokenRevoked = 40107;

    // —— 权限段（403xx，对应 HTTP 403）——

    /// <summary>无权限访问。</summary>
    public const int AuthForbidden = 40300;

    /// <summary>跨租户访问被拒绝（请求头租户与登录用户所属租户不一致）。</summary>
    public const int TenantForbidden = 40301;

    /// <summary>首次登录须先修改初始密码，当前访问被拦截。</summary>
    public const int MustChangePasswordRequired = 40302;

    // —— 用户模块段（4002xx，对应 HTTP 400）——

    /// <summary>用户不存在。</summary>
    public const int UserNotFound = 400201;

    /// <summary>用户已停用。</summary>
    public const int UserDisabled = 400202;

    /// <summary>用户名已存在（唯一性冲突）。</summary>
    public const int UserNameDuplicate = 400204;

    /// <summary>内置超管账号（admin）受保护，不允许该操作。</summary>
    public const int UserAdminProtected = 400205;

    /// <summary>不能删除当前登录账号自己。</summary>
    public const int UserDeleteSelfForbidden = 400206;

    /// <summary>用户导入文件非法（空文件 / 无法解析 / 行数超上限）。</summary>
    public const int UserImportFileInvalid = 400207;

    /// <summary>原密码错误（自助改密时旧密码校验失败）。</summary>
    public const int UserPasswordError = 400208;

    // —— 角色模块段（4003xx，对应 HTTP 400）——

    /// <summary>角色不存在。</summary>
    public const int RoleNotFound = 400301;

    /// <summary>角色权限字符（roleKey）已存在。</summary>
    public const int RoleKeyDuplicate = 400302;

    /// <summary>角色权限字符为内置保留（super_admin），不可使用。</summary>
    public const int RoleKeyReserved = 400303;

    /// <summary>内置超管角色（super_admin）受保护，不允许编辑或删除。</summary>
    public const int RoleProtected = 400304;

    /// <summary>角色已被用户引用，不允许删除。</summary>
    public const int RoleInUse = 400305;

    // —— 菜单模块段（4004xx，对应 HTTP 400）——

    /// <summary>菜单不存在。</summary>
    public const int MenuNotFound = 400401;

    /// <summary>存在子菜单，不允许删除。</summary>
    public const int MenuHasChildren = 400402;

    /// <summary>菜单已被角色引用，不允许删除。</summary>
    public const int MenuInUse = 400403;

    /// <summary>菜单字段校验不通过（目录必有 path；菜单必有 path 与 component；按钮必有 perms）。</summary>
    public const int MenuRuleInvalid = 400404;

    /// <summary>父级菜单不合法（不可选自身或自己的后代）。</summary>
    public const int MenuParentInvalid = 400405;

    // —— 部门模块段（4005xx，对应 HTTP 400）——

    /// <summary>部门不存在。</summary>
    public const int DeptNotFound = 400501;

    /// <summary>存在子部门，不允许删除。</summary>
    public const int DeptHasChildren = 400502;

    /// <summary>部门下存在用户，不允许删除。</summary>
    public const int DeptHasUsers = 400503;

    /// <summary>上级部门不合法（不可选自身或自己的后代）。</summary>
    public const int DeptParentInvalid = 400504;

    /// <summary>上级部门不存在。</summary>
    public const int DeptParentNotFound = 400505;

    // —— 字典模块段（4006xx，对应 HTTP 400）——

    /// <summary>字典类型不存在。</summary>
    public const int DictTypeNotFound = 400601;

    /// <summary>字典类型（dictType）已存在（唯一性冲突）。</summary>
    public const int DictTypeDuplicate = 400602;

    /// <summary>字典类型（dictType）不可变更。</summary>
    public const int DictTypeImmutable = 400603;

    /// <summary>字典类型下存在字典数据，不允许删除。</summary>
    public const int DictTypeHasData = 400604;

    /// <summary>字典数据不存在。</summary>
    public const int DictDataNotFound = 400605;

    /// <summary>同字典类型下字典标签 / 键值已存在（唯一性冲突）。</summary>
    public const int DictDataDuplicate = 400606;

    // —— 参数配置模块段（4007xx，对应 HTTP 400）——

    /// <summary>参数配置不存在。</summary>
    public const int ConfigNotFound = 400701;

    /// <summary>参数键名（configKey）已存在（唯一性冲突）。</summary>
    public const int ConfigKeyDuplicate = 400702;

    /// <summary>内置参数受保护，不允许删除。</summary>
    public const int ConfigBuiltInProtected = 400703;

    // —— 租户模块段（4008xx，对应 HTTP 400）——

    /// <summary>租户不存在。</summary>
    public const int TenantNotFound = 400801;

    /// <summary>租户标识（tenantKey）已存在（唯一性冲突）。</summary>
    public const int TenantKeyDuplicate = 400802;

    /// <summary>租户标识（tenantKey）不可变更。</summary>
    public const int TenantKeyImmutable = 400803;

    /// <summary>租户下存在归属用户，不允许删除。</summary>
    public const int TenantInUse = 400804;

    /// <summary>租户已停用（不可用于登录或数据访问）。</summary>
    public const int TenantDisabled = 400805;

    /// <summary>租户已到期（不可用于登录或数据访问）。</summary>
    public const int TenantExpired = 400806;

    // —— 定时任务模块段（4009xx，对应 HTTP 400）——

    /// <summary>定时任务不存在。</summary>
    public const int JobNotFound = 400901;

    /// <summary>任务类型（JobType）不可解析或未实现 Quartz IJob。</summary>
    public const int JobTypeInvalid = 400902;

    /// <summary>Cron 表达式不合法。</summary>
    public const int CronInvalid = 400903;

    // —— 通知公告模块段（4010xx，对应 HTTP 400）——

    /// <summary>通知公告不存在。</summary>
    public const int NoticeNotFound = 401001;

    /// <summary>通知公告字段校验不通过（标题 / 内容必填、类型与状态取值合法）。</summary>
    public const int NoticeRuleInvalid = 401002;

    // —— 在线会话模块段（4011xx，对应 HTTP 400）——

    /// <summary>在线会话不存在或已下线（强退目标会话已吊销 / 已过期 / 已被删除）。</summary>
    public const int OnlineSessionNotFound = 401101;

    // —— 文件存储模块段（4012xx，对应 HTTP 400）——

    /// <summary>文件不存在（元数据在库但物理文件缺失、或路径非法越界）。</summary>
    public const int FileNotFound = 401201;

    /// <summary>文件扩展名不在上传白名单内。</summary>
    public const int FileExtensionNotAllowed = 401202;

    /// <summary>文件大小超出上限。</summary>
    public const int FileSizeExceeded = 401203;

    // —— 岗位模块段（4013xx，对应 HTTP 400）——

    /// <summary>岗位不存在。</summary>
    public const int PostNotFound = 401301;

    /// <summary>岗位编码重复（唯一性冲突）。</summary>
    public const int PostCodeDuplicate = 401302;

    /// <summary>岗位字段校验不通过（名称 / 编码必填、长度与取值合法）。</summary>
    public const int PostRuleInvalid = 401303;

    /// <summary>岗位仍被用户引用，不允许删除（须先解除用户-岗位关联）。</summary>
    public const int PostInUse = 401304;

    // —— 系统段 ——

    /// <summary>系统内部错误（对应 HTTP 500，不向客户端泄露详情）。</summary>
    public const int CommonSystemError = 50000;

    /// <summary>根据业务错误码映射 HTTP 状态码（全局异常处理中间件使用）。</summary>
    /// <param name="errorCode">业务错误码。</param>
    /// <returns>HTTP 状态码：认证段 401、权限段 403、资源段 404、系统段 500，其余默认 400。</returns>
    public static int GetHttpStatusCode(int errorCode) => errorCode switch
    {
        Result.SuccessCode => 200,
        AuthCaptchaError => 400,
        CommonTooManyRequests => 429,
        >= 40100 and <= 40199 => 401,
        >= 40300 and <= 40399 => 403,
        >= 40400 and <= 40499 => 404,
        CommonSystemError => 500,
        _ => 400
    };
}
