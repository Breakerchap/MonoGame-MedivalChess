using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>
/// Scores opening royal squares. A royal is the loss condition in Regicide, so its depth in its
/// own territory deliberately outweighs cosmetic cover or central-board convenience.
/// </summary>
public static class CpuRoyalPlacementHeuristics
{
  public static float Score(
    Board board,
    BattlefieldTerrain terrain,
    NetworkTeam team,
    (int x, int y) position,
    int width,
    int height,
    int playerCount,
    CpuProfile profile
  )
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(profile);

    int rearDepth = GetRearTerritoryDepth(board, team, position, width, height, playerCount);
    int forestCover = Footprint(position, width, height).Count(terrain.IsForest);
    int nearbyForests = Enumerable.Range(-1, width + 2)
      .SelectMany(offsetX => Enumerable.Range(-1, height + 2)
        .Select(offsetY => (position.x + offsetX, position.y + offsetY)))
      .Count(terrain.IsForest);
    int riverEdges = Footprint(position, width, height).Sum(square => new[]
    {
      (square.x - 1, square.y), (square.x + 1, square.y),
      (square.x, square.y - 1), (square.x, square.y + 1)
    }.Count(neighbour => board.ContainsCell(neighbour) && terrain.HasRiverBetween(square, neighbour)));

    // Even an aggressive personality must protect its royal. The difficulty changes how tightly
    // the CPU follows this policy, but every profile values a rear setup over a forward one.
    float protectionBias = Math.Max(0.65f,
      profile.Personality.RoyalProtection * profile.Personality.Caution - profile.Personality.Aggression * 0.15f);
    float depthWeight = profile.Difficulty switch
    {
      CpuDifficultyLevel.Best => 320f,
      CpuDifficultyLevel.Hard => 280f,
      CpuDifficultyLevel.Medium => 240f,
      _ => 190f
    };

    return rearDepth * depthWeight * protectionBias +
      forestCover * 18f * protectionBias +
      nearbyForests * 4f * profile.Personality.Caution -
      riverEdges * 6f * profile.Personality.Caution;
  }

  /// <summary>Returns how many friendly-territory steps sit between a footprint and the front.</summary>
  public static int GetRearTerritoryDepth(
    Board board,
    NetworkTeam team,
    (int x, int y) position,
    int width,
    int height,
    int playerCount
  )
  {
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    int frontProjection = board.Cells
      .Where(square => MatchRules.GetSquareOwner(board, "Regicide", square, playerCount) == team)
      .Select(square => square.x * forward.x + square.y * forward.y)
      .DefaultIfEmpty(0)
      .Max();
    // For a large royal, use the leading occupied tile: the whole footprint must remain deep,
    // not merely its rear-most corner.
    int footprintProjection = Footprint(position, width, height)
      .Select(square => square.x * forward.x + square.y * forward.y)
      .DefaultIfEmpty(frontProjection)
      .Max();
    return Math.Max(0, frontProjection - footprintProjection);
  }

  private static IEnumerable<(int x, int y)> Footprint((int x, int y) position, int width, int height)
  {
    for (int offsetY = 0; offsetY < height; offsetY++)
    for (int offsetX = 0; offsetX < width; offsetX++)
    {
      yield return (position.x + offsetX, position.y + offsetY);
    }
  }
}
