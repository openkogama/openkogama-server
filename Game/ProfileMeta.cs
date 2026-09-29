using System.Collections;
using System.Text.Json;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Game;

public static class ProfileMeta
{
    const int EventCount = 256;
    const string NoHighlights = "{}";

    public static void Send(Player player)
    {
        if (player.Peer.Translator is not Kogama.Protocols.OperationRemap remap || !remap.Knows(EventCode.GetProfileMetaData)) return;

        string json = JsonSerializer.Serialize(new
        {
            FirstTimeState = new { ByteArray = Convert.ToBase64String(State(player.ProfileId)) },
            TestData = new Dictionary<string, string>(),
        });
        player.Peer.Send(new EventData((byte)EventCode.GetProfileMetaData)
        {
            Parameters =
            {
                [(byte)ParameterKey.Bool] = true,
                [(byte)ParameterKey.MetaData] = json,
                [(byte)ParameterKey.Data] = NoHighlights,
            },
        });
    }

    public static void Set(int profile, int firstTimeEvent, bool value)
    {
        if (firstTimeEvent < 0) return;
        var bits = new BitArray(State(profile));
        if (bits.Length <= firstTimeEvent) bits.Length = firstTimeEvent + 1;
        bits[firstTimeEvent] = value;
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        bits.CopyTo(bytes, 0);
        Stores.Profiles.SetFirstTime(profile, bytes);
    }

    public static void Reset(int profile, bool value) =>
        Stores.Profiles.SetFirstTime(profile, Filled(value));

    static byte[] State(int profile) => Stores.Profiles.FirstTime(profile) ?? Filled(true);

    static byte[] Filled(bool value) => Enumerable.Repeat(value ? (byte)0xFF : (byte)0, EventCount / 8).ToArray();
}
