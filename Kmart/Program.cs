using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Crypto.Parameters;
using SszSharp;

namespace Kmart
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            // var localTraces = Directory.GetFiles(".");
            //
            // foreach (var maybeTrace in localTraces)
            // {
            //     var filename = Path.GetFileName(maybeTrace);
            //
            //     if (filename.StartsWith("trace-") && !filename.EndsWith(".parsed"))
            //     {
            //         ReplayParser.DumpAllEvents(maybeTrace);
            //     }
            // }
            //
            // return;
            
            SizePreset.DefaultPreset = SizePreset.MinimalPreset;

            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
            using (var container = Bootstrapper.Bootstrap(configuration))
            {
                BlobManager.DataDirectory = Environment.CurrentDirectory = container.Resolve<KmartConfiguration>().DataDirectory;
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