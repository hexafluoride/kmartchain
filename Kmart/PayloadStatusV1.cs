using System.Text.Json.Serialization;

namespace Kmart;

public class PayloadStatusV1
{
    public PayloadStatusV1(string status, string? latestValidHash, string? validationError)
    {
        Status = status;
        LatestValidHash = latestValidHash;
        ValidationError = validationError;
    }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "INVALID";
    [JsonPropertyName("latestValidHash")]
    public string? LatestValidHash { get; set; }
    [JsonPropertyName("validationError")]
    public string? ValidationError { get; set; }
}