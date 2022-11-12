using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using JWT;
using JWT.Algorithms;
using JWT.Serializers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Parameters;
using SszSharp;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Kmart;

public class ExecutionLayerState
{
    public byte[] HeadBlock { get; set; }
    public byte[] FinalizedBlock { get; set; }
    public ulong HeadHeight { get; set; }
}

public class ExecutionLayerServer
{
    private readonly HttpListener HttpListener;
    private readonly IJwtAlgorithm Algorithm;
    private readonly JwtDecoder JwtDecoder;
    private readonly ILogger<ExecutionLayerServer> Logger;
    private readonly ChainState ChainState;
    private readonly ContractExecutor Executor;
    private readonly BlockStorage BlockStorage;
    private readonly KmartConfiguration Configuration;
    private readonly FakeEthereumBlockSource FakeEthereumBlockSource;

    public Dictionary<long, ExecutionPayload> Payloads = new();
    private readonly JsonSerializerOptions DefaultSerializerOptions = new JsonSerializerOptions()
    {
    };

    public ExecutionLayerServer(
        ILogger<ExecutionLayerServer> logger,
        ChainState chainState,
        ContractExecutor contractExecutor,
        BlockStorage blockStorage,
        KmartConfiguration configuration,
        FakeEthereumBlockSource fakeEthereumBlockSource
        )
    {
        HttpListener = new HttpListener();
        Algorithm = new HMACSHA256Algorithm();
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ChainState = chainState ?? throw new ArgumentNullException(nameof(chainState));
        Executor = contractExecutor ?? throw new ArgumentNullException(nameof(contractExecutor));
        BlockStorage = blockStorage ?? throw new ArgumentNullException(nameof(blockStorage));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        FakeEthereumBlockSource =
            fakeEthereumBlockSource ?? throw new ArgumentNullException(nameof(fakeEthereumBlockSource));
        
        FakeEthereumBlockSource.UseChainState(chainState); // HACK
        
        IJsonSerializer serializer = new JsonNetSerializer();
        IDateTimeProvider provider = new UtcDateTimeProvider();
        IJwtValidator validator = new JwtValidator(serializer, provider);
        IBase64UrlEncoder urlEncoder = new JwtBase64UrlEncoder();

        JwtDecoder = new JwtDecoder(serializer, validator, urlEncoder, Algorithm);
    }

    public void Start()
    {
        var keypair = SignatureTools.GenerateKeypair();
        signerPrivateKey = ((ECPrivateKeyParameters) keypair.Private).D.ToByteArrayUnsigned();
        signerAddress = ((ECPublicKeyParameters) keypair.Public).Q.GetEncoded();
        signerAddress = signerAddress.Skip(signerAddress.Length - 20).ToArray();

        var prefix = $"http://{Configuration.RpcHost}:{Configuration.RpcPort}/";

        HttpListener.Prefixes.Add(prefix);
        HttpListener.Start();
        Logger.LogInformation("Execution layer server started listening on prefixes {prefixes}", string.Join(", ", HttpListener.Prefixes));
    }

