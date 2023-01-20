using System.Collections.Generic;

namespace Kmart.Interfaces;

public interface IBlock
{
    public byte[] Hash { get; }
    public byte[] Parent { get; }
    public byte[] FeeRecipient { get; }
    public byte[] StateRoot { get; }
    public byte[] PrevRandao { get; }
    public byte[] ReceiptsRoot { get; }
    public byte[] LogsBloom { get; }
    public byte[] ExtraData { get; }
    public ulong Height { get; }
    public ulong Timestamp { get; }
    public ulong GasLimit { get; }
    public ulong GasUsed { get; }
    public ulong BaseFeePerGas { get; }
    public IEnumerable<byte[]> TransactionsEncoded { get; }
}