namespace Domain.Services.Auth;

/// <summary>密码哈希接口：注册 / 种子数据时哈希，登录时校验。</summary>
public interface IPasswordHasher
{
    /// <summary>对明文密码生成哈希（自动加盐）。</summary>
    /// <param name="password">明文密码。</param>
    /// <returns>哈希字符串（含盐与成本因子，可直接持久化）。</returns>
    public string Hash(string password);

    /// <summary>校验明文密码与哈希是否匹配。</summary>
    /// <param name="password">明文密码。</param>
    /// <param name="hash">存储的哈希字符串。</param>
    /// <returns>匹配返回 true；哈希格式非法时返回 false（不抛异常）。</returns>
    public bool Verify(string password, string hash);
}
