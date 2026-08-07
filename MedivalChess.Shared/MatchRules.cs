namespace MedivalChess.Shared;

/// <summary>Match-wide constants and board-zone calculations used by local and online games.</summary>
public static class MatchRules
{
  public const int ActionsPerTurn = 999;
  public const int DefaultNoMansLandHalfHeight = 3;
  public const int ConquestNoMansLandExtraHalfHeight = 1;
  public const int PlunderNoMansLandExtraHalfHeight = 2;
  public const int DefaultConquestWinScore = 15;

  public static int GetNoMansLandHalfHeight(string gameMode) =>
    gameMode switch
    {
      "Conquest" => DefaultNoMansLandHalfHeight + ConquestNoMansLandExtraHalfHeight,
      "Plunder" => DefaultNoMansLandHalfHeight + PlunderNoMansLandExtraHalfHeight,
      _ => DefaultNoMansLandHalfHeight
    };

  public static NetworkTeam? GetSquareOwner(Board board, string gameMode, (int x, int y) square, int playerCount = 2)
  {
    int arrayY = square.y - board.MinY;
    if (arrayY < 0 || arrayY >= board.BoardArray.GetLength(0) || !board.ContainsCell(square)) return null;
    if (playerCount == 2) return GetTeamForArrayRow(board, gameMode, arrayY);
    if (!TeamRules.IsValidPlayerCount(playerCount)) return null;

    int arrayX = square.x - board.MinX;
    int boardWidth = board.BoardArray.GetLength(1);
    int boardHeight = board.BoardArray.GetLength(0);
    int centreX = boardWidth / 2;
    int centreY = boardHeight / 2;
    int halfHeight = GetNoMansLandHalfHeight(gameMode);
    int halfWidth = Math.Max(halfHeight, (int)MathF.Round(halfHeight * boardWidth / (float)boardHeight));
    int deltaX = arrayX - centreX;
    int deltaY = arrayY - centreY;
    if (Math.Abs(deltaX) <= halfWidth && Math.Abs(deltaY) <= halfHeight) return null;

    // Assign each outer square to the closest cardinal side. Normalising the
    // axes keeps diagonal corners balanced on non-square battlefields.
    float horizontal = Math.Abs(deltaX) / (float)Math.Max(1, centreX - halfWidth);
    float vertical = Math.Abs(deltaY) / (float)Math.Max(1, centreY - halfHeight);
    NetworkTeam owner = vertical >= horizontal
      ? deltaY > 0 ? NetworkTeam.Red : NetworkTeam.Blue
      : deltaX < 0 ? NetworkTeam.Green : NetworkTeam.Yellow;
    return TeamRules.GetActiveTeams(playerCount).Contains(owner) ? owner : null;
  }

  public static NetworkTeam? GetTeamForArrayRow(Board board, string gameMode, int arrayY)
  {
    if (arrayY < 0 || arrayY >= board.BoardArray.GetLength(0)) return null;
    int centreRow = board.BoardArray.GetLength(0) / 2;
    int halfHeight = GetNoMansLandHalfHeight(gameMode);
    if (arrayY < centreRow - halfHeight) return NetworkTeam.Blue;
    if (arrayY > centreRow + halfHeight) return NetworkTeam.Red;
    return null;
  }

  public static bool IsConquestSquare(Board board, (int x, int y) square)
  {
    int centreX = board.MinX + board.BoardArray.GetLength(1) / 2;
    int centreY = board.MinY + board.BoardArray.GetLength(0) / 2;
    return board.ContainsCell(square) && Math.Abs(square.x - centreX) <= 1 && Math.Abs(square.y - centreY) <= 1;
  }

  public static IReadOnlyList<(int x, int y)> GetDominionControlPoints(Board board)
  {
    int centreX = board.MinX + board.BoardArray.GetLength(1) / 2;
    int centreY = board.MinY + board.BoardArray.GetLength(0) / 2;
    // Keep all three points inside the central neutral zone on four-player boards too.
    int spacing = 2;
    return [
      FindNearestBoardCell(board, (centreX - spacing, centreY)),
      FindNearestBoardCell(board, (centreX, centreY)),
      FindNearestBoardCell(board, (centreX + spacing, centreY))
    ];
  }

  public static (int x, int y) GetTreasureSpawn(Board board)
  {
    int centreX = board.MinX + board.BoardArray.GetLength(1) / 2;
    int centreY = board.MinY + board.BoardArray.GetLength(0) / 2;
    return FindNearestBoardCell(board, (centreX, centreY));
  }

