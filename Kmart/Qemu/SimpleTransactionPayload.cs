using System.IO;
using System.Text.Json;

namespace Kmart.Qemu
{
    struct SimpleTransactionPayload
    {
        public byte[] From { get; set; }
        public byte[] To { get; set; }
        public ulong Amount { get; set; }

        public static SimpleTransactionPayload FromTransaction(Transaction tx)
        {
            if (tx.Type != TransactionType.Simple)
                throw new InvalidDataException("Invalid data");

            return JsonSerializer.Deserialize<SimpleTransactionPayload>(tx.Payload);
        }

        public byte[] Serialize()
        {
            return JsonSerializer.SerializeToUtf8Bytes(this);
        }
    }
}