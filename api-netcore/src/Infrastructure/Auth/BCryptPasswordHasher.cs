namespace Infrastructure.Auth;

using Domain.Services.Auth;

/// <summary>基于 BCrypt 的密码哈希实现（BCrypt.Net-Next，自动生成盐，成本因子 11）。</summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    /// <summary>BCrypt 计算成本因子（2^11 轮，兼顾安全与性能）。</summary>
    private const int WorkFactor = 11;

    /// <inheritdoc />
    public string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        // 哈希格式非法（空串、非 BCrypt 格式、非法盐版本等）时 Verify 会抛出多种异常，统一按"不匹配"处理
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
