using System.Text.Json.Serialization;

namespace Kmart;

public class ForkchoiceStateV1
{
    public ForkchoiceStateV1(string headBlockHash, string safeBlockHash, string finalizedBlockHash)
    {
        HeadBlockHash = headBlockHash;
        SafeBlockHash = safeBlockHash;
        FinalizedBlockHash = finalizedBlockHash;
    }

    [JsonPropertyName("headBlockHash")]
    public string HeadBlockHash { get; set; }
    [JsonPropertyName("safeBlockHash")]
    public string SafeBlockHash { get; set; }
    [JsonPropertyName("finalizedBlockHash")]
    public string FinalizedBlockHash { get; set; }
}