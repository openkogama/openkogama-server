using OpenKogama.Kogama;

namespace OpenKogama.Game;

public static class StatCounters
{
    static readonly GameStatCounterType[] WithoutAltitude =
    [
        GameStatCounterType.None, GameStatCounterType.Kill, GameStatCounterType.Flag, GameStatCounterType.Collectible,
        GameStatCounterType.Time, GameStatCounterType.FlagCaptured, GameStatCounterType.OculusKill, GameStatCounterType.GameCoin,
    ];

    public static bool HasAltitude(string clientVersion) =>
        !Version.TryParse(clientVersion, out Version? version)
        || version.Major == 1 && version.Minor < 85 && version.Minor != 83;

    public static byte? ToClient(GameStatCounterType type, string clientVersion)
    {
        if (HasAltitude(clientVersion)) return (byte)type;
        int code = Array.IndexOf(WithoutAltitude, type);
        return code < 0 ? null : (byte)code;
    }
}
