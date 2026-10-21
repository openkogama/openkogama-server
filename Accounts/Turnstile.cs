using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenKogama.Accounts;

public static class Turnstile
{
    const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    const int MaxToken = 2048;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static Func<string?, string, Task<bool>>? FromEnvironment() =>
        Environment.GetEnvironmentVariable("OPENKOGAMA_TURNSTILE_SECRET") is { Length: > 0 } secret
            ? (token, source) => CheckAsync(secret, token, source)
            : null;

    static async Task<bool> CheckAsync(string secret, string? token, string source)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxToken) return false;
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["secret"] = secret, ["response"] = token, ["remoteip"] = source });
            using HttpResponseMessage response = await Http.PostAsync(VerifyUrl, form);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())?["success"]?.GetValue<bool>() == true;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Console.WriteLine($"turnstile: {error.Message}");
            return false;
        }
    }
}
