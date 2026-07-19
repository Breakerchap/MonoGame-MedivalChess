namespace MedivalChess.GameBoard;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;

class Tile
{
  public Piece Piece;
}

class BoardData
{
  [JsonPropertyName("cells")]
  public List<int[]> Cells { get; set; } = new();
}

public class Board
{
  public int[,] BoardArray;
  public int MinX { get; private set; }
  public int MinY { get; private set; }

  public Board()
  {
    string boardPath = Path.Combine(AppContext.BaseDirectory, "GameBoard", "board.json");
    if (!File.Exists(boardPath))
    {
      boardPath = Path.Combine(Directory.GetCurrentDirectory(), "GameBoard", "board.json");
    }

    if (!File.Exists(boardPath))
    {
      throw new FileNotFoundException("Could not find board.json. Expected at GameBoard/board.json", boardPath);
    }

    string json = File.ReadAllText(boardPath);
    BoardData? data = JsonSerializer.Deserialize<BoardData>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (data?.Cells == null || data.Cells.Count == 0)
    {
      throw new InvalidDataException("board.json does not contain any cells.");
    }

    MinX = data.Cells.Min(cell => cell[0]);
    int maxX = data.Cells.Max(cell => cell[0]);

    MinY = data.Cells.Min(cell => cell[1]);
    int maxY = data.Cells.Max(cell => cell[1]);

    int width = maxX - MinX + 1;
    int height = maxY - MinY + 1;

    BoardArray = new int[height, width];

    foreach (int[] cell in data.Cells)
    {
      int x = cell[0] - MinX;
      int y = cell[1] - MinY;

      BoardArray[y, x] = 1;
    }
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
