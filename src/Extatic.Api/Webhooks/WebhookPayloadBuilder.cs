using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Extatic.Api.Webhooks;

public class WebhookPayloadBuilder
{
    public string Build(string eventType, Guid appId, object data)
    {
        var payload = new
        {
            @event = eventType,
            timestamp = DateTime.UtcNow,
            app_id = appId,
            data
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
    }

    public string ComputeSignature(string secret, string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
