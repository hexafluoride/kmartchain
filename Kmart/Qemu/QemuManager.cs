using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Kmart.Qemu
{
    public class QemuManager
    {
        private readonly BlobManager BlobManager;
        private readonly ILogger<QemuManager> Logger;
        private readonly ILogger<QemuInstance> InstanceLogger;

        public QemuManager(BlobManager blobManager, ILogger<QemuManager> logger, ILogger<QemuInstance> instanceLogger)
        {
            BlobManager = blobManager ?? throw new ArgumentNullException(nameof(blobManager));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            InstanceLogger = instanceLogger ?? throw new ArgumentNullException(nameof(instanceLogger));
        }

        public QemuInstance? StartReplay(ChainState chainState, ContractInvocationReceipt receipt)
        {
            var tempDir = Path.GetTempFileName();
            // var logFile = Path.GetTempFileName();
            File.Delete(tempDir);
            Directory.CreateDirectory(tempDir);
            
            var imagePath = BlobManager.GetPath(receipt.Call.Contract, BlobManager.ContractImageKey);
            var initrdPath = BlobManager.GetPath(receipt.Call.Contract, BlobManager.ContractInitrdKey);
            var replayPath = $"{tempDir}/replay.bin";
            var serialPath = $"{tempDir}/contract_output";
            var qemuMonitorPath = $"{tempDir}/qemu_monitor";

            // Set up QEMU state
            var contract = chainState.Contracts[receipt.Call.Contract];

            File.WriteAllBytes(replayPath, receipt.ExecutionTrace);

            var qemuBinary = "/usr/bin/qemu-system-x86_64";
            var qemuCommandLine =
                    $"-no-shutdown " +
                    $"-icount shift=0,rr=replay,rrfile={replayPath} " +
                    //$"-icount shift=0,rr=replay,rrfile={replayPath} " +
                    $"-drive file={imagePath},if=none,snapshot=on,id=img-direct " +
                    $"-drive driver=blkreplay,if=none,image=img-direct,id=img-blkreplay " +
                    $"-device ide-hd,drive=img-blkreplay " +
                    // $"-netdev user,id=net1 -device rtl8139,netdev=net1 " +
                    // $"-object filter-replay,id=replay,netdev=net1 " +
                    // $"-chardev socket,id=serial,path={serialPath},server=on,wait=off,logfile={logFile} " +
                    // $"-device isa-serial,baudbase=115200,chardev=serial " +
                    $"-serial unix:{serialPath},server=on,wait=off " +
                    $"-monitor unix:{qemuMonitorPath},server=on,wait=off " +
                    $"-vga std -daemonize " +
                    $"-no-user-config -nographic -nodefaults "
                    //$"-bios /home/kate/repos/seabios/out/bios.bin "
                    // $"-bios /home/kate/repos/qboot/build/bios.bin " +
                    //$"-M microvm "
                ;

            if (contract.BootType == ContractBootType.Multiboot)
            {
                if (new FileInfo(initrdPath).Length > 0)
                {
                    qemuCommandLine += $"-initrd {initrdPath} ";
                }
                
                qemuCommandLine +=
                    $"-kernel {imagePath} ";
            }

            // Console.WriteLine($"Replay log file {logFile}");
            
            var psi = new ProcessStartInfo(qemuBinary, qemuCommandLine);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            var qemuProcess = Process.Start(psi);

            if (qemuProcess == null)
                return null;
            
            // Connect to QEMU monitor
            var stderrOut = qemuProcess.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(stderrOut))
                Logger.LogWarning($"QEMU stderr: {stderrOut}");

            var contractSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            contractSocket.Connect(new UnixDomainSocketEndPoint(serialPath));
            var serialStream = new NetworkStream(contractSocket);
            
            var monitorSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            monitorSocket.Connect(new UnixDomainSocketEndPoint(qemuMonitorPath));
            var monitorStream = new NetworkStream(monitorSocket);

            return new QemuInstance(tempDir, qemuProcess, receipt.Call, serialStream, monitorStream, false, InstanceLogger);
        }
        
        public QemuInstance StartRecord(ChainState chainState, ContractCall call)
        {
            var tempDir = Path.GetTempFileName();
            // var logFile = Path.GetTempFileName();
            File.Delete(tempDir);
            Directory.CreateDirectory(tempDir);

            // Set up QEMU state
            var imagePath = BlobManager.GetPath(call.Contract, BlobManager.ContractImageKey);
            var initrdPath = BlobManager.GetPath(call.Contract, BlobManager.ContractInitrdKey);
            var replayPath = $"{tempDir}/replay.bin";
            var serialPath = $"{tempDir}/contract_interface";
            var qemuMonitorPath = $"{tempDir}/qemu_monitor";
            
            var contract = chainState.Contracts[call.Contract];

            var qemuBinary = "/usr/bin/qemu-system-x86_64";
            var qemuCommandLine =
                $"-no-shutdown " +
                $"-icount shift=0,rr=record,rrfile={replayPath} " +
                //$"-icount shift=auto,rr=record,rrfile={replayPath} " +
                $"-drive file={imagePath},if=none,snapshot=on,id=img-direct " +
                $"-drive driver=blkreplay,if=none,image=img-direct,id=img-blkreplay " +
                $"-device ide-hd,drive=img-blkreplay " +
                //$"-device virtio-blk-device,drive=img-blkreplay " +
                // $"-netdev user,id=net1 -device rtl8139,netdev=net1 " +
                // $"-object filter-replay,id=replay,netdev=net1 " +
                // $"-chardev socket,id=serial,path={serialPath},server=on,wait=off,logfile={logFile} " +
                // $"-device isa-serial,baudbase=115200,chardev=serial " +
                $"-serial unix:{serialPath},server=on,wait=off " +
                //$"-serial file:{serialPath} " +
                $"-monitor unix:{qemuMonitorPath},server=on,wait=off " +
                $"-vga std -daemonize " +
                $"-no-user-config -nodefaults -nographic "
                //$"-bios /home/kate/repos/seabios/out/bios.bin " +
                //$"-M microvm "
                ;
            
            if (contract.BootType == ContractBootType.Multiboot)
            {
                if (new FileInfo(initrdPath).Length > 0)
                {
                    qemuCommandLine += $"-initrd {initrdPath} ";
                }
                
                qemuCommandLine +=
                    $"-kernel {imagePath} ";
            }
            
            var psi = new ProcessStartInfo(qemuBinary, qemuCommandLine);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            var qemuProcess = Process.Start(psi);

            if (qemuProcess == null)
                throw new Exception("Failed to start qemu");

            var stderrOut = qemuProcess.StandardError.ReadToEnd();
            
            if (!string.IsNullOrWhiteSpace(stderrOut))
                Logger.LogWarning($"QEMU stderr: {stderrOut}");
            // Encode and send call information to VM
            var contractSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            contractSocket.Connect(new UnixDomainSocketEndPoint(serialPath));
            // var contractSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // contractSocket.Connect(new IPEndPoint(IPAddress.Loopback, serialPort));
            
            var contractStream = new NetworkStream(contractSocket);
            // var contractStream = File.OpenRead(serialPath);
            
            var monitorSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            monitorSocket.Connect(new UnixDomainSocketEndPoint(qemuMonitorPath));
            var monitorStream = new NetworkStream(monitorSocket);

            return new QemuInstance(tempDir, qemuProcess, call, contractStream, monitorStream, true, InstanceLogger);
        }
    }

    public class QemuInstance
    {
        public ContractCall Call;

        public readonly string StateDirectory;
        public readonly Process Process;
        public readonly Stream SerialPort;
        public readonly Stream Monitor;

        private readonly StreamWriter MonitorWriter;
        private readonly StreamReader MonitorReader;
        private readonly ILogger<QemuInstance> Logger;
        
        public GuestState State { get; set; }
        public ContractMessage? OutstandingRequest;

        private bool GuestReady;
        private bool CanPauseUnpause;

        public QemuInstance(string dir, Process process, ContractCall call, Stream serial, Stream monitor, bool canPauseUnpause, ILogger<QemuInstance> logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            StateDirectory = dir;
            
            Process = process;
            Call = call;
            SerialPort = serial;
            Monitor = monitor;

            MonitorWriter = new StreamWriter(monitor) {AutoFlush = true};
            MonitorReader = new StreamReader(monitor);
            CanPauseUnpause = canPauseUnpause;
        }

        // TODO: Manage this better
        public byte[] GetExecutionTrace() => File.ReadAllBytes($"{StateDirectory}/replay.bin");

        public void Shutdown()
        {
            try
            {
                if (State != GuestState.Done)
                {
                    WriteToMonitor("quit");
                    MonitorReader.ReadToEnd();

                    if (!Process.WaitForExit(500))
                    {
                        Logger.LogDebug("qemu killed");
                        Process.Kill();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to shutdown");
            }

            State = GuestState.Done;
        }
        public void Cleanup()
        {
            Shutdown();
            SerialPort.Dispose();
            Monitor.Dispose();

            if (Directory.Exists(StateDirectory))
            {
                Directory.Delete(StateDirectory, true);
            }
        }

        void WriteToMonitor(string message) => MonitorWriter.WriteLine(message);

        ContractMessage? ConsumeMessage()
        {
            if (!GuestReady)
            {
                return null;
            }

            return ContractMessage.Consume(SerialPort);
        }

        public bool WaitForGuest()
        {
            int preambleCounter = 0;
            while (preambleCounter < 2048)
            {
                int readByte = SerialPort.ReadByte();
                
                // Console.Write((char)readByte);

                if (readByte < 0)
                    return false;
                if (readByte != 0xaa && readByte != 0x55)
                    preambleCounter = 0;
                else
                    preambleCounter++;
            }

            return GuestReady = true;
        }
        
        void PostMessage(ContractMessage message)
        {
            if (SerialPort.CanWrite)
            {
                //Console.WriteLine();
                message.Write(SerialPort);
                SerialPort.Flush();
            }
        }
        
        public void FulfillRequest(ContractMessage response)
        {
            // Post message to guest and resume execution
            response.Write(SerialPort);
            OutstandingRequest = null;
            Resume();
        }

        public bool ProcessMessage()
        {
            var maybeMessage = ConsumeMessage();
            if (maybeMessage is null)
                return false;

            Pause();
            var message = maybeMessage.Value;

            switch (message.Type)
            {
                case MessageType.Ready:
                    Logger.LogDebug($"Ready message received");
                    Resume();
                    PostMessage(ContractMessage.ConstructCall(Call));
                    break;
                case MessageType.MetadataRead:
                case MessageType.Return:
                case MessageType.StateRead:
                case MessageType.StateWrite:
                case MessageType.Call:
                    OutstandingRequest = message;
                    break;
            }

            return true;
        }

        public void Pause()
        {
            if (CanPauseUnpause)
            {
                WriteToMonitor("stop");
                State = GuestState.Paused;
            }
        }

        public void Resume()
        {
            if (CanPauseUnpause)
            {
                WriteToMonitor("cont");
                State = GuestState.Running;
            }
        }

        public ulong GetInstructionCount()
        {
            WriteToMonitor("info replay");

            while (MonitorReader.ReadLine() is { } line)
            {
                if (!line.StartsWith("Recording execution") && !line.StartsWith("Replaying execution"))
                    continue;

                var count = line.Split("instruction count = ")[1];
                return ulong.Parse(count);
            }

            return 0;
        }
    }

    public enum GuestState
    {
        Unknown,
        Running,
        Paused,
        Done,
        Error
    }
}