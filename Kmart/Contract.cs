using System;
using System.Collections.Generic;

namespace Kmart
{
    public class Contract
    {
        public byte[] Address { get; set; }
        public HashSet<string> Functions { get; set; }
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