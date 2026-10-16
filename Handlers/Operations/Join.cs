using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Web;

namespace OpenKogama.Handlers.Operations;

public sealed class Join(Session session) : IOperationHandler
{
    const int OldSpinEnabled = 0x20;
    const int SpinEnabled = 0x10;
    static readonly Version MovedFlagsVersion = new(1, 74);
    const byte Www = 4;
    internal static readonly int[] AllPermissions = [14, 15, 16, 17, 18, 19, 20, 22];
    const string NoBannedTools = """{"ApplicationDescs":[],"applicationDescFactoryBase":{"ApplicationDescs":[]}}""";

    public byte Code => (byte)OperationCode.Join;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player player = session.For(peer) ?? session.Add(peer);
        if (peer.Translator is not Kogama.Protocols.LegacyTranslator)
        {
            player.ClientVersion = peer.Translator is Kogama.Protocols.OperationRemap remap ? remap.Version : Kogama.Protocols.ClientProtocols.NativeVersion(request);
            string? hint = Kogama.Protocols.ClientProtocols.Hint(request)?.Split('@')[0];
            string? reported = request[(byte)ParameterKey.Version] as string;
            player.Build = hint is not null && reported?.StartsWith(hint + ".") == true ? reported : hint ?? reported ?? player.ClientVersion;
        }
        if (request[(byte)ParameterKey.GameMode] is { } mode) player.Mode = (GameMode)Convert.ToInt32(mode);
        if (request[(byte)ParameterKey.ClientBuildTarget] is byte target) player.BuildTarget = target;
        (int profile, string? nickname) = SessionToken.Parse(request[(byte)ParameterKey.Token] as string);
        if (profile > 0) player.ProfileId = profile;
        nickname ??= profile > 0 ? Storage.Stores.Profiles.Name(profile) : null;
        if (nickname is not null)
            player.Nickname = session.Players.Any(other => other != player && other.Username == nickname) ? $"{nickname} {player.Actor}" : nickname;
        player.Unity = ClientEngines.Of(player.ProfileId);
        if (Bans.Holds(player))
        {
            Console.WriteLine($"peer {peer.Id}: {player.Username} is banned");
            peer.Disconnect();
            return;
        }
        if (player.SpawnRoles && player.Mode is GameMode.Edit or GameMode.CharacterEditor) session.AddBuildAvatar(player);

        PhotonDictionary prices = PhotonDictionary.Untyped();
        prices.Add("GameCoinBoost", new[] { 0, 0 });

        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Username] = player.Username,
                [(byte)ParameterKey.PlanetOwnershipType] = (int)session.OwnershipOf(player),
                [(byte)ParameterKey.PlanetPermissions] = session.OwnershipOf(player) == PlanetOwnership.None ? Array.Empty<int>() : AllPermissions,
                [(byte)ParameterKey.IsGamePublished] = session.Published,
                [(byte)ParameterKey.GameType] = (int)GameType.Classic,
                [(byte)ParameterKey.TeamID] = (int)player.Team,
                [(byte)ParameterKey.ClientSettingFlags] = Version.TryParse(player.Build, out Version? build) && build >= MovedFlagsVersion ? SpinEnabled : OldSpinEnabled,
                [(byte)ParameterKey.Prices] = prices,
                [(byte)ParameterKey.GameCoinBoosterLeft] = CoinBoost.Left(player),
                [(byte)ParameterKey.MarketPlaceLevel] = 0,
                [(byte)ParameterKey.PublishLevel] = 0,
                [(byte)ParameterKey.Format] = SecurityHelper.Encrypt("openkogama"),
                [(byte)ParameterKey.APIUrl] = $"{SessionLocator.UrlFor(player.ProfileId)}/api/",
                [(byte)ParameterKey.AssetBundleRootUrl] = peer.Translator is Kogama.Protocols.LegacyTranslator ? $"{SessionLocator.BundlesFor(player.ProfileId)}/bundles/" : BundleSets.Url(player.Build, peer.IsWebSocket, player.Unity, SessionLocator.BundlesFor(player.ProfileId)),
                [(byte)ParameterKey.LevelingSilentMode] = false,
                [(byte)ParameterKey.SentryUrl] = SessionLocator.UrlFor(player.ProfileId).Replace("://", "://openkogama:openkogama@") + "/sentry/1",
                [(byte)ParameterKey.AntiCheatData] = NoBannedTools,
                [(byte)ParameterKey.IsAdmin] = Plugins.PluginHost.Admin(player),
                [(byte)ParameterKey.Region] = Www,
                [(byte)ParameterKey.ThemesEnabled] = true,
                [(byte)ParameterKey.GoldAmount] = Storage.Stores.Profiles.Gold(player.ProfileId),
                [(byte)ParameterKey.EnableInterstitialAds] = false,
                [(byte)ParameterKey.EnableRewardedAds] = false,
                [(byte)ParameterKey.PostGameInterstitialIntervalInSeconds] = 0,
                [(byte)ParameterKey.UserProfileData] = player.ProfileData(),
                [(byte)ParameterKey.AdConsentEndpointURL] = "",
                [(byte)ParameterKey.KogamaMainpageURL] = "",
                [(byte)ParameterKey.TouristPromotionCreyFrequencyPercent] = 0,
                [(byte)ParameterKey.TouristPromotionCreyURL] = "",
                [(byte)ParameterKey.TouristPromotionCreyRedirectUser] = false,
                [(byte)ParameterKey.ElitePromotionEnabled] = false,
                [(byte)ParameterKey.ElitePromotionInterval] = 0,
                [(byte)ParameterKey.ReviveEnabledFlags] = 0,
                [(byte)ParameterKey.AdTimeoutAsSuccessWebGL] = false,
                [(byte)ParameterKey.AdTimeoutAsSuccessDelayWebGL] = 0,
                [(byte)ParameterKey.InterstitialTimeoutAfterRewardedAdWebGL] = 0,
                [(byte)ParameterKey.CustomTouristPromotionRedirectToURL] = false,
                [(byte)ParameterKey.CustomTouristPromotionURL] = "",
                [(byte)ParameterKey.CustomTouristPromotionAssetURL] = "",
                [(byte)ParameterKey.CustomTouristPromotionFrequencyPercent] = 0,
            },
        };

        if (peer.IsWebSocket) response.Parameters[(byte)ParameterKey.AssetBundleRootUrlWebGL] = BundleSets.Url(player.Build, webgl: true, player.Unity, SessionLocator.BundlesFor(player.ProfileId));
        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: joined");
    }
}