    public async Task Serve()
    {
        while (HttpListener.IsListening)
        {
            var context = await HttpListener.GetContextAsync();

            try
            {
                var request = context.Request;
                
                var token = request.Headers["JWT"];

                if (token is not null)
                {
                    var decoded = JwtDecoder.Decode(token);
                    Logger.LogInformation("Received JWT token {decoded}", decoded);
                }

                using var bodyReader = new StreamReader(request.InputStream);
                var body = await bodyReader.ReadToEndAsync();
                
                Logger.LogInformation("Received request body {body}", body.Substring(0, Math.Min(2000, body.Length)));

                var bodyDecoded = JsonDocument.Parse(body);
                var bodyElement = bodyDecoded.RootElement;
                var method = bodyElement.GetProperty("method").GetString() ?? throw new Exception("Could not decode method in JSON-RPC request");
                var id = bodyElement.GetProperty("id").GetInt32();
                var hasParams = bodyElement.TryGetProperty("params", out JsonElement parameters);

                if (ChainState.GenesisState is null)
                {
                    ResetFromGenesis();
                }

                await ServeRequest(context, id, method, parameters);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Exception caught while serving HTTP request");
                // throw;
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    async Task ServeRequest(HttpListenerContext context, int id, string method, JsonElement parameters)
    {
        object? responseValue = null;
        int? errorCode = null;
        string? errorMessage = null;
        
        switch (method)
        {
            case "eth_syncing":
            {
                responseValue = false;
                break;
            }
            case "eth_chainId":
            {
                responseValue = "0x1092";
                break;
            }
            case "eth_getLogs":
            {
                responseValue = FakeEthereumBlockSource.GetLogsResponse(parameters);
                break;
            }
            case "eth_getBlockByNumber":
            {
                var heightSpec = parameters[0].GetString() ?? throw new Exception($"Could not obtain height spec");
                var currentHeight = ChainState.LastBlock.Height;
                var targetHeight = heightSpec.Equals("latest", StringComparison.InvariantCultureIgnoreCase)
                    ? (int)Math.Max(currentHeight, (double)FakeEthereumBlockSource.LastFakedHeight + 1)
                    : (int)heightSpec.ToQuantity();

                var resultBlock = FakeEthereumBlockSource.GetBlock(targetHeight);
                if (resultBlock.Height == (int)(FakeEthereumBlockSource.MergeHeight + 1))
                {
                    newGenesisBlock = resultBlock;
                    ResetFromGenesis();
                }

                responseValue = resultBlock.Encode();
                break;
            }
            case "eth_getBlockByHash":
            {
                var hashSpec = parameters[0].GetString() ?? throw new Exception($"Could not obtain hash spec");
                var hash = hashSpec.StartsWith("0x") ? hashSpec.ToByteArray() :
                    hashSpec.Length == 64 ? hashSpec.ToByteArray() : new byte[0];

                if (hash.Length == 32 && ChainState.IsAncestorOfHead(hash))
                {
                    var realBlock = BlockStorage.GetBlock(hash);
                    if (realBlock is not null)
                    {
                        FakeEthereumBlockSource.CreateFromRealBlock(realBlock);
                    }
                }

                var resultBlock = FakeEthereumBlockSource.GetBlock(hash);
                responseValue = resultBlock?.Encode();
                break;
            }
            case "engine_forkchoiceUpdatedV1":
            {
                var forkChoice = JsonSerializer.Deserialize<ForkchoiceStateV1>(parameters[0]);
                var forkChoiceHead = forkChoice.HeadBlockHash.ToByteArray();
                var forkChoiceBlock = BlockStorage.GetBlock(forkChoiceHead);

                // Logger.LogInformation(
                //     $"Fork choice block == null: {forkChoiceBlock is null}, common ancestor block == null: {commonAncestorBlock is null}");

                // If fork choice head is same as canonical head
                if (ChainState.LastBlockHash.SequenceEqual(forkChoiceHead))
                {
                    if (parameters[1].ValueKind == JsonValueKind.Null)
                    {
                        responseValue = new
                        {
                            payloadStatus =
                                new PayloadStatusV1("VALID", ChainState.LastBlockHash.ToPrettyString(true), null),
                            payloadId = (object)null!
                        };
                    }
                    else
                    {
                        var payloadAttribs =
                            JsonConvert.DeserializeObject<PayloadAttributesV1>(parameters[1].GetRawText());
                        var payloadId = CreatePayload(payloadAttribs);
                        responseValue = new
                        {
                            payloadStatus =
                                new PayloadStatusV1("VALID", ChainState.LastBlockHash.ToPrettyString(true), null),
                            payloadId = BitConverter.GetBytes(payloadId).ToPrettyString(true)
                        };
                    }
                }
                // If fork choice head is behind canonical head
                else if (ChainState.IsAncestorOfHead(forkChoiceHead))
                {
                    Logger.LogInformation($"Fork choice head {forkChoiceBlock.Height}/{forkChoiceHead.ToPrettyString()} is ancestor of canonical head {ChainState.LastBlock.Height}/{ChainState.LastBlockHash.ToPrettyString()}");
                    responseValue = new
                    {
                        payloadStatus = new PayloadStatusV1("VALID", forkChoiceHead.ToPrettyString(true), null),
                        payloadId = (object) null!
                    };
                }
                else
                {
                    Block? commonAncestorBlock =
                        forkChoiceBlock is not null ? ChainState.GetCommonAncestor(forkChoiceBlock) : null;
                    // If fork choice head is ahead of canonical head, with requisite blocks in db
                    if (commonAncestorBlock is not null)
                    {
                        if (!commonAncestorBlock.Hash.SequenceEqual(ChainState.LastBlockHash))
                        {
                            // throw new Exception(
                            //     $"Found common ancestor {commonAncestorBlock.Hash.ToPrettyString()} for fork choice head {forkChoiceHead.ToPrettyString()} that is not the canonical head {ChainState.LastBlockHash.ToPrettyString()}");
                            var loadResult = ChainState.LoadSnapshot(commonAncestorBlock.Hash);
                            if (loadResult != true)
                            {
                                throw new Exception(
                                    $"Failed to load state at common ancestor {commonAncestorBlock.Hash.ToPrettyString()}");
                            }
                        }
                        
                        var fastForwardList = GetFastForwardList(commonAncestorBlock, forkChoiceBlock);
                        foreach (var block in fastForwardList)
                        {
                            (var success, var rollback) = ChainState.ProcessBlock(block);

                            if (!success)
                            {
                                throw new Exception(
                                    $"Failed to fast forward from {commonAncestorBlock.Hash.ToPrettyString()} to {forkChoiceBlock.Hash.ToPrettyString()}");
                            }
                        }

                        if (parameters[1].ValueKind == JsonValueKind.Null)
                        {
                            responseValue = new
                            {
                                payloadStatus =
                                    new PayloadStatusV1("VALID", ChainState.LastBlockHash.ToPrettyString(true), null),
                                payloadId = (object) null!
                            };
                        }
                        else
                        {
                            var payloadAttribs =
                                JsonConvert.DeserializeObject<PayloadAttributesV1>(parameters[1].GetRawText());
                            var payloadId = CreatePayload(payloadAttribs);
                            responseValue = new
                            {
                                payloadStatus =
                                    new PayloadStatusV1("VALID", ChainState.LastBlockHash.ToPrettyString(true), null),
                                payloadId = BitConverter.GetBytes(payloadId).ToPrettyString(true)
                            };
                        }
                    }
                    // If fork choice head has unresolved prerequisited
                    else
                    {
                        StartSyncFromHash(forkChoice.HeadBlockHash.ToByteArray());
                        responseValue = new
                        {
                            payloadStatus = new PayloadStatusV1("SYNCING", null, null),
                            payloadId = (object) null!
                        };
                    }
                }
            }
            break;
            case "engine_getPayloadV1":
            {
                var payloadId = (long) BitConverter.ToUInt64(parameters[0].GetString().ToByteArray());

                if (!Payloads.ContainsKey(payloadId))
                {
                    errorCode = -38001;
                    errorMessage = "Unknown payload";
                }
                else
                {
                    CreateBlockFromPayload(Payloads[payloadId]);
                    responseValue = ExecutionPayloadWrapper.FromPayload(Payloads[payloadId]);
                }
            }
            break;
            case "engine_newPayloadV1":
            {
                var payloadWrapped = JsonSerializer.Deserialize<ExecutionPayloadWrapper>(parameters[0]);
                var payload = payloadWrapped.Payload;
                var localBlock = CreateBlockFromPayload(payload);
                BlockStorage.StoreBlock(localBlock);
                // Extends the canonical chain
                if (payload.BlockNumber > ChainState.LastBlock.Height)
                {
                    // Block is valid and extends chain
                    var payloadParent = payload.Root;

                    if (!ChainState.LastBlockHash.SequenceEqual(payloadParent))
                    {
                        // Parent state is known
                        var parentBlock = BlockStorage.GetBlock(payloadParent);
                        if (parentBlock is not null)
                        {
                            // Quickly catch up to parent block
                            lock (ChainState)
                            {
                                var currentHead = ChainState.LastBlockHash;
                                var stateLoadResult = ChainState.LoadSnapshot(payloadParent);

                                // Loaded parent state from snapshot
                                if (stateLoadResult == true)
                                {
                                    var valid = ChainState.ValidateBlock(localBlock);

                                    if (valid)
                                    {
                                        responseValue =
                                            new PayloadStatusV1("VALID", payload.BlockHash.ToPrettyString(true), null);
                                        BlockStorage.StoreBlock(localBlock);
                                    }
                                    else
                                    {
                                        responseValue = new PayloadStatusV1("INVALID",
                                            ChainState.LastBlockHash.ToPrettyString(true), "Could not validate");
                                    }

                                    // ChainState.LoadSnapshot(currentHead);
                                }
                                // Recreate parent state from individual blocks
                                else
                                {
                                    throw new Exception($"Could not load snapshot {payloadParent.ToPrettyString()}");
                                }
                            }
                        }
                        else
                        {
                            var heightDiff = payload.BlockNumber - ChainState.LastBlock.Height;
                            Logger.LogInformation($"Payload submitted with height {payload.BlockNumber}, canonical head is at height {ChainState.LastBlock.Height}. Initiating sync to parent {payload.BlockNumber - 1}/{payloadParent.ToPrettyString()}");
                            StartSyncFromHash(payload.BlockHash);
                            responseValue = new PayloadStatusV1("SYNCING", null, null);
                        }
                    }
                    else
                    {
                        lock (ChainState)
                        {
                            var valid = ChainState.ValidateBlock(localBlock);

                            if (valid)
                            {
                                responseValue =
                                    new PayloadStatusV1("VALID", payload.BlockHash.ToPrettyString(true), null);
                                BlockStorage.StoreBlock(localBlock);
                            }
                            else
                            {
                                responseValue = new PayloadStatusV1("INVALID",
                                    ChainState.LastBlockHash.ToPrettyString(true), "Could not validate");
                            }
                        }
                    }
                }
                else
                {
                    // Does not extend the canonical chain
                    responseValue = new PayloadStatusV1("VALID", null, null);
                }
            }
            break;
            case "engine_exchangeTransitionConfigurationV1":
            {
                responseValue = parameters[0];
            } 
            break;
        }

        object? responseObject = null;
        if (errorCode is not null)
        {
            responseObject = new
            {
                jsonrpc = "2.0",
                id,
                error = new
                {
                    code = errorCode,
                    message = errorMessage
                }
            };
        }
        else
        {
            responseObject = new
            {
                jsonrpc = "2.0",
                id,
                result = responseValue
            };
        }

        var encoded = JsonSerializer.Serialize(responseObject, DefaultSerializerOptions);
        
        Logger.LogInformation("Responding with body {body}", encoded.Substring(0, Math.Min(encoded.Length, 2000)));
        using var sw = new StreamWriter(context.Response.OutputStream);
        await sw.WriteAsync(encoded);
        //await JsonSerializer.SerializeAsync(context.Response.OutputStream, responseObject, DefaultSerializerOptions);
        context.Response.ContentType = "application/json";
    }
    
    Block CreateBlockFromPayload(ExecutionPayload payload)
    {
        lock (payload)
        {
            var block = new Block()
            {
                Height = payload.BlockNumber,
                Coinbase = payload.FeeRecipient,
                Nonce = new byte[8],
                Hash = payload.BlockHash,
                Parent = payload.Root,
                Timestamp = payload.Timestamp,
                Transactions = payload.Transactions.Select(txBytes => Transaction.Deserialize(txBytes).Item1).ToArray(),
            };
            block.CalculateHash();
            payload.BlockHash = block.Hash;
            return block;
        }
    }
    long CreatePayload(PayloadAttributesV1 attribs)
    {
        var payload = new ExecutionPayload()
        {
            Timestamp = (ulong)attribs.Timestamp.Value,
            FeeRecipient = attribs.SuggestedFeeRecipient.ToByteArray(),
            PrevRandao = attribs.PrevRandao.ToByteArray(),
            BaseFeePerGas = 1,
            BlockHash = new byte[32],
            BlockNumber = ChainState.LastBlock.Height + 1,
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
    private int injectedTx = 0;
    private byte[] signerPrivateKey = new byte[32];
    private byte[] signerAddress = new byte[20];
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
                    Nonce = (ulong) injectedTx++,
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
                    Nonce = (ulong) injectedTx,
                    Type = TransactionType.Invoke
                };

                var sw = Stopwatch.StartNew();
                lock (ChainState.LockObject)
                {
                    var callResult = Executor.Execute(contractCallData, executionTx, ChainState) ??
                                     throw new Exception("what");
                    var receipt = callResult.Receipt.Value;
                    callResult.RollbackContext.ExecuteRollback();

                    try
                    {
                        // Verify receipt that we just created
                        var verifyResult = Executor.Execute(contractCallData, executionTx, ChainState, receipt);
                        verifyResult.RollbackContext.ExecuteRollback();
                        if (!verifyResult.Verified)
                        {
                            throw new Exception($"Failed to verify generated receipt");
                        }

                        var executionPayload = ContractInvokePayload.FromReceipt(receipt);

                        Logger.LogInformation(
                            $"Calldata for {function} is {calldata.ToPrettyString()}, return value is {receipt.ReturnValue.ToPrettyString()}, call took {sw.Elapsed}, {receipt.InstructionCount} instructions");
                        executionTx.Payload = executionPayload.SerializeWithTrace();

                        //File.WriteAllText($"./tx-{n++}", JsonSerializer.Serialize(executionTx));

                        injectedTx++;
                        return executionTx;
                    }
                    catch (Exception e)
                    {
                        Logger.LogError(e, "Failed to generate contract call to inject");
                        return null;
                    }
                }
            }

            Transaction? txToInject = null;
            if (injectedTx == 0 && !ChainState.Contracts.Any())
            {
                txToInject = CreateDeploy("/home/kate/repos/x86-bare-metal-examples/multiboot/osdev/iso/boot/token.elf",
                    new[] {"read", "write", "hash", "mint", "get_balance", "transfer"}, multiboot: true);
            }
            else
            {
                txToInject = CreateContractCall(injectedTx % 2 == 1 ? "write" : "read", new byte[0]);
            }

            if (txToInject is null)
            {
                Logger.LogWarning($"Could not inject tx into payload {payloadId}");
                return;
            }

            txToInject = SignTransaction(txToInject);
            var payload = Payloads[payloadId];
            var buf = new byte[16777216];
            var written = txToInject.Serialize(new Span<byte>(buf));
            var newArray = new byte[written];
            //buf.CopyTo(newArray, 0);
            Array.Copy(buf, 0, newArray, 0, written);
            payload.Transactions.Add(newArray);

        }
        catch (Exception e)
        {
            Logger.LogError(e, $"Failed to inject tx {injectedTx} into payload {payloadId}");
        }
    }

    private bool syncing = false;
    
    private FakeEthereumBlock? newGenesisBlock;
    
    void ResetFromGenesis()
    {
        injectedTx = 0;
        var beaconStateType = SszContainer.GetContainer<BeaconState>();
        var genesisPath = Configuration.GenesisStatePath;

        if (!FakeEthereumBlockSource.GenesisDeposits.Any())
        {
            var validatorPaths = new List<string>();
            
            // ~/.lighthouse/local-testnet/node_1/validators/0x81283b7a20e1ca460ebd9bbd77005d557370cabb1f9a44f530c4c4c66230f675f8df8b4c2818851aa7d77a80ca5a4a5e/eth1-deposit-data.rlp
            var validatorRoot = Configuration.LighthouseValidatorDirectory;
            
            foreach (var nodeDir in Directory.GetDirectories(validatorRoot))
            {
                if (nodeDir.Substring(0, nodeDir.Length - 1).EndsWith("node_"))
                {
                    validatorPaths.AddRange(Directory.GetDirectories($"{nodeDir}/validators/").Where(p => Path.GetFileName(p).StartsWith("0x")));
                }
            }
            
            Logger.LogInformation($"Found {validatorPaths.Count} validators");

            foreach (var validatorPath in validatorPaths)
            {
                var depositTx = File.ReadAllText($"{validatorPath}/eth1-deposit-data.rlp");
                FakeEthereumBlockSource.GenesisDeposits.Add(DepositData.FromDepositTransactionRlp(depositTx.ToByteArray()));
            }
        }
        
        (var deserializedState, _) =
            beaconStateType.Deserialize(File.ReadAllBytes(genesisPath));
        if (newGenesisBlock is not null)
        {
            Logger.LogInformation($"Injecting fake genesis block header into state with root hash {ChainState.LastStateRoot.ToPrettyString()}");
            var prevHeader = deserializedState.LastExecutionPayloadHeader;
            deserializedState.LastExecutionPayloadHeader = new ExecutionPayloadHeader()
            {
                BaseFeePerGas = 1,
                BlockHash = newGenesisBlock.Hash.ToByteArray(),
                BlockNumber = (ulong)newGenesisBlock.Height,
                ExtraData = new byte[1],
                FeeRecipient = new byte[20],
                GasLimit = 100,
                GasUsed = 1,
                LogsBloom = new byte[256],
                PrevRandao = new byte[32],
                ReceiptsRoot = prevHeader.ReceiptsRoot,
                Timestamp = (ulong)newGenesisBlock.Timestamp,
                StateRoot = prevHeader.StateRoot,
                Root = prevHeader.Root,
                TransactionsRoot = prevHeader.TransactionsRoot,
            };
        }
        ChainState.SetGenesisState(deserializedState);
        ChainState.Snapshot.LastStateRoot = Merkleizer.HashTreeRoot(beaconStateType, deserializedState);
        Logger.LogInformation($"Loaded genesis state with root hash {ChainState.LastStateRoot.ToPrettyString()}");

    }

    List<Block> GetFastForwardList(Block sourceBlock, Block targetBlock)
    {
        // Start from target block, walk back to source block, return path of blocks
        var ret = new List<Block>();
        ret.Add(targetBlock);

        Block? currentBlock = targetBlock;
        while (currentBlock is not null && !currentBlock.Hash.SequenceEqual(sourceBlock.Hash))
        {
            currentBlock = BlockStorage.GetBlock(currentBlock.Parent);
        }

        if (currentBlock?.Hash.SequenceEqual(sourceBlock.Hash) == true)
        {
            ret.Reverse();
            return ret;
        }
        
        ret.Clear();
        return ret;
    }
    
    void StartSyncFromHash(byte[] hash)
    {
        lock (this)
        {
            if (syncing)
                return;

            syncing = true;
        }

        new Thread((ThreadStart) delegate
        {
            using var t = Logger.BeginScope("sync to {headTruncated}", hash.ToPrettyString().Substring(0, 4));
            try
            {
                lock (ChainState)
                {
                    var genesisHash = ChainState.GenesisState.LastExecutionPayloadHeader.BlockHash;
                    var currentHead = ChainState.LastBlockHash;
                    var head = hash;
                    var blockSequence = new List<Block>();
                    Block? headBlock;

                    Logger.LogInformation($"Syncing to head {head.ToPrettyString()}");

                    while (!head.SequenceEqual(genesisHash) && !head.SequenceEqual(currentHead))
                    {
                        headBlock = BlockStorage.GetBlock(head) ??
                                    throw new Exception($"Could not retrieve block at {head.ToPrettyString()}");
                        blockSequence.Add(headBlock);
                        head = headBlock.Parent;
                    }

                    if (head.SequenceEqual(genesisHash))
                    {
                        Logger.LogInformation(
                            $"Syncing to {head.ToPrettyString()} from genesis hash {genesisHash.ToPrettyString()}");
                        ResetFromGenesis();
                    }
                    else if (head.SequenceEqual(currentHead))
                    {
                        Logger.LogInformation(
                            $"Syncing to {head.ToPrettyString()} from head {ChainState.LastBlock.Height}/{currentHead.ToPrettyString()}");
                    }
                    else
                    {
                        throw new Exception(
                            $"Reached block {head.ToPrettyString()}, which is neither current head {currentHead.ToPrettyString()} nor genesis {genesisHash.ToPrettyString()}");
                    }

                    blockSequence.Reverse();

                    foreach (var block in blockSequence)
                    {
                        (var processed, _) = ChainState.ProcessBlock(block);
                        if (!processed)
                        {
                            throw new Exception($"Failed to process block {block.Hash.ToPrettyString()}");
                        }
                    }

                    Logger.LogInformation($"Sync complete, now at {ChainState.LastBlockHash.ToPrettyString()}");
                    syncing = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Sync failed");
            }
        }).Start();
    }
}