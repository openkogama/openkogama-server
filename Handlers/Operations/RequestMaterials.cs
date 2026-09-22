using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestMaterials : IOperationHandler
{
    public byte Code => (byte)OperationCode.RequestMaterials;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        PhotonDictionary list = PhotonDictionary.Untyped();

        foreach (Material material in Materials.For("2015"))
        {
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)DBQueryKey.MaterialName, material.Name);
            entry.Add((byte)DBQueryKey.MaterialDescription, material.Description);
            entry.Add((byte)DBQueryKey.MaterialPath, material.Path);
            entry.Add((byte)DBQueryKey.MaterialSound, material.Sound);
            entry.Add((byte)DBQueryKey.AvatarModifierPackageType, material.Modifier);
            entry.Add((byte)DBQueryKey.MaterialAnimatorTypeName, material.Animation);
            entry.Add((byte)DBQueryKey.MaterialUnlockPrice, material.PriceGold);
            entry.Add((byte)DBQueryKey.MaterialUnlockPriceSilver, material.PriceSilver);
            entry.Add((byte)DBQueryKey.MaterialUnlocked, true);
            entry.Add((byte)DBQueryKey.MaterialPhysicalProperties, material.Physical);
            list.Add((byte)material.Id, entry);
        }

        OperationResponse response = new(request)
        {
            Parameters = { [(byte)ParameterKey.MaterialList] = list },
        };

        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: sent {list.Entries.Count} materials");
    }
}