  private static (int x, int y) FindNearestBoardCell(Board board, (int x, int y) preferred)
  {
    return board.Cells
      .OrderBy(square => Math.Abs(square.x - preferred.x) + Math.Abs(square.y - preferred.y))
      .ThenBy(square => square.y)
      .ThenBy(square => square.x)
      .First();
  }

  public static IEnumerable<(int x, int y)> GetRoyalSpawnCandidates(
    Board board,
    NetworkTeam team,
    int width,
    int height,
    int playerCount = 2
  )
  {
    if (playerCount > 2)
    {
      foreach ((int x, int y) position in GetSideRoyalSpawnCandidates(board, team, width, height))
      {
        bool footprintIsOnBoard = Enumerable.Range(0, height).All(offsetY =>
          Enumerable.Range(0, width).All(offsetX =>
            board.ContainsCell((position.x + offsetX, position.y + offsetY)) &&
            GetSquareOwner(board, "Regicide", (position.x + offsetX, position.y + offsetY), playerCount) == team));
        if (footprintIsOnBoard) yield return position;
      }
      yield break;
    }

    int boardHeight = board.BoardArray.GetLength(0);
    int firstArrayY = team == NetworkTeam.Red ? boardHeight - height : 0;
    int rowStep = team == NetworkTeam.Red ? -1 : 1;
    int centreX = board.BoardArray.GetLength(1) / 2;

    for (int rowOffset = 0; rowOffset < boardHeight; rowOffset++)
    {
      int arrayY = firstArrayY + rowOffset * rowStep;
      if (arrayY < 0 || arrayY + height > boardHeight) continue;

      for (int offset = 0; offset < board.BoardArray.GetLength(1); offset++)
      {
        int[] candidateXs = offset == 0 ? [centreX] : [centreX - offset, centreX + offset];
        foreach (int arrayX in candidateXs)
        {
          if (arrayX < 0 || arrayX + width > board.BoardArray.GetLength(1)) continue;
          var position = (x: arrayX + board.MinX, y: arrayY + board.MinY);
          bool footprintIsOnBoard = Enumerable.Range(0, height).All(offsetY =>
            Enumerable.Range(0, width).All(offsetX => board.ContainsCell((position.x + offsetX, position.y + offsetY))));
          if (footprintIsOnBoard) yield return position;
        }
      }
    }
  }

  public static bool IsOnEnemyBackEdge(Board board, NetworkTeam team, (int x, int y) square)
  {
    int maxX = board.MinX + board.BoardArray.GetLength(1) - 1;
    int maxY = board.MinY + board.BoardArray.GetLength(0) - 1;
    return team switch
    {
      NetworkTeam.Red => square.y == board.MinY,
      NetworkTeam.Blue => square.y == maxY,
      NetworkTeam.Green => square.x == maxX,
      NetworkTeam.Yellow => square.x == board.MinX,
      _ => false
    };
  }

  private static IEnumerable<(int x, int y)> GetSideRoyalSpawnCandidates(
    Board board,
    NetworkTeam team,
    int width,
    int height
  )
  {
    int boardWidth = board.BoardArray.GetLength(1);
    int boardHeight = board.BoardArray.GetLength(0);
    int centreX = boardWidth / 2;
    int centreY = boardHeight / 2;
    bool horizontalSide = team is NetworkTeam.Green or NetworkTeam.Yellow;
    int primaryLimit = horizontalSide ? boardWidth : boardHeight;
    int secondaryLimit = horizontalSide ? boardHeight : boardWidth;
    int footprintPrimary = horizontalSide ? width : height;
    int footprintSecondary = horizontalSide ? height : width;
    int firstPrimary = team is NetworkTeam.Red or NetworkTeam.Yellow ? primaryLimit - footprintPrimary : 0;
    int primaryStep = team is NetworkTeam.Red or NetworkTeam.Yellow ? -1 : 1;
    int centreSecondary = horizontalSide ? centreY : centreX;

    for (int primaryOffset = 0; primaryOffset < primaryLimit; primaryOffset++)
    {
      int primary = firstPrimary + primaryOffset * primaryStep;
      if (primary < 0 || primary + footprintPrimary > primaryLimit) continue;
      for (int offset = 0; offset < secondaryLimit; offset++)
      {
        int[] candidates = offset == 0 ? [centreSecondary] : [centreSecondary - offset, centreSecondary + offset];
        foreach (int secondary in candidates)
        {
          if (secondary < 0 || secondary + footprintSecondary > secondaryLimit) continue;
          yield return horizontalSide
            ? (primary + board.MinX, secondary + board.MinY)
            : (secondary + board.MinX, primary + board.MinY);
        }
      }
    }
  }
}
