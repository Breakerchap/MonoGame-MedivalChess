namespace MedivalChess.Shared;

/// <summary>Shared board selection and placement-zone rules for headless and online matches.</summary>
public static class BoardRules
{
  private static readonly IReadOnlyDictionary<string, Board> Boards = new Dictionary<string, Board>(StringComparer.Ordinal)
  {
    ["Small"] = new Board("board_small.json"),
    ["Medium"] = new Board("board_medium.json"),
    ["Large"] = new Board("board_large.json")
  };

  public static Board GetBoard(NetworkMatchConfiguration configuration) => GetBoard(configuration.BoardSize);

  public static Board GetBoard(string boardSize) => Boards.TryGetValue(boardSize, out Board? board)
    ? board
    : throw new ArgumentOutOfRangeException(nameof(boardSize), boardSize, "Unknown board size.");

  public static bool Contains(NetworkMatchConfiguration configuration, int x, int y) =>
    GetBoard(configuration).ContainsCell((x, y));

  public static bool FootprintFitsBoard(
    NetworkMatchConfiguration configuration,
    int x,
    int y,
    int width,
    int height
  )
  {
    for (int offsetY = 0; offsetY < height; offsetY++)
    {
      for (int offsetX = 0; offsetX < width; offsetX++)
      {
        if (!Contains(configuration, x + offsetX, y + offsetY))
        {
          return false;
        }
      }
    }

    return true;
  }

  public static bool CanPlaceForTeam(
    NetworkMatchConfiguration configuration,
    NetworkTeam team,
    int x,
    int y,
    int width,
    int height
  )
  {
    Board board = GetBoard(configuration);
    for (int offsetY = 0; offsetY < height; offsetY++)
    {
      for (int offsetX = 0; offsetX < width; offsetX++)
      {
        (int x, int y) square = (x + offsetX, y + offsetY);
        if (!board.ContainsCell(square) ||
            MatchRules.GetSquareOwner(board, configuration.GameMode, square, configuration.PlayerCount) != team)
        {
          return false;
        }
      }
    }

    return true;
  }

  public static bool CanPlaceMercenary(NetworkMatchConfiguration configuration, int x, int y)
  {
    Board board = GetBoard(configuration);
    return board.ContainsCell((x, y)) &&
      MatchRules.GetSquareOwner(board, configuration.GameMode, (x, y), configuration.PlayerCount) is null;
  }

  public static bool IsInTeamTerritory(NetworkMatchConfiguration configuration, NetworkTeam team, int x, int y)
  {
    Board board = GetBoard(configuration);
    return board.ContainsCell((x, y)) &&
      MatchRules.GetSquareOwner(board, configuration.GameMode, (x, y), configuration.PlayerCount) == team;
  }
}
