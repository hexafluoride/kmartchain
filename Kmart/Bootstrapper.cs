using System;
using Autofac;
using Microsoft.Extensions.Logging;

namespace Kmart
{
    public static class Bootstrapper
    {
        public static IContainer Bootstrap()
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<BlobManager>().AsSelf();
            builder.RegisterType<BlockStorage>().AsSelf();
            builder.RegisterType<ContractExecutor>().AsSelf();
            builder.RegisterType<ChainState>().AsSelf();
            builder.RegisterType<QemuManager>().AsSelf();
            builder.RegisterType<ExecutionLayerServer>().AsSelf();
            
            builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.Register(handler => LoggerFactory.Create(configure =>
            {
                configure.AddConsole();
            })).As<ILoggerFactory>().SingleInstance();
            
            return builder.Build();
        }
    }
}