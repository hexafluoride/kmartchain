using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Kmart.Interfaces;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using SszSharp;

namespace Kmart.Qemu;

public class PayloadManager : IPayloadManager
{
    private ChainState ChainState;
    private readonly ContractExecutor Executor;
    private readonly ILogger<PayloadManager> Logger;
    
    public int InjectedTx;
    public Dictionary<long, ExecutionPayload> Payloads = new();

    public List<Transaction> Mempool = new();

    public PayloadManager(ChainState chainState, ContractExecutor executor, ILogger<PayloadManager> logger)
    {
        ChainState = chainState ?? throw new ArgumentNullException(nameof(chainState));
        Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var keypair = SignatureTools.GenerateKeypair();
        signerPrivateKey = ((ECPrivateKeyParameters) keypair.Private).D.ToByteArrayUnsigned();
        signerAddress = ((ECPublicKeyParameters) keypair.Public).Q.GetEncoded();
        signerAddress = signerAddress.Skip(signerAddress.Length - 20).ToArray();
    }

    public void Reset()
    {
        InjectedTx = 0;
    }

    public ExecutionPayload? GetPayload(long payloadId)
    {
        if (Payloads.ContainsKey(payloadId))
            return Payloads[payloadId];

        return null;
    }
    
    public IBlock CreateBlockFromPayload(ExecutionPayload payload)
    {
        lock (payload)
        {
            var block = new Block()
            {
                Height = payload.BlockNumber,
                FeeRecipient = payload.FeeRecipient,
                Nonce = new byte[8],
                Parent = payload.Root,
                Timestamp = payload.Timestamp,
                Transactions = payload.Transactions.Select(txBytes => Transaction.Deserialize(txBytes.RlpUnwrap()).Item1).ToArray(),
                BaseFeePerGas = (ulong) payload.BaseFeePerGas,
                ExtraData = payload.ExtraData,
                GasLimit = payload.GasLimit,
                GasUsed = payload.GasUsed,
                LogsBloom = payload.LogsBloom,
                PrevRandao = payload.PrevRandao,
                ReceiptsRoot = payload.ReceiptsRoot,
                StateRoot = payload.StateRoot
            };
            block.CalculateHash();
            payload.BlockHash = block.Hash;
            return block;
        }
    }
    
    public long CreatePayload(PayloadAttributesV1 attribs)
    {
        var payload = new ExecutionPayload()
        {
            Timestamp = (ulong)attribs.Timestamp.Value,
            FeeRecipient = attribs.SuggestedFeeRecipient.ToByteArray(),
            PrevRandao = attribs.PrevRandao.ToByteArray(),
            BaseFeePerGas = 1,
            BlockHash = new byte[32],
            BlockNumber = ChainState.LastBlock!.Height + 1,
            ExtraData = new byte[1],
            GasLimit = int.MaxValue,
            GasUsed = 0,
            LogsBloom = new byte[256],
            Root = ChainState.LastBlockHash,
            StateRoot = ChainState.LastStateRoot ?? new byte[32],
            Transactions = new List<byte[]>(),
            ReceiptsRoot = new byte[32]
        };
        
        var payloadId = Random.Shared.NextInt64();
        Payloads[payloadId] = payload;

        CreateBlockFromPayload(payload);
        InjectTransactionsIntoPayload(payloadId);
        
        
        
        return payloadId;
    }

    private Random eth1Rng = new Random(1);
    private byte[] signerPrivateKey;
    private byte[] signerAddress;
    
