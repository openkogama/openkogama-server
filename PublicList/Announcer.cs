using System.Net.Http.Json;

namespace OpenKogama.PublicList;

public static class Announcer
{
    public const string DefaultList = "https://list.openkogama.org";
    const int EveryMs = 20_000;
    const int GoodbyeMs = 2_000;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static string? _target;
    static Func<object>? _goodbye;

    public static void Start(string list, Func<object> body, Func<object> goodbye)
    {
        _target = list.TrimEnd('/') + "/announce";
        _goodbye = goodbye;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using HttpResponseMessage response = await Http.PostAsJsonAsync(_target, body());
                    if (!response.IsSuccessStatusCode) Console.WriteLine($"server list answered {(int)response.StatusCode}");
                }
                catch (Exception error)
                {
                    Console.WriteLine($"server list unreachable: {error.Message}");
                }
                await Task.Delay(EveryMs);
            }
        });
    }

    public static void Leave()
    {
        if (_target is null || _goodbye is null) return;
        try
        {
            Http.PostAsJsonAsync(_target, _goodbye()).Wait(GoodbyeMs);
        }
        catch (Exception)
        {
        }
    }
}
