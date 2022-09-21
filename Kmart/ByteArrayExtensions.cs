using System;
using System.Globalization;

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
        public static string ToPrettyString(this byte[] arr, bool prefix = false)
        {
            if (arr == null)
                return "(null)";
            
            var str = BitConverter.ToString(arr).Replace("-", "").ToLowerInvariant();
            if (prefix)
                return "0x" + str;
            return str;
        }
    }
}