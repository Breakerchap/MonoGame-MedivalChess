using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>Cheap deterministic placement priorities shared by opening search and candidate ranking.</summary>
internal static class CpuPlacementHeuristics
{
  internal static float GetFarmProtectionScore(CpuGameState state, NetworkTeam team, int x, int y)
  {
    return GetFarmProtectionScore(state, team, x, y, GetFurthestForwardProjection(state, team));
  }

  internal static int GetFurthestForwardProjection(CpuGameState state, NetworkTeam team)
  {
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    return state.Board.Cells
      .Where(position => MatchRules.GetSquareOwner(state.Board, state.Configuration.GameMode, position, state.Configuration.PlayerCount) == team)
      .Select(position => position.x * forward.x + position.y * forward.y)
      .DefaultIfEmpty(0)
      .Max();
  }

  internal static float GetFarmProtectionScore(
    CpuGameState state,
    NetworkTeam team,
    int x,
    int y,
    int furthestForwardProjection
  )
  {
    if (!UnitRules.TryGet("Farm", out UnitRule farm))
    {
      return 0f;
    }
    int forestSquares = 0;
    for (int offsetY = 0; offsetY < farm.Height; offsetY++)
    for (int offsetX = 0; offsetX < farm.Width; offsetX++)
    {
      if (state.Terrain.IsForest((x + offsetX, y + offsetY))) forestSquares++;
    }

    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    int positionProjection = x * forward.x + y * forward.y;
    int rearTerritoryDepth = Math.Max(0, furthestForwardProjection - positionProjection);
    // Farms are long-lived income assets, so safe home territory matters more than it does for a
    // forward combat deployment. Keep forest cover valuable, but reward a deeper placement more
    // strongly and over a wider part of the home zone than the original shallow bonus did.
    return forestSquares * 7f + Math.Min(8, rearTerritoryDepth) * 3f;
  }

  internal static bool ProtectsFriendlyFarm(CpuGameState state, NetworkTeam team, (int x, int y) position) => state.Pieces.Any(piece =>
    piece.Team == team && piece.Type == "Farm" && UnitRules.TryGet(piece.Type, out UnitRule farm) &&
    Enumerable.Range(piece.X - 1, farm.Width + 2).Any(x => x == position.x) &&
    Enumerable.Range(piece.Y - 1, farm.Height + 2).Any(y => y == position.y));
}
