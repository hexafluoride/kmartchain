using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;
using SszSharp;

namespace Kmart;

public class ExecutionPayloadWrapper
{
    public static ExecutionPayloadWrapper FromPayload(ExecutionPayload payload)
    {
        return new ExecutionPayloadWrapper() {Payload = payload};
    }
    public ExecutionPayload Payload = new();

    [JsonPropertyName("parentHash")]
    public string ParentHash
    {
        get => Payload.Root.ToPrettyString(true);
        set => Payload.Root = value.ToByteArray();
    }
    [JsonPropertyName("feeRecipient")]
    public string FeeRecipient
    {
        get => Payload.FeeRecipient.ToPrettyString(true);
        set => Payload.FeeRecipient = value.ToByteArray();
    }
    [JsonPropertyName("stateRoot")]
    public string StateRoot
    {
        get => Payload.StateRoot.ToPrettyString(true);
        set => Payload.StateRoot = value.ToByteArray();
    }
    [JsonPropertyName("receiptsRoot")]
    public string ReceiptsRoot
    {
        get => Payload.ReceiptsRoot.ToPrettyString(true);
        set => Payload.ReceiptsRoot = value.ToByteArray();
    }
    [JsonPropertyName("logsBloom")]
    public string LogsBloom
    {
        get => Payload.LogsBloom.ToPrettyString(true);
        set => Payload.LogsBloom = value.ToByteArray();
    }
    [JsonPropertyName("blockNumber")]
    public string BlockNumber
    {
        get => BitConverter.GetBytes(Payload.BlockNumber).Reverse().ToArray().ToPrettyString(true);
        set => Payload.BlockNumber = BitConverter.ToUInt64(value.ToByteArray(8).Reverse().ToArray());
    }
    [JsonPropertyName("gasLimit")]
    public string GasLimit
    {
        get => BitConverter.GetBytes(Payload.GasLimit).Reverse().ToArray().ToPrettyString(true);
        set => Payload.GasLimit = BitConverter.ToUInt64(value.ToByteArray(8).Reverse().ToArray());
    }
    [JsonPropertyName("gasUsed")]
    public string GasUsed
    {
        get => BitConverter.GetBytes(Payload.GasUsed).Reverse().ToArray().ToPrettyString(true);
        set => Payload.GasUsed = BitConverter.ToUInt64(value.ToByteArray(8).Reverse().ToArray());
    }
    [JsonPropertyName("timestamp")]
    public string Timestamp
    {
        get => BitConverter.GetBytes(Payload.Timestamp).Reverse().ToArray().ToPrettyString(true);
        set => Payload.Timestamp = BitConverter.ToUInt64(value.ToByteArray(8).Reverse().ToArray());
    }
    [JsonPropertyName("extraData")]
    public string ExtraData
    {
        get => Payload.ExtraData.ToPrettyString(true);
        set => Payload.ExtraData = value.ToByteArray();
    }
    [JsonPropertyName("baseFeePerGas")]
    public string BaseFeePerGas
    {
        get => Payload.BaseFeePerGas.ToByteArray(true, true).ToPrettyString(true);
        set => Payload.BaseFeePerGas = new BigInteger(value.ToByteArray(), true, true);
    }
    
    [JsonPropertyName("blockHash")]
    public string BlockHash
    {
        get => Payload.BlockHash.ToPrettyString(true);
        set => Payload.BlockHash = value.ToByteArray();
    }
    [JsonPropertyName("prevRandao")]
    public string PrevRandao
    {
        get => Payload.PrevRandao.ToPrettyString(true);
        set => Payload.PrevRandao = value.ToByteArray();
    }

    [JsonPropertyName("transactions")]
    public List<string> Transactions
    {
        get => Payload.Transactions.Select(tx => tx.ToPrettyString()).ToList();
        set => Payload.Transactions = value.Select(tx => tx.ToByteArray()).ToList();
    }
}