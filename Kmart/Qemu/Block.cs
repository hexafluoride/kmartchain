using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Kmart.Interfaces;
using SszSharp;

namespace Kmart.Qemu
{
    public class Block : IBlock
    {
        public static SszContainer<Block> SszType = SszContainer.GetContainer<Block>();

        public byte[] Hash => _hash ??= SszContainer.HashTreeRoot(this);
        private byte[]? _hash;

        [SszElement(0, "Vector[uint8, 8]")]
        public byte[] Nonce { get; set; } = new byte[8];
        [SszElement(1, "Vector[uint8, 32]")]
        public byte[] Parent { get; set; } = new byte[32];
        
        [SszElement(2, "uint64")]
        public ulong Timestamp { get; set; }

        private Transaction[] _transactions = new Transaction[0];
        
        [SszElement(3, "List[Container, 65536]")]
        public Transaction[] Transactions
        {
            get => _transactions;
            set
            {
                _transactions = value;
                transactionsEncoded = _transactions.Select(tx => SszContainer.Serialize(tx)).ToList();
            }
        }
        [SszElement(4, "uint64")]
        public ulong Height { get; set; }
        [SszElement(5, "Vector[uint8, 20]")]
        public byte[] FeeRecipient { get; set; } = new byte[20];
        
        [SszElement(6, "Vector[uint8, 32]")] public byte[] PrevRandao { get; set; } = new byte[32];
        [SszElement(7, "Vector[uint8, 32]")] public byte[] StateRoot { get; set; } = new byte[32];
        [SszElement(8, "Vector[uint8, 32]")] public byte[] ReceiptsRoot { get; set; } = new byte[32];
        [SszElement(9, "Vector[uint8, 32]")] public byte[] LogsBloom { get; set; } = new byte[32];
        [SszElement(10, "Vector[uint8, 32]")] public byte[] ExtraData { get; set; } = new byte[0];
        [SszElement(11, "uint64")] public ulong GasLimit { get; set; }
        [SszElement(12, "uint64")] public ulong GasUsed { get; set; }
        [SszElement(13, "uint64")] public ulong BaseFeePerGas { get; set; }
        private List<byte[]> transactionsEncoded = new();
        public IEnumerable<byte[]> TransactionsEncoded => transactionsEncoded;

        public List<DepositData> GetDeposits()
        {
            // TODO: Parse special Kmart-native validator deposit transaction
            return new List<DepositData>();
        }
        
        public void CalculateHash() => _hash = SszContainer.HashTreeRoot(this);
        public void FakeHash(byte[] hash) => _hash = hash;

        public override string ToString()
        {
            return $"{Height}/{Hash.ToPrettyString()}";
        }
    }
}