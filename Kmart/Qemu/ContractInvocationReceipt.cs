using System;
using System.Collections.Generic;
using System.Linq;
using SszSharp;

namespace Kmart.Qemu;

public class ContractInvocationReceipt
{
    [SszElement(0, "List[uint8, 16777216]")]
    public byte[] ReturnValue { get; set; } = new byte[0];

    [SszElement(1, "List[uint8, 16777216]")]
    public byte[] ExecutionTrace { get; set; } = new byte[0];

    [SszElement(2, "List[uint8, 16777216]")]
    public byte[] StateLog { get; set; } = new byte[0];
        
    [SszElement(3, "uint64")]
    public ulong InstructionCount { get; set; }
    [SszElement(4, "List[List[uint8, 16777216], 65536]")]
    public List<byte[]> ChildCallBlobs
    {
        get => ChildCallsBacking;
        set
        {
            ChildCallsBacking = value;
            ChildCalls = value.Select(child => SszContainer.Deserialize<ContractInvocationReceipt>(child).Item1)
                .ToArray();
        }
    }

    public Transaction Transaction { get; set; }
    public ContractCall Call { get; set; }
    public bool Reverted { get; set; }
    private List<byte[]> ChildCallsBacking = new();
    public ContractInvocationReceipt[] ChildCalls { get; set; } = Array.Empty<ContractInvocationReceipt>();

    public static ContractInvocationReceipt FromTransaction(Transaction transaction)
    {
        var receipt = SszContainer.Deserialize<ContractInvocationReceipt>(transaction.Receipt).Item1;
        receipt.Transaction = transaction;
        return receipt;
    }
    public byte[] Serialize()
    {
        return SszContainer.Serialize(this);
    }
}