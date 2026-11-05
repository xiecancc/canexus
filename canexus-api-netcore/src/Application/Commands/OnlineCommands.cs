
using Application.Abstractions;
using Application.Contracts.Commands;
using Domain.Repositories;
using Domain.Services.Auth;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>强制下线指定在线会话命令处理器：按记录 Id 吊销活跃 refreshToken，并按会话标识吊销其 accessToken。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="refreshTokenRepository">刷新令牌仓储。</param>
/// <param name="tokenVersionService">令牌版本服务（写入会话级吊销集合）。</param>
public sealed class OnlineForceLogoutCommandHandler(
    ISysRefreshTokenRepository refreshTokenRepository,
    ITokenVersionService tokenVersionService) : RequestHandler<OnlineForceLogoutCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(OnlineForceLogoutCommand command, CancellationToken cancellationToken)
    {
        var session = await refreshTokenRepository.GetAsync([it => it.Id == command.TokenId], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.OnlineSessionNotFound, "在线会话不存在或已下线");

        // 会话级吊销：accessToken 的 sid 声明与该会话一致，下一次请求即 401；其他设备会话不受影响
        if (session.SessionId is { } sessionId && sessionId != Guid.Empty)
        {
            await tokenVersionService.RevokeSessionAsync(sessionId, cancellationToken);
        }
        else
        {
            // 升级前签发、无会话标识的历史会话：只能退化为该用户全量下线，避免旧 accessToken 继续有效
            await tokenVersionService.RevokeAllAsync(session.UserId, cancellationToken);
        }

        // 单条 UPDATE 按列吊销（仅未吊销且未过期记录受影响）；0 行说明会话已下线 / 已过期
        var affected = await refreshTokenRepository.RevokeByIdAsync(command.TokenId, DateTime.UtcNow, cancellationToken);
        if (affected == 0)
        {
            throw new BusinessException(ErrorCodes.OnlineSessionNotFound, "在线会话不存在或已下线");
        }
    }
}
