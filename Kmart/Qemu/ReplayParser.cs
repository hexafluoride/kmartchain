using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kmart.Qemu;

public enum ReplayMessageDirection
{
    Invalid,
    HostToGuest,
    GuestToHost
}

public class ReplayedMessage
{
    public ulong StartInstruction;
    public ulong EndInstruction;
    public byte[] Contents = Array.Empty<byte>();
    public ReplayMessageDirection Direction;

    public override string ToString()
    {
        if (Direction == ReplayMessageDirection.GuestToHost)
        {
            return $"{Direction} message took {EndInstruction - StartInstruction} instructions (start {StartInstruction}, end {EndInstruction})";
        }

        return $"{Direction} message {Contents.Length} bytes, took {EndInstruction - StartInstruction} instructions (start {StartInstruction}, end {EndInstruction})";
    }
}

public class ReplayParser
{
    private readonly Stream File;
    
    public ReplayParser(Stream stream)
    {
        File = stream;
        Init();
    }

    public static void DumpAllEvents(string path)
    {
        using var fs = System.IO.File.OpenRead(path);
        using var output = new StreamWriter($"{path}.parsed", false) { AutoFlush = true };
        var parser = new ReplayParser(fs);
        parser.Init();

        ReplayEvent? nextEvent;
        var position = fs.Position;
        while ((nextEvent = parser.ParseNextEvent()) is not null)
        {
            if (nextEvent.Type == ReplayEventType.REPLAY_ASYNC)
            {
                output.WriteLine($"Read event {(AsyncEventType)nextEvent.Contents[1][0]} (contents {nextEvent.Contents.Last().ToPrettyString()}) at {position:x8}");
            }
            else if (nextEvent.Type == ReplayEventType.EVENT_INSTRUCTION)
            {
                output.WriteLine($"Read event {nextEvent.Type} at {position:x8} (instruction {BitConverter.ToUInt32(nextEvent.Contents[0].Reverse().ToArray())})");
            }
            else
            {
                output.WriteLine($"Read event {nextEvent.Type} at {position:x8}");
                
            }
            position = fs.Position;
        }
        
        Console.WriteLine($"Done with {path}");
    }

    byte[] ReadBytes(int n)
    {
        var ret = new byte[n];
        var offset = 0;

        while (offset < n)
        {
            offset += File.Read(ret, offset, ret.Length - offset);
        }

        return ret;
    }

    byte[] ReadArray()
    {
        var lenbytes = ReadBytes(4);
        Array.Reverse(lenbytes);
        var len = BitConverter.ToInt32(lenbytes);
        return ReadBytes(len);
    }

    public void Init()
    {
        var version = ReadBytes(4);
        // Console.WriteLine($"Replay file version {version.ToPrettyString()}");
        var reserved = ReadBytes(8);

        if (!reserved.All(b => b == 0))
        {
            throw new Exception("Reserved field not empty");
        }
    }

