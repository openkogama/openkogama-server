namespace OpenKogama.Kogama;

public enum OperationCode : byte
{
    SetActorReady = 0,
    DBQuery = 1,
    UpdateWorldObject = 6,
    RequestFriends = 20,
    UpdateWorldObjectRunTimeData = 35,
    UpdateLineOfFire = 37,
    PostGameMsg = 40,
    SetTeam = 41,
    RequestMaterials = 46,
    GetNextGameBatch = 51,
    RequestStreamingAssetList = 52,
    RequestStreamingAssetInventory = 53,
    GetCreditStatus = 62,
    CreateGameSnapshot = 69,
    GetDBTimeTicks = 64,
    Join = 255,
}
