using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>Immediate attacks against one piece from a single team's current position.</summary>
public sealed record CpuPieceThreat(
  string PieceId,
  int TotalExpectedDamage,
  int AttackerCount,
  int StrongestAttack,
  bool IsLethal,
  bool IsStrategicallyImportant,
  IReadOnlyList<string> AttackerIds
);

/// <summary>
/// Immutable tactical summary for one team. It is intentionally limited to attacks available now;
/// speculative move-and-attack sequences remain the beam search's responsibility.
/// </summary>
public sealed class CpuThreatMap
{
  private readonly Dictionary<string, CpuPieceThreat> _threatsByPiece;
  private readonly Lazy<IReadOnlySet<(int x, int y)>> _attackedSquares;

  internal CpuThreatMap(
    NetworkTeam attackingTeam,
    IDictionary<string, CpuPieceThreat> threatsByPiece,
    Func<IReadOnlySet<(int x, int y)>> attackedSquaresFactory
  )
  {
    AttackingTeam = attackingTeam;
    _threatsByPiece = new Dictionary<string, CpuPieceThreat>(threatsByPiece, StringComparer.Ordinal);
    _attackedSquares = new Lazy<IReadOnlySet<(int x, int y)>>(attackedSquaresFactory);
  }

  public NetworkTeam AttackingTeam { get; }
  public IReadOnlyDictionary<string, CpuPieceThreat> ThreatsByPiece => _threatsByPiece;
  /// <summary>Threatened board cells, calculated only when debugging or a caller needs them.</summary>
  public IReadOnlySet<(int x, int y)> AttackedSquares => _attackedSquares.Value;

  public bool Threatens(string pieceId) => _threatsByPiece.ContainsKey(pieceId);

  public CpuPieceThreat? GetThreat(string pieceId) =>
    _threatsByPiece.TryGetValue(pieceId, out CpuPieceThreat? threat) ? threat : null;
}

/// <summary>Builds immediate tactical maps through the CPU rule adapter, never through UI state.</summary>
public interface ICpuThreatMapBuilder
{
  CpuThreatMap Build(CpuGameState state, NetworkTeam attackingTeam);
}

public sealed class CpuThreatMapBuilder : ICpuThreatMapBuilder
{
  public CpuThreatMap Build(CpuGameState state, NetworkTeam attackingTeam)
  {
    ArgumentNullException.ThrowIfNull(state);
    Dictionary<string, List<(string attackerId, int damage)>> attacksByTarget = new(StringComparer.Ordinal);
    NetworkPiece[] attackers = state.Pieces
      .Where(piece => piece.Team == attackingTeam && piece.AttachedToId is null && !piece.HasAttackedThisTurn)
      .OrderBy(piece => piece.Id, StringComparer.Ordinal)
      .ToArray();

    foreach (NetworkPiece attacker in attackers)
    {
      if (!UnitRules.TryGet(attacker.Type, out UnitRule attackerRule) || attackerRule.Attack <= 0)
      {
        continue;
      }

      foreach (NetworkPiece target in state.Pieces.Where(piece => piece.Team != attackingTeam &&
        piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null).OrderBy(piece => piece.Id, StringComparer.Ordinal))
      {
        if (!CpuGameRules.CanDirectlyAttack(state, attacker, target))
        {
          continue;
        }

        if (!attacksByTarget.TryGetValue(target.Id, out List<(string attackerId, int damage)>? attacks))
        {
          attacks = [];
          attacksByTarget[target.Id] = attacks;
        }
        attacks.Add((attacker.Id, CpuGameRules.EstimateAttackDamage(state, attacker, target)));
      }
    }

    Dictionary<string, CpuPieceThreat> threats = [];
    foreach ((string targetId, List<(string attackerId, int damage)> attacks) in attacksByTarget)
    {
      NetworkPiece target = state.Pieces.First(piece => piece.Id == targetId);
      bool important = UnitRules.TryGet(target.Type, out UnitRule targetRule) && targetRule.Category == RuleCategory.Royal ||
        state.TreasureCarrierId == target.Id ||
        state.Scenario?.VictoryGoals.Any(goal => goal.GenerateIntents(state, target.Team)
          .Any(intent => intent.PieceId == target.Id || intent.TargetPieceId == target.Id)) == true;
      int totalDamage = attacks.Sum(attack => attack.damage);
      threats[targetId] = new CpuPieceThreat(
        targetId,
        totalDamage,
        attacks.Count,
        attacks.Max(attack => attack.damage),
        totalDamage >= target.Health,
        important,
        attacks.Select(attack => attack.attackerId).ToArray()
      );
    }

    return new CpuThreatMap(attackingTeam, threats, () => BuildAttackedSquares(state, attackers));
  }

  private static IReadOnlySet<(int x, int y)> BuildAttackedSquares(CpuGameState state, IEnumerable<NetworkPiece> attackers)
  {
    HashSet<(int x, int y)> result = [];
    foreach (NetworkPiece attacker in attackers)
    {
      foreach ((int x, int y) square in state.Board.Cells)
      {
        if (CpuGameRules.CanDirectlyAttackSquare(state, attacker, square.x, square.y))
        {
          result.Add(square);
        }
      }
    }
    return result;
  }
}

/// <summary>Per-decision cache for expensive, deterministic evaluation artefacts.</summary>
public sealed class CpuEvaluationCache
{
  private readonly Dictionary<(ulong stateHash, NetworkTeam team), CpuThreatMap> _threatMaps = [];
  private readonly GameStateHasher _hasher = new();

  public CpuThreatMap GetThreatMap(CpuGameState state, NetworkTeam attackingTeam, ICpuThreatMapBuilder builder)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(builder);
    (ulong stateHash, NetworkTeam team) key = (_hasher.ComputeSearchHash(state), attackingTeam);
    if (!_threatMaps.TryGetValue(key, out CpuThreatMap? map))
    {
      map = builder.Build(state, attackingTeam);
      _threatMaps[key] = map;
    }
    return map;
  }
}
