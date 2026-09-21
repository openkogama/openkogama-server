WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<PhotonListener>();
WebApplication app = builder.Build();

app.MapGet("/session", () => new SessionData("127.0.0.1:5055", 0, 0, GameMode.Play, "en_US", false, "0"));

// run kogama with "kogama.exe kogamaPackage:aHR0cDovLzEyNy4wLjAuMTo4MDgwL3Nlc3Npb24="
// "aHR0cDovLzEyNy4wLjAuMTo4MDgwL3Nlc3Npb24=" is url in base64
app.Run("http://127.0.0.1:8080");