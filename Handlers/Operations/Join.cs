using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class Join(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.Join;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player player = session.For(peer) ?? session.Add(peer);
        if (peer.Translator is null) player.ClientVersion = Kogama.Protocols.ClientProtocols.NativeVersion(request);
        if (int.TryParse(request[(byte)ParameterKey.Token] as string, out int profile) && profile > 0)
            player.ProfileId = profile;

        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Username] = player.Username,
                [(byte)ParameterKey.PlanetOwnershipType] = (int)session.OwnershipOf(player),
                [(byte)ParameterKey.IsGamePublished] = session.Published,
                [(byte)ParameterKey.GameType] = (int)GameType.Classic,
                [(byte)ParameterKey.ClientSettingFlags] = 0,
                [(byte)ParameterKey.Prices] = PhotonDictionary.Untyped(),
                [(byte)ParameterKey.GameCoinBoosterLeft] = 0,
                [(byte)ParameterKey.MarketPlaceLevel] = 0,
                [(byte)ParameterKey.PublishLevel] = 0,
                [(byte)ParameterKey.Format] = SecurityHelper.Encrypt("openkogama"),
                [(byte)ParameterKey.APIUrl] = "http://127.0.0.1:8080/api/",
                [(byte)ParameterKey.AssetBundleRootUrl] = "http://127.0.0.1:8080/bundles/",
                [(byte)ParameterKey.LevelingSilentMode] = false,
            },
        };

        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: joined");
    }
}
