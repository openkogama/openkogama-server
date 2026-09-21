using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class Join : IOperationHandler
{
    public byte Code => (byte)OperationCode.Join;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = (int)peer.Id,
                [(byte)ParameterKey.Username] = "Player",
                [(byte)ParameterKey.PlanetOwnershipType] = 0,
                [(byte)ParameterKey.IsGamePublished] = false,
                [(byte)ParameterKey.GameType] = (int)GameType.Classic,
                [(byte)ParameterKey.ClientSettingFlags] = 0,
                [(byte)ParameterKey.Prices] = PhotonDictionary.Untyped(),
                [(byte)ParameterKey.GameCoinBoosterLeft] = 0,
                [(byte)ParameterKey.MarketPlaceLevel] = 0,
                [(byte)ParameterKey.PublishLevel] = 0,
                [(byte)ParameterKey.Format] = SecurityHelper.Encrypt("openkogama"),
                [(byte)ParameterKey.APIUrl] = "http://127.0.0.1:8080/api/",
                [(byte)ParameterKey.AssetBundleRootUrl] = "http://127.0.0.1:8080/assets/",
                [(byte)ParameterKey.LevelingSilentMode] = true,
            },
        };

        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: joined");
    }
}
