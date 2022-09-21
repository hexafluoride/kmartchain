using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SszSharp;

namespace Kmart;

public class BlockStorage
{
    public Dictionary<ulong, List<Block>> BlocksByHeight = new();
    public Dictionary<byte[], Block> BlocksByHash = new(new ByteArrayComparer());

    private readonly BlobManager BlobManager;
    private readonly ILogger<BlobManager> Logger;
    
    public BlockStorage(BlobManager blobManager)
    {
        BlobManager = blobManager ?? throw new ArgumentNullException(nameof(blobManager));
    }
    
    public Block? GetBlock(ulong height)
    {
        if (!BlocksByHeight.ContainsKey(height))
            return null;

        if (BlocksByHeight[height].Count > 1)
            throw new Exception($"Conflicting blocks found for height {height}");

        return BlocksByHeight[height][0];
    }

    public Block? GetBlock(byte[] hash)
    {
        if (!BlocksByHash.ContainsKey(hash))
        {
            var path = BlobManager.GetPath(hash, BlobManager.BlockKey);
            if (!File.Exists(path))
                return null;

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

    public void StoreBlock(Block block)
    {
        lock (BlocksByHeight)
        lock (BlocksByHash)
        {
            if (BlocksByHash.ContainsKey(block.Hash))
                return;

            if (!BlocksByHeight.ContainsKey(block.Height))
                BlocksByHeight[block.Height] = new();

            BlocksByHeight[block.Height].Add(block);
            BlocksByHash[block.Hash] = block;
            
            File.WriteAllBytes(BlobManager.GetPath(block.Hash, BlobManager.BlockKey), SszContainer.Serialize(block));
        }
    }
}