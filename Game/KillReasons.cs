namespace OpenKogama.Game;

public static class KillReasons
{
    static readonly Version Renumbered = new(2, 6);
    static readonly Version WithRayHealGun = new(2, 30, 46);
    static readonly Version WithCustomGun = new(3, 1, 1);
    const byte SlapGun = 21;
    const byte RayHealGun = 22;
    const byte CustomGun = 23;

    public static byte ToClient(KilledBy reason, string build)
    {
        if (!Version.TryParse(build, out Version? version) || version < Renumbered)
            return reason is KilledBy.RayHealGun or KilledBy.CustomGun ? (byte)KilledBy.None : (byte)reason;

        return reason switch
        {
            <= KilledBy.MultiThrowingStar => (byte)reason,
            KilledBy.SlapGun => SlapGun,
            KilledBy.RayHealGun when version >= WithRayHealGun => RayHealGun,
            KilledBy.CustomGun when version >= WithCustomGun => CustomGun,
            _ => (byte)KilledBy.None,
        };
    }

    public static KilledBy FromClient(byte value, string build)
    {
        if (!Version.TryParse(build, out Version? version) || version < Renumbered)
            return value <= (byte)KilledBy.SlapGun ? (KilledBy)value : KilledBy.None;

        return value switch
        {
            <= (byte)KilledBy.MultiThrowingStar => (KilledBy)value,
            SlapGun => KilledBy.SlapGun,
            RayHealGun when version >= WithRayHealGun => KilledBy.RayHealGun,
            CustomGun when version >= WithCustomGun => KilledBy.CustomGun,
            _ => KilledBy.None,
        };
    }
}
