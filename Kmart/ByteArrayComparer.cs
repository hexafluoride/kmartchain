using System;
using System.Collections.Generic;
using System.Linq;

namespace Kmart
{
    public class ByteArrayComparer : EqualityComparer<byte[]>
    {
        public override bool Equals(byte[] first, byte[] second)
        {
            if (first == null || second == null) {
                // null == null returns true.
                // non-null == null returns false.
                return first == second;
            }
            if (ReferenceEquals(first, second)) {
                return true;
            }
            if (first.Length != second.Length) {
                return false;
            }
            // Linq extension method is based on IEnumerable, must evaluate every item.
            return first.SequenceEqual(second);
        }
        public override int GetHashCode(byte[] obj)
        {
            if (obj == null) {
                throw new ArgumentNullException("obj");
            }
            if (obj.Length >= 4) {
                return BitConverter.ToInt32(obj, 0);
            }
            // Length occupies at most 2 bits. Might as well store them in the high order byte
            int value = obj.Length;
            foreach (var b in obj) {
                value <<= 8;
                value += b;
            }
            return value;
        }
    }
}