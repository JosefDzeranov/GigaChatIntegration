using System.Text.Json.Serialization;

namespace GigaChatIntegration.GigaChat.Models
{
    internal record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_at")] long ExpiresAt);
}
