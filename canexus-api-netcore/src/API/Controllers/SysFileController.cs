using API.Authorization;
using API.Extensions;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Files;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Services.Storage;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>文件存储管理接口：上传（multipart/form-data）、分页、详情与下载。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
/// <param name="fileStorage">文件存储端口（物理文件读写）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/system/file")]
[Authorize]
public class SysFileController(ISender sender, IFileStorage fileStorage) : ControllerBase
{
    /// <summary>上传文件（multipart/form-data；扩展名白名单与大小上限校验失败返回 401202 / 401203；请求体上限经 Kestrel 按 FileStorage:MaxSizeMB 配置对齐）。
    /// 未选择文件时抛业务异常（FileNotFound），由全局异常中间件统一映射 HTTP 状态（P1-R1：失败响应不再以 HTTP 200 包装）。</summary>
    /// <param name="file">上传文件。</param>
    /// <param name="bizCategory">业务用途分类（可空）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>文件 Id 与元数据摘要。</returns>
    [HttpPost("upload")]
    [RequirePermission(PermissionDefinitions.SystemFileUpload)]
    [OperLog("文件存储", OperBusinessType.Add)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string? bizCategory = null, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            throw new BusinessException(Domain.Shared.Results.ErrorCodes.FileNotFound, "请选择要上传的文件");
        }

        await using var stream = file.OpenReadStream();
        var data = await sender.Send(new FileUploadCommand(file.FileName, file.ContentType, bizCategory, stream), cancellationToken);
        return Ok(Result.Ok(data, message: "上传成功"));
    }

    /// <summary>分页查询文件元数据（文件名模糊 / 用途分类过滤，按创建时间倒序）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.SystemFileList)]
    public async Task<IActionResult> Page([FromQuery] FilePageRequest request, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new FileQueryPaged(request), cancellationToken)));
    }

    /// <summary>按 Id 查询文件详情（元数据回显）。</summary>
    /// <param name="id">文件 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>文件详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionDefinitions.SystemFileQuery)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new FileQueryDetail(id), cancellationToken)));
    }

    /// <summary>下载文件（流式回传物理文件，文件名沿用原始上传名）。</summary>
    /// <param name="id">文件 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>文件流。</returns>
    [HttpGet("{id:guid}/download")]
    [RequirePermission(PermissionDefinitions.SystemFileDownload)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await sender.Send(new FileQueryDetail(id), cancellationToken);
        var stream = fileStorage.OpenRead(detail.RelativePath);

        // 下载一律以二进制类型输出并禁用内容嗅探：即使库中 MIME 被历史脏数据污染，
        // 浏览器也不会把上传物按 HTML / SVG 等可执行类型渲染；Content-Disposition 仍为 attachment
        // （File(...) 内部经 ContentDispositionHeaderValue 编码文件名，非 ASCII 名按 RFC 5987 处理）
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, "application/octet-stream", detail.FileName);
    }

    /// <summary>删除文件（软删除元数据；物理文件保留供恢复与审计回溯）。</summary>
    /// <param name="id">文件 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemFileRemove)]
    [OperLog("文件存储", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new FileDeleteCommand(id, version), cancellationToken);
        return Ok(Result.Ok(affected, message: "删除成功"));
    }
}
