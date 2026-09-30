using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestAccessoryData(Session session) : IOperationHandler
{
    const int AccessoryAsset = 2;
    const int HatsCategory = 1;
    const int ParticlesCategory = 2;
    const int BackCategory = 3;

    public byte Code => (byte)OperationCode.RequestAccessoryData;

    static int CategoryOf(StreamingAsset asset) => asset.Category is ParticlesCategory or BackCategory ? asset.Category : HatsCategory;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var accessories = ClientContent.Streaming(peer, session.For(peer)?.Build).Assets.Where(asset => asset.Type == AccessoryAsset).ToList();
        bool shortNames = session.For(peer)?.AccessoryQueries == true;
        var shop = new
        {
            accessoryDatas = accessories.Select((asset, index) => (asset, index)).ToDictionary(entry => entry.asset.Id.ToString(), entry =>
                shortNames ? (object)new
                {
                    aMDID = entry.asset.Id,
                    sAID = entry.asset.Id,
                    iAvlb = true,
                    iNew = false,
                    iFtr = false,
                    cost = 0,
                    dsc = 0,
                    lvl = 0,
                    name = entry.asset.Name,
                    cat = CategoryOf(entry.asset),
                    pos = entry.index,
                    url = entry.asset.Path,
                    owns = true,
                    slot = Accessories.ModernSlotOf(entry.asset),
                    time = new { timeLimit = 0 },
                } : new
                {
                    accessoryMetaDataID = entry.asset.Id,
                    streamingAssetID = entry.asset.Id,
                    isAvailable = true,
                    isNew = false,
                    isFeatured = false,
                    priceGold = 0,
                    discount = 0,
                    level = 0,
                    name = entry.asset.Name,
                    category = CategoryOf(entry.asset),
                    position = entry.index,
                    url = entry.asset.Path,
                    owns = true,
                    accessorySlotType = Accessories.ModernSlotOf(entry.asset),
                    timelimit = new { timeLimit = 0 },
                }),
            accessoryBundle = new
            {
                accessoryBundleID = -1,
                accessoryBundleItems = Array.Empty<object>(),
                discount = 0,
                level = 0,
                name = "",
                isAvailable = false,
                timelimit = new { timeLimit = 0 },
            },
        };
        string json = JsonSerializer.Serialize(shop);
        if (session.For(peer) is { AccessoryQueries: true } player)
        {
            peer.Send(new EventData((byte)EventCode.GetGameBatch)
            {
                Parameters =
                {
                    [(byte)ParameterKey.ActorNr] = player.Actor,
                    [(byte)ParameterKey.Data] = System.Text.Encoding.ASCII.GetBytes(json),
                    [(byte)ParameterKey.QueryType] = (byte)QueryType.AccessoryUserData,
                    [(byte)ParameterKey.QueryId] = GetNextGameBatch.NextQueryId(),
                    [(byte)ParameterKey.QueryDataLeft] = false,
                },
            });
            return;
        }
        peer.Send(new OperationResponse(request) { Parameters = { [(byte)ParameterKey.MetaData] = json } });
    }
}

public sealed class UnEquipAccessory(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UnEquipAccessory;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int bodyId = Convert.ToInt32(request[(byte)ParameterKey.AvatarID]);
        int slot = Accessories.ToLegacySlot(Convert.ToInt32(request[(byte)ParameterKey.Id]));

        Accessories.Change(session, bodyId, worn =>
            worn.RemoveAll(pair => pair.Value is List<(string Key, PackedType Type, object Value)> entry && Accessories.SlotOf(entry) == slot));
        if (session.For(peer) is Player owner)
            Stores.Profiles.ClearAccessorySlot(session.AvatarOfBody(bodyId, owner.ProfileId), slot);

        peer.Send(new OperationResponse(request));
    }
}

public sealed class UpdateAvatarAccessoryScale(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdateAvatarAccessoryScale;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int bodyId = Convert.ToInt32(request[(byte)ParameterKey.AvatarID]);
        int slot = Accessories.ToLegacySlot(Convert.ToInt32(request[(byte)ParameterKey.AvatarAccessorySlot]));
        float scale = Convert.ToSingle(request[(byte)ParameterKey.Scale]);

        Accessories.Change(session, bodyId, worn =>
        {
            foreach ((_, _, object value) in worn)
                if (value is List<(string Key, PackedType Type, object Value)> entry && Accessories.SlotOf(entry) == slot)
                    Accessories.Set(entry, "7", PackedType.Single, scale);
        });
        if (session.For(peer) is Player owner)
            Stores.Profiles.SetAccessoryScale(session.AvatarOfBody(bodyId, owner.ProfileId), slot, scale);
    }
}

public sealed class Acknowledge(OperationCode code) : IOperationHandler
{
    public byte Code => (byte)code;

    public void Handle(PhotonPeer peer, OperationRequest request) => peer.Send(new OperationResponse(request));
}

public sealed class Ignore(OperationCode code) : IOperationHandler
{
    public byte Code => (byte)code;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
    }
}
