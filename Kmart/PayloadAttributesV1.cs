using System.Text.Json.Serialization;
using Nethereum.Hex.HexTypes;
using Newtonsoft.Json;

namespace Kmart;

public class PayloadAttributesV1
{
    public PayloadAttributesV1(HexBigInteger timestamp, string prevRandao, string suggestedFeeRecipient)
    {
        Timestamp = timestamp;
        PrevRandao = prevRandao;
        SuggestedFeeRecipient = suggestedFeeRecipient;
    }

    [JsonProperty("timestamp")]
    public HexBigInteger Timestamp { get; set; }
    [JsonProperty("prevRandao")]
    public string PrevRandao { get; set; }
    [JsonProperty("suggestedFeeRecipient")]
    public string SuggestedFeeRecipient { get; set; }
}