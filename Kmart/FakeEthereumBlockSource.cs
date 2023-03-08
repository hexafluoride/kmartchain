using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Kmart.Interfaces;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Math;

namespace Kmart;

public class FakeEthereumBlockSource
{
    private ILogger<FakeEthereumBlockSource> Logger;
    private IChainState ChainState; // We only use this for the genesis state
    private IBlockStorage BlockStorage;
    
    public int LastFakedHeight = 16;
    public ulong MergeHeight = 19;
    public List<DepositData> GenesisDeposits = new();
    
    private Dictionary<string, FakeEthereumBlock> fakeBlocks = new();
    private DateTime anchoringTime = DateTime.MinValue;
    private int anchoringHeight = -1;

    public FakeEthereumBlockSource(IChainState chainState, ILogger<FakeEthereumBlockSource> logger, IBlockStorage blockStorage)
    {
        ChainState = chainState ?? throw new ArgumentNullException(nameof(chainState));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        BlockStorage = blockStorage ?? throw new ArgumentNullException(nameof(blockStorage));
    }

    public FakeEthereumBlock? GetBlock(byte[] hash)
    {
        var hashString = hash.ToPrettyString(true);
        if (fakeBlocks.ContainsKey(hashString))
            return fakeBlocks[hashString];

        return null;
    }

    public FakeEthereumBlock GetBlock(int newHeight, IBlock? realBlock)
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
        if (realBlock is not null)
        {
            newHashStr = realBlock.Hash.ToPrettyString(true);
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
            // time = anchoringTime + TimeSpan.FromSeconds(10 * heightDiff);
            time = DateTime.UnixEpoch.AddSeconds((ChainState?.GenesisState?.GenesisTime ?? 0) + (ulong) (10 * (newHeight - (int) MergeHeight)));
        }

        var newBlock = new FakeEthereumBlock()
        {
            Height = newHeight,
            Difficulty = newHeight > (int) MergeHeight
                ? FakeEthereumBlock.PostMergeDifficulty
                : FakeEthereumBlock.PreMergeDifficulty,
            Hash = newHashStr,
            ParentHash = lastHash.ToPrettyString(true),
            Timestamp = (int) (time - DateTime.UnixEpoch).TotalSeconds,
            BaseFeePerGas = 0,
            FeeRecipient = new byte[20].ToPrettyString(true),
            GasUsed = 0,
            GasLimit = 0,
            PrevRandao = new byte[32].ToPrettyString(true),
            LogsBloom = new byte[256].ToPrettyString(true),
            StateRoot = new byte[32].ToPrettyString(true),
            RecipientsRoot = new byte[32].ToPrettyString(true),
            Transactions = new string[0]
        };

        if (newHeight > LastFakedHeight)
            LastFakedHeight = newHeight;
        fakeBlocks[newBlock.Hash] = newBlock;

