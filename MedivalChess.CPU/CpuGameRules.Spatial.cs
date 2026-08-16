using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>Board geometry, movement paths, occupancy, and line-of-sight helpers for CPU simulation.</summary>
public static partial class CpuGameRules
{
  private static IReadOnlyDictionary<(int x, int y), List<(int x, int y)>> GetLegalMovementPaths(
    CpuGameState source,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece piece,
    UnitRule rule
  )
  {
    rule = GetEffectiveMovementRule(source, pieces, piece, rule);
    bool hasPalaceSupport = HasPalaceSupport(pieces, piece);
    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      rule,
      (piece.X, piece.Y),
      piece.Team,
      destination => CanLand(source, pieces, piece, rule, destination, hasPalaceSupport),
      (from, destination) => CanTravelThrough(source, pieces, piece, rule, from, destination),
      destination => GetMovementCost(source, piece, rule, destination),
      (from, destination) => CrossesRiver(source, pieces, piece, rule, from, destination),
      (from, destination) => GetMovementCost(source, pieces, piece, rule, from, destination),
      destination => rule.MoveRange + (IsPalaceAssistedMovement(
        pieces, piece, rule, (piece.X, piece.Y), destination) ? 1 : 0),
      rule.MoveRange + (hasPalaceSupport ? 1 : 0),
      position => CanContinueChessPath(pieces, piece, rule, position)
    );
    AddPawnCapturePaths(source, pieces, piece, rule, paths);
    return paths;
  }

  private static UnitRule GetEffectiveMovementRule(CpuGameState state, IReadOnlyList<NetworkPiece> pieces, NetworkPiece piece, UnitRule rule)
  {
    NetworkPiece? oxAttachment = pieces.FirstOrDefault(other =>
      other.AttachedToId == piece.Id && other.Type == nameof(PieceType.Ox));
    if (oxAttachment is not null)
    {
      rule = rule with
      {
        MoveRange = rule.MoveRange + AbilityRules.GetAttachmentMovementBonus(oxAttachment.Type)
      };
    }
    if (state.TreasureCarrierId == piece.Id)
    {
      rule = rule with { MoveRange = Math.Max(1, rule.MoveRange - 1) };
    }

    return AbilityRules.CanUseCavalierFollowUpMove(piece.Type, piece.CavalierFollowUpMoveAvailable)
      ? rule with { MoveRange = AbilityRules.CavalierFollowUpMovement, MovePattern = RuleShape.Straight }
      : rule;
  }

  private static bool CanLand(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece piece,
    UnitRule rule,
    (int x, int y) destination,
    bool mayUsePalaceSupport = false
  )
  {
    if (CanChessCaptureLand(state, pieces, piece, rule, destination)) return true;
    return CanPlace(
      state,
      pieces,
      rule,
      destination.x,
      destination.y,
      piece.Id,
      AbilityRules.IgnoresImpassableTerrain(rule) || mayUsePalaceSupport,
      AbilityRules.IsTrampleAttacker(rule) ? piece.Team : null
    );
  }

  private static bool CanPlace(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    UnitRule rule,
    int x,
    int y,
    string? ignoredPieceId = null,
    bool canIgnoreLakes = false,
    NetworkTeam? teamWhoseEnemiesMayBeOverlapped = null
  )
  {
    if (!BoardRules.FootprintFitsBoard(state.Board, x, y, rule.Width, rule.Height))
    {
      return false;
    }
    foreach ((int x, int y) square in OccupiedSquares(rule, (x, y)))
    {
      if ((!canIgnoreLakes && state.Terrain.IsLake(square)) || state.Barricades.ContainsKey(square))
      {
        return false;
      }
    }

    HashSet<string> ignored = pieces.Where(piece => piece.Id == ignoredPieceId || piece.AttachedToId == ignoredPieceId)
      .Select(piece => piece.Id)
      .ToHashSet(StringComparer.Ordinal);
    return !pieces.Any(piece => !ignored.Contains(piece.Id) &&
      (rule.Type == "Farm" || piece.Type != "Farm") &&
      !(teamWhoseEnemiesMayBeOverlapped is NetworkTeam team && piece.Team != team) &&
      UnitRules.TryGet(piece.Type, out UnitRule otherRule) &&
      UnitRules.FootprintsOverlap(piece.X, piece.Y, otherRule.Width, otherRule.Height, x, y, rule.Width, rule.Height));
  }

  private static bool CanTravelThrough(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece piece,
    UnitRule rule,
    (int x, int y) from,
    (int x, int y) destination
  )
  {
    foreach ((int x, int y) position in PositionsBetween(from, destination))
    {
      foreach ((int x, int y) square in OccupiedSquares(rule, position))
      {
        bool ignoresTerrain = AbilityRules.IgnoresImpassableTerrain(rule) ||
          IsPalaceAssistedMovement(pieces, piece, rule, from, destination);
        if (!BoardRules.Contains(state.Board, square.x, square.y) ||
            (!ignoresTerrain && state.Terrain.IsLake(square)) || state.Barricades.ContainsKey(square))
        {
          return false;
        }
        NetworkPiece? blocker = pieces.FirstOrDefault(other => other.Id != piece.Id && other.AttachedToId != piece.Id &&
          other.Type != "Farm" && UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, square));
        if (blocker is not null && !AbilityRules.CanTravelThroughUnit(rule, piece.Team, blocker.Team) &&
            GetChessCaptureTarget(pieces, piece, rule, position) != blocker)
        {
          return false;
        }
      }
    }
    return true;
  }

  private static int GetMovementCost(CpuGameState state, NetworkPiece piece, UnitRule rule, (int x, int y) destination)
  {
    return GetMovementCost(state, state.Pieces, piece, rule, (piece.X, piece.Y), destination);
  }

  private static int GetMovementCost(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece piece,
    UnitRule rule,
    (int x, int y) from,
    (int x, int y) destination
  )
  {
    int cost = 0;
    bool ignoresTerrain = IsPalaceAssistedMovement(pieces, piece, rule, from, destination);
    foreach ((int x, int y) square in OccupiedSquares(rule, destination))
    {
      bool usesOwnedRoad = state.Roads.TryGetValue(square, out NetworkTeam roadOwner) &&
        (roadOwner == piece.Team || roadOwner == NetworkTeam.Neutral);
      int ordinaryCost = state.Terrain.IsForest(square) && !usesOwnedRoad && !ignoresTerrain
        ? 2
        : usesOwnedRoad && !state.Terrain.IsForest(square) ? 0 : 1;
      cost = Math.Max(cost, AbilityRules.ApplyTerrainMovementCost(rule, ordinaryCost));
    }
    return cost;
  }

  private static bool CrossesRiver(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece piece,
    UnitRule rule,
    (int x, int y) from,
    (int x, int y) to
  )
  {
    if (AbilityRules.IgnoresRivers(rule) || IsPalaceAssistedMovement(pieces, piece, rule, from, to))
    {
      return false;
    }
    foreach ((int x, int y) fromSquare in OccupiedSquares(rule, from))
    {
      (int x, int y) toSquare = (fromSquare.x + to.x - from.x, fromSquare.y + to.y - from.y);
      if (StepsBetween(fromSquare, toSquare).Any(edge => state.Terrain.HasRiverBetween(edge.first, edge.second) &&
        !state.RiverBridges.Contains(TileEdge.Between(edge.first, edge.second))))
      {
        return true;
      }
    }
    return false;
  }

  private static bool HasPalaceSupport(IReadOnlyList<NetworkPiece> pieces, NetworkPiece piece) =>
    piece.Type != "Palace" && pieces.Any(candidate => candidate.Team == piece.Team &&
      candidate.AttachedToId is null && candidate.Type == "Palace");

  private static bool IsPalaceAssistedMovement(
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece piece,
    UnitRule movingRule,
    (int x, int y) from,
    (int x, int y) to
  )
  {
    NetworkPiece? palace = pieces.FirstOrDefault(candidate => candidate.Team == piece.Team &&
      candidate.AttachedToId is null && candidate.Type == "Palace");
    return palace is not null && UnitRules.TryGet(palace.Type, out UnitRule palaceRule) &&
      AbilityRules.MovesTowardPalace(movingRule, from, to, palaceRule, (palace.X, palace.Y));
  }

  private static bool HasClearAttackPath(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece attacker,
    NetworkPiece target,
    IReadOnlyDictionary<(int x, int y), int> barricades
  )
  {
    if (!UnitRules.TryGet(target.Type, out UnitRule targetRule))
    {
      return false;
    }
    return OccupiedSquares(targetRule, (target.X, target.Y)).Any(targetSquare =>
      HasClearAttackPath(state, pieces, attacker, targetSquare, target.Id, barricades));
  }

  private static bool HasClearAttackPath(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece attacker,
    (int x, int y) target,
    string? targetId,
    IReadOnlyDictionary<(int x, int y), int> barricades
  )
  {
    if (!UnitRules.TryGet(attacker.Type, out UnitRule rule))
    {
      return false;
    }
    return LineOfSightRules.HasClearAttackPath(
      rule,
      OccupiedSquares(rule, (attacker.X, attacker.Y)),
      target,
      state.Terrain.IsForest,
      barricades.ContainsKey,
      square => pieces.Any(other => other.Id != attacker.Id && other.Id != targetId && other.AttachedToId is null &&
        other.Type != "Farm" && !(attacker.Type == "Sorceress" && other.Team == attacker.Team) &&
        UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, square))
    );
  }

  private static bool CanPickUpTreasure(CpuGameState state, NetworkPiece actor, int targetX, int targetY) =>
    state.Configuration.GameMode == "Plunder" && state.TreasureCarrierId is null && state.TreasurePosition == (targetX, targetY) &&
    actor.AttachedToId is null && UnitRules.TryGet(actor.Type, out UnitRule rule) && rule.Width == 1 && rule.Height == 1 &&
    rule.Category != RuleCategory.Royal && !PieceOccupies(state.Pieces, (targetX, targetY)) &&
    Math.Abs(actor.X - targetX) + Math.Abs(actor.Y - targetY) == 1;

  private static bool PieceOccupies(IEnumerable<NetworkPiece> pieces, (int x, int y) position) => pieces.Any(piece =>
    UnitRules.TryGet(piece.Type, out UnitRule rule) && Occupies(rule, piece, position));

  private static bool Occupies(UnitRule rule, NetworkPiece piece, (int x, int y) position) =>
    position.x >= piece.X && position.x < piece.X + rule.Width && position.y >= piece.Y && position.y < piece.Y + rule.Height;

  private static IEnumerable<(int x, int y)> OccupiedSquares(UnitRule rule, (int x, int y) position)
  {
    for (int offsetY = 0; offsetY < rule.Height; offsetY++)
    {
      for (int offsetX = 0; offsetX < rule.Width; offsetX++)
      {
        yield return (position.x + offsetX, position.y + offsetY);
      }
    }
  }

  private static IEnumerable<(int x, int y)> PositionsBetween((int x, int y) from, (int x, int y) destination)
  {
    int steps = Math.Max(Math.Abs(destination.x - from.x), Math.Abs(destination.y - from.y));
    for (int step = 1; step <= steps; step++)
    {
      yield return (
        from.x + (int)MathF.Round((destination.x - from.x) * step / (float)steps),
        from.y + (int)MathF.Round((destination.y - from.y) * step / (float)steps)
      );
    }
  }

  private static IEnumerable<((int x, int y) first, (int x, int y) second)> StepsBetween((int x, int y) from, (int x, int y) to)
  {
    int steps = Math.Max(Math.Abs(to.x - from.x), Math.Abs(to.y - from.y));
    (int x, int y) current = from;
    for (int step = 1; step <= steps; step++)
    {
      (int x, int y) next = (
        from.x + (int)MathF.Round((to.x - from.x) * step / (float)steps),
        from.y + (int)MathF.Round((to.y - from.y) * step / (float)steps)
      );
      if (next.x != current.x && next.y != current.y)
      {
        yield return (current, (next.x, current.y));
        yield return ((next.x, current.y), next);
        yield return (current, (current.x, next.y));
        yield return ((current.x, next.y), next);
      }
      else
      {
        yield return (current, next);
      }
      current = next;
    }
  }
}
