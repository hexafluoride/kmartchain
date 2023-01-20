using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nethereum.RLP;
using Nethereum.Signer;
using Nethereum.Util;

namespace Kmart
{
    public class ByteArrayJsonConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            reader.GetString()!.ToByteArray();

        public override void Write(
            Utf8JsonWriter writer,
            byte[] arr,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(arr.ToPrettyString(true));
    }
    
    public static class ByteArrayExtensions
    {
        public static byte[] RlpWrap(this byte[] bytes)
        {
            var ethTx = new LegacyTransaction(1.ToBytesForRLPEncoding(),
                1.ToBytesForRLPEncoding(),
                1.ToBytesForRLPEncoding(),
                new byte[20],
                1.ToBytesForRLPEncoding(),
                bytes,
                new byte[]
                    {
                        1
                    }.Concat(new byte[31])
                    .ToArray(),
                new byte[]
                    {
                        1
                    }.Concat(new byte[31])
                    .ToArray(),
                1);
            return ethTx.GetRLPEncoded();
        }

        public static byte[] RlpUnwrap(this byte[] bytes)
        {
            var ethCollection = RLP.Decode(bytes) as RLPCollection;
            return ethCollection[5].RLPData;
        }

        public static object RlpUnwrapTransaction(this byte[] bytes)
        {
            var ethCollection = RLP.Decode(bytes) as RLPCollection;
            return new
            {
                type = "0x0",
                nonce = ethCollection[0].RLPData.ToPrettyString(true, true),
                gasPrice = ethCollection[1].RLPData.ToPrettyString(true, true),
                gas = ethCollection[2].RLPData.ToPrettyString(true, true),
                to = ethCollection[3].RLPData.ToPrettyString(true),
                value = ethCollection[4].RLPData.ToPrettyString(true, true),
                input = ethCollection[5].RLPData.ToPrettyString(true),
                v = ethCollection[6].RLPData.ToPrettyString(true, true),
                r = ethCollection[7].RLPData.ToPrettyString(true, true),
                s = ethCollection[8].RLPData.ToPrettyString(true, true),
                hash = Sha3Keccack.Current.CalculateHash(bytes).ToPrettyString(true)
            };
        }

        public static byte[] ToByteArray(this string str, int padLeft = 0)
        {
            var strClipped = str.AsSpan();

            if (str.StartsWith("0x"))
                strClipped = strClipped.Slice(2);

            if (strClipped.Length % 2 == 1)
            {
                strClipped = "0" + new string(strClipped);
            }

            var providedLength = strClipped.Length / 2;
            int offset = providedLength < padLeft ? padLeft - providedLength : 0;
            var paddedLength = providedLength < padLeft ? padLeft : providedLength;
            var ret = new byte[paddedLength];
            for (int i = 0; i < providedLength; i++)
            {
                ret[offset + i] = byte.Parse(strClipped.Slice(i * 2, 2), NumberStyles.HexNumber);
            }

            return ret;
        }
        public static string ToPrettyString(this byte[] arr, bool prefix = false, bool skipZeroes = false)
        {
            if (arr == null)
                return "(null)";
            
            var str = BitConverter.ToString(arr).Replace("-", "").ToLowerInvariant();

            if (skipZeroes)
            {
                str = str.TrimStart('0');
                if (str.Length == 0)
                    str = "0";
            }

            if (prefix)
                return "0x" + str;
            return str;
        }

        public static ulong ToQuantity(this string hexStr) => BitConverter.ToUInt64(hexStr.ToByteArray(8).Reverse().ToArray());

        public static string EncodeQuantity(this ulong quantity) => BitConverter.GetBytes(quantity).Reverse().ToArray()
            .ToPrettyString(prefix: true, skipZeroes: true);
    }
}