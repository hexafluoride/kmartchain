using System;
using System.Text.Json;
using SszSharp;

namespace Kmart
{
    public class ContractDeployPayload
    {
        [SszElement(0, "List[uint8, 16777216]")]
        public byte[] Image { get; set; } = Array.Empty<byte>();

        [SszElement(1, "List[List[uint8, 256], 256]")]
        public string[] Functions { get; set; } = Array.Empty<string>();
        [SszElement(2, "uint8")]
        public ContractBootType BootType { get; set; }

        [SszElement(3, "List[uint8, 16777216]")]
        public byte[] Initrd { get; set; } = Array.Empty<byte>();

        public static ContractDeployPayload FromTransaction(Transaction transaction)
        {
            //return JsonSerializer.Deserialize<ContractDeployPayload>(transaction.Payload);
            return SszContainer.Deserialize<ContractDeployPayload>(transaction.Payload).Item1;
        }

        public byte[] Serialize() => SszContainer.Serialize(this);
    }
}