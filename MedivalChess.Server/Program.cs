using MedivalChess.Server;

var builder = WebApplication.CreateBuilder(args);
string port = Environment.GetEnvironmentVariable("PORT") ?? "5057";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddSignalR();
builder.Services.AddSingleton<MatchStore>();

var app = builder.Build();
app.MapGet("/", () => Results.Ok(new { service = "Crown & Siege match server", status = "online" }));
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapHub<MatchHub>("/gamehub");
app.Run();
