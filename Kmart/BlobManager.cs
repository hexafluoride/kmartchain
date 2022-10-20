using System.IO;

namespace Kmart
{
    public class BlobManager
    {
        public const string ContractImageKey = "contract_image";
        public const string ContractInitrdKey = "contract_initrd";
        public const string ExecutionTraceKey = "exec_trace";
        public const string BlockKey = "block";
        public const string StateSnapshotKey = "state_snapshot";
        
        public static string DataDirectory { get; set; } = ".";
        
        public string GetPath(byte[] id, string type) => $"{DataDirectory}/blobs/{type}/{id.ToPrettyString()}.dat";

        public void InitializeDirectories()
        {
            var keys = new[]
            {
                ContractImageKey,
                ContractInitrdKey,
                ExecutionTraceKey,
                BlockKey,
                StateSnapshotKey
            };

            foreach (var key in keys)
            {
                Directory.CreateDirectory($"{DataDirectory}/blobs/{key}");
            }
        }
    }
}