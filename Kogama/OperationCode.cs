namespace OpenKogama.Kogama;

public enum OperationCode : byte
{
    SetActorReady = 0,
    DBQuery = 1,
    LargeDBQuery = 2,
    GetNextResultSet = 3,
    UpdateWorldObject = 6,
    UpdatePrototype = 12,
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
    GetBuiltInItemBusinessData = 63,
    CreateGameSnapshot = 69,
    GetDBTimeTicks = 64,
    Join = 255,
}
