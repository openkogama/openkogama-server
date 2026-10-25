using System.Numerics;

namespace OpenKogama.Api;

public sealed record NpcDamage(float Amount, KillReason Reason, IPlayer? By = null, INpc? ByNpc = null, Vector3 Impulse = default);
