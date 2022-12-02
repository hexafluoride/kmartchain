using System;
using System.IO;
using System.Text;

namespace Kmart.Qemu
{
    public struct ContractMessage
    {
        public MessageType Type { get; set; }
        public byte[] Payload { get; set; }

        public ContractMessage(MessageType type, byte[] payload)
        {
            Type = type;
            Payload = payload;
        }

        public static ContractMessage? Consume(Stream stream)
        {
            Span<byte> headerSpan = stackalloc byte[6];
            var read = 0;
            var offset = 0;

            while (offset < 6)
            {
                read = stream.Read(headerSpan.Slice(offset));
                if (read == 0)
                    break;
                
                offset += read;
            }

            if (offset < 6)
                return null;
            // if (read == 0)
            //     return null;
            //
            // if (read != 6)
            //     throw new Exception("Could not read message header");

            var ret = new ContractMessage();
            ret.Type = (MessageType) BitConverter.ToInt16(headerSpan);
            var payloadLength = BitConverter.ToInt32(headerSpan[2..]);

            if (payloadLength > 65536)
            {
                throw new Exception("Payload too long");
            }

            ret.Payload = new byte[payloadLength];
            
            read = 0;
            offset = 0;
            
            while (offset < payloadLength)
            {
                read = stream.Read(ret.Payload, offset, ret.Payload.Length - offset);
                offset += read;
            }
            
            return ret;
        }

        public void Write(Stream stream)
        {
            Span<byte> headerSpan = stackalloc byte[6];
            BitConverter.TryWriteBytes(headerSpan, (short) Type);
            BitConverter.TryWriteBytes(headerSpan.Slice(2), Payload.Length);

            stream.Write(headerSpan);
            stream.Write(Payload, 0, Payload.Length);
        }

        public static ContractMessage ConstructCall(ContractCall call)
        {
            var function = call.Function;
            var calldata = call.CallData;
            
            var functionNameLength = Encoding.UTF8.GetByteCount(function);
            byte[] buffer = new byte[36 + functionNameLength + calldata.Length];
            var bufferSpan = new Span<byte>(buffer);

            Array.Copy(call.Contract, 0, buffer, 0, 20);
            BitConverter.TryWriteBytes(bufferSpan, functionNameLength);
            Encoding.UTF8.GetBytes(function, bufferSpan.Slice(4));
            BitConverter.TryWriteBytes(bufferSpan.Slice(4 + functionNameLength), calldata.Length);
            Array.Copy(calldata, 0, buffer, 8 + functionNameLength, calldata.Length);

            return new ContractMessage(MessageType.Call, buffer);
        }

        public ContractCall? GetCall()
        {
            if (Type != MessageType.Call)
            {
                throw new Exception("Not a call");
            }

            var functionNameLength = BitConverter.ToInt32(Payload, 0);
            if (functionNameLength > Payload.Length - 8)
            {
                throw new Exception("Invalid call");
            }

            var functionName = Payload.AsSpan(4, functionNameLength);
            var calldataLength = BitConverter.ToInt32(Payload, 4 + functionNameLength);

            if (calldataLength > Payload.Length - (8 + functionNameLength))
            {
                throw new Exception("Invalid call");
            }

            var calldata = Payload.AsSpan(4 + functionNameLength, calldataLength);
            var call = new ContractCall()
            {
                Function = Encoding.UTF8.GetString(functionName),
                CallData = calldata.ToArray()
            };

            return call;
        }
    }

    public enum MessageType
    {
        Invalid = 0,
        Ready = 1,
        Return = 2,
        StateRead,
        StateWrite,
        Call,
        CallResult,
        MetadataRead, // for msg.sender etc
        Revert
    }
}