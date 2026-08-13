namespace MedivalChess.Shared;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class BoardData
{
  [JsonPropertyName("cells")]
  public List<int[]> Cells { get; set; } = new();
}

public sealed class Board
{
  private readonly HashSet<(int x, int y)> _cells = [];

  public int[,] BoardArray { get; private set; } = default!;
  public int MinX { get; private set; }
  public int MinY { get; private set; }
  public IReadOnlyCollection<(int x, int y)> Cells => _cells;

  public Board(string boardFileName = "board_medium.json")
  {
    string json = LoadBoardJson(boardFileName);
    BoardData? data = JsonSerializer.Deserialize<BoardData>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (data?.Cells == null || data.Cells.Count == 0)
    {
      throw new InvalidDataException($"{boardFileName} does not contain any cells.");
    }

    Initialise(data.Cells.Select(cell => (cell[0], cell[1])));
  }

  /// <summary>
  /// Creates a board from explicit playable cells. Custom campaign levels use this
  /// constructor so their geometry is consumed by the same board and movement rules
  /// as built-in battlefields.
  /// </summary>
  public Board(IEnumerable<(int x, int y)> cells)
  {
    ArgumentNullException.ThrowIfNull(cells);
    Initialise(cells);
  }

  private static string LoadBoardJson(string boardFileName)
  {
    string boardPath = Path.Combine(AppContext.BaseDirectory, "GameBoard", boardFileName);
    if (!File.Exists(boardPath))
    {
      boardPath = Path.Combine(Directory.GetCurrentDirectory(), "GameBoard", boardFileName);
    }

    if (File.Exists(boardPath))
    {
      return File.ReadAllText(boardPath);
    }

    // Android packages (and some other single-file/package layouts) do not expose
    // copied data files as ordinary files beside the executable. The shared project
    // therefore also embeds the built-in board definitions and falls back to them.
    Assembly assembly = typeof(Board).Assembly;
    string? resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
      name.EndsWith('.' + boardFileName, StringComparison.OrdinalIgnoreCase));
    if (resourceName is not null)
    {
      using Stream? stream = assembly.GetManifestResourceStream(resourceName);
      if (stream is not null)
      {
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
      }
    }

    throw new FileNotFoundException(
      $"Could not find {boardFileName}. Expected it at GameBoard/{boardFileName} or as an embedded board resource.",
      boardPath
    );
  }

  private void Initialise(IEnumerable<(int x, int y)> cells)
  {
    (int x, int y)[] uniqueCells = cells.Distinct().ToArray();
    if (uniqueCells.Length == 0)
    {
      throw new ArgumentException("A board must contain at least one playable cell.", nameof(cells));
    }

    MinX = uniqueCells.Min(cell => cell.x);
    int maxX = uniqueCells.Max(cell => cell.x);
    MinY = uniqueCells.Min(cell => cell.y);
    int maxY = uniqueCells.Max(cell => cell.y);

    int width = maxX - MinX + 1;
    int height = maxY - MinY + 1;
    BoardArray = new int[height, width];

    foreach ((int x, int y) cell in uniqueCells)
    {
      BoardArray[cell.y - MinY, cell.x - MinX] = 1;
      _cells.Add(cell);
    }
  }

  public bool ContainsCell((int x, int y) position)
  {
    return _cells.Contains(position);
  }

  public override string ToString()
  {
    string boardOutput = "";

    for (int y = 0; y < BoardArray.GetLength(0); y++)
    {
      for (int x = 0; x < BoardArray.GetLength(1); x++)
      {
        boardOutput += BoardArray[y, x] == 1 ? "X" : " ";
      }

      boardOutput += "\n";
    }

    return boardOutput;
  }
}
