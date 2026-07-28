namespace MedivalChess.Shared;

/// <summary>Match-wide constants and board-zone calculations used by local and online games.</summary>
public static class MatchRules
{
  public const int ActionsPerTurn = 3;
  public const int DefaultNoMansLandHalfHeight = 3;
  public const int ConquestNoMansLandExtraHalfHeight = 1;
  public const int DefaultConquestWinScore = 15;

  public static int GetNoMansLandHalfHeight(string gameMode) =>
    string.Equals(gameMode, "Conquest", StringComparison.Ordinal) ?
      DefaultNoMansLandHalfHeight + ConquestNoMansLandExtraHalfHeight :
      DefaultNoMansLandHalfHeight;

  public static NetworkTeam? GetSquareOwner(Board board, string gameMode, (int x, int y) square)
  {
    int arrayY = square.y - board.MinY;
    if (arrayY < 0 || arrayY >= board.BoardArray.GetLength(0) || !board.ContainsCell(square)) return null;

    return GetTeamForArrayRow(board, gameMode, arrayY);
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

  public static IEnumerable<(int x, int y)> GetRoyalSpawnCandidates(
    Board board,
    NetworkTeam team,
    int width,
    int height
  )
  {
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
}
