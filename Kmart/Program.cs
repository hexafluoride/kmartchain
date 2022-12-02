using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Kmart.Interfaces;
using Kmart.Qemu;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

                Environment.CurrentDirectory = dataDir;

                // Register chain implementation types as singletons in a lifetime
                using (var chainScope = container.BeginLifetimeScope(QemuBootstrapper.Register))
                {
                    var server = chainScope.Resolve<ExecutionLayerServer>();
                    server.Start();
                    await server.Serve();   
                }
            }
        }
    }
}