using VPetLLM.Core.Interaction;

namespace RemoteChatPlugin;

/// <summary>
/// 把处理管线中途的交互请求经加密中继推送到浏览器，并等待用户回执。
/// 绑定到单次远端聊天请求（requestId）。
/// </summary>
internal sealed class RemoteInteractionResponder : IRemoteInteractionResponder
{
    private readonly RemoteChatConnection _connection;
    private readonly string _requestId;

    internal RemoteInteractionResponder(RemoteChatConnection connection, string requestId)
    {
        _connection = connection;
        _requestId = requestId;
    }

    public async Task<InteractionResult> RequestAsync(InteractionRequest request, CancellationToken cancellationToken = default)
    {
        var (confirmed, value) = await _connection
            .RequestInteractionAsync(_requestId, request, cancellationToken)
            .ConfigureAwait(false);
        return confirmed ? InteractionResult.Accepted(value) : InteractionResult.Rejected;
    }
}
