using System;
using System.Runtime.InteropServices;
using MedivalChess.GameBoard;
using MedivalChess;

internal static class Program
{
#if DEBUG
  [DllImport("kernel32.dll")]
  private static extern bool AllocConsole();
#endif

  static void Main()
  {
#if DEBUG
    AllocConsole();
#endif

    try
    {
      Console.WriteLine("Running");

      using var game = new Game1();
      game.Run();
    }
    catch (Exception ex)
    {
      Console.WriteLine();
      Console.WriteLine("GAME CRASHED:");
      Console.WriteLine(ex);

      Console.WriteLine();
      Console.WriteLine("Press Enter to close...");
      Console.ReadLine();
    }
  }
}