namespace Infrastructure.Storage;

/// <summary>按文件头（magic number）识别的内容格式族。</summary>
internal enum FileFormatFamily
{
    /// <summary>JPEG。</summary>
    Jpeg,

    /// <summary>PNG。</summary>
    Png,

    /// <summary>GIF。</summary>
    Gif,

    /// <summary>WebP（RIFF....WEBVP）。</summary>
    WebP,

    /// <summary>BMP。</summary>
    Bmp,

    /// <summary>PDF。</summary>
    Pdf,

    /// <summary>OLE 复合文档（doc / xls / ppt 共用）。</summary>
    Ole,

    /// <summary>ZIP 容器（zip / docx / xlsx / pptx 共用）。</summary>
    Zip,

    /// <summary>RAR。</summary>
    Rar,

    /// <summary>7-Zip。</summary>
    SevenZip,

    /// <summary>无特征签名的纯文本（txt / csv / md）。</summary>
    Text,

    /// <summary>无法识别。</summary>
    Unknown
}

/// <summary>
/// 文件内容签名校验（T-14）：以文件头而非客户端声明判定类型，并把扩展名收敛到内置 MIME 白名单。
/// <para>OLE / ZIP 容器为多个格式共用签名，因此按「签名族 + 扩展名同族」双向校验；
/// 落库 MIME 只取本表推导值，客户端 Content-Type 一律忽略。</para>
/// </summary>
internal static class FileContentInspector
{
    /// <summary>扩展名 →（签名族，规范 MIME）白名单；不在表内的扩展名一律拒绝。</summary>
    private static readonly Dictionary<string, (FileFormatFamily Family, string Mime)> MimeWhitelist =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["jpg"] = (FileFormatFamily.Jpeg, "image/jpeg"),
            ["jpeg"] = (FileFormatFamily.Jpeg, "image/jpeg"),
            ["png"] = (FileFormatFamily.Png, "image/png"),
            ["gif"] = (FileFormatFamily.Gif, "image/gif"),
            ["webp"] = (FileFormatFamily.WebP, "image/webp"),
            ["bmp"] = (FileFormatFamily.Bmp, "image/bmp"),
            ["pdf"] = (FileFormatFamily.Pdf, "application/pdf"),
            ["doc"] = (FileFormatFamily.Ole, "application/msword"),
            ["xls"] = (FileFormatFamily.Ole, "application/vnd.ms-excel"),
            ["ppt"] = (FileFormatFamily.Ole, "application/vnd.ms-powerpoint"),
            ["zip"] = (FileFormatFamily.Zip, "application/zip"),
            ["docx"] = (FileFormatFamily.Zip, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ["xlsx"] = (FileFormatFamily.Zip, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            ["pptx"] = (FileFormatFamily.Zip, "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            ["rar"] = (FileFormatFamily.Rar, "application/vnd.rar"),
            ["7z"] = (FileFormatFamily.SevenZip, "application/x-7z-compressed"),
            ["txt"] = (FileFormatFamily.Text, "text/plain"),
            ["csv"] = (FileFormatFamily.Text, "text/csv"),
            ["md"] = (FileFormatFamily.Text, "text/markdown")
        };

    /// <summary>按文件头识别签名族。</summary>
    /// <param name="header">文件前若干字节（建议至少 16 字节）。</param>
    /// <returns>内容格式族。</returns>
    public static FileFormatFamily Detect(ReadOnlySpan<byte> header)
    {
        if (StartsWith(header, 0xFF, 0xD8, 0xFF))
        {
            return FileFormatFamily.Jpeg;
        }

        if (StartsWith(header, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
        {
            return FileFormatFamily.Png;
        }

        if (StartsWith(header, 0x47, 0x49, 0x46, 0x38))
        {
            return FileFormatFamily.Gif;
        }

        if (StartsWith(header, 0x52, 0x49, 0x46, 0x46) && header.Length >= 12
            && StartsWith(header[8..], 0x57, 0x45, 0x42, 0x50))
        {
            return FileFormatFamily.WebP;
        }

        if (StartsWith(header, 0x42, 0x4D))
        {
            return FileFormatFamily.Bmp;
        }

        if (StartsWith(header, 0x25, 0x50, 0x44, 0x46, 0x2D))
        {
            return FileFormatFamily.Pdf;
        }

        if (StartsWith(header, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1))
        {
            return FileFormatFamily.Ole;
        }

        if (StartsWith(header, 0x50, 0x4B, 0x03, 0x04)
            || StartsWith(header, 0x50, 0x4B, 0x05, 0x06)
            || StartsWith(header, 0x50, 0x4B, 0x07, 0x08))
        {
            return FileFormatFamily.Zip;
        }

        if (StartsWith(header, 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07))
        {
            return FileFormatFamily.Rar;
        }

        if (StartsWith(header, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C))
        {
            return FileFormatFamily.SevenZip;
        }

        // 纯文本：不得含 NUL 或除制表 / 换行 / 回车外的控制字符（二进制载荷借此被拒）
        if (header.Length > 0)
        {
            var isText = true;
            foreach (var value in header)
            {
                if (IsBinaryControlByte(value))
                {
                    isText = false;
                    break;
                }
            }

            if (isText)
            {
                return FileFormatFamily.Text;
            }
        }

        return FileFormatFamily.Unknown;
    }

    /// <summary>签名族与扩展名同族时返回规范 MIME；否则返回 false（伪装后缀 / 未登记类型被拒）。</summary>
    /// <param name="extension">不含点的小写扩展名。</param>
    /// <param name="family">实测签名族。</param>
    /// <param name="mimeType">规范 MIME（白名单内取值）。</param>
    /// <returns>允许入库返回 true。</returns>
    public static bool TryResolveMime(string extension, FileFormatFamily family, out string mimeType)
    {
        if (MimeWhitelist.TryGetValue(extension, out var entry) && entry.Family == family)
        {
            mimeType = entry.Mime;
            return true;
        }

        mimeType = string.Empty;
        return false;
    }

    /// <summary>判断字节是否为二进制控制字符。</summary>
    private static bool IsBinaryControlByte(byte value)
    {
        return value == 0 || (value < 0x20 && value is not (0x09 or 0x0A or 0x0D));
    }

    /// <summary>判断文件头是否以指定字节序列开头。</summary>
    private static bool StartsWith(ReadOnlySpan<byte> header, params byte[] signature)
    {
        return header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);
    }
}
