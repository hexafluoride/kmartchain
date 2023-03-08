using SszSharp;

namespace Kmart;

public class AnnotatedDepositData
{
    [SszElement(0, "Container")] public DepositData DepositData { get; set; }
    [SszElement(1, "List[uint8, MAX_BLOCK_HASH]")] public byte[] BlockHash { get; set; } = new byte[0];
    [SszElement(2, "uint64")] public ulong BlockHeight { get; set; }
    [SszElement(3, "List[uint8, MAX_TX_HASH]")] public byte[] TransactionHash { get; set; } = new byte[0];
    [SszElement(4, "uint32")] public uint TransactionIndex { get; set; }
    [SszElement(5, "List[uint8, MAX_ACCOUNT]")] public byte[] Address { get; set; } = new byte[0];
    [SszElement(6, "uint32")] public uint LogIndex { get; set; }

    [SszElement(7, "List[uint8, MAX_BLOCK_EXTRA]")] public byte[] Topic { get; set; } = new byte[0];
    public object CreateLogObject()
    {
        return DepositData.CreateLogObject(BlockHash.ToPrettyString(true), BlockHeight,
            TransactionHash.ToPrettyString(true), TransactionIndex, Address.ToPrettyString(true),
            LogIndex, Topic.ToPrettyString(true));
    }
}