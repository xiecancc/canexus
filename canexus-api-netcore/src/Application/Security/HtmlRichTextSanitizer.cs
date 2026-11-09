
using Ganss.Xss;

namespace Application.Security;
/// <summary>
/// 富文本 HTML 净化器（防存储型 XSS）：富文本字段（如公告正文）入库前经白名单净化，
/// 移除 script / iframe / 事件属性（onclick 等）/ javascript: 协议等危险内容，
/// 保留常规排版标签。净化基于 HtmlSanitizer 默认白名单（业界标准库），输入侧单点收口；
/// 渲染侧仍应遵循前端框架默认转义，双层防御。
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="sanitizer">HtmlSanitizer 实例（线程安全，官方推荐单例复用以复用内部解析缓存）。</param>
public sealed class HtmlRichTextSanitizer(HtmlSanitizer sanitizer)
{
    /// <summary>净化富文本 HTML：返回移除危险内容后的白名单内 HTML（null/空白原样返回空串）。</summary>
    /// <param name="html">待净化的富文本（可含任意用户输入）。</param>
    /// <returns>净化后的 HTML。</returns>
    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return sanitizer.Sanitize(html);
    }
}
