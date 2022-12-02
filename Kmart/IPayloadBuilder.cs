using SszSharp;

namespace Kmart;

public interface IPayloadBuilder
{
    // public long StartBuilding(ExecutionPayload)
}

// Data structure: 
// - committed state: flat map of finalized state from ~256 blocks ago
// - hot state slices: 256 thin slices containing state diff of each new block in the past 256 blocks
// - latest state map: key -> slice index map for O(1) lookups
//
// fast, cheap copy: a state transition can create a new state object with 1 slice + 1 state map alloc
 