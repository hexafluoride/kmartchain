using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Org.BouncyCastle.Crypto.Parameters;
using SszSharp;

namespace Kmart
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            SizePreset.DefaultPreset = SizePreset.MinimalPreset;

            if (args.Any(Directory.Exists))
            {
                Environment.CurrentDirectory = args.First(Directory.Exists);
            }
            
            using (var container = Bootstrapper.Bootstrap())
            {
                var blobManager = container.Resolve<BlobManager>();
                blobManager.InitializeDirectories();
                var executor = container.Resolve<ContractExecutor>();
                var chainState = container.Resolve<ChainState>();
                var server = container.Resolve<ExecutionLayerServer>();

                server.Start();
                await server.Serve();
            }
        }
    }
}