using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SszSharp;

namespace Kmart
{
    public class ChainState
    {
        private object LockObject = new object();
        public BeaconState? GenesisState { get; set; }
        public Dictionary<byte[], ulong> Balances { get; set; } = new(new ByteArrayComparer());
        public Dictionary<byte[], Contract> Contracts { get; set; } = new(new ByteArrayComparer());

        public byte[] LastBlockHash { get; set; } = new byte[0];
        public Block LastBlock { get; set; }
        public byte[] LastStateRoot { get; set; }
        public List<byte[]> Ancestors = new List<byte[]>();

        public RollbackContext CurrentRollbackContext { get; private set; } = new();
        public Transaction CurrentTransaction { get; private set; }

        private readonly ContractExecutor ContractExecutor;
        private readonly BlobManager BlobManager;
        private readonly BlockStorage BlockStorage;
        private readonly ILogger<ChainState> Logger;

        public ChainState(ContractExecutor contractExecutor, BlobManager blobManager, BlockStorage blockStorage, ILogger<ChainState> logger)
        {
            ContractExecutor = contractExecutor ?? throw new ArgumentNullException(nameof(contractExecutor));
            BlobManager = blobManager ?? throw new ArgumentNullException(nameof(blobManager));
            BlockStorage = blockStorage ?? throw new ArgumentNullException(nameof(blockStorage));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsAncestorOfHead(byte[] hash) => Ancestors.Any(ancestorHash => hash.SequenceEqual(ancestorHash));

        public void SetGenesisState(BeaconState state)
        {
            GenesisState = state;

            var genesisBlock = new Block()
            {
                Hash = GenesisState.LastExecutionPayloadHeader.BlockHash,
                Height = GenesisState.LastExecutionPayloadHeader.BlockNumber,
                Timestamp = GenesisState.LastExecutionPayloadHeader.Timestamp
            };

            LastBlock = genesisBlock;
            LastBlockHash = genesisBlock.Hash;
            
            Balances.Clear();
            Contracts.Clear();
            Ancestors.Clear();
            
            Ancestors.Add(LastBlockHash);

            for (int i = 0; i < GenesisState.Validators.Count; i++)
            {
                var validatorAddr = GenesisState.Validators[i].Pubkey;
            }
        }

        public bool ValidateBlock(Block block)
        {
            lock (LockObject)
            {
                (var isValid, var rollback) = ProcessBlock(block);
                rollback?.ExecuteRollback();
                return isValid;
            }
        }
        
        public (bool, RollbackContext?) ProcessBlock(Block block)
        {
            var blockRollback = new RollbackContext();
            try
            {
                block.CalculateHash();

                if (!block.Parent.SequenceEqual(LastBlockHash))
                {
                    throw new Exception($"Block {block.Height}/{block.Hash.ToPrettyString()} parent hash {block.Parent.ToPrettyString()} does not match chain state's last block hash {LastBlock.Height}/{LastBlockHash.ToPrettyString()}");
                }

                if (block.Height != LastBlock.Height + 1)
                {
                    throw new Exception($"Block {block.Height} is too far ahead of {LastBlock.Height}");
                }

                for (int i = 0; i < block.Transactions.Length; i++)
                {
                    ProcessTransaction(block.Transactions[i], i);
                    blockRollback.AddRollbackActions(CurrentRollbackContext);
                }
                
                Logger.LogInformation($"Processed block {block.Hash.ToPrettyString()}: state advanced from height {LastBlock.Height} to {block.Height}");

                var prevLastBlock = LastBlock;
                var prevLastBlockHash = LastBlockHash;

                blockRollback.AddRollbackAction(() =>
                {
                    Ancestors.Remove(block.Hash);
                    LastBlock = prevLastBlock;
                    LastBlockHash = prevLastBlockHash;
                });
                
                Ancestors.Add(block.Hash);
                LastBlockHash = block.Hash;
                LastBlock = block;

                BlockStorage.StoreBlock(block);
            }
            catch (Exception e)
            {
                blockRollback.ExecuteRollback();
                Logger.LogError(e, $"Failed to process block {block.Height}/{block.Hash.ToPrettyString()}");
                return (false, null);
            }

            return (true, blockRollback);
        }

        void FailTransactionWithPunishment()
        {
            CurrentRollbackContext.ExecuteRollback();

            // TODO: Calculate this based off of transaction gas limit or something
            var punishment = GlobalConstants.CoinbaseReward / 100u;

            if (!Balances.ContainsKey(CurrentTransaction.Address))
            {
                throw new Exception("Sender does not exist");
            }

            if (Balances[CurrentTransaction.Address] < punishment)
            {
                throw new Exception(
                    $"{CurrentTransaction.Address.ToPrettyString()} cannot pay fees for failed tx {CurrentTransaction.Hash.ToPrettyString()}");
            }

            Balances[CurrentTransaction.Address] -= punishment;
            CurrentRollbackContext.AddRollbackAction(() => { Balances[CurrentTransaction.Address] += punishment; });
        }
        
        void ProcessTransaction(Transaction transaction, int index)
        {
            transaction.CalculateHash();
            CurrentTransaction = transaction;

            var signed = false;

            try
            {
                //signed = Ed25519.Verify(transaction.Signature, transaction.Hash, transaction.Address);
                signed = SignatureTools.VerifySignature(transaction.Hash, transaction.Signature);
            }
            catch (Exception e)
            {
                Console.WriteLine($"failed to verify tx {transaction.Hash.ToPrettyString()} signature: {e}");
            }

            if (!signed)
            {
                throw new Exception($"tx {transaction.Hash.ToPrettyString()} not signed");
            }

            CurrentRollbackContext.RollbackActions.Clear();
            
            switch (transaction.Type)
            {
                case TransactionType.Simple:
                    var payload = SimpleTransactionPayload.FromTransaction(transaction);

                    if (!Balances.ContainsKey(payload.To))
                    {
                        Balances[payload.To] = 0;
                        CurrentRollbackContext.AddRollbackAction(() => { Balances.Remove(payload.To); });
                    }
                    
                    // Non-coinbase payment
                    if (index != 0)
                    {
                        if (!Balances.ContainsKey(payload.From))
                        {
                            CurrentRollbackContext.ExecuteRollback();
                            throw new InvalidOperationException(
                                $"Payer address {payload.From.ToPrettyString()} does not exist");
                        }

                        if (payload.Amount > Balances[payload.From])
                        {
                            FailTransactionWithPunishment();
                            break;
                        }

                        Balances[payload.To] += payload.Amount;
                        Balances[payload.From] -= payload.Amount;
                        
                        CurrentRollbackContext.AddRollbackAction(() =>
                        {
                            Balances[payload.To] -= payload.Amount;
                            Balances[payload.From] += payload.Amount;
                        });
                    }
                    else
                    {
                        // Check coinbase eligibility
                        if (payload.Amount != GlobalConstants.CoinbaseReward)
                        {
                            throw new Exception($"Invalid coinbase payment amount {payload.Amount}");
                        }

                        Balances[payload.To] += payload.Amount;
                        CurrentRollbackContext.AddRollbackAction(() => { Balances[payload.To] -= payload.Amount; });
                    }
                    break;
                case TransactionType.Deploy:
                {
                    var deployPayload = ContractDeployPayload.FromTransaction(transaction);
                    var contractHash = SHA256.HashData(transaction.Payload).Skip(12).ToArray();

                    if (Contracts.ContainsKey(contractHash))
                    {
                        throw new Exception($"Contract {contractHash.ToPrettyString()} already deployed");
                    }

                    var contract = new Contract()
                    {
                        Address = contractHash,
                        Functions = deployPayload.Functions.ToHashSet(),
                        BootType = deployPayload.BootType
                    };

                    Contracts[contractHash] = contract;

                    switch (deployPayload.BootType)
                    {
                        case ContractBootType.Multiboot:
                            File.WriteAllBytes(BlobManager.GetPath(contractHash, BlobManager.ContractInitrdKey), deployPayload.Initrd);
                            File.WriteAllBytes(BlobManager.GetPath(contractHash, BlobManager.ContractImageKey), deployPayload.Image);
                            break;
                        case ContractBootType.Legacy:
                            File.WriteAllBytes(BlobManager.GetPath(contractHash, BlobManager.ContractImageKey), deployPayload.Image);
                            break;
                    }
                    
                    CurrentRollbackContext.AddRollbackAction(() =>
                    {
                        Contracts.Remove(contractHash);
                        File.Delete(BlobManager.GetPath(contractHash, BlobManager.ContractImageKey));
                    });
                    break;
                }
                case TransactionType.Invoke:
                {
                    var invokePayload = ContractInvokePayload.FromTransaction(transaction);
                    // pass in calldata
                    // contract can call to host to read/write state
                    // contract returns value
                    var receipt = new ContractInvocationReceipt();
                    var call = new ContractCall();

                    if (Contracts.ContainsKey(invokePayload.Target))
                    {
                        call.Contract = invokePayload.Target;
                    }
                    else
                    {
                        FailTransactionWithPunishment();
                        break;
                    }

                    call.CallData = invokePayload.CallData;
                    call.Function = invokePayload.Function;
                    receipt.ReturnValue = invokePayload.ReturnValue;
                    receipt.ExecutionTrace = invokePayload.ExecutionTrace;
                    receipt.Transaction = transaction;
                    receipt.StateLog = invokePayload.StateLog;
                    receipt.InstructionCount = invokePayload.InstructionCount;
                    call.Caller = call.Source = transaction.Address;
                    receipt.Call = call;
                    
                    var callResult = ContractExecutor.Execute(receipt.Call, transaction, this, receipt);

                    if (callResult is null || !callResult.Verified)
                    {
                        throw new Exception("Contract call failed to verify");
                    }
                    break;
                }
                default:
                {
                    FailTransactionWithPunishment();
                    break;
                }
            }
        }
    }
}