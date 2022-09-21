using System.IO;

namespace Kmart
{
    public class BlobManager
    {
        public const string ContractImageKey = "contract_image";
        public const string ContractInitrdKey = "contract_initrd";
        public const string ExecutionTraceKey = "exec_trace";
        public const string BlockKey = "block";
        
        public string GetPath(byte[] id, string type) => $"blobs/{type}/{id.ToPrettyString()}.dat";

        public void InitializeDirectories()
        {
            var keys = new[]
            {
                ContractImageKey,
                ContractInitrdKey,
                ExecutionTraceKey,
                BlockKey
            };

            foreach (var key in keys)
            {
                Directory.CreateDirectory($"blobs/{key}");
            }
        }
    }
}