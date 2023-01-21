using Autofac;
using Kmart.Interfaces;

namespace Kmart.Qemu;

public static class QemuBootstrapper
{
    public static void Register(ContainerBuilder builder)
    {
        builder.RegisterType<BlobManager>().AsSelf();
        builder.RegisterType<ContractExecutor>().AsSelf();
        builder.RegisterType<QemuManager>().AsSelf();
        
        builder.RegisterType<ChainState>().As<IChainState>().AsSelf().SingleInstance();
        builder.RegisterType<PayloadManager>().As<IPayloadManager>().AsSelf().SingleInstance();
        builder.RegisterType<BlockStorage>().As<IBlockStorage>().AsSelf().SingleInstance();

        builder.RegisterType<QemuRpcProvider>().As<IRpcProvider>().AsSelf().SingleInstance();
    }
}