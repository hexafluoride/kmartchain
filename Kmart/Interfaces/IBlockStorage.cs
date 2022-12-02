namespace Kmart;

public interface IBlockStorage
{
    public IBlock? GetBlock(ulong height);
    public IBlock? GetBlock(byte[] hash);
    public void StoreBlock(IBlock block);
}