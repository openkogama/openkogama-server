using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace OpenKogama.Accounts;

public sealed class Mailer(string? apiKey, string from)
{
    const string ResendUrl = "https://api.resend.com/emails";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static Mailer FromEnvironment() => new(
        Environment.GetEnvironmentVariable("OPENKOGAMA_RESEND_KEY"),
        Environment.GetEnvironmentVariable("OPENKOGAMA_MAIL_FROM") ?? "OpenKogama <no-reply@openkogama.org>");

    public async Task SendAsync(string to, CodeMail mail)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine($"mail to {to}: {mail.Subject}");
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ResendUrl)
        {
            Content = JsonContent.Create(new { from, to = new[] { to }, subject = mail.Subject, text = mail.Text, html = mail.Html }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using HttpResponseMessage response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) Console.WriteLine($"mail to {to} failed with {(int)response.StatusCode}");
        }
        catch (HttpRequestException error)
        {
            Console.WriteLine($"mail to {to} failed: {error.Message}");
        }
    }
}
