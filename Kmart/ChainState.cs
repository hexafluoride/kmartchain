using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SszSharp;

namespace Kmart
{
    public class ChainStateSnapshot
    {
        [System.Text.Json.Serialization.JsonConverter(typeof(ByteArrayKeyDictionaryConverter<ulong>))]
        public Dictionary<byte[], ulong> Balances { get; set; } = new(new ByteArrayComparer());
        [System.Text.Json.Serialization.JsonConverter(typeof(ByteArrayKeyDictionaryConverter<Contract>))]
        public Dictionary<byte[], Contract> Contracts { get; set; } = new(new ByteArrayComparer());
        public byte[] LastBlockHash { get; set; } = new byte[32];
        public Block? LastBlock { get; set; }
        public byte[] LastStateRoot { get; set; } = new byte[32];
        public List<byte[]> Ancestors { get; set; } = new();

        public byte[] SaveSnapshot() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this);

        public static ChainStateSnapshot? LoadSnapshot(Stream stream) => System.Text.Json.JsonSerializer.Deserialize<ChainStateSnapshot>(stream);
    }

    public class ChainState : IChainState
    {
        public ChainStateSnapshot Snapshot { get; set; } = new();
        public Dictionary<byte[], ulong> Balances => Snapshot.Balances;
        public Dictionary<byte[], Contract> Contracts => Snapshot.Contracts;
        public byte[] LastBlockHash => Snapshot.LastBlockHash;
        public IBlock? LastBlock => Snapshot.LastBlock;
        public byte[] LastStateRoot => Snapshot.LastStateRoot;
        public List<byte[]> Ancestors => Snapshot.Ancestors;

        public object LockObject = new object();
        public object LockObject2 = new object();
        public BeaconState? GenesisState { get; set; }

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

        public bool HasSnapshot(byte[] blockHash)
        {
            var targetSnapshotPath = BlobManager.GetPath(blockHash, BlobManager.StateSnapshotKey);
            return File.Exists(targetSnapshotPath);
        }
        
        public bool? LoadSnapshot(byte[] blockHash)
        {
            if (GenesisState is null)
                throw new Exception($"Cannot load snapshot while genesis state is null");
            
            if (blockHash.SequenceEqual(GenesisState.LastExecutionPayloadHeader.BlockHash))
            {
                Logger.LogInformation($"Switched to genesis state");
                SetGenesisState(GenesisState);
                return true;
            }

            lock (LockObject)
            {
                var targetSnapshotPath = BlobManager.GetPath(blockHash, BlobManager.StateSnapshotKey);
                if (!File.Exists(targetSnapshotPath))
                {
                    Logger.LogWarning(
                        $"Asked to load state snapshot {blockHash.ToPrettyString()} that could not be found");
                    return false;
                }

                var lastSnapshot = Snapshot;
                using var snapshotStream = File.OpenRead(targetSnapshotPath);
                var snapshot = ChainStateSnapshot.LoadSnapshot(snapshotStream) ?? throw new Exception($"Failed to load snapshot from {targetSnapshotPath}");
                
                Snapshot = snapshot;
                Logger.LogInformation(
                    $"Switched from state {lastSnapshot.LastBlock?.Height}/{lastSnapshot.LastBlockHash.ToPrettyString()} to {snapshot.LastBlock?.Height}/{snapshot.LastBlockHash.ToPrettyString()}");
                return true;
            }
        }

        public IBlock? GetCommonAncestor(IBlock block)
        {
            IBlock? nextBlock = block;
            while (nextBlock is not null)
            {
                if (IsAncestorOfHead(nextBlock.Hash))
                    return nextBlock;

                nextBlock = BlockStorage.GetBlock(nextBlock.Parent);
            }

            return null;
        }
        
        public bool IsAncestorOfHead(byte[] hash) => Ancestors.Any(ancestorHash => hash.SequenceEqual(ancestorHash));

        public void SetGenesisState(BeaconState state)
        {
            lock (LockObject)
            {
                GenesisState = state;

                var genesisBlock = new Block()
                {
                    Hash = GenesisState.LastExecutionPayloadHeader.BlockHash,
                    Height = GenesisState.LastExecutionPayloadHeader.BlockNumber,
                    Timestamp = GenesisState.LastExecutionPayloadHeader.Timestamp,
                    Parent = new byte[32],
                    Coinbase = new byte[20],
                    Nonce = new byte[8],
                    Transactions = new Transaction[0]
                };

                Snapshot = new ChainStateSnapshot();

                Snapshot.LastBlock = genesisBlock;
                Snapshot.LastBlockHash = genesisBlock.Hash;

                BlockStorage.StoreBlock(genesisBlock);

                Ancestors.Add(LastBlockHash);

                for (int i = 0; i < GenesisState.Validators.Count; i++)
                {
                    var validatorAddr = GenesisState.Validators[i].Pubkey;
                }
            }
        }

        public bool ValidateBlock(IBlock block)
        {
            lock (LockObject2)
            {
                var currentHead = Snapshot.LastBlockHash.ToArray();
                (var isValid, _) = ProcessBlock(block);
                LoadSnapshot(currentHead);
                return isValid;
            }
        }
        
        public (bool, RollbackContext?) ProcessBlock(IBlock genericBlock)
        {
            if (!(genericBlock is Block block))
            {
                throw new Exception($"Type mismatch, expected {typeof(Block)}, got {genericBlock.GetType()}");
            }
            
            lock (LockObject)
            {
                var blockRollback = new RollbackContext();
                try
                {
                    if (LastBlock is null)
                    {
                        throw new Exception($"Last block is null, cannot process new blocks");
                    }

                    block.CalculateHash();
                    if (!block.Parent.SequenceEqual(LastBlockHash))
                    {
                        throw new Exception(
                            $"Block {block.Height}/{block.Hash.ToPrettyString()} parent hash {block.Parent.ToPrettyString()} does not match chain state's last block hash {LastBlock.Height}/{LastBlockHash.ToPrettyString()}");
                    }

                    if (block.Height != LastBlock.Height + 1)
                    {
                        throw new Exception($"Block {block.Height} is too far ahead of {LastBlock.Height}");
                    }

                    for (int i = 0; i < block.Transactions.Length; i++)
                    {
                        blockRollback.AddRollbackActions(ProcessTransaction(block.Transactions[i], i));
                    }

                    Logger.LogInformation(
                        $"Processed block {block.Hash.ToPrettyString()}: state advanced from height {LastBlock.Height} to {block.Height}");

                    Block? prevLastBlock = LastBlock as Block;
                    var prevLastBlockHash = LastBlockHash;

                    blockRollback.AddRollbackAction(() =>
                    {
                        Ancestors.Remove(block.Hash);
                        Snapshot.LastBlock = prevLastBlock;
                        Snapshot.LastBlockHash = prevLastBlockHash;
                    });

                    Ancestors.Add(block.Hash);
                    Snapshot.LastBlockHash = block.Hash;
                    Snapshot.LastBlock = block;

                    BlockStorage.StoreBlock(block);
                    var snapshotBytes = Snapshot.SaveSnapshot();
                    File.WriteAllBytes(BlobManager.GetPath(block.Hash, BlobManager.StateSnapshotKey), snapshotBytes);
                }
                catch (Exception e)
                {
                    Logger.LogError(e,
                        $"Failed to process block {block.Height}/{block.Hash.ToPrettyString()}, current block {LastBlock?.Height}/{LastBlockHash.ToPrettyString()}");

                    try
                    {
                        blockRollback.ExecuteRollback();
                    }
                    catch (Exception e2)
                    {
                        Logger.LogError(e2,
                            $"Failed to execute rollback while processing block {block.Height}/{block.Hash.ToPrettyString()}");
                    }
                    
                    return (false, null);
                }

                return (true, blockRollback);
            }
        }

        void FailTransactionWithPunishment(RollbackContext targetContext, Transaction transaction)
        {
            targetContext.ExecuteRollback();

            // TODO: Calculate this based off of transaction gas limit or something
            var punishment = GlobalConstants.CoinbaseReward / 100u;

            if (!Balances.ContainsKey(transaction.Address))
            {
                throw new Exception("Sender does not exist");
            }

            if (Balances[transaction.Address] < punishment)
            {
                throw new Exception(
                    $"{transaction.Address.ToPrettyString()} cannot pay fees for failed tx {transaction.Hash.ToPrettyString()}");
            }

            Balances[transaction.Address] -= punishment;
            targetContext.AddRollbackAction(() => { Balances[transaction.Address] += punishment; });
        }
        
        RollbackContext ProcessTransaction(Transaction transaction, int index)
        {
            var rollbackContext = new RollbackContext();
            try
            {
                transaction.CalculateHash();

                var signed = false;
                try
                {
                    signed = SignatureTools.VerifySignature(transaction.Hash, transaction.Signature);
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"Failed to verify tx {transaction.Hash.ToPrettyString()} signature");
                }

                if (!signed)
                {
                    throw new Exception($"Transaction {transaction.Hash.ToPrettyString()} not signed");
                }

                switch (transaction.Type)
                {
                    case TransactionType.Simple:
                        var payload = SimpleTransactionPayload.FromTransaction(transaction);

                        if (!Balances.ContainsKey(payload.To))
                        {
                            Balances[payload.To] = 0;
                            rollbackContext.AddRollbackAction(() => { Balances.Remove(payload.To); });
                        }

                        // Non-coinbase payment
                        if (index != 0)
                        {
                            if (!Balances.ContainsKey(payload.From))
                            {
                                throw new InvalidOperationException(
                                    $"Payer address {payload.From.ToPrettyString()} does not exist");
                            }

                            if (payload.Amount > Balances[payload.From])
                            {
                                FailTransactionWithPunishment(rollbackContext, transaction);
                                break;
                            }

                            Balances[payload.To] += payload.Amount;
                            Balances[payload.From] -= payload.Amount;

                            rollbackContext.AddRollbackAction(() =>
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
                            rollbackContext.AddRollbackAction(() => { Balances[payload.To] -= payload.Amount; });
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
                                File.WriteAllBytes(BlobManager.GetPath(contractHash, BlobManager.ContractInitrdKey),
                                    deployPayload.Initrd);
                                File.WriteAllBytes(BlobManager.GetPath(contractHash, BlobManager.ContractImageKey),
                                    deployPayload.Image);
                                break;
                            case ContractBootType.Legacy:
                                File.WriteAllBytes(BlobManager.GetPath(contractHash, BlobManager.ContractImageKey),
                                    deployPayload.Image);
                                break;
                        }

                        rollbackContext.AddRollbackAction(() =>
                        {
                            Contracts.Remove(contractHash);
                            // File.Delete(BlobManager.GetPath(contractHash, BlobManager.ContractImageKey));
                            Logger.LogInformation($"Executed rollback of tx {transaction.Hash.ToPrettyString()}");
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
                            FailTransactionWithPunishment(rollbackContext, transaction);
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

                        if (callResult?.RollbackContext is not null)
                            rollbackContext.AddRollbackActions(callResult.RollbackContext);

                        if (callResult is null || !callResult.Verified)
                        {
                            throw new Exception("Contract call failed to verify");
                        }

                        break;
                    }
                    default:
                    {
                        FailTransactionWithPunishment(rollbackContext, transaction);
                        break;
                    }
                }

                return rollbackContext;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to process transaction {transaction.Hash.ToPrettyString()}");

                try
                {
                    rollbackContext.ExecuteRollback();
                }
                catch (Exception e2)
                {
                    Logger.LogError(e2,
                        $"Failed to process rollback for transaction {transaction.Hash.ToPrettyString()} after previous error");
                }
                
                throw;
            }
        }
    }
}