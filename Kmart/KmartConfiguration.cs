namespace Kmart;

public sealed class KmartConfiguration
{
    public string RpcHost { get; set; } = "127.0.0.1";
    public int RpcPort { get; set; } = 8551;
    public string BeaconApiEndpoint { get; set; } = "";
    public string GenesisStatePath { get; set; } = "";
    public string LighthouseValidatorDirectory { get; set; } = "";
    public string DataDirectory { get; set; } = "./datadir";
}