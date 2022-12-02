using SszSharp;

namespace Kmart;

public interface IPayloadManager
{
    ExecutionPayload? GetPayload(long payloadId);
    IBlock CreateBlockFromPayload(ExecutionPayload payload);
    long CreatePayload(PayloadAttributesV1 attribs);
    void Reset();
}