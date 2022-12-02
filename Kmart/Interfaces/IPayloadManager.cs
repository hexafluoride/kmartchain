using SszSharp;

namespace Kmart.Interfaces;

public interface IPayloadManager
{
    ExecutionPayload? GetPayload(long payloadId);
    IBlock CreateBlockFromPayload(ExecutionPayload payload);
    long CreatePayload(PayloadAttributesV1 attribs);
    void Reset();
}