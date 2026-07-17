using System;
using MedivalChess.GameBoard;
using MedivalChess;

using System.Runtime.InteropServices;

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

    using var game = new Game1();
    game.Run();

    Console.WriteLine("Running");
  }
}