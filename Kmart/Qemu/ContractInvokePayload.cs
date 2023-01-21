using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SszSharp;

namespace Kmart.Qemu
{
    public class ContractInvokePayload
    {
        [SszElement(0, "Vector[uint8, 20]")]
        public byte[] Target { get; set; } = new byte[20];
        [SszElement(1, "List[uint8, 1024]")]
        public byte[] FunctionBytes
        {
            get => Encoding.UTF8.GetBytes(Function);
            set => Function = Encoding.UTF8.GetString(value);
        }

        public string Function { get; set; } = "";

        [SszElement(2, "List[uint8, 16777216]")]
        public byte[] CallData { get; set; } = new byte[0];

        public static ContractInvokePayload FromReceipt(ContractInvocationReceipt receipt)
        {
            var ret = new ContractInvokePayload()
            {
                CallData = receipt.Call.CallData,
                Function = receipt.Call.Function,
                Target = receipt.Call.Contract
            };

            return ret;
        }
        
        public static ContractInvokePayload FromTransaction(Transaction transaction)
        {
            return SszContainer.Deserialize<ContractInvokePayload>(transaction.Payload).Item1;
        }

        public byte[] Serialize()
        {
            return SszContainer.Serialize(this);
        }
    }
}