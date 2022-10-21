using System;
using System.Globalization;
using System.Linq;

namespace Kmart
{
    static class ByteArrayExtensions
    {
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