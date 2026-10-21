using System.Buffers.Text;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using OpenKogama.P2p;

namespace OpenKogama.Accounts;

public sealed record Reply(int Status, JsonObject Body);

sealed record Handoff(Account Account, string Challenge, long Expires);

public sealed partial class AccountService(AccountStore store, ECDsa key, Func<string, CodeMail, Task> send)
{
    const int MaxBody = 4096;
    const int MinPassword = 8;
    const int MaxPassword = 200;
    const int MaxEmail = 254;
    const int MaxServer = 200;
    const int ChallengeSize = 43;
    const int MaxVerifier = 128;
    const int HandoffSize = 24;
    const int MaxTracked = 10_000;
    const int SqliteConstraint = 19;
    const string Verify = "verify";
    const string Reset = "reset";
    static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(15);
    static readonly TimeSpan UnverifiedLife = TimeSpan.FromHours(24);
    static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    static readonly TimeSpan HandoffLife = TimeSpan.FromMinutes(5);
    static readonly Lazy<string> Decoy = new(() => Passwords.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))));

    readonly Dictionary<string, Queue<long>> _attempts = [];
    readonly Dictionary<string, Handoff> _handoffs = [];

    public HashSet<string> Origins { get; init; } = ["https://openkogama.org", "https://www.openkogama.org"];
    public Func<string?, string, Task<bool>>? Human { get; init; }

    public static int Run(string[] options)
    {
        int port = int.TryParse(Tunnel.Option(options, "port"), out int given) ? given : 8098;
        var store = new AccountStore(Tunnel.Option(options, "db") ?? "accounts.db");
        ECDsa key = Tickets.LoadOrCreate(Tunnel.Option(options, "key") ?? "accounts.key");
        var service = new AccountService(store, key, Mailer.FromEnvironment().SendAsync) { Human = Turnstile.FromEnvironment() };
        foreach (string origin in (Tunnel.Option(options, "origins") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            service.Origins.Add(origin);
        service.Serve(port);
        return 0;
    }

    void Serve(int port)
    {
        var listener = new HttpListener { Prefixes = { $"http://127.0.0.1:{port}/" } };
        listener.Start();
        Console.WriteLine($"accounts {port}");
        while (true)
        {
            HttpListenerContext context = listener.GetContext();
            _ = Task.Run(() => ServeAsync(context));
        }
    }

    async Task ServeAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        if (request.Headers["Origin"] is string origin && Origins.Contains(origin))
        {
            response.AddHeader("Access-Control-Allow-Origin", origin);
            response.AddHeader("Vary", "Origin");
        }
        if (request.HttpMethod == "OPTIONS")
        {
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
            response.AddHeader("Access-Control-Max-Age", "600");
            response.StatusCode = 204;
            response.Close();
            return;
        }

        Reply reply;
        try
        {
            JsonObject? body = request.HttpMethod == "POST" ? await BodyAsync(request) : null;
            reply = await HandleAsync(request.HttpMethod, request.Url?.AbsolutePath.TrimEnd('/') ?? "", body, SourceOf(request));
        }
        catch (JsonException)
        {
            reply = Error(400, "bad request");
        }

        byte[] data = Encoding.UTF8.GetBytes(reply.Body.ToJsonString());
        response.StatusCode = reply.Status;
        response.ContentType = "application/json";
        response.ContentLength64 = data.Length;
        await response.OutputStream.WriteAsync(data);
        response.Close();
    }

    public async Task<Reply> HandleAsync(string method, string path, JsonObject? body, string source) => (method, path) switch
    {
        ("GET", "/key") => new Reply(200, new JsonObject { ["key"] = Tickets.PublicKey(key), ["alg"] = "ES256" }),
        ("POST", "/register") => await RegisterAsync(body, source),
        ("POST", "/verify") => Confirm(body, source),
        ("POST", "/resend") => await ResendAsync(body, source),
        ("POST", "/login") => await LoginAsync(body, source),
        ("POST", "/reset") => await ResetAsync(body, source),
        ("POST", "/reset/confirm") => ConfirmReset(body, source),
        ("POST", "/me") => Me(body),
        ("POST", "/ticket") => Issue(body),
        ("POST", "/logout") => Logout(body),
        ("POST", "/email") => await AddEmailAsync(body, source),
        ("POST", "/email/confirm") => ConfirmEmail(body, source),
        ("POST", "/launcher/code") => HandOff(body),
        ("POST", "/launcher/token") => TakeOver(body, source),
        _ => Error(404, "not found"),
    };

    async Task<Reply> RegisterAsync(JsonObject? body, string source)
    {
        if (!Allow("register " + source, 5, Hour)) return TooMany();
        if (!await HumanAsync(body, source)) return Robot();
        string? name = Text(body, "name"), given = Text(body, "email")?.Trim(), password = Text(body, "password");
        string? email = string.IsNullOrEmpty(given) ? null : Email(given);
        if (name is null || !NamePattern().IsMatch(name)) return Error(400, "a name has 3 to 20 letters, digits or _");
        if (!string.IsNullOrEmpty(given) && email is null) return Error(400, "that email does not look right");
        if (!PasswordFits(password)) return Error(400, $"a password needs at least {MinPassword} characters");

        store.DropUnverified(UnverifiedLife);
        if (store.ByName(name) is not null) return Error(409, "that name is taken");
        if (email is not null && store.ByEmail(email) is not null) return Error(409, "that email already has an account");
        string hash = Passwords.Hash(password!);
        long id;
        try
        {
            id = store.Add(name, email, hash, verified: email is null, google: null);
        }
        catch (SqliteException error) when (error.SqliteErrorCode == SqliteConstraint)
        {
            return Error(409, "that name or email is taken");
        }
        if (email is null) return Signed(new Account(id, name, null, hash, true, null));
        await SendCodeAsync(email, Verify, name);
        return Ok(new JsonObject { ["pending"] = true });
    }

    Reply Confirm(JsonObject? body, string source)
    {
        if (!Allow("code " + source, 20, TenMinutes)) return TooMany();
        if (Email(Text(body, "email")) is not string email || Text(body, "code") is not string code) return Error(400, "missing email or code");
        if (store.ByEmail(email) is not Account account || !store.UseCode(email, Verify, code)) return Error(401, "wrong or expired code");
        store.MarkVerified(account.Id);
        return Signed(account);
    }

    async Task<Reply> ResendAsync(JsonObject? body, string source)
    {
        if (!Allow("mail " + source, 5, Hour)) return TooMany();
        if (Email(Text(body, "email")) is string email && store.ByEmail(email) is { Verified: false } account && Allow("mail " + email, 5, Hour))
            await SendCodeAsync(email, Verify, account.Name);
        return Ok(new JsonObject { ["sent"] = true });
    }

    async Task<Reply> LoginAsync(JsonObject? body, string source)
    {
        if (!Allow("login " + source, 20, TenMinutes)) return TooMany();
        if (!await HumanAsync(body, source)) return Robot();
        if (Text(body, "login") is not string login || Text(body, "password") is not string password) return Error(400, "missing login or password");
        Account? account = store.Find(login.Trim());
        if (!Passwords.Verify(password, account?.Password ?? Decoy.Value) || account is null) return Error(401, "wrong login or password");
        if (!account.Verified)
        {
            Reply unverified = Error(403, "confirm your email first");
            unverified.Body["unverified"] = true;
            unverified.Body["email"] = account.Email;
            return unverified;
        }
        return Signed(account);
    }

    async Task<Reply> ResetAsync(JsonObject? body, string source)
    {
        if (!Allow("mail " + source, 5, Hour)) return TooMany();
        if (!await HumanAsync(body, source)) return Robot();
        if (Email(Text(body, "email")) is string email && store.ByEmail(email) is { } account && Allow("mail " + email, 5, Hour))
            await SendCodeAsync(email, Reset, account.Name);
        return Ok(new JsonObject { ["sent"] = true });
    }

    Reply ConfirmReset(JsonObject? body, string source)
    {
        if (!Allow("code " + source, 20, TenMinutes)) return TooMany();
        string? email = Email(Text(body, "email")), code = Text(body, "code"), password = Text(body, "password");
        if (email is null || code is null) return Error(400, "missing email or code");
        if (!PasswordFits(password)) return Error(400, $"a password needs at least {MinPassword} characters");
        if (store.ByEmail(email) is not Account account || !store.UseCode(email, Reset, code)) return Error(401, "wrong or expired code");
        store.SetPassword(account.Id, Passwords.Hash(password!));
        store.EndSessions(account.Id);
        return Signed(account);
    }

    async Task<Reply> AddEmailAsync(JsonObject? body, string source)
    {
        if (!Allow("mail " + source, 5, Hour)) return TooMany();
        if (Text(body, "token") is not string token || store.Session(token) is not Account account) return Error(401, "sign in again");
        if (Email(Text(body, "email")) is not string email) return Error(400, "that email does not look right");
        if (store.ByEmail(email) is { } owner && owner.Id != account.Id) return Error(409, "that email already has an account");
        if (!Allow("mail " + email, 5, Hour)) return TooMany();
        await SendCodeAsync(email, EmailFor(account), account.Name);
        return Ok(new JsonObject { ["sent"] = true });
    }

    Reply ConfirmEmail(JsonObject? body, string source)
    {
        if (!Allow("code " + source, 20, TenMinutes)) return TooMany();
        if (Text(body, "token") is not string token || store.Session(token) is not Account account) return Error(401, "sign in again");
        if (Email(Text(body, "email")) is not string email || Text(body, "code") is not string code) return Error(400, "missing email or code");
        if (!store.UseCode(email, EmailFor(account), code)) return Error(401, "wrong or expired code");
        try
        {
            store.SetEmail(account.Id, email);
        }
        catch (SqliteException error) when (error.SqliteErrorCode == SqliteConstraint)
        {
            return Error(409, "that email already has an account");
        }
        return Ok(new JsonObject { ["email"] = email });
    }

    static string EmailFor(Account account) => "email " + account.Id;

    Reply Me(JsonObject? body) => Text(body, "token") is string token && store.Session(token) is Account account
        ? Ok(new JsonObject { ["name"] = account.Name, ["email"] = account.Email })
        : Error(401, "sign in again");

    Reply Issue(JsonObject? body)
    {
        if (Text(body, "token") is not string token || store.Session(token) is not Account account) return Error(401, "sign in again");
        if (Text(body, "server") is not { Length: > 0 and <= MaxServer } server) return Error(400, "missing server");
        return Ok(new JsonObject { ["ticket"] = Tickets.Issue(key, account.Id, account.Name, server), ["name"] = account.Name });
    }

    Reply Logout(JsonObject? body)
    {
        if (Text(body, "token") is string token) store.EndSession(token);
        return Ok(new JsonObject());
    }

    Reply HandOff(JsonObject? body)
    {
        if (Text(body, "token") is not string token || store.Session(token) is not Account account) return Error(401, "sign in again");
        if (Text(body, "challenge") is not { Length: ChallengeSize } challenge) return Error(400, "missing challenge");
        string code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(HandoffSize));
        long now = Environment.TickCount64;
        lock (_handoffs)
        {
            foreach (string old in _handoffs.Where(pair => pair.Value.Expires < now).Select(pair => pair.Key).ToList()) _handoffs.Remove(old);
            if (_handoffs.Count >= MaxTracked) return TooMany();
            _handoffs[code] = new Handoff(account, challenge, now + (long)HandoffLife.TotalMilliseconds);
        }
        return Ok(new JsonObject { ["code"] = code });
    }

    Reply TakeOver(JsonObject? body, string source)
    {
        if (!Allow("code " + source, 20, TenMinutes)) return TooMany();
        if (Text(body, "code") is not string code || Text(body, "verifier") is not { Length: >= ChallengeSize and <= MaxVerifier } verifier) return Error(400, "missing code");
        Handoff? handoff;
        lock (_handoffs) _handoffs.Remove(code, out handoff);
        byte[] challenge = Encoding.ASCII.GetBytes(Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        if (handoff is null || handoff.Expires < Environment.TickCount64 || !CryptographicOperations.FixedTimeEquals(challenge, Encoding.ASCII.GetBytes(handoff.Challenge)))
            return Error(401, "that sign in link expired, sign in from the launcher again");
        return Signed(handoff.Account);
    }

    async Task SendCodeAsync(string email, string purpose, string name)
    {
        string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        store.SetCode(email, purpose, code, CodeLife);
        int minutes = (int)CodeLife.TotalMinutes;
        CodeMail mail = purpose switch
        {
            Verify => new CodeMail($"{code} is your OpenKogama code", "Confirm your email",
                $"Hi {name}, enter this code to finish creating your OpenKogama account.", code, minutes,
                "If you did not create an OpenKogama account, you can ignore this email."),
            Reset => new CodeMail($"{code} is your OpenKogama password reset code", "Reset your password",
                $"Hi {name}, enter this code to choose a new password.", code, minutes,
                "If you did not ask for a new password, you can ignore this email. Your password stays the same."),
            _ => new CodeMail($"{code} is your code to add this email to OpenKogama", "Add this email",
                $"Hi {name}, enter this code to add this email to your OpenKogama account.", code, minutes,
                "If you did not ask for this, you can ignore this email."),
        };
        await send(email, mail);
    }

    Reply Signed(Account account) => Ok(new JsonObject { ["token"] = store.StartSession(account.Id), ["name"] = account.Name });

    static Reply Ok(JsonObject body) => new(200, body);
    static Reply Error(int status, string message) => new(status, new JsonObject { ["error"] = message });
    static Reply TooMany() => Error(429, "too many tries, wait a bit");

    async Task<bool> HumanAsync(JsonObject? body, string source) => Human is null || await Human(Text(body, "captcha"), source);

    static Reply Robot()
    {
        Reply reply = Error(400, "the check that you are not a bot failed, try again");
        reply.Body["captcha"] = true;
        return reply;
    }

    static bool PasswordFits(string? password) => password is { Length: >= MinPassword and <= MaxPassword };

    static string? Text(JsonObject? body, string name) =>
        body?[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    static string? Email(string? text)
    {
        string? trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxEmail || !MailAddress.TryCreate(trimmed, out MailAddress? address)
            || address.Address != trimmed || !address.Host.Contains('.'))
            return null;
        return trimmed.ToLowerInvariant();
    }

    bool Allow(string key, int limit, TimeSpan window)
    {
        long now = Environment.TickCount64;
        lock (_attempts)
        {
            if (_attempts.Count > MaxTracked)
                foreach (string stale in _attempts.Where(pair => pair.Value.Count == 0 || now - pair.Value.Last() > Hour.TotalMilliseconds).Select(pair => pair.Key).ToList())
                    _attempts.Remove(stale);
            if (!_attempts.TryGetValue(key, out Queue<long>? times)) _attempts[key] = times = new Queue<long>();
            while (times.Count > 0 && now - times.Peek() > window.TotalMilliseconds) times.Dequeue();
            if (times.Count >= limit) return false;
            times.Enqueue(now);
            return true;
        }
    }

    static string SourceOf(HttpListenerRequest request)
    {
        IPAddress remote = request.RemoteEndPoint.Address;
        string? forwarded = request.Headers["X-Forwarded-For"]?.Split(',')[0].Trim();
        return IPAddress.IsLoopback(remote) && !string.IsNullOrEmpty(forwarded) ? forwarded : remote.ToString();
    }

    static async Task<JsonObject?> BodyAsync(HttpListenerRequest request)
    {
        var buffer = new byte[MaxBody + 1];
        int read = 0;
        while (read < buffer.Length && await request.InputStream.ReadAsync(buffer.AsMemory(read)) is int chunk and > 0) read += chunk;
        return read is > 0 and <= MaxBody ? JsonNode.Parse(buffer.AsSpan(0, read)) as JsonObject : null;
    }

    [GeneratedRegex("^[A-Za-z0-9_]{3,20}$")]
    private static partial Regex NamePattern();
}
