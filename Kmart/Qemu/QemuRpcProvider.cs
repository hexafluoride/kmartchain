using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Kmart.Interfaces;
using Kmart.Qemu;
using Microsoft.Extensions.Logging;
using SszSharp;

namespace Kmart;

public class QemuRpcProvider : IRpcProvider
{
    public IEnumerable<string> Commands { get; } = new[] { "qemu_constructTransaction", "qemu_sendTransaction", "qemu_listContracts" };

    private ILogger<QemuRpcProvider> Logger;
    private ChainState ChainState;
    private ContractExecutor ContractExecutor;
    private PayloadManager PayloadManager;

    public QemuRpcProvider(ILogger<QemuRpcProvider> logger, ChainState chainState, ContractExecutor contractExecutor, PayloadManager payloadManager)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ChainState = chainState ?? throw new ArgumentNullException(nameof(chainState));
        ContractExecutor = contractExecutor ?? throw new ArgumentNullException(nameof(contractExecutor));
        PayloadManager = payloadManager ?? throw new ArgumentNullException(nameof(payloadManager));
    }
    
    public async Task ServeRequestAsync(HttpListenerContext context, int id, string method, JsonElement parameters)
    {        
        context.Response.ContentType = "application/json";
        object? responseValue = null;
        int? errorCode = null;
        string? errorMessage = null;

        try
        {
            switch (method)
            {
                case "qemu_listContracts":
                {
                    Logger.LogInformation($"We have {ChainState.Contracts.Count} contracts");
                    responseValue = ChainState.Contracts.ToDictionary(c => c.Key.ToPrettyString(true), c => c.Value.Functions.ToList());
                    break;
                }
                case "qemu_constructTransaction":
                {
                    // from, contract, function, calldata
                    var txFrom = parameters[0].GetString()?.ToByteArray() ?? throw new Exception("No from");
                    var targetContract = parameters[1].GetString()?.ToByteArray() ?? throw new Exception("No contract");
                    var targetFunction = parameters[2].GetString() ?? throw new Exception("No function");
                    var calldata = parameters[3].GetString()?.ToByteArray() ?? throw new Exception("No calldata");

                    bool createReceipt = false;

                    if (parameters.GetArrayLength() > 4)
                    {
                        createReceipt = parameters[4].GetBoolean();
                    }

                    var newTransaction = new Transaction()
                    {
                        Address = txFrom,
                        FeePerByte = 1,
                        Nonce = 1,
                        Timestamp = (ulong) (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds,
                        Type = TransactionType.Invoke
                    };

                    var payload = new ContractInvokePayload()
                    {
                        CallData = calldata,
                        Target = targetContract,
                    };

                    payload.Function = targetFunction;

                    newTransaction.Payload = payload.Serialize();
                    newTransaction.CalculateHash();

                    ContractInvocationReceipt? receipt = null;

                    if (createReceipt)
                    {
                        lock (ChainState.LockObject)
                        {
                            var call = ContractCall.FromPayload(newTransaction, payload);
                            var executionResult = ContractExecutor.Execute(call, newTransaction, ChainState) ?? throw new Exception($"Failed to execute");
                            executionResult.RollbackContext?.ExecuteRollback();
                            receipt = executionResult.Receipt;
                        }
                    }

                    responseValue = new
                    {
                        txHash = newTransaction.Hash.ToPrettyString(true),
                        tx = newTransaction,
                        txSerialized = SszContainer.Serialize(newTransaction).ToPrettyString(true),
                        payload,
                        receipt
                    };
                    break;
                }
                case "qemu_sendTransaction":
                {
                    var txRaw = parameters[0].GetString()?.ToByteArray() ?? throw new Exception($"No tx");
                    var signature = parameters[1].GetString()?.ToByteArray() ?? throw new Exception("No signature");
                    signature = signature.Take(64).ToArray();
                    var txDecoded = Transaction.Deserialize(txRaw).Item1;
                    txDecoded.Signature = signature;
                    txDecoded.CalculateHash();

                    PayloadManager.Mempool.Add(txDecoded);
                    responseValue = txDecoded.Hash;
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"Exception thrown while serving RPC request");
            errorCode = -2;
            errorMessage = e.Message;
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

        var encoded = JsonSerializer.Serialize(responseObject, new JsonSerializerOptions()
        {
            Converters = {new ByteArrayJsonConverter()},
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        Logger.LogInformation("Responding with body {body}", encoded.Substring(0, Math.Min(encoded.Length, 2000)));
        using var sw = new StreamWriter(context.Response.OutputStream);
        await sw.WriteAsync(encoded);
    }
}