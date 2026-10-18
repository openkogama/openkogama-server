using System.Text.Json.Serialization;

public record SessionData(string ServerIP, int ProfileID, int PlanetID, GameMode GameMode, string Language, bool Embedded, string Token, string PingURL, string DisconnectURL,
    string GameRewardDataURL,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PlanetName = null);