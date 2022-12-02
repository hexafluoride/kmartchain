using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Kmart;

public class FakeEthereumBlockSource
{
    private readonly IBlockStorage BlockStorage;
    private ILogger<FakeEthereumBlockSource> Logger;
    private IChainState? ChainState; // TODO: Reduce dependence on ChainState for this
    
    public int LastFakedHeight = 16;
    public ulong MergeHeight = 19;
    public List<DepositData> GenesisDeposits = new();
    
    private Dictionary<string, FakeEthereumBlock> fakeBlocks = new();
    private DateTime anchoringTime = DateTime.MinValue;
    private int anchoringHeight = -1;

    public FakeEthereumBlockSource(BlockStorage blockStorage, ILogger<FakeEthereumBlockSource> logger)
    {
        BlockStorage = blockStorage ?? throw new ArgumentNullException(nameof(blockStorage));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void UseChainState(IChainState chainState)
    {
        ChainState = chainState ?? throw new ArgumentNullException(nameof(chainState));
    }

    public FakeEthereumBlock? GetBlock(byte[] hash)
    {
        var hashString = hash.ToPrettyString(true);
        if (fakeBlocks.ContainsKey(hashString))
            return fakeBlocks[hashString];

        return null;
    }
    
    public FakeEthereumBlock GetBlock(int newHeight)
    {
        var localRng = new Random(newHeight);
        var newHash = new byte[32];
        localRng.NextBytes(newHash);
        var newHashStr = newHash.ToPrettyString(true);

        if (fakeBlocks.ContainsKey(newHashStr))
            return fakeBlocks[newHashStr];

        var lastHash = new byte[32];
        var lastRng = new Random(newHeight - 1);

        if (newHeight != 0)
            lastRng.NextBytes(lastHash);

        var time = DateTime.UtcNow;
        var possibleRealMatch = ChainState?.Ancestors?.FirstOrDefault(ancestorHash =>
            BlockStorage.GetBlock(ancestorHash)?.Height == (ulong) newHeight);
        if (possibleRealMatch is not null)
        {
            var realBlock = BlockStorage.GetBlock(possibleRealMatch) ?? throw new Exception($"Could not find block {possibleRealMatch.ToPrettyString()}");
            newHash = realBlock.Hash;
            lastHash = realBlock.Parent;
            time = DateTime.UnixEpoch + TimeSpan.FromSeconds(realBlock.Timestamp);
        }
        else
        {
            if (anchoringHeight == -1)
            {
                anchoringHeight = (int) MergeHeight;
                Logger.LogInformation($"Genesis time is {ChainState?.GenesisState?.GenesisTime.ToString() ?? "(null)"}");
                anchoringTime = DateTime.UnixEpoch.AddSeconds(
                    ChainState?.GenesisState?.GenesisTime ?? 0);
            }

            var heightDiff = newHeight - anchoringHeight;
            time = anchoringTime + TimeSpan.FromSeconds(1 * heightDiff);
        }

        var newBlock = new FakeEthereumBlock()
        {
            Height = newHeight,
            Difficulty = newHeight > (int) MergeHeight
                ? FakeEthereumBlock.PostMergeDifficulty
                : FakeEthereumBlock.PreMergeDifficulty,
            Hash = newHashStr,
            ParentHash = lastHash.ToPrettyString(true),
            Timestamp = (int) (time - DateTime.UnixEpoch).TotalSeconds
        };

        if (newHeight > LastFakedHeight)
            LastFakedHeight = newHeight;
        fakeBlocks[newBlock.Hash] = newBlock;

        return newBlock;
    }

    public FakeEthereumBlock CreateFromRealBlock(IBlock block)
    {
        var hashString = block.Hash.ToPrettyString();
        fakeBlocks[hashString] = new FakeEthereumBlock()
        {
            Hash = block.Hash.ToPrettyString(true),
            Difficulty = block.Height < MergeHeight
                ? FakeEthereumBlock.PreMergeDifficulty
                : FakeEthereumBlock.PostMergeDifficulty,
            Height = (int) block.Height,
            ParentHash = block.Parent.ToPrettyString(true),
            Timestamp = (int) block.Timestamp
        };

        return fakeBlocks[hashString];
    }

    public object GetLogsResponse(JsonElement parameters)
    {
        var spec = parameters[0];
        var fromBlockSpec = spec.GetProperty("fromBlock").GetString() ?? throw new Exception("No fromBlock in logs request");
        var toBlockSpec = spec.GetProperty("toBlock").GetString() ?? throw new Exception("No toBlock in logs request");
        ulong fromBlock, toBlock;

        if (fromBlockSpec == "latest")
            fromBlock = (ulong)LastFakedHeight;
        else
        {
            fromBlock = fromBlockSpec.ToQuantity();
        }

        if (toBlockSpec == "latest")
            toBlock = (ulong) LastFakedHeight;
        else
        {
            toBlock = toBlockSpec.ToQuantity();
        }

        var topic = spec.GetProperty("topics")[0].GetString() ?? throw new Exception($"No topic in logs request");
        
        if (fromBlock <= MergeHeight && toBlock >= MergeHeight)
        {
            var mergeBlockHash = fakeBlocks.First(p => p.Value.Height == (int)MergeHeight).Key;
            return GenesisDeposits.Select((depositData, i) => depositData.CreateLogObject(mergeBlockHash, MergeHeight,
                Enumerable.Repeat((byte) 32, 32).ToArray().ToPrettyString(true), 0,
                Enumerable.Repeat((byte) 32, 32).ToArray().ToPrettyString(true), (ulong) i, topic)).ToArray();
        }

        // TODO: Look at native blocks to see if they create any deposits
        return new object[0];
    }
}

public class FakeEthereumBlock
{
    public int Height { get; set; }
    public int Timestamp { get; set; }
    public string ParentHash { get; set; } = "0x0";
    public string Hash { get; set; } = "0x0";
    public string Difficulty { get; set; } = "0x0";

    public const string PreMergeDifficulty = "0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffbff";
    public const string PostMergeDifficulty = "0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffc01";

    public object Encode()
    {
        return new
        {
            number = BitConverter.GetBytes(Height)
                .Reverse().ToArray().ToPrettyString(true, true),
            hash = Hash,
            parentHash = ParentHash,
            nonce = new byte[8].ToPrettyString(prefix: true),
            difficulty = Difficulty,
            totalDifficulty = Difficulty,
            timestamp = BitConverter.GetBytes(Timestamp)
                .Reverse().ToArray().ToPrettyString(true, true)
        };
    }
}