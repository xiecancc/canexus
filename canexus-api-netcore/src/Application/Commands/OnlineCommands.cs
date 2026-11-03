
using Application.Abstractions;
using Application.Contracts.Commands;
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>强制下线指定在线会话命令处理器：按记录 Id 吊销活跃 refreshToken，会话随后无法刷新令牌。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="refreshTokenRepository">刷新令牌仓储。</param>
public sealed class OnlineForceLogoutCommandHandler(ISysRefreshTokenRepository refreshTokenRepository) : RequestHandler<OnlineForceLogoutCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(OnlineForceLogoutCommand command, CancellationToken cancellationToken)
    {
        // 单条 UPDATE 按列吊销（仅未吊销且未过期记录受影响）；0 行说明会话不存在 / 已下线 / 已过期
        var affected = await refreshTokenRepository.RevokeByIdAsync(command.TokenId, DateTime.UtcNow, cancellationToken);
        if (affected == 0)
        {
            throw new BusinessException(ErrorCodes.OnlineSessionNotFound, "在线会话不存在或已下线");
        }
    }
}
