using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Security;
/// <summary>
/// 刷新令牌哈希器：对明文刷新令牌计算 SHA-256，输出 64 位小写十六进制，用于数据库持久化与按值查询。
/// <para>数据库仅存哈希：即使库表泄露也无法还原可用的刷新令牌；查询 / 吊销一律以哈希值匹配。</para>
/// </summary>
public static class RefreshTokenHasher
{
    /// <summary>计算刷新令牌的 SHA-256 哈希（64 位小写十六进制）。</summary>
    /// <param name="token">明文刷新令牌（非空，由调用方保证）。</param>
    /// <returns>哈希值。</returns>
    /// <exception cref="ArgumentNullException">令牌为 null。</exception>
    /// <exception cref="ArgumentException">令牌为空白字符串。</exception>
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
