using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SszSharp;

namespace Kmart;

public interface IBlockStorage
{
    public IBlock? GetBlock(ulong height);
    public IBlock? GetBlock(byte[] hash);
    public void StoreBlock(IBlock block);
}

public class BlockStorage : IBlockStorage
{
    public Dictionary<ulong, List<IBlock>> BlocksByHeight = new();
    public Dictionary<byte[], IBlock> BlocksByHash = new(new ByteArrayComparer());

    private readonly BlobManager BlobManager;
    private readonly ILogger<BlockStorage> Logger;
    
    public BlockStorage(BlobManager blobManager, ILogger<BlockStorage> logger)
    {
        BlobManager = blobManager ?? throw new ArgumentNullException(nameof(blobManager));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public IBlock? GetBlock(ulong height)
    {
        if (!BlocksByHeight.ContainsKey(height))
            return null;

        if (BlocksByHeight[height].Count > 1)
            throw new Exception($"Conflicting blocks found for height {height}");

        return BlocksByHeight[height][0];
    }

    public IBlock? GetBlock(byte[] hash)
    {
        if (!BlocksByHash.ContainsKey(hash))
        {
            var path = BlobManager.GetPath(hash, BlobManager.BlockKey);
            if (!File.Exists(path))
            {
                Logger.LogWarning($"Could not find block {hash.ToPrettyString()}");
                return null;
            }

            try
            {
                (var blockLoaded, _) = SszContainer.Deserialize<Block>(File.ReadAllBytes(path));
                if (!blockLoaded.Hash.SequenceEqual(hash))
                {
                    throw new Exception("Loaded block does not match block hash");
                }

                lock (BlocksByHash)
                {
                    return BlocksByHash[hash] = blockLoaded;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Could not load block with hash {blockHash}", hash.ToPrettyString());
                return null;
            }
        }

        return BlocksByHash[hash];
    }

    public void StoreBlock(IBlock genericBlock)
    {
        if (!(genericBlock is Block block))
            throw new Exception($"Type mismatch, expected {typeof(Block)}, got {genericBlock.GetType()}");
        
        lock (BlocksByHeight)
        lock (BlocksByHash)
        {
            if (BlocksByHash.ContainsKey(block.Hash))
                return;

            if (!BlocksByHeight.ContainsKey(block.Height))
                BlocksByHeight[block.Height] = new();

            BlocksByHeight[block.Height].Add(block);
            BlocksByHash[block.Hash] = block;
            
            Logger.LogInformation($"Stored block {block.Height}/{block.Hash.ToPrettyString()}");
            File.WriteAllBytes(BlobManager.GetPath(block.Hash, BlobManager.BlockKey), SszContainer.Serialize<Block>(block));
        }
    }
}