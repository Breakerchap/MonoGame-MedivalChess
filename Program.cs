using System;
using System.IO;
using System.Runtime.InteropServices;
using MedivalChess;

internal static class Program
{
#if DEBUG
  [DllImport("kernel32.dll")]
  private static extern bool AllocConsole();
#endif

  private static StreamWriter _log = null;

  private static int Main(string[] args)
  {
    StartLogging();

    try
    {
      Log("Crown & Siege starting.");
      Log($"OS: {Environment.OSVersion}");
      Log($"Runtime: {Environment.Version} ({RuntimeInformation.RuntimeIdentifier})");
      Log($"Architecture: {RuntimeInformation.ProcessArchitecture}");
      Log($"Base directory: {AppContext.BaseDirectory}");
      Log($"Working directory: {Environment.CurrentDirectory}");
      Log($"Arguments: {string.Join(" ", args)}");

#if DEBUG
      // AllocConsole is Windows-only. Calling it unconditionally prevents a
      // Debug build from starting on macOS and Linux.
      if (OperatingSystem.IsWindows())
      {
        AllocConsole();
      }
#endif

      Log("Creating game.");
      using var game = new Game1();
      Log("Starting MonoGame loop.");
      game.Run();
      Log("Game loop exited normally.");
    }
    catch (Exception ex)
    {
      Log("GAME CRASHED:");
      Log(ex.ToString());
      Log($"Startup log: {GetLogPath()}");
      return 1;
    }
    finally
    {
      _log?.Dispose();
    }

    return 0;
  }

  private static void StartLogging()
  {
    try
    {
      string logPath = GetLogPath();
      Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
      _log = new StreamWriter(logPath, append: true) { AutoFlush = true };
      Log($"\n--- {DateTimeOffset.Now:O} ---");
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Could not create startup log: {ex}");
    }
  }

  private static string GetLogPath()
  {
    string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(appData))
    {
      appData = AppContext.BaseDirectory;
    }

    return Path.Combine(appData, "CrownAndSiege", "startup.log");
  }

  private static void Log(string message)
  {
    Console.Error.WriteLine(message);
    _log?.WriteLine(message);
  }
}
