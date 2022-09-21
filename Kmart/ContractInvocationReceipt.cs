namespace Kmart;

public struct ContractInvocationReceipt
{
    public Transaction Transaction { get; set; }
    public ContractCall Call { get; set; }
        
    public byte[] ExecutionTrace { get; set; }
    public byte[] ReturnValue { get; set; }
    public byte[] StateLog { get; set; }
    public ulong InstructionCount { get; set; }
    public ContractInvocationReceipt[] ChildCalls { get; set; }
    public bool Reverted { get; set; }
}