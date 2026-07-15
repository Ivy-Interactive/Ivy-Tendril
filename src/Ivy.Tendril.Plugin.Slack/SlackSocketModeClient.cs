using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Plugins.Slack;

public record SlackEnvelope(string Type, string? EnvelopeId, JsonElement Payload);

public class SlackSocketModeClient(
    SlackWebApiClient appClient,
    Func<SlackEnvelope, Task<object?>> envelopeHandler,
    ILogger logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoffSeconds = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var url = await appClient.OpenSocketUrlAsync(cancellationToken);
                logger.LogInformation("Slack Socket Mode connecting");
                await RunConnectionAsync(url, cancellationToken);
                backoffSeconds = 1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Slack Socket Mode connection error, reconnecting in {Backoff}s", backoffSeconds);
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken);
                backoffSeconds = Math.Min(backoffSeconds * 2, 60);
            }
        }
    }

    private async Task RunConnectionAsync(string url, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), cancellationToken);

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var message = new MemoryStream();
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                var shouldReconnect = await HandleMessageAsync(socket, text, cancellationToken);
                if (shouldReconnect)
                    return;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<bool> HandleMessageAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken)
    {
        JsonElement json;
        try
        {
            json = JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            logger.LogWarning("Slack Socket Mode received invalid JSON");
            return false;
        }

        var type = json.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";
        if (type == "disconnect")
        {
            logger.LogInformation("Slack requested reconnect");
            return true;
        }

        var envelopeId = json.TryGetProperty("envelope_id", out var idProp) ? idProp.GetString() : null;
        var payload = json.TryGetProperty("payload", out var payloadProp) ? payloadProp : default;

        object? ackPayload = null;
        if (type is "events_api" or "slash_commands" or "interactive")
        {
            try
            {
                ackPayload = await envelopeHandler(new SlackEnvelope(type, envelopeId, payload));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Slack envelope handler failed for {Type}", type);
            }
        }

        if (envelopeId != null)
        {
            var ack = ackPayload == null
                ? JsonSerializer.Serialize(new { envelope_id = envelopeId })
                : JsonSerializer.Serialize(new { envelope_id = envelopeId, payload = ackPayload });
            await socket.SendAsync(
                new ArraySegment<byte>(Encoding.UTF8.GetBytes(ack)),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }

        return false;
    }
}
