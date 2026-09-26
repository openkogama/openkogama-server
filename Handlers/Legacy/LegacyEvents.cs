using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Legacy;

public static class LegacyEvents
{
    enum WinningState { NoWinner, OneWinner }
    enum WinningCondition { ReachTheFlagFirst, MostKills, FindAllCollectibles, HighestAltitude, LowestAltitude }

    public static EventData WinnerReport(LegacyTranslator protocol, WinnerReport report)
    {
        WinningCondition condition = report.Condition switch
        {
            GameStatCounterType.Flag or GameStatCounterType.FlagCaptured => WinningCondition.ReachTheFlagFirst,
            GameStatCounterType.Collectible => WinningCondition.FindAllCollectibles,
            GameStatCounterType.YUp => WinningCondition.HighestAltitude,
            GameStatCounterType.YDown => WinningCondition.LowestAltitude,
            _ => WinningCondition.MostKills,
        };

        int[] winners = report.Winner is { } winner ? [report.TeamGame ? (int)winner.Team : winner.Actor] : [];
        int[] scores = report.Winner is { } best ? [best.Score] : [];

        return new EventData(protocol.Event("PostWinnerReport"))
        {
            Parameters =
            {
                [protocol.Key("WinningState")] = (int)(winners.Length > 0 ? WinningState.OneWinner : WinningState.NoWinner),
                [protocol.Key("WinningType")] = (int)condition,
                [protocol.Key("WinnerListActors")] = winners,
                [protocol.Key("WinnerListTimes")] = scores,
            },
        };
    }

    public static EventData? Rewrite(EventData data, LegacyTranslator protocol)
    {
        if (data.Code == (byte)EventCode.PostGameMsg
            && Convert.ToInt32(data[(byte)ParameterKey.GameMsgType] ?? -1) == SendChatMsg.ChatMessage
            && PhotonValues.Table(data[(byte)ParameterKey.GameMsgData]) is { } message
            && protocol.HasEvent("ChatMsgEvent"))
        {
            return new EventData(protocol.Event("ChatMsgEvent"))
            {
                Parameters =
                {
                    [protocol.Key("ActorNr")] = Convert.ToInt32(message.GetValueOrDefault(SendChatMsg.Sender) ?? 0),
                    [protocol.Key("ChatMsg")] = message.GetValueOrDefault(SendChatMsg.Text) as string ?? "",
                },
            };
        }

        return null;
    }
}
