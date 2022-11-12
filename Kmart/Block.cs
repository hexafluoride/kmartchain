using System.Collections.Generic;
using System.Text.Json.Serialization;
using Nethereum.Model;
using SszSharp;

namespace Kmart
{
    
    public class Block
    {
        public static SszContainer<Block> SszType = SszContainer.GetContainer<Block>();

        [JsonIgnore]
        [SszElement(0, "Vector[uint8, 32]")]
        public byte[] Hash { get; set; } = new byte[32];

        [SszElement(1, "Vector[uint8, 8]")]
        public byte[] Nonce { get; set; } = new byte[8];
        [SszElement(2, "Vector[uint8, 32]")]
        public byte[] Parent { get; set; } = new byte[32];
        
        [SszElement(3, "uint64")]
        public ulong Timestamp { get; set; }

        [SszElement(4, "List[Container, 65536]")]
        public Transaction[] Transactions { get; set; } = new Transaction[0];
        [SszElement(5, "uint64")]
        public ulong Height { get; set; }
        [SszElement(6, "Vector[uint8, 20]")]
        public byte[] Coinbase { get; set; } = new byte[20];

        public BlockHeader GetEthBlockHeader()
        {
            return new BlockHeader()
            {
                ParentHash = Parent,
                BaseFee = 1,
                BlockNumber = Height,
                Coinbase = Coinbase.ToPrettyString(),
                Difficulty = 0,
                ExtraData = new byte[0],
                GasLimit = 2,
                GasUsed = 0,
                LogsBloom = new byte[256],
                MixHash = new byte[32],
                UnclesHash = "0x1dcc4de8dec75d7aab85b567b6ccd41ad312451b948a7413f0a142fd40d49347".ToByteArray(),
                Nonce = new byte[8],
                Timestamp = (long)Timestamp,
                TransactionsHash = new byte[32],
                ReceiptHash = new byte[32],
                StateRoot = new byte[32]
            };
        }

        public List<DepositData> GetDeposits()
        {
            // TODO: Parse special Kmart-native validator deposit transaction
            return new List<DepositData>();
        }
        
        public void CalculateHash()
        {
            Hash = new byte[32];
            Hash = Merkleizer.HashTreeRoot(SszType, this);
        }

        public override string ToString()
        {
            return $"{Height}/{Hash.ToPrettyString()}";
        }
    }
}