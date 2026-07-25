namespace MedivalChess.Server;

public sealed class RoomCleanupService(MatchStore matches) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
      matches.CleanupExpired();
    }
  }
}