    public IEnumerable<ReplayedMessage> ParseAllMessages()
    {
        var currentBuf = new MemoryStream();
        long startIP = -1;
        long currentIP = -1;
        long lastByteIP = -1;
        
        ReplayMessageDirection currentDirection = ReplayMessageDirection.Invalid;

        ReplayedMessage ConsumeCurrent()
        {
            var msg = new ReplayedMessage()
            {
                Contents = currentBuf.ToArray(),
                StartInstruction = (ulong) startIP,
                EndInstruction = (ulong) lastByteIP,
                Direction = currentDirection
            };

            startIP = -1;
            lastByteIP = -1;
            currentDirection = ReplayMessageDirection.Invalid;
            currentBuf.Dispose();

            return msg;
        }

        void InitializeMessage(ReplayMessageDirection direction)
        {
            currentDirection = direction;
            startIP = currentIP;
            currentBuf = new MemoryStream();
        }
        
        ReplayEvent? nextEvent;
        while ((nextEvent = ParseNextEvent()) is not null)
        {
            if (nextEvent.Type == ReplayEventType.REPLAY_ASYNC)
            {
                var asyncEventType = (AsyncEventType)nextEvent.Contents[1][0];
                if (asyncEventType == AsyncEventType.REPLAY_ASYNC_EVENT_CHAR_READ)
                {
                    if (currentDirection == ReplayMessageDirection.GuestToHost)
                    {
                        yield return ConsumeCurrent();
                    }

                    if (currentDirection == ReplayMessageDirection.Invalid)
                    {
                        InitializeMessage(ReplayMessageDirection.HostToGuest);
                    }
                    
                    currentBuf.Write(nextEvent.Contents.Last());
                    lastByteIP = currentIP;
                }
            }
            else if (nextEvent.Type == ReplayEventType.EVENT_INSTRUCTION)
            {
                currentIP += BitConverter.ToUInt32(nextEvent.Contents[0].Reverse().ToArray());
            }
            else if (nextEvent.Type == ReplayEventType.EVENT_CHAR_WRITE)
            {
                if (currentDirection == ReplayMessageDirection.HostToGuest)
                {
                    yield return ConsumeCurrent();
                }

                if (currentDirection == ReplayMessageDirection.Invalid)
                {
                    InitializeMessage(ReplayMessageDirection.GuestToHost);
                }
                
                lastByteIP = currentIP;
            }
        }

        if (currentDirection != ReplayMessageDirection.Invalid)
        {
            yield return ConsumeCurrent();
        }
    }

    public ReplayEvent? ParseNextEvent()
    {
        if (File.Length == File.Position)
        {
            return null;
        }
        
        var nextType = (ReplayEventType)File.ReadByte();
        var ret = new ReplayEvent() {Type = nextType};

        AsyncEventType? subtype = null;
        
        if (!Enum.IsDefined<ReplayEventType>(nextType))
        {
            throw new Exception($"Unidentified type {nextType} at position {File.Position - 1}");
        }

        switch (nextType)
        {
            case ReplayEventType.EVENT_INSTRUCTION:
                ret.Contents.Add(ReadBytes(4));
                break;
            case ReplayEventType.EVENT_INTERRUPT:
            case ReplayEventType.EVENT_EXCEPTION:
                break;
            case ReplayEventType.REPLAY_ASYNC:
                ret.Contents.Add(ReadBytes(1));
                
                if (subtype is null)
                    subtype = (AsyncEventType)File.ReadByte();
                if (!Enum.IsDefined<AsyncEventType>(subtype.Value))
                {
                    throw new Exception($"Undefined async subtype {subtype} at location {File.Position}");
                }
                
                ret.Contents.Add(new byte[] { (byte)subtype.Value });
                
                switch (subtype)
                {
                    case AsyncEventType.REPLAY_ASYNC_EVENT_INPUT_SYNC:
                        break;
                    case AsyncEventType.REPLAY_ASYNC_EVENT_BH:
                    case AsyncEventType.REPLAY_ASYNC_EVENT_BH_ONESHOT:
                        ret.Contents.Add(ReadBytes(8));
                        break;
                    case AsyncEventType.REPLAY_ASYNC_EVENT_CHAR_READ:
                        ret.Contents.Add(ReadBytes(1));
                        ret.Contents.Add(ReadArray());
                        break;
                    case AsyncEventType.REPLAY_ASYNC_EVENT_BLOCK:
                        ret.Contents.Add(ReadBytes(8));
                        break;
                }
                break;
            case ReplayEventType.EVENT_CHAR_WRITE:
                ret.Contents.Add(ReadBytes(4));
                ret.Contents.Add(ReadBytes(4));
                break;
            case ReplayEventType.EVENT_CHAR_READ_ALL:
                ret.Contents.Add(ReadArray());
                break;
            case ReplayEventType.EVENT_CHAR_READ_ALL_ERROR:
                ret.Contents.Add(ReadBytes(4));
                break;
            case ReplayEventType.EVENT_CLOCK_HOST:
            case ReplayEventType.EVENT_CLOCK_VIRTUAL_RT:
                ret.Contents.Add(ReadBytes(8));
                break;
            default:
                break;
        }

        return ret;
    }
}

public class ReplayEvent
{
    public ReplayEventType Type { get; set; }
    public List<byte[]> Contents { get; set; } = new();
}