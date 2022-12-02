using System;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Kmart
{
    public static class Bootstrapper
    {
        public static IContainer Bootstrap(IConfiguration configuration)
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<BlobManager>().AsSelf();
            builder.RegisterType<ContractExecutor>().AsSelf();
            builder.RegisterType<QemuManager>().AsSelf();
            builder.RegisterType<ExecutionLayerServer>().AsSelf();
            builder.RegisterType<FakeEthereumBlockSource>().AsSelf();
            builder.RegisterType<PayloadManager>().AsSelf();
            
            builder.RegisterType<ChainState>().As<IChainState>();
            builder.RegisterType<ChainState>().AsSelf();

            builder.RegisterType<BlockStorage>().As<IBlockStorage>();
            builder.RegisterType<BlockStorage>().AsSelf();

            builder.RegisterInstance<IConfiguration>(configuration);
            builder.RegisterInstance(configuration.Get<KmartConfiguration>());
            
            builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.Register(handler => LoggerFactory.Create(configure =>
            {
                configure.AddSimpleConsole(opt =>
                {
                    opt.SingleLine = true;
                    opt.TimestampFormat = "HH:mm:ss.ffff ";
                });
            })).As<ILoggerFactory>().SingleInstance();
            
            return builder.Build();
        }
    }
}