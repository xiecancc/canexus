namespace Infrastructure.Shared.Options;

/// <summary>
/// 登录失败锁定阈值（绑定配置节 "Security:LoginLock"，EX-05 阈值 Option 化）。
/// <para>原为 <c>AuthLoginCommandHandler</c> 内的私有常量（5 次 / 10 分钟），改由配置注入后
/// 可按部署环境调整而不必改代码发版；默认值与历史行为保持一致。不引入一文件多类拆分等规范化改动。</para>
/// </summary>
public sealed class LoginLockOption : OptionBase
{
    /// <summary>允许的最大连续失败次数（达到即锁定），默认 5 次。</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>锁定时长（分钟；每次失败滑动重置窗口），默认 10 分钟。</summary>
    public int LockMinutes { get; set; } = 10;

    /// <inheritdoc />
    public override void Validate()
    {
        EnsureInRange(MaxFailedAttempts, 1, 100, nameof(MaxFailedAttempts),
            "Security:LoginLock:MaxFailedAttempts 必须在 1-100 之间");
        EnsureInRange(LockMinutes, 1, 1440, nameof(LockMinutes),
            "Security:LoginLock:LockMinutes 必须在 1-1440 分钟之间");
    }
}
