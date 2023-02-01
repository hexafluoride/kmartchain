using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Kmart.Qemu
{
    public class ContractCallResult
    {
        public ContractInvocationReceipt? Receipt { get; set; }
        public RollbackContext? RollbackContext { get; set; }
        public bool Verified { get; set; }
    }
    
    public class ContractExecutor
    {
        private readonly BlobManager BlobManager;
        private readonly QemuManager QemuManager;
        private readonly ILogger<ContractExecutor> Logger;

        public ContractExecutor(BlobManager blobManager, QemuManager qemuManager, ILogger<ContractExecutor> logger)
        {
            BlobManager = blobManager ?? throw new ArgumentNullException(nameof(blobManager));
            QemuManager = qemuManager ?? throw new ArgumentNullException(nameof(qemuManager));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public ContractCallResult? Execute(ContractCall call, Transaction transaction, ChainState chainState, ContractInvocationReceipt? verifyReceipt = null)
        {
            if (!chainState.Contracts.ContainsKey(call.Contract))
            {
                throw new Exception($"Contract {call.Contract.ToPrettyString()} does not exist");
            }
            
            QemuInstance? qemuInstance = null;
            var contract = chainState.Contracts[call.Contract];
            var callRollbackContext = new RollbackContext();
            bool verify = verifyReceipt is not null;

            try
            {
                var childCalls = new List<ContractInvocationReceipt>();

                if (verify)
                {
                    Logger.LogDebug("Verifying {nonce}-th tx", transaction.Nonce);
                    qemuInstance = QemuManager.StartReplay(chainState, verifyReceipt!) ??
                                   throw new Exception("Could not initialize qemu");
                }
                else
                {
                    qemuInstance = QemuManager.StartRecord(chainState, call) ??
                                   throw new Exception("Could not initialize qemu");
                }

                qemuInstance.WaitForGuest();

                // Enter message processing loop until return value is sent
                var stateStepHashes = new List<byte[]>();

                ContractMessage? returnMessage = null;
                bool executionReverted = false;

                int callIndex = 0;

                while (returnMessage is null && !executionReverted && qemuInstance.ProcessMessage())
                {
                    if (qemuInstance.OutstandingRequest is not null)
                    {
                        var message = qemuInstance.OutstandingRequest.Value;
                        
                        //Console.WriteLine($"Message {message.Type} from guest");

                        switch (message.Type)
                        {
                            case MessageType.Return:
                                returnMessage = message;
                                break;
                            // case MessageType.Revert:
                            //     executionReverted = true;
                            //     break;
                            case MessageType.StateWrite:
                            {
                                if (message.Payload.Length != 64)
                                {
                                    throw new InvalidOperationException("Invalid StateWrite message");
                                }

                                var key = message.Payload.AsSpan(0, 32).ToArray();
                                var value = message.Payload.AsSpan(32, 32).ToArray();
                                var previousValue = contract.ReadState(key);

                                contract.WriteState(key, value);
                                
                                stateStepHashes.Add(message.Payload.Concat(previousValue).ToArray());

                                callRollbackContext.AddRollbackAction(
                                    () => { contract.WriteState(key, previousValue); });
                                break;
                            }
                            case MessageType.StateRead:
                            {
                                if (message.Payload.Length != 32)
                                {
                                    throw new InvalidOperationException("Invalid StateRead message");
                                }

                                Logger.LogDebug($"Reading from state slot {message.Payload.ToPrettyString()}");
                                var stateValue = contract.ReadState(message.Payload);
                                var response = new ContractMessage(MessageType.StateRead,
                                    message.Payload.Concat(stateValue).ToArray());

                                qemuInstance.FulfillRequest(response);
                                break;
                            }
                            case MessageType.Call:
                            {
                                var maybeChildCall = message.GetCall();

                                // TODO: Add safe calling mechanism that does not revert entire transaction
                                if (maybeChildCall is null)
                                {
                                    throw new Exception("Subcall failed");
                                }

                                var childCall = maybeChildCall.Value;

                                if (!chainState.Contracts.ContainsKey(childCall.Contract))
                                {
                                    throw new Exception("Subcall failed");
                                }

                                childCall.Caller = call.Contract;
                                childCall.Source = transaction.Address;
                                childCall.Contract = childCall.Contract;

                                var childCallResult = this.Execute(childCall, transaction, chainState, verify ? verifyReceipt?.ChildCalls?[callIndex++] : null);
                                if (childCallResult is null || childCallResult.Receipt is null || childCallResult.RollbackContext is null)
                                {
                                    throw new Exception("Subcall failed");
                                }
                                
                                callRollbackContext.AddRollbackActions(childCallResult.RollbackContext);

                                // Generate CallResult contractmessage
                                byte[] callResultEncoded = childCallResult.Receipt.Reverted ? new byte[1] : new byte[childCallResult.Receipt.ReturnValue.Length + 1];
                                if (childCallResult.Receipt.Reverted)
                                {
                                    callResultEncoded[0] = 0xFF;
                                }
                                else
                                {
                                    Array.Copy(childCallResult.Receipt.ReturnValue, 0, callResultEncoded, 1, childCallResult.Receipt.ReturnValue.Length);
                                }
                                
                                childCalls.Add(childCallResult.Receipt);
                                var callResultContractMessage = new ContractMessage(MessageType.CallResult, callResultEncoded);

                                qemuInstance.FulfillRequest(callResultContractMessage);
                                if (verify)
                                {
                                    if (!childCallResult.Verified)
                                    {
                                        throw new Exception("Failed to verify child call");
                                    }
                                }
                                break;
                            }
                            case MessageType.MetadataRead:
                            {
                                var response = new ContractMessage(MessageType.MetadataRead, call.Caller);
                                qemuInstance.FulfillRequest(response);
                                break;
                            }
                        }
                    }
                }

                if (returnMessage == null)
                    throw new Exception("Guest did not return value");

                //Console.WriteLine($"Guest took {qemuInstance.GetInstructionCount()} instructions");
                //qemuInstance.Pause();
                var instructionCount = qemuInstance.GetInstructionCount();
                qemuInstance.Shutdown();

                // Calculate chainState log
                using var ms = new MemoryStream();
                foreach (var step in stateStepHashes)
                {
                    ms.Write(step, 0, step.Length);
                }

                var executionTrace = verifyReceipt?.ExecutionTrace ?? qemuInstance.GetExecutionTrace();
                ulong fairCompute = instructionCount;
                
                using (var traceMs = new MemoryStream(executionTrace))
                {
                    var allMessages = new ReplayParser(traceMs).ParseAllMessages().ToList();
                    foreach (var message in allMessages)
                    {
                        Logger.LogInformation(message.ToString());
                    }

                    fairCompute = CalculateFairComputeUse(allMessages);
                    Logger.LogInformation($"Fair compute use was {fairCompute} instructions out of {instructionCount} total");

                    if (verify)
                    {
                        int hostWriteIndex = 0;
                        for (int i = 0; i < allMessages.Count; i++)
                        {
                            if (allMessages[i].Direction != ReplayMessageDirection.HostToGuest)
                                continue;

                            using var messageMs = new MemoryStream();
                            qemuInstance.WrittenMessages[hostWriteIndex].Write(messageMs);
                            var honestMessageSerialized = messageMs.ToArray();
                            var messageInLog = allMessages[i].Contents;

                            if (!honestMessageSerialized.SequenceEqual(messageInLog))
                            {
                                Logger.LogError(
                                    $"Host -> guest message #{hostWriteIndex} does not match: honest {honestMessageSerialized.ToPrettyString()}, found {messageInLog.ToPrettyString()}");
                            }

                            hostWriteIndex++;
                        }
                    }
                }

                var stateChangeHash = SHA256.HashData(ms.ToArray());

                // Rollback all state actions if call is reverted
                if (executionReverted)
                {
                    callRollbackContext.ExecuteRollback();
                }

                if (verify)
                {
                    if (verifyReceipt is null)
                    {
                        throw new Exception("This is just to shut up the static nullability analyzer");
                    }

                    var returnMessageMatch = returnMessage.Value.Payload.SequenceEqual(verifyReceipt.ReturnValue);
                    var stateChangeMatch = stateChangeHash.SequenceEqual(verifyReceipt.StateLog);
                    var instructionCountMatch = fairCompute == verifyReceipt.InstructionCount;
                    
                    var callResult = new ContractCallResult()
                    {
                        Receipt = verifyReceipt,
                        RollbackContext = callRollbackContext,
                        Verified = returnMessageMatch && stateChangeMatch && instructionCountMatch
                    };

                    if (!callResult.Verified)
                    {
                        Logger.LogWarning(
                            $"Failed to verify transaction {transaction.Hash.ToPrettyString()}, return: {returnMessageMatch}, state: {stateChangeMatch}, instruction: {instructionCountMatch}");
                    }

                    return callResult;
                }
                else
                {
                    // Save replay.bin + chainState writes into ContractCall
                    var receipt = new ContractInvocationReceipt()
                    {
                        Call = call,
                        Transaction = transaction,
                        ExecutionTrace = executionTrace,
                        ReturnValue = returnMessage.Value.Payload,
                        StateLog = stateChangeHash,
                        ChildCalls = childCalls.ToArray(),
                        InstructionCount = fairCompute,
                        Reverted = executionReverted
                    };

                    var callResult = new ContractCallResult()
                    {
                        Receipt = receipt,
                        RollbackContext = callRollbackContext
                    };

                    return callResult;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to execute transaction {txHash}", transaction.Hash.ToPrettyString());
                callRollbackContext.ExecuteRollback();
                
                return new ContractCallResult()
                {
                    Receipt = null,
                    RollbackContext = callRollbackContext,
                    Verified = false
                };
            }
            finally
            {
                if (qemuInstance is not null)
                {
                    qemuInstance.Cleanup();
                }
            }
        }

        public ulong CalculateFairComputeUse(IEnumerable<ReplayedMessage> messages)
        {
            ulong usedInstructions = 0;
            ulong lastHostWrite = 0;

            foreach (var message in messages)
            {
                if (message.Direction == ReplayMessageDirection.HostToGuest)
                {
                    lastHostWrite = message.EndInstruction;
                }

                if (message.Direction == ReplayMessageDirection.GuestToHost)
                {
                    usedInstructions += message.EndInstruction - lastHostWrite;
                }
            }

            return usedInstructions;
        }
    }
}