using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class Syncronize(Session session, OperationRouter router, GetNextGameBatch batch) : IOperationHandler
{
    public byte Code => (byte)OperationCode.Syncronize;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not { } player) return;

        var remap = peer.Translator as Kogama.Protocols.OperationRemap;
        if (remap is not null) remap.Pushing = true;
        try
        {
            Push(peer, player);
        }
        finally
        {
            if (remap is not null) remap.Pushing = false;
        }

        Console.WriteLine($"peer {peer.Id}: synchronized");
    }

    void Push(PhotonPeer peer, Player player)
    {
        Run(peer, OperationCode.GetCreditStatus);
        Run(peer, OperationCode.GetDBTimeTicks);
        Run(peer, OperationCode.RequestMaterials, (ParameterKey.ProfileID, player.ProfileId));
        Run(peer, OperationCode.GetItemCategories);
        Run(peer, OperationCode.GetPlanetOwnershipTypes);
        Run(peer, OperationCode.RequestStreamingAssetList);
        Run(peer, OperationCode.RequestStreamingAssetInventory);

        if (player.Mode == GameMode.Edit)
        {
            Send(peer, OperationCode.GetItemInventory, (ParameterKey.Data, GetNextResultSet.Inventory(player.ProfileId, player.ClientVersion)));
            Send(peer, OperationCode.GetItemShopInventory, (ParameterKey.Data, GetNextResultSet.ItemShopInventory()), (ParameterKey.HasMoreResultSets, false));
            Run(peer, OperationCode.GetBuiltInItemBusinessData);
        }

        if (player.Mode == GameMode.CharacterEditor)
        {
            Send(peer, OperationCode.LargeDBQueryAvatarShopInventory, (ParameterKey.Data, GetNextResultSet.AvatarShopInventory()));
            Run(peer, OperationCode.InitializeAvatarEdit);
        }

        Run(peer, OperationCode.CreateGameSnapshot);
        batch.SendWorld(peer, world => Send(peer, OperationCode.GameSnapshotData,
            (ParameterKey.Data, world), (ParameterKey.QueryType, (byte)QueryType.GameWorld), (ParameterKey.QueryDataLeft, false)));
        Run(peer, OperationCode.RequestFriends);
        Run(peer, OperationCode.SetActorReady);

        if (player.Mode == GameMode.CharacterEditor)
            Run(peer, OperationCode.GetActiveAvatar);

        if (peer.Translator is Kogama.Protocols.OperationRemap)
            peer.Send(new EventData((byte)EventCode.SyncronizePing));
    }

    void Run(PhotonPeer peer, OperationCode code, params (ParameterKey Key, object Value)[] parameters)
    {
        var request = new OperationRequest { OperationCode = (byte)code };
        foreach ((ParameterKey key, object value) in parameters) request.Parameters[(byte)key] = value;
        router.Handle(peer, request);
    }

    static void Send(PhotonPeer peer, OperationCode code, params (ParameterKey Key, object Value)[] parameters)
    {
        var response = new OperationResponse((byte)code);
        foreach ((ParameterKey key, object value) in parameters) response.Parameters[(byte)key] = value;
        peer.Send(response);
    }
}