    // TODO: Replace this with a generic transaction injector object, a mempool impl in production and something like this for testing.
    void InjectTransactionsIntoPayload(long payloadId)
    {
        try
        {
            Transaction SignTransaction(Transaction transaction)
            {
                transaction.CalculateHash();
                transaction.Signature = SignatureTools.GenerateSignature(transaction.Hash, signerPrivateKey);
                return transaction;
            }

            Transaction CreateDeploy(string imagePath, string[] functions, bool multiboot = false,
                string initrdPath = "")
            {
                return new Transaction()
                {
                    Timestamp = 1662165113,
                    Address = signerAddress,
                    FeePerByte = 0,
                    Nonce = (ulong) InjectedTx++,
                    Type = TransactionType.Deploy,
                    Payload = new ContractDeployPayload()
                    {
                        // TODO: Support barebones multiboot images
                        Image = File.ReadAllBytes(imagePath),
                        Functions = functions,
                        BootType = multiboot ? ContractBootType.Multiboot : ContractBootType.Legacy,
                        Initrd = string.IsNullOrWhiteSpace(initrdPath) ? new byte[0] : File.ReadAllBytes(initrdPath)
                    }.Serialize()
                };
            }
            Transaction? CreateContractCall(string function, byte[] calldata)
            {
                var contractAddr = ChainState.Contracts.Single().Key;
                // if (File.Exists($"./tx-{nthtx}"))
                // {
                //     var loadedTx = JsonSerializer.Deserialize<Transaction>(File.ReadAllText($"./tx-{nthtx}"));
                //     var payload = ContractInvokePayload.FromTransaction(loadedTx);
                //     File.WriteAllBytes($"./trace-{nthtx}",payload.ExecutionTrace);
                //     nthtx++;
                //     return loadedTx;
                // }
                var contract = ChainState.Contracts[contractAddr];
                var contractCallData = new ContractCall()
                {
                    Contract = contractAddr,
                    CallData = calldata,
                    Function = function
                };

                var executionTx = new Transaction()
                {
                    Timestamp = 1662165113,
                    Address = signerAddress,
                    FeePerByte = 0,
                    Nonce = (ulong) InjectedTx,
                    Type = TransactionType.Invoke
                };

                var sw = Stopwatch.StartNew();
                lock (ChainState.LockObject)
                {
                    var callResult = Executor.Execute(contractCallData, executionTx, ChainState) ??
                                     throw new Exception("Execution failed");
                    
                    var receipt = callResult.Receipt ?? throw new Exception($"Execution yielded no receipt");
                    callResult.RollbackContext?.ExecuteRollback();

                    try
                    {
                        // Verify receipt that we just created
                        var verifyResult = Executor.Execute(contractCallData, executionTx, ChainState, receipt) ??
                                           throw new Exception($"Failed to verify execution we created");
                        verifyResult.RollbackContext?.ExecuteRollback();
                        if (!verifyResult.Verified)
                        {
                            throw new Exception($"Failed to verify generated receipt");
                        }

                        var executionPayload = ContractInvokePayload.FromReceipt(receipt);

                        Logger.LogInformation(
                            $"Calldata for {function} is {calldata.ToPrettyString()}, return value is {receipt.ReturnValue.ToPrettyString()}, call took {sw.Elapsed}, {receipt.InstructionCount} instructions");
                        executionTx.Payload = executionPayload.Serialize();
                        executionTx.Receipt = receipt.Serialize();

                        //File.WriteAllText($"./tx-{n++}", JsonSerializer.Serialize(executionTx));

                        InjectedTx++;
                        return executionTx;
                    }
                    catch (Exception e)
                    {
                        Logger.LogError(e, "Failed to generate contract call to inject");
                        return null;
                    }
                }
            }

            Transaction? txToInject;
            if (InjectedTx == 0 && !ChainState.Contracts.Any())
            {
                txToInject = CreateDeploy("/home/kate/repos/x86-bare-metal-examples/multiboot/osdev/iso/boot/token.elf",
                    new[] {"read", "write", "hash", "mint", "get_balance", "transfer"}, multiboot: true);
            }
            else
            {
                txToInject = CreateContractCall(InjectedTx % 2 == 1 ? "write" : "read", new byte[0]);
            }

            if (txToInject is null)
            {
                Logger.LogWarning($"Could not inject tx into payload {payloadId}");
                return;
            }

            txToInject = SignTransaction(txToInject);
            var payload = Payloads[payloadId];
            payload.Transactions.Add(SszContainer.Serialize(txToInject).RlpWrap());

            if (Mempool.Any())
            {
                foreach (var transaction in Mempool)
                {
                    switch (transaction.Type)
                    {
                        case TransactionType.Invoke:
                        {
                            try
                            {
                                lock (ChainState.LockObject)
                                {
                                    var payloadDecoded = ContractInvokePayload.FromTransaction(transaction);
                                    var payloadCall = ContractCall.FromPayload(transaction, payloadDecoded);
                                    var executionResult = Executor.Execute(payloadCall, transaction, ChainState) ??
                                                          throw new Exception($"Failed to execute");
                                    executionResult.RollbackContext?.ExecuteRollback();
                                    
                                    var executionReceipt = executionResult.Receipt ??
                                                           throw new Exception($"Failed to generate execution receipt");

                                    transaction.Receipt = executionReceipt.Serialize();
                                    payload.Transactions.Add(SszContainer.Serialize(transaction).RlpWrap());
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.LogError(e, $"Failed to process mempool tx {transaction.Hash.ToPrettyString()}");
                            }
                            break;   
                        }
                    }
                }

                Mempool.Clear();
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"Failed to inject tx {InjectedTx} into payload {payloadId}");
        }
    }

}