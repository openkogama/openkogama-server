using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class Join(Session session) : IOperationHandler
{
    const int SpinEnabled = 0x20;
    const byte Www = 4;
    const string NoBannedTools = """{"ApplicationDescs":[],"applicationDescFactoryBase":{"ApplicationDescs":[]}}""";

    public byte Code => (byte)OperationCode.Join;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player player = session.For(peer) ?? session.Add(peer);
        if (peer.Translator is not Kogama.Protocols.LegacyTranslator)
        {
            player.ClientVersion = peer.Translator is Kogama.Protocols.OperationRemap remap ? remap.Version : Kogama.Protocols.ClientProtocols.NativeVersion(request);
            player.Build = Kogama.Protocols.ClientProtocols.Hint(request) ?? request[(byte)ParameterKey.Version] as string ?? player.ClientVersion;
        }
        if (request[(byte)ParameterKey.GameMode] is { } mode) player.Mode = (GameMode)Convert.ToInt32(mode);
        if (request[(byte)ParameterKey.ClientBuildTarget] is byte target) player.BuildTarget = target;
        if (int.TryParse(request[(byte)ParameterKey.Token] as string, out int profile) && profile > 0)
            player.ProfileId = profile;

        PhotonDictionary prices = PhotonDictionary.Untyped();
        prices.Add("GameCoinBoost", new[] { 0, 0 });

        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Username] = player.Username,
                [(byte)ParameterKey.PlanetOwnershipType] = (int)session.OwnershipOf(player),
                [(byte)ParameterKey.IsGamePublished] = session.Published,
                [(byte)ParameterKey.GameType] = (int)GameType.Classic,
                [(byte)ParameterKey.TeamID] = (int)player.Team,
                [(byte)ParameterKey.ClientSettingFlags] = SpinEnabled,
                [(byte)ParameterKey.Prices] = prices,
                [(byte)ParameterKey.GameCoinBoosterLeft] = CoinBoost.Left(player),
                [(byte)ParameterKey.MarketPlaceLevel] = 0,
                [(byte)ParameterKey.PublishLevel] = 0,
                [(byte)ParameterKey.Format] = SecurityHelper.Encrypt("openkogama"),
                [(byte)ParameterKey.APIUrl] = "http://127.0.0.1:8080/api/",
                [(byte)ParameterKey.AssetBundleRootUrl] = peer.Translator is Kogama.Protocols.LegacyTranslator ? "http://127.0.0.1:8080/bundles/" : BundleSets.Url(player.Build),
                [(byte)ParameterKey.LevelingSilentMode] = false,
                [(byte)ParameterKey.SentryUrl] = "http://openkogama:openkogama@127.0.0.1:8080/sentry/1",
                [(byte)ParameterKey.AntiCheatData] = NoBannedTools,
                [(byte)ParameterKey.IsAdmin] = false,
                [(byte)ParameterKey.Region] = Www,
                [(byte)ParameterKey.ThemesEnabled] = true,
            },
        };

        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: joined");
    }
}
