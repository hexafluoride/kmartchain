using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kmart.Qemu
{
    public class StateMap
    {
        public readonly ReadOnlyMemory<byte> Backing;
        private readonly int Entries;
        private const int WordSize = 32;

        private readonly Dictionary<byte[], byte[]> Cache = new(new ByteArrayComparer());

        public StateMap(ReadOnlyMemory<byte> backing)
        {
            Backing = backing;
            Entries = backing.Length / (WordSize * 2);
        }

        public byte[]? ReadBytes(byte[] key) => Read(key)?.ToArray();
        public ReadOnlyMemory<byte>? Read(byte[] key)
        {
            if (Cache.ContainsKey(key))
                return Cache[key];

            var index = FindIndex(key);
            if (index == -1)
            {
                return null;
            }
            
            return Backing.Slice(WordSize * (Entries + index), WordSize);
        }

        int FindIndex(byte[] key)
        {
            int slice = 0;
            return -1;
        }
    }
    
    public class Contract
    {
        public byte[] Address { get; set; } = new byte[20];
        public HashSet<string> Functions { get; set; } = new();

        [JsonConverter(typeof(ByteArrayKeyDictionaryConverter<byte[]>))]
        public Dictionary<byte[], byte[]> State { get; set; } = new(new ByteArrayComparer());
        public ContractBootType BootType { get; set; }

        public byte[] ReadState(byte[] key)
        {
            if (!State.ContainsKey(key))
            {
                return new byte[32];
            }
        
            return State[key];
        }

        public void WriteState(byte[] key, byte[] value)
        {
            if (key.Length != 32 || value.Length != 32)
                throw new InvalidOperationException();

            State[key] = value;
        }
    }

    public enum ContractBootType
    {
        Legacy,
        Multiboot
    }
}