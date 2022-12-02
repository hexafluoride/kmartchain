namespace Kmart;

public interface IBlock
{
    public byte[] Hash { get; }
    public byte[] Parent { get; }
    public ulong Height { get; }
    public ulong Timestamp { get; }
}