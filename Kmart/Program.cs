using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using SszSharp;

namespace Kmart
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            SizePreset.DefaultPreset = SizePreset.MinimalPreset;

            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
            using (var container = Bootstrapper.Bootstrap(configuration))
            {
                var logger = container.Resolve<ILogger<Program>>();
                var dataDir = container.Resolve<KmartConfiguration>().DataDirectory;
                var dataDirParent = Path.GetDirectoryName(dataDir);

                if (!Directory.Exists(dataDir) && Directory.Exists(dataDirParent))
                {
                    try
                    {
                        Directory.CreateDirectory(dataDir);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e,
                            $"Configured data directory {dataDir} doesn't exist, exception was thrown while trying to create it");
                        Environment.Exit(1);
                    }
                }
                
                BlobManager.DataDirectory = Environment.CurrentDirectory = dataDir;
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