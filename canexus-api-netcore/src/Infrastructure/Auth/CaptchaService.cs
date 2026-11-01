
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Domain.Services.Security;
using Infrastructure.Shared.Caching;

namespace Infrastructure.Auth;
/// <summary>
/// 图形验证码实现（自绘 SVG，不依赖第三方包）：生成时按 Hot 档写入双层缓存，校验一次性失效。
/// </summary>
public sealed class CaptchaService(ICache cache) : ICaptchaService
{
    /// <summary>验证码位数。</summary>
    private const int CodeLength = 4;

    /// <summary>可用字符集（剔除易混淆的 0O1Iil）。</summary>
    private const string AvailableChars = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <inheritdoc />
    public async ValueTask<CaptchaImage> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var code = RandomNumberGenerator.GetItems<char>(AvailableChars, CodeLength);

        // 校验按大写比对，存储统一大写
        var normalized = new string(code).ToUpperInvariant();
        var captchaId = Guid.NewGuid().ToString("N");
        var svg = BuildSvg(code);
        var imageBase64 = $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";

        // Hot 档：L1 30s（本实例秒级校验）/ L2 5min（分布式部署跨实例校验）
        await cache.SetAsync(ICaptchaService.CacheKeyPrefix + captchaId, normalized, CacheTtl.Hot, cancellationToken);
        return new CaptchaImage(captchaId, imageBase64);
    }

    /// <inheritdoc />
    public async ValueTask<bool> ValidateAsync(string captchaId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(captchaId))
        {
            return false;
        }

        var key = ICaptchaService.CacheKeyPrefix + captchaId;
        var expected = await cache.GetAsync<string>(key, cancellationToken);

        // 一次性：无论命中与否均移除，防止重放
        await cache.RemoveAsync(key, cancellationToken);
        return !string.IsNullOrWhiteSpace(expected)
            && !string.IsNullOrWhiteSpace(code)
            && string.Equals(expected, code.Trim().ToUpperInvariant(), StringComparison.Ordinal);
    }

    /// <summary>绘制验证码 SVG：背景 + 干扰线 + 噪点 + 随机姿态字符。</summary>
    /// <param name="code">验证码字符。</param>
    /// <returns>SVG 字符串。</returns>
    private static string BuildSvg(char[] code)
    {
        var builder = new StringBuilder();
        const int width = 120;
        const int height = 40;

        builder.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">""");
        builder.Append(CultureInfo.InvariantCulture, $"""<rect width="{width}" height="{height}" fill="#f4f6f8"/>""");

        // 干扰线：3 条随机贝塞尔曲线
        for (var i = 0; i < 3; i++)
        {
            var x1 = RandomNumberGenerator.GetInt32(0, width);
            var y1 = RandomNumberGenerator.GetInt32(0, height);
            var x2 = RandomNumberGenerator.GetInt32(0, width);
            var y2 = RandomNumberGenerator.GetInt32(0, height);
            var cx = RandomNumberGenerator.GetInt32(0, width);
            var cy = RandomNumberGenerator.GetInt32(0, height);
            builder.Append(CultureInfo.InvariantCulture,
                $"""<path d="M {x1} {y1} Q {cx} {cy} {x2} {y2}" stroke="{RandomColor()}" stroke-width="1" fill="none" opacity="0.6"/>""");
        }

        // 噪点：30 个随机小圆
        for (var i = 0; i < 30; i++)
        {
            var cx = RandomNumberGenerator.GetInt32(0, width);
            var cy = RandomNumberGenerator.GetInt32(0, height);
            var r = RandomNumberGenerator.GetInt32(1, 2);
            builder.Append(CultureInfo.InvariantCulture,
                $"""<circle cx="{cx}" cy="{cy}" r="{r}" fill="{RandomColor()}" opacity="0.4"/>""");
        }

        // 字符：均分横向布局，随机纵向偏移、旋转与颜色
        var slot = width / (CodeLength + 1);
        for (var i = 0; i < CodeLength; i++)
        {
            var x = slot * (i + 1) + RandomNumberGenerator.GetInt32(-6, 7);
            var y = 28 + RandomNumberGenerator.GetInt32(-4, 5);
            var rotate = RandomNumberGenerator.GetInt32(-25, 26);
            var fontSize = RandomNumberGenerator.GetInt32(22, 29);
            builder.Append(CultureInfo.InvariantCulture,
                $"""<text x="{x}" y="{y}" font-family="Arial,SimHei" font-size="{fontSize}" font-weight="bold" fill="{RandomColor()}" text-anchor="middle" transform="rotate({rotate} {x} {y})">{code[i]}</text>""");
        }

        builder.Append("</svg>");
        return builder.ToString();
    }

    /// <summary>生成随机颜色（中低亮度色系，保证可读性）。</summary>
    /// <returns>十六进制颜色字符串。</returns>
    private static string RandomColor()
    {
        var r = RandomNumberGenerator.GetInt32(30, 140);
        var g = RandomNumberGenerator.GetInt32(30, 140);
        var b = RandomNumberGenerator.GetInt32(30, 140);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
