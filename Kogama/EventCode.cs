namespace OpenKogama.Kogama;

public enum EventCode : byte
{
    UnregisterWorldObject = 1,
    UpdateWorldObject = 2,
    UpdateWorldObjectRunTimeData = 31,
    UpdateLineOfFire = 33,
    PostGameMsg = 36,
    SetTeam = 37,
    GetGameBatch = 44,
    GameQueryReady = 45,
    Join = 255,
}
