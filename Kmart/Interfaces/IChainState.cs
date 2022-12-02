using System.Collections.Generic;
using SszSharp;

namespace Kmart.Interfaces;

public interface IChainState
{
    public IBlock? LastBlock { get; }
    public byte[] LastBlockHash { get; }
    public BeaconState? GenesisState { get; }
    public List<byte[]> Ancestors { get; }
    bool HasSnapshot(byte[] blockHash);
    bool? LoadSnapshot(byte[] blockHash);
    IBlock? GetCommonAncestor(IBlock block);
    bool IsAncestorOfHead(byte[] hash);
    void SetGenesisState(BeaconState state);
    bool ValidateBlock(IBlock block);
    (bool, RollbackContext?) ProcessBlock(IBlock block);
}