using System.Text.Json.Serialization;
using Nethereum.Hex.HexTypes;

namespace Kmart;

public class TransitionConfigurationV1
{
    public TransitionConfigurationV1(string terminalTotalDifficulty, string terminalBlockHash, string terminalBlockNumber)
    {
        TerminalTotalDifficulty = terminalTotalDifficulty;
        TerminalBlockHash = terminalBlockHash;
        TerminalBlockNumber = terminalBlockNumber;
    }

    [JsonPropertyName("terminalTotalDifficulty")]
    public string TerminalTotalDifficulty { get; set; }
    [JsonPropertyName("terminalBlockHash")]
    public string TerminalBlockHash { get; set; }
    [JsonPropertyName("terminalBlockNumber")]
    public string TerminalBlockNumber { get; set; }
}