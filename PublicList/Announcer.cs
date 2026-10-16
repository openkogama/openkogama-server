using System.Net.Http.Json;

namespace OpenKogama.PublicList;

public static class Announcer
{
    public const string DefaultList = "https://list.openkogama.org";
    const int EveryMs = 60_000;

    public static void Start(string list, Func<object> body)
    {
        _ = Task.Run(async () =>
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            string target = list.TrimEnd('/') + "/announce";
            while (true)
            {
                try
                {
                    using HttpResponseMessage response = await http.PostAsJsonAsync(target, body());
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
}
