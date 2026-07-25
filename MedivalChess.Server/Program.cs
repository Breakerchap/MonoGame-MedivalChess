using MedivalChess.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<MatchStore>();

var app = builder.Build();
app.MapGet("/", () => "Crown & Siege local match server");
app.MapHub<MatchHub>("/gamehub");
app.Run();
