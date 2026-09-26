using OpenKogama.Kogama;

namespace OpenKogama.Game;

public sealed record WinnerReport(GameStatCounterType Condition, (int Actor, Team Team, int Score)? Winner, bool TeamGame);
