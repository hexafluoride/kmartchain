using Autofac;
using Kmart.Qemu;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kmart
{
    public static class Bootstrapper
    {
        public static IContainer Bootstrap(IConfiguration configuration)
        {
            var builder = new ContainerBuilder();
            
            builder.RegisterType<ExecutionLayerServer>().AsSelf();
            builder.RegisterType<FakeEthereumBlockSource>().AsSelf();

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