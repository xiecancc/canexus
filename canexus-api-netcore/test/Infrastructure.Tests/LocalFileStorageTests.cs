using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Options;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Infrastructure.Tests;

/// <summary>
/// T-14 本地存储测试：内容签名与 MIME 白名单、忽略客户端声明、不可 seek 流的大小准确性、
/// 三道大小限制与路径遍历防护。
/// </summary>
[TestFixture]
public class LocalFileStorageTests
{
    private string _root = null!;
    private LocalFileStorage _storage = null!;

    /// <summary>每个用例使用独立临时根目录。</summary>
    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "super-admin-storage-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _storage = new LocalFileStorage(Options.Create(new FileStorageOption
        {
            RootPath = _root,
            MaxSizeMB = 1
        }));
    }

    /// <summary>清理临时目录。</summary>
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>纯文本按内容识别为 text/plain，返回实际落盘字节数。</summary>
    [Test]
    public async Task Save_DetectsTextMime_AndReturnsActualSize()
    {
        var payload = "canexus content signature probe\n"u8.ToArray();
        var result = await _storage.SaveAsync(new MemoryStream(payload), "probe.txt");

        Assert.Multiple(() =>
        {
            Assert.That(result.DetectedContentType, Is.EqualTo("text/plain"));
            Assert.That(result.SizeBytes, Is.EqualTo(payload.Length));
            Assert.That(result.StoredName, Does.EndWith(".txt"));
            Assert.That(File.ReadAllBytes(Path.Combine(_root, result.RelativePath.Replace('/', Path.DirectorySeparatorChar))),
                Is.EqualTo(payload));
        });
    }

    /// <summary>PNG 文件头被识别为 image/png，伪装成文本后缀则被拒。</summary>
    [Test]
    public async Task Save_PngSignature_MatchesOnlyPngExtension()
    {
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
        ];
        var accepted = await _storage.SaveAsync(new MemoryStream(png), "real.png");
        Assert.That(accepted.DetectedContentType, Is.EqualTo("image/png"));

        var disguised = Assert.ThrowsAsync<BusinessException>(
            () => _storage.SaveAsync(new MemoryStream("plain text"u8.ToArray()), "fake.png"));
        Assert.That(disguised!.Code, Is.EqualTo(ErrorCodes.FileExtensionNotAllowed));

        var mismatched = Assert.ThrowsAsync<BusinessException>(
            () => _storage.SaveAsync(new MemoryStream(png), "fake.txt"));
        Assert.That(mismatched!.Code, Is.EqualTo(ErrorCodes.FileExtensionNotAllowed));
    }

    /// <summary>客户端声明不参与判定：SaveAsync 不接受 MIME 参数，推导值只来自内容。</summary>
    [Test]
    public async Task Save_NeverTrustsClientDeclaredMime()
    {
        // 模拟客户端声明 text/html：控制器传入的 ContentType 根本不进入存储端口，
        // 因此无论声明为何，落库候选 MIME 恒为内容签名推导值
        var result = await _storage.SaveAsync(new MemoryStream("<html>not html</html>"u8.ToArray()), "page.txt");
        Assert.That(result.DetectedContentType, Is.EqualTo("text/plain"));
    }

    /// <summary>不可 seek 的输入流仍准确统计落盘字节数。</summary>
    [Test]
    public async Task Save_NonSeekableStream_KeepsAccurateSize()
    {
        var payload = "non-seekable content"u8.ToArray();
        var result = await _storage.SaveAsync(new NonSeekableStream(payload), "stream.txt");
        Assert.That(result.SizeBytes, Is.EqualTo(payload.Length));
    }

    /// <summary>超过上限的可 seek 流在读取前即被拒（第一道），不可 seek 流在缓冲循环内被拒（第二道）。</summary>
    [Test]
    public void Save_Oversize_RejectedForBothStreamKinds()
    {
        var big = new byte[1024 * 1024 + 1];
        var seekable = Assert.ThrowsAsync<BusinessException>(
            () => _storage.SaveAsync(new MemoryStream(big), "big.txt"));
        var nonSeekable = Assert.ThrowsAsync<BusinessException>(
            () => _storage.SaveAsync(new NonSeekableStream(big), "big-stream.txt"));

        Assert.Multiple(() =>
        {
            Assert.That(seekable!.Code, Is.EqualTo(ErrorCodes.FileSizeExceeded));
            Assert.That(nonSeekable!.Code, Is.EqualTo(ErrorCodes.FileSizeExceeded));
        });
    }

    /// <summary>路径遍历仍被拒绝（定位路径不得越出存储根目录）。</summary>
    [Test]
    public void OpenRead_RejectsPathTraversal()
    {
        var exception = Assert.Throws<BusinessException>(() => _storage.OpenRead("../../etc/passwd"));
        Assert.That(exception!.Code, Is.EqualTo(ErrorCodes.FileNotFound));
    }

    /// <summary>不可 seek 的只读流桩。</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private int _position;

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            var available = Math.Min(count, data.Length - _position);
            if (available <= 0)
            {
                return 0;
            }

            Array.Copy(data, _position, buffer, offset, available);
            _position += available;
            return available;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
