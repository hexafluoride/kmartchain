using System;
using System.Text.Json.Serialization;
using SszSharp;

namespace Kmart.Qemu
{
    public class Transaction
    {
        public static SszContainer<Transaction> SszType = SszContainer.GetContainer<Transaction>();

        [JsonIgnore]
        [SszElement(0, "Vector[uint8, 32]")]
        public byte[] Hash { get; set; } = new byte[32];
        [SszElement(1, "uint8")]
        public byte TypeByte { get; set; }
        public TransactionType Type
        {
            get => (TransactionType) TypeByte;
            set => TypeByte = (byte) value;
        }

        [SszElement(2, "Vector[uint8, 20]")]
        public byte[] Address { get; set; } = new byte[20];
        [SszElement(3, "uint64")]
        public ulong Nonce { get; set; }

        [SszElement(4, "Vector[uint8, 64]")]
        public byte[] Signature { get; set; } = new byte[64];
        [SszElement(5, "uint64")]
        public ulong Timestamp { get; set; }
        
        [SszElement(6, "uint64")]
        public ulong FeePerByte { get; set; }

        [SszElement(7, "List[uint8, 16777216]")]
        public byte[] Payload { get; set; } = new byte[0];

        public void CalculateHash()
        {
            var sigTemp = Signature;
            Signature = new byte[64];
            Hash = new byte[32];
            Hash = Merkleizer.HashTreeRoot(SszType, this);
            Signature = sigTemp;
        }

        public static (Transaction, int) Deserialize(ReadOnlySpan<byte> bytes)
        {
            return SszType.Deserialize(bytes);
        }

        public int Serialize(Span<byte> bytes)
        {
            return SszType.Serialize(this, bytes);
        }
    }

    public enum TransactionType
    {
        Simple = 0,
        Deploy = 1,
        Invoke = 2
    }
}