using System.Security.Cryptography;
using System.Text;
using Infrastructure.Security;
using NUnit.Framework;

namespace Infrastructure.Tests;
/// <summary>刷新令牌哈希器单元测试：验证 SHA-256 哈希的确定性、64 位小写十六进制输出与空值拒绝。</summary>
[TestFixture]
public class RefreshTokenHasherTests
{
    /// <summary>验证哈希值与标准 SHA-256 计算一致，且为 64 位小写十六进制、不等于明文。</summary>
    [Test]
    public void Hash_IsDeterministic_Sha256HexLowercase()
    {
        const string token = "abc123def4567890fedcba0987654321";

        var hash = RefreshTokenHasher.Hash(token);
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        Assert.Multiple(() =>
        {
            Assert.That(hash, Is.EqualTo(expected), "哈希值应与标准 SHA-256 计算结果一致");
            Assert.That(hash, Has.Length.EqualTo(64));
            Assert.That(hash, Does.Match("^[0-9a-f]{64}$"), "输出应为 64 位小写十六进制");
            Assert.That(hash, Is.Not.EqualTo(token), "哈希值不得与明文相同");
        });
    }

    /// <summary>验证同一令牌重复哈希结果一致（确定性）。</summary>
    [Test]
    public void Hash_SameToken_ProducesSameHash()
    {
        const string token = "same-token-value";
        var first = RefreshTokenHasher.Hash(token);

        Assert.That(RefreshTokenHasher.Hash(token), Is.EqualTo(first));
    }

    /// <summary>验证不同令牌产生不同哈希（无碰撞假设的最小断言）。</summary>
    [Test]
    public void Hash_DifferentTokens_ProduceDifferentHashes()
    {
        Assert.That(RefreshTokenHasher.Hash("token-a"), Is.Not.EqualTo(RefreshTokenHasher.Hash("token-b")));
    }

    /// <summary>验证空 / 空白令牌被拒绝（调用方约定令牌非空）。</summary>
    [Test]
    public void Hash_NullOrWhiteSpace_Throws()
    {
        Assert.That(() => RefreshTokenHasher.Hash(""), Throws.TypeOf<ArgumentException>());
        Assert.That(() => RefreshTokenHasher.Hash("   "), Throws.TypeOf<ArgumentException>());
    }
}
