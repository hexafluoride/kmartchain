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
using Nethereum.Model;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Parameters;
using SszSharp;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Kmart;

public class ExecutionLayerServer
{
    private readonly HttpListener HttpListener;
    private readonly IJwtAlgorithm Algorithm;
    private readonly JwtDecoder JwtDecoder;
    private readonly ILogger<ExecutionLayerServer> Logger;
    private readonly ChainState ChainState;
    private readonly ContractExecutor Executor;
    private readonly BlockStorage BlockStorage;

    public Dictionary<long, ExecutionPayload> Payloads = new();
    private readonly JsonSerializerOptions DefaultSerializerOptions = new JsonSerializerOptions()
    {
    };

    public ExecutionLayerServer(ILogger<ExecutionLayerServer> logger, ChainState chainState, ContractExecutor contractExecutor, BlockStorage blockStorage)
    {
        HttpListener = new HttpListener();
        Algorithm = new HMACSHA256Algorithm();
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ChainState = chainState ?? throw new ArgumentNullException(nameof(chainState));
        Executor = contractExecutor ?? throw new ArgumentNullException(nameof(contractExecutor));
        BlockStorage = blockStorage ?? throw new ArgumentNullException(nameof(blockStorage));
        
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

        var prefix = "http://127.0.0.1:8551/";

        if (Environment.GetEnvironmentVariable("ELS_PORT") is not null)
        {
            prefix = $"http://127.0.0.1:{Environment.GetEnvironmentVariable("ELS_PORT")}/";
        }
        
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

    private int lastFakedHeight = 16;
    private string lastFakedHash = "";

    class FakeEthereumBlock
    {
        public int Height { get; set; }
        public int Timestamp { get; set; }
        public string ParentHash { get; set; }
        public string Hash { get; set; }
        public string Difficulty { get; set; } 

        public const string PreMergeDifficulty = "0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffbff";
        public const string PostMergeDifficulty = "0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffc01";

        public object Encode()
        {
            return new
            {
                number = BitConverter.GetBytes(Height)
                    .Reverse().ToArray().ToPrettyString(true, true),
                hash = Hash,
                parentHash = ParentHash,
                nonce = new byte[8].ToPrettyString(prefix: true),
                difficulty = Difficulty,
                totalDifficulty = Difficulty,
                timestamp = BitConverter.GetBytes(Timestamp)
                    .Reverse().ToArray().ToPrettyString(true, true)
            };
        }
    }

    private Dictionary<string, FakeEthereumBlock> fakeBlocks = new();

    object? GetEthereumBlockAtHeight(string heightSpec)
    {
        /*
         *     number: QUANTITY - the block number. null when its pending block.
    hash: DATA, 32 Bytes - hash of the block. null when its pending block.
    parentHash: DATA, 32 Bytes - hash of the parent block.
    nonce: DATA, 8 Bytes - hash of the generated proof-of-work. null when its pending block.
    sha3Uncles: DATA, 32 Bytes - SHA3 of the uncles data in the block.
    logsBloom: DATA, 256 Bytes - the bloom filter for the logs of the block. null when its pending block.
    transactionsRoot: DATA, 32 Bytes - the root of the transaction trie of the block.
    stateRoot: DATA, 32 Bytes - the root of the final state trie of the block.
    receiptsRoot: DATA, 32 Bytes - the root of the receipts trie of the block.
    miner: DATA, 20 Bytes - the address of the beneficiary to whom the mining rewards were given.
    difficulty: QUANTITY - integer of the difficulty for this block.
    totalDifficulty: QUANTITY - integer of the total difficulty of the chain until this block.
    extraData: DATA - the "extra data" field of this block.
    size: QUANTITY - integer the size of this block in bytes.
    gasLimit: QUANTITY - the maximum gas allowed in this block.
    gasUsed: QUANTITY - the total used gas by all transactions in this block.
    timestamp: QUANTITY - the unix timestamp for when the block was collated.
    transactions: Array - Array of transaction objects, or 32 Bytes transaction hashes depending on the last given parameter.
    uncles: Array - Array of uncle hashes.
         */
        
        var targetHeight = heightSpec.Equals("latest", StringComparison.InvariantCultureIgnoreCase)
            ? -1
            : (long)heightSpec.ToQuantity();
        var currentHeight = ChainState.LastBlock.Height;
        var newHash = new byte[32];

        // if (currentHeight == 0)
        // {
            if (targetHeight == -1)
            {
                // return height 16, difficulty high
                var newHeight = lastFakedHeight + 1;
                var localRng = new Random(newHeight);
                localRng.NextBytes(newHash);
                var newBlock = new FakeEthereumBlock()
                {
                    Height = newHeight,
                    Difficulty = newHeight > 20 ? FakeEthereumBlock.PostMergeDifficulty : FakeEthereumBlock.PreMergeDifficulty,
                    Hash = newHash.ToPrettyString(true),
                    ParentHash = string.IsNullOrWhiteSpace(lastFakedHash) ? new byte[32].ToPrettyString(prefix: true) : lastFakedHash,
                    Timestamp = (int) (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds
                };

                if (newHeight == 21)
                {
                    newGenesisBlock = newBlock;
                    ResetFromGenesis();
                }

                lastFakedHeight = newHeight;
                lastFakedHash = newBlock.Hash;
                fakeBlocks[newBlock.Hash] = newBlock;

                return newBlock.Encode();
                // return new
                // {
                //     number = BitConverter.GetBytes(lastFakedHeight++)
                //         .Reverse().ToArray().ToPrettyString(true, true),
                //     hash = $"0x{new string('b', 64)}",
                //     parentHash = new byte[31].ToPrettyString(prefix: true) + "01",
                //     nonce = new byte[8].ToPrettyString(prefix: true),
                //     difficulty = "0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffc01",
                //     totalDifficulty = "0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffc01",
                //     timestamp = BitConverter.GetBytes((long) (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds)
                //         .Reverse().ToArray().ToPrettyString(true, true)
                // };
            }
            else
            {
                var matchingBlock = fakeBlocks.Values.FirstOrDefault(b => b.Height == targetHeight);
                if (matchingBlock is null)
                    return null;
                return matchingBlock.Encode();

                // return new
                // {
                //     number =  BitConverter.GetBytes(targetHeight)
                //         .Reverse().ToArray().ToPrettyString(true, true),
                //     hash = $"0x{new string('b', 64)}",
                //     parentHash = new byte[31].ToPrettyString(prefix: true) + "01",
                //     nonce = new byte[8].ToPrettyString(prefix: true),
                //     difficulty = "0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffc01",
                //     totalDifficulty = "0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffc01",
                //     timestamp = BitConverter.GetBytes((long) (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds)
                //         .Reverse().ToArray().ToPrettyString(true, true)
                // };
            }
        // }

        throw new Exception($"What {heightSpec}");
    }

    async Task<JsonElement> GetLogsResponse(JsonElement parameters)
    {
        var ganacheUrl = "http://127.0.0.1:8545/";
        var request = new
        {
            jsonrpc = "2.0",
            method = "eth_getLogs",
            id = 1,
            @params = parameters
        };

        var client = new HttpClient();
        var requestEncoded = JsonSerializer.Serialize(request);
        //var response = await client.PostAsJsonAsync(ganacheUrl, request);
        var response = await client.PostAsync(ganacheUrl, new StringContent(requestEncoded));
        var responseStream = await response.Content.ReadAsStreamAsync();
        var responseObj = await JsonDocument.ParseAsync(responseStream);
        var result = responseObj.RootElement.GetProperty("result");
        return result;
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
                responseValue = await GetLogsResponse(parameters);
                break;
            }
            case "eth_getBlockByNumber":
            {
                var heightSpec = parameters[0].GetString() ?? throw new Exception($"Could not obtain height spec");
                responseValue = GetEthereumBlockAtHeight(heightSpec);
                break;
            }
            case "eth_getBlockByHash":
            {
                var hashSpec = parameters[0].GetString() ?? throw new Exception($"Could not obtain hash spec");
                responseValue = fakeBlocks.ContainsKey(hashSpec) ? fakeBlocks[hashSpec].Encode() : null;
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

                                    ChainState.LoadSnapshot(currentHead);
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
        var genesisPath = "/home/kate/.lighthouse/local-testnet/testnet/genesis.ssz";
        //var genesisPath = "/home/kate/repos/prysm/devnet/genesis.ssz";
        // var genesisPath = "/home/kate/repos/lodestar/genesis.ssz";
        
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

                    // var targetHeadBlock = BlockStorage.GetBlock(head);
                    // if (targetHeadBlock is not null)
                    //     blockSequence.Add(targetHeadBlock);

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