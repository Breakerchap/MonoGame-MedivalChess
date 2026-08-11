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

    // Farms are long-lived income assets rather than frontline pieces. Rear depth intentionally
    // dominates every ordinary placement consideration: moving a farm just a couple of rows back
    // should usually matter more than gaining perfect forest cover. Very deep sites get a small
    // additional premium so the CPU naturally fills the safest backline before creeping forward.
    float rearSafety = Math.Min(12, rearTerritoryDepth) * 10f +
      Math.Max(0, rearTerritoryDepth - 4) * 3f;
    float cover = forestSquares * 4f;
    return rearSafety + cover;
  }

  internal static bool ProtectsFriendlyFarm(CpuGameState state, NetworkTeam team, (int x, int y) position) => state.Pieces.Any(piece =>
    piece.Team == team && piece.Type == "Farm" && UnitRules.TryGet(piece.Type, out UnitRule farm) &&
    Enumerable.Range(piece.X - 1, farm.Width + 2).Any(x => x == position.x) &&
    Enumerable.Range(piece.Y - 1, farm.Height + 2).Any(y => y == position.y));
}