using System;
using System.Linq;
using SszSharp;

namespace Kmart;

public class DepositData
{
    [SszElement(0, "Vector[uint8, 48]")] public byte[] Pubkey { get; set; } = new byte[48];
    [SszElement(1, "Vector[uint8, 32]")] public byte[] WithdrawalCredentials { get; set; } = new byte[32];
    [SszElement(2, "uint64")] public ulong DepositAmount { get; set; }
    [SszElement(3, "Vector[uint8, 96]")] public byte[] Signature { get; set; } = new byte[96];
    public ulong Index { get; set; }
    public static ulong TotalDeposits = 0;

    public static DepositData FromDepositTransactionRlp(byte[] rlp, ulong depositAmount = 32_000_000_000)
    {
        var rlpSpan = rlp.AsSpan();
        if (rlpSpan[0] == 0x22)
            rlpSpan = rlpSpan.Slice(4);

        var rootHash = rlpSpan.Slice(96, 32).ToArray();
        var depositData = new DepositData()
        {
            DepositAmount = depositAmount,
            Pubkey = rlpSpan.Slice(160, 48).ToArray(),
            WithdrawalCredentials = rlpSpan.Slice(256, 32).ToArray(),
            Signature = rlpSpan.Slice(320, 96).ToArray(),
            Index = TotalDeposits++
        };
        
        var depositRoot = SszContainer.GetContainer<DepositData>().HashTreeRoot(depositData);
        if (!depositRoot.SequenceEqual(rootHash))
            throw new Exception(
                $"Deposit data root hash from transaction is {rootHash.ToPrettyString()}, calculated {depositRoot.ToPrettyString()}");

        return depositData;
    }

    public string GenerateLogBlob()
    {
        // I know
        var blobStr =
            @$"0x00000000000000000000000000000000000000000000000000000000000000a0
0000000000000000000000000000000000000000000000000000000000000100
0000000000000000000000000000000000000000000000000000000000000140
0000000000000000000000000000000000000000000000000000000000000180
0000000000000000000000000000000000000000000000000000000000000200
0000000000000000000000000000000000000000000000000000000000000030
{Pubkey.ToPrettyString()}
00000000000000000000000000000000
0000000000000000000000000000000000000000000000000000000000000020
{WithdrawalCredentials.ToPrettyString()}
0000000000000000000000000000000000000000000000000000000000000008
{BitConverter.GetBytes(DepositAmount).ToPrettyString()}
000000000000000000000000000000000000000000000000
0000000000000000000000000000000000000000000000000000000000000060
{Signature.ToPrettyString()}
0000000000000000000000000000000000000000000000000000000000000008
{BitConverter.GetBytes(Index).ToPrettyString()}
000000000000000000000000000000000000000000000000".Replace("\n", "");

        return blobStr;
    }

    public object CreateLogObject(string blockHash, ulong blockNumber, string transactionHash, ulong transactionIndex, string address, ulong logIndex, string topic)
    {
        return new
        {
            blockHash,
            blockNumber = blockNumber.EncodeQuantity(),
            transactionHash,
            transactionIndex = transactionIndex.EncodeQuantity(),
            address,
            removed = false,
            logIndex = logIndex.EncodeQuantity(),
            topics = new[] { topic },
            data = GenerateLogBlob()
        };
    }
}