        return newBlock;
    }

    public FakeEthereumBlock CreateFromRealBlock(IBlock block)
    {
        var hashString = block.Hash.ToPrettyString(true);
        fakeBlocks[hashString] = new FakeEthereumBlock()
        {
            Hash = block.Hash.ToPrettyString(true),
            Difficulty = block.Height < MergeHeight
                ? FakeEthereumBlock.PreMergeDifficulty
                : FakeEthereumBlock.PostMergeDifficulty,
            Height = (int) block.Height,
            ParentHash = block.Parent.ToPrettyString(true),
            Timestamp = (int) block.Timestamp,
            BaseFeePerGas = block.BaseFeePerGas,
            ExtraData = block.ExtraData.ToPrettyString(true),
            FeeRecipient = block.FeeRecipient.ToPrettyString(true),
            GasUsed = block.GasUsed,
            GasLimit = block.GasLimit,
            PrevRandao = block.PrevRandao.ToPrettyString(true),
            LogsBloom = block.LogsBloom.ToPrettyString(true),
            StateRoot = block.StateRoot.ToPrettyString(true),
            RecipientsRoot = block.ReceiptsRoot.ToPrettyString(true),
            Transactions = block.TransactionsEncoded.Select(txEncoded => txEncoded.RlpWrap().RlpUnwrapTransaction()).ToArray()
        };

        return fakeBlocks[hashString];
    }

    private Dictionary<ulong, List<object>> CachedDepositLogs = new(); 
    public List<object> GetDepositLogObjects(ulong height)
    {
        if (!CachedDepositLogs.ContainsKey(height))
        {
            IBlock? block = null;
            try
            {
                block = BlockStorage.GetBlock(height);
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Could not fetch block at {height}");
            }
            if (block is null)
            {
                return new List<object>();
            }

            // CachedDepositLogs[height] = block.GetDeposits().Select((depositData, i) =>
            //     depositData.CreateLogObject(block.Hash.ToPrettyString(true), block.Height, depositData.TransactionHash,
            //         (ulong) depositData.TransactionIndex, depositData.Address, (ulong) depositData.LogIndex,
            //         depositData.Topic)).ToList();
            CachedDepositLogs[height] = block.GetDeposits().Select(d => d.CreateLogObject()).ToList();
        }

        return CachedDepositLogs[height];
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
        var deposits = new List<object>();
        
        for (ulong u = fromBlock; u <= toBlock; u++)
        {
            // GetBlock((int) u, BlockStorage.GetBlock(u));
            deposits.AddRange(GetDepositLogObjects(u));
        }
        
        if (fromBlock <= MergeHeight && toBlock >= MergeHeight)
        {
            if (!CachedDepositLogs.ContainsKey(MergeHeight) || !CachedDepositLogs[MergeHeight].Any())
            {
                GetBlock((int) MergeHeight, null);
                var mergeBlockHash = fakeBlocks.First(p => p.Value.Height == (int) MergeHeight).Key;
                CachedDepositLogs[MergeHeight] = new();
                CachedDepositLogs[MergeHeight].AddRange(GenesisDeposits.Select((depositData, i) =>
                    depositData.CreateLogObject(mergeBlockHash, MergeHeight,
                        Enumerable.Repeat((byte) 32, 32).ToArray().ToPrettyString(true), 0,
                        Enumerable.Repeat((byte) 32, 32).ToArray().ToPrettyString(true), (ulong) i, topic)).ToArray());
            }
            deposits.AddRange(CachedDepositLogs[MergeHeight]);
        }

        // TODO: Look at native blocks to see if they create any deposits
        return deposits;
    }
}

public class FakeEthereumBlock
{
    public int Height { get; set; }
    public int Timestamp { get; set; }
    public string ParentHash { get; set; } = "0x";
    public string Hash { get; set; } = "0x";
    public string Difficulty { get; set; } = "0x";
    public string FeeRecipient { get; set; } = "0x";
    public string StateRoot { get; set; } = "0x";
    public string RecipientsRoot { get; set; } = "0x";
    public string LogsBloom { get; set; } = "0x";
    public string ExtraData { get; set; } = "0x";
    public string PrevRandao { get; set; } = "0x";
    public ulong GasLimit { get; set; }
    public ulong GasUsed { get; set; }
    public ulong BaseFeePerGas { get; set; }
    public object[] Transactions { get; set; }

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
                .Reverse().ToArray().ToPrettyString(true, true),
            miner = FeeRecipient,
            stateRoot = StateRoot,
            receiptsRoot = RecipientsRoot,
            logsBloom = LogsBloom,
            gasLimit = GasLimit.EncodeQuantity(),
            gasUsed = GasUsed.EncodeQuantity(),
            baseFeePerGas = BaseFeePerGas.EncodeQuantity(),
            extraData = ExtraData,
            mixHash = PrevRandao,
            uncles = new object[0],
            sha3Uncles = "0x1dcc4de8dec75d7aab85b567b6ccd41ad312451b948a7413f0a142fd40d49347",
            transactions = Transactions
        };
    }
}