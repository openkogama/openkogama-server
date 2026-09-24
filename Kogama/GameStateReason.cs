namespace OpenKogama.Kogama;

public enum GameStateReason
{
    None,
    Timeout,
    FlagReached,
    AllCollectiblesFound,
    GameHasExceededMaxRuntimeEvents,
}

public enum GameStatCounterType : byte
{
    None,
    Kill,
    YUp,
    YDown,
    Flag,
    Collectible,
    Time,
    FlagCaptured,
    OculusKill,
    GameCoin,
}
