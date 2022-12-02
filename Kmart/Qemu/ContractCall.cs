namespace Kmart.Qemu;

public struct ContractCall
{
    //public Contract Contract { get; set; }
    public byte[] Contract { get; set; }
    public byte[] Caller { get; set; }
    public byte[] Source { get; set; }
    public string Function { get; set; }
    public byte[] CallData { get; set; }
}