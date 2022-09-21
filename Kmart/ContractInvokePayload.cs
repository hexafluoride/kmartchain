using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SszSharp;

namespace Kmart
{
    public class ContractInvokePayload
    {
        [SszElement(0, "Vector[uint8, 20]")]
        public byte[] Target { get; set; }
        [SszElement(1, "List[uint8, 1024]")]
        public byte[] FunctionBytes
        {
            get => Encoding.UTF8.GetBytes(Function);
            set => Function = Encoding.UTF8.GetString(value);
        }
        public string Function { get; set; }
        [SszElement(2, "List[uint8, 16777216]")]
        public byte[] CallData { get; set; }
        [SszElement(3, "List[uint8, 16777216]")]
        public byte[] ReturnValue { get; set; }
        
        [SszElement(4, "List[uint8, 16777216]")]
        public byte[] ExecutionTrace { get; set; }
        
        [SszElement(5, "List[uint8, 16777216]")]
        public byte[] StateLog { get; set; }
        
        [SszElement(6, "uint64")]
        public ulong InstructionCount { get; set; }
        [SszElement(7, "List[List[uint8, 16777216], 65536]")]
        public List<byte[]> ChildCallBlobs
        {
            get => ChildCallsBacking;
            set
            {
                ChildCallsBacking = value;
                ChildCalls = value.Select(child => SszContainer.Deserialize<ContractInvokePayload>(child).Item1)
                    .ToArray();
            }
        }

        private List<byte[]> ChildCallsBacking = new();
        public ContractInvokePayload[] ChildCalls { get; set; }

        public static ContractInvokePayload FromReceipt(ContractInvocationReceipt receipt)
        {
            var ret = new ContractInvokePayload()
            {
                CallData = receipt.Call.CallData,
                Function = receipt.Call.Function,
                Target = receipt.Call.Contract,
                ReturnValue = receipt.ReturnValue,
                // When submitting a tx to miners this wouldn't be filled out
                // Here we are simulating this tx as it would exist on-chain
                ExecutionTrace = receipt.ExecutionTrace,
                StateLog = receipt.StateLog,
                InstructionCount = receipt.InstructionCount
            };

            ret.ChildCalls = receipt.ChildCalls.Select(childCall => ContractInvokePayload.FromReceipt(childCall))
                .ToArray();

            return ret;
        }
        
        public static ContractInvokePayload FromTransaction(Transaction transaction)
        {
            return SszContainer.Deserialize<ContractInvokePayload>(transaction.Payload).Item1;
        }

        public byte[] SerializeWithoutTrace()
        {
            var prevTrace = ExecutionTrace;
            var prevLog = StateLog;

            ExecutionTrace = StateLog = new byte[0];
            var serialized = SerializeWithTrace();

            ExecutionTrace = prevTrace;
            StateLog = prevLog;

            return serialized;
        }

        public byte[] SerializeWithTrace()
        {
            return SszContainer.Serialize(this);
        }
    }
}