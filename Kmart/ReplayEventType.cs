namespace Kmart;

public enum AsyncEventType
{
    REPLAY_ASYNC_EVENT_BH,
    REPLAY_ASYNC_EVENT_BH_ONESHOT,
    REPLAY_ASYNC_EVENT_INPUT,
    REPLAY_ASYNC_EVENT_INPUT_SYNC,
    REPLAY_ASYNC_EVENT_CHAR_READ,
    REPLAY_ASYNC_EVENT_BLOCK,
    REPLAY_ASYNC_EVENT_NET,
}
public enum ReplayEventType
{
    EVENT_INSTRUCTION = 0,
    EVENT_INTERRUPT = 1,
    EVENT_EXCEPTION = 2,
    REPLAY_ASYNC = 3,
    
    //REPLAY_ASYNC_END = 10,
    EVENT_SHUTDOWN = 4,
    EVENT_SHUTDOWN_BLAH1,
    EVENT_SHUTDOWN_BLAH2,
    EVENT_SHUTDOWN_BLAH3,
    EVENT_SHUTDOWN_BLAH4,
    EVENT_SHUTDOWN_BLAH5,
    EVENT_SHUTDOWN_BLAH6,
    EVENT_SHUTDOWN_BLAH7,
    EVENT_SHUTDOWN_BLAH8,
    EVENT_SHUTDOWN_BLAH9,
    EVENT_SHUTDOWN_LAST = 14,
    EVENT_CHAR_WRITE,
    EVENT_CHAR_READ_ALL,
    EVENT_CHAR_READ_ALL_ERROR,
    EVENT_AUDIO_OUT,
    EVENT_AUDIO_IN,
    EVENT_RANDOM,
    EVENT_CLOCK_HOST,
    EVENT_CLOCK_VIRTUAL_RT,
    CHECKPOINT_CLOCK_WARP_START,
    CHECKPOINT_CLOCK_WARP_ACCOUNT,
    CHECKPOINT_RESET_REQUESTED,
    CHECKPOINT_SUSPEND_REQUESTED,
    CHECKPOINT_CLOCK_VIRTUAL,
    CHECKPOINT_CLOCK_HOST,
    CHECKPOINT_CLOCK_VIRTUAL_RT,
    CHECKPOINT_INIT,
    CHECKPOINT_RESET,
    EVENT_END
    // /*
    //  *     EVENT_INSTRUCTION,
    // /* for software interrupt */
    // EVENT_INTERRUPT,
    // /* for emulated exceptions */
    // EVENT_EXCEPTION,
    // /* for async events */
    // EVENT_ASYNC,
    // EVENT_ASYNC_LAST = EVENT_ASYNC + REPLAY_ASYNC_COUNT - 1,
    // /* for shutdown requests, range allows recovery of ShutdownCause */
    // EVENT_SHUTDOWN,
    // 'none', 'host-error', 'host-qmp-quit', 'host-qmp-system-reset',
    // 'host-signal', 'host-ui', 'guest-shutdown', 'guest-reset',
    // 'guest-panic', 'subsystem-reset'
    // EVENT_SHUTDOWN_LAST = EVENT_SHUTDOWN + SHUTDOWN_CAUSE__MAX,
    // /* for character device write event */
    // EVENT_CHAR_WRITE,
    // /* for character device read all event */
    // EVENT_CHAR_READ_ALL,
    // EVENT_CHAR_READ_ALL_ERROR,
    // /* for audio out event */
    // EVENT_AUDIO_OUT,
    // /* for audio in event */
    // EVENT_AUDIO_IN,
    // /* for random number generator */
    // EVENT_RANDOM,
    // /* for clock read/writes */
    // /* some of greater codes are reserved for clocks */
    // EVENT_CLOCK,
    // EVENT_CLOCK_LAST = EVENT_CLOCK + REPLAY_CLOCK_COUNT - 1,
    // /* for checkpoint event */
    // /* some of greater codes are reserved for checkpoints */
    // EVENT_CHECKPOINT,
    // EVENT_CHECKPOINT_LAST = EVENT_CHECKPOINT + CHECKPOINT_COUNT - 1,
    // /* end of log event */
    // EVENT_END,
    // EVENT_COUNT
     
}

//
// public enum AsyncEventType
// {
//     REPLAY_ASYNC_EVENT_BH,
//     REPLAY_ASYNC_EVENT_BH_ONESHOT,
//     REPLAY_ASYNC_EVENT_INPUT,
//     REPLAY_ASYNC_EVENT_INPUT_SYNC,
//     REPLAY_ASYNC_EVENT_CHAR_READ,
//     REPLAY_ASYNC_EVENT_BLOCK,
//     REPLAY_ASYNC_EVENT_NET,
// }
// public enum ReplayEventType
// {
//     EVENT_INSTRUCTION = 0,
//     EVENT_INTERRUPT = 1,
//     EVENT_EXCEPTION = 2,
//     REPLAY_ASYNC = 3,
//     REPLAY_ASYNC_LAST = 9,
//     
//     //REPLAY_ASYNC_END = 10,
//     EVENT_SHUTDOWN = 10,
//     EVENT_SHUTDOWN_BLAH1,
//     EVENT_SHUTDOWN_BLAH2,
//     EVENT_SHUTDOWN_BLAH3,
//     EVENT_SHUTDOWN_BLAH4,
//     EVENT_SHUTDOWN_BLAH5,
//     EVENT_SHUTDOWN_BLAH6,
//     EVENT_SHUTDOWN_BLAH7,
//     EVENT_SHUTDOWN_BLAH8,
//     EVENT_SHUTDOWN_BLAH9,
//     EVENT_SHUTDOWN_LAST = 20,
//     EVENT_CHAR_WRITE,
//     EVENT_CHAR_READ_ALL,
//     EVENT_CHAR_READ_ALL_ERROR,
//     EVENT_AUDIO_OUT,
//     EVENT_AUDIO_IN,
//     EVENT_RANDOM,
//     EVENT_CLOCK_HOST,
//     EVENT_CLOCK_VIRTUAL_RT,
//     CHECKPOINT_CLOCK_WARP_START,
//     CHECKPOINT_CLOCK_WARP_ACCOUNT,
//     CHECKPOINT_RESET_REQUESTED,
//     CHECKPOINT_SUSPEND_REQUESTED,
//     CHECKPOINT_CLOCK_VIRTUAL,
//     CHECKPOINT_CLOCK_HOST,
//     CHECKPOINT_CLOCK_VIRTUAL_RT,
//     CHECKPOINT_INIT,
//     CHECKPOINT_RESET,
//     EVENT_END
//     // /*
//     //  *     EVENT_INSTRUCTION,
//     // /* for software interrupt */
//     // EVENT_INTERRUPT,
//     // /* for emulated exceptions */
//     // EVENT_EXCEPTION,
//     // /* for async events */
//     // EVENT_ASYNC,
//     // EVENT_ASYNC_LAST = EVENT_ASYNC + REPLAY_ASYNC_COUNT - 1,
//     // /* for shutdown requests, range allows recovery of ShutdownCause */
//     // EVENT_SHUTDOWN,
//     // 'none', 'host-error', 'host-qmp-quit', 'host-qmp-system-reset',
//     // 'host-signal', 'host-ui', 'guest-shutdown', 'guest-reset',
//     // 'guest-panic', 'subsystem-reset'
//     // EVENT_SHUTDOWN_LAST = EVENT_SHUTDOWN + SHUTDOWN_CAUSE__MAX,
//     // /* for character device write event */
//     // EVENT_CHAR_WRITE,
//     // /* for character device read all event */
//     // EVENT_CHAR_READ_ALL,
//     // EVENT_CHAR_READ_ALL_ERROR,
//     // /* for audio out event */
//     // EVENT_AUDIO_OUT,
//     // /* for audio in event */
//     // EVENT_AUDIO_IN,
//     // /* for random number generator */
//     // EVENT_RANDOM,
//     // /* for clock read/writes */
//     // /* some of greater codes are reserved for clocks */
//     // EVENT_CLOCK,
//     // EVENT_CLOCK_LAST = EVENT_CLOCK + REPLAY_CLOCK_COUNT - 1,
//     // /* for checkpoint event */
//     // /* some of greater codes are reserved for checkpoints */
//     // EVENT_CHECKPOINT,
//     // EVENT_CHECKPOINT_LAST = EVENT_CHECKPOINT + CHECKPOINT_COUNT - 1,
//     // /* end of log event */
//     // EVENT_END,
//     // EVENT_COUNT
//      
// }