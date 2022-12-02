using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kmart
{
    public sealed class ByteArrayKeyDictionaryConverter<TValue>
        : JsonConverter<Dictionary<byte[], TValue>>
    {
        public override Dictionary<byte[], TValue> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var dic = (Dictionary<string, TValue>)(JsonSerializer
                .Deserialize(ref reader, typeof(Dictionary<string, TValue>), options) ?? throw new Exception($"Failed to deserialize"));
            return dic.ToDictionary(k => k.Key.ToByteArray(), k => k.Value, new ByteArrayComparer());
        }

        public override void Write(
            Utf8JsonWriter writer,
            Dictionary<byte[], TValue> value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(
                writer, value.ToDictionary(p => p.Key.ToPrettyString(), p => p.Value), typeof(Dictionary<string, TValue>), options);
        }
    }

    
    public class ByteArrayComparer : EqualityComparer<byte[]>
    {
        public override bool Equals(byte[]? first, byte[]? second)
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
            
            for (int i = 0; i < first.Length; i++)
                if (first[i] != second[i])
                    return false;

            return true;
            //return first.SequenceEqual(second);
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
            for (int i = 0; i < obj.Length; i++)
            {
                value <<= 8;
                value += obj[i];
            }
            return value;
        }
    }
}