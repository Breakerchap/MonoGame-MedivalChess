using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>
/// Board-aware implementation of the soft strategic guidance in <c>Combos.md</c>.
/// These values intentionally supplement the tactical search rather than replacing it: a legal
/// immediate win, a campaign objective, and the real damage simulation remain more important.
/// </summary>
public static class CpuStrategicHeuristics
{
  // A leading '$' denotes a UnitRule category and a leading '#' denotes a board situation.
  // Keeping the reference data here (rather than in UI code) makes it usable by search,
  // purchase selection, and headless simulations alike.
  private static readonly IReadOnlyDictionary<string, string[]> GoodAgainst = new Dictionary<string, string[]>(StringComparer.Ordinal)
  {
    ["Swordsman"] = ["Peasant", "Archer", "Spy", "Engineer", "Bombard", "Cannon"],
    ["Defender"] = ["Swordsman", "Peasant", "Guard", "Mercenary"],
    ["Archer"] = ["Defender", "Peasant", "Engineer", "Spy"],
    ["Peasant"] = ["#DamagedExpensive"],
    ["Knight"] = ["Archer", "Spy", "Engineer", "Bombard", "Cannon", "Catapult", "Ballista", "Cavalier", "Swordsman", "Defender", "Peasant", "Mercenary"],
    ["Crossbowman"] = ["Swordsman", "Archer", "Spy", "Bombard", "Engineer", "Mercenary"],
    ["Cavalier"] = ["Archer", "Crossbowman", "Spy", "Engineer", "Bombard", "Cannon", "Catapult"],
    ["Chariot"] = ["Archer", "Crossbowman", "Spy", "Engineer", "Bombard"],
    ["Cannon"] = ["Knight", "Defender", "Guard", "$Mechanical", "$Large"],
    ["Spy"] = ["King", "Sorceress", "Palace", "Baron", "Elephant", "Knight", "Guard", "$Mechanical", "#HighHealth"],
    ["Catapult"] = ["Archer", "Crossbowman", "Cannon", "Ballista", "Bombard", "Engineer", "Sorceress", "Palace", "Baron"],
    ["Bombard"] = ["#Clustered"],
    ["Ballista"] = ["Defender", "Baron", "$Mechanical", "$Large", "#Aligned"],
    ["Elephant"] = ["Peasant", "Defender", "#Barricade"],
    ["Mercenary"] = ["Archer", "Spy", "Engineer", "Bombard"],
    ["King"] = ["Peasant", "Swordsman", "Defender", "Mercenary"],
    ["Sorceress"] = ["Archer", "Defender"],
    ["Baron"] = ["Peasant", "Swordsman", "Defender"]
  };

  // The original table expressed only what each unit likes to fight. Combos.md also gives the
  // reverse half of the relationship, which is crucial for recruitment: a unit that has a target
  // must still be rejected when it will be immediately countered by the enemy formation.
  private static readonly IReadOnlyDictionary<string, string[]> BadAgainst = new Dictionary<string, string[]>(StringComparer.Ordinal)
  {
    ["Swordsman"] = ["Knight", "Crossbowman", "Chariot", "Cannon", "Defender"],
    ["Defender"] = ["Cannon", "Crossbowman", "Ballista", "Bombard", "Spy"],
    ["Archer"] = ["Knight", "Cavalier", "Chariot", "Crossbowman", "Catapult", "Sorceress"],
    ["Peasant"] = ["Bombard", "Ballista", "Elephant"],
    ["Knight"] = ["Cannon", "Crossbowman", "Ballista", "Spy"],
    ["Crossbowman"] = ["Knight", "Cavalier", "Chariot", "Catapult", "Defender"],
    ["Cavalier"] = ["Knight", "Defender"],
    ["Chariot"] = ["Cannon", "Catapult", "Ballista"],
    ["Cannon"] = ["Knight", "Cavalier", "Chariot", "Swordsman", "Peasant"],
    ["Spy"] = ["Archer", "Knight", "Cavalier", "Chariot", "Crossbowman"],
    ["Catapult"] = ["Knight", "Cavalier", "Chariot"],
    ["Bombard"] = ["Knight", "Cavalier", "Chariot", "Crossbowman"],
    ["Ox"] = ["Knight", "Cavalier", "Chariot", "Crossbowman"],
    ["Engineer"] = ["Archer", "Knight", "Cavalier", "Chariot", "Catapult"],
    ["Ballista"] = ["Knight", "Cavalier", "Chariot"],
    ["Elephant"] = ["Spy", "Cannon", "Ballista", "Crossbowman"],
    ["Guard"] = ["Cannon", "Spy", "Bombard"],
    ["Mercenary"] = ["Defender", "Knight", "Crossbowman"],
    ["Farm"] = ["Knight", "Cavalier", "Chariot", "Cannon", "Catapult", "Ballista", "Bombard"],
    ["King"] = ["Cannon", "Ballista", "Spy"],
    ["Sorceress"] = ["Knight", "Cavalier", "Chariot", "Cannon", "Catapult", "Ballista"],
    ["Palace"] = ["Cannon", "Catapult", "Ballista"],
    ["Baron"] = ["Bombard", "Ballista", "Catapult"],
  };

  private static readonly HashSet<string> ValuableRanged = new(StringComparer.Ordinal)
  {
    "Crossbowman", "Cannon", "Catapult", "Ballista"
  };

  private static readonly HashSet<string> Artillery = new(StringComparer.Ordinal)
  {
    "Cannon", "Catapult", "Ballista"
  };

  private static readonly HashSet<string> FastDivers = new(StringComparer.Ordinal)
  {
    "Knight", "Cavalier", "Chariot"
  };

  private static readonly HashSet<string> GuardPriorities = new(StringComparer.Ordinal)
  {
    "Cannon", "Catapult", "Ballista", "Crossbowman", "Spy", "Engineer"
  };

  /// <summary>Returns a positive score when the first unit is the better matchup.</summary>
  public static float GetMatchupScore(CpuGameState state, NetworkPiece attacker, NetworkPiece target)
  {
    if (!UnitRules.TryGet(attacker.Type, out UnitRule attackerRule) ||
        !UnitRules.TryGet(target.Type, out UnitRule targetRule))
    {
      return 0f;
    }

    float score = 0f;
    bool attackerCountersTarget = IsListedGoodAgainst(state, attacker, target);
    bool targetCountersAttacker = IsListedGoodAgainst(state, target, attacker);
    if (attackerCountersTarget) score += 1f;
    if (targetCountersAttacker) score -= 1f;

    // A Defender wall, a fragile ranged unit, and artillery minimum range are all positional
    // parts of the reference.  The term stays modest so exact attack simulation can disagree.
    if (attacker.Type == "Defender" && targetRule.Category == RuleCategory.Melee && targetRule.Attack <= 15) score += 0.35f;
    if (target.Type == "Defender" && attackerRule.Category == RuleCategory.Melee && attackerRule.Attack <= 15) score -= 0.35f;
    if (attacker.Type == "Cannon" && Distance(attacker, target) <= 1) score -= 0.8f;
    if (target.Type == "Cannon" && Distance(attacker, target) <= 1) score += 0.8f;
    if (attacker.Type is "Catapult" or "Bombard" && Distance(attacker, target) < attackerRule.MinimumAttackRange) score -= 0.65f;
    if (target.Type is "Catapult" or "Bombard" && Distance(attacker, target) < targetRule.MinimumAttackRange) score += 0.65f;
    return score;
  }

  /// <summary>
  /// Full counter relationship used for recruitment. The normal board evaluator stays focused on
  /// concrete positions, while this adds the explicit "Bad against" half of Combos.md to reject
  /// a new unit that would simply walk into an established counter formation.
  /// </summary>
  internal static float GetRecruitmentMatchupScore(CpuGameState state, NetworkPiece attacker, NetworkPiece target)
  {
    float score = GetMatchupScore(state, attacker, target);
    if (IsListedBadAgainst(state, attacker, target)) score -= 0.9f;
    if (IsListedBadAgainst(state, target, attacker)) score += 0.9f;
    return score;
  }

  /// <summary>Scores one team's counters, supported formations, and avoidable anti-combos.</summary>
  public static float ScoreTeam(CpuGameState state, NetworkTeam team)
  {
    NetworkPiece[] friendly = ActivePieces(state, team).ToArray();
    NetworkPiece[] enemies = state.Pieces.Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral &&
      piece.AttachedToId is null).ToArray();
    float score = 0f;

    foreach (NetworkPiece friendlyPiece in friendly)
    {
      if (!UnitRules.TryGet(friendlyPiece.Type, out UnitRule friendlyRule)) continue;
      foreach (NetworkPiece enemy in enemies)
      {
        if (!UnitRules.TryGet(enemy.Type, out UnitRule enemyRule)) continue;
        float proximity = GetEngagementProximity(friendlyPiece, friendlyRule, enemy, enemyRule);
        if (proximity <= 0f) continue;
        float matchup = GetMatchupScore(state, friendlyPiece, enemy);
        if (matchup == 0f) continue;
        // Exact attack lines are already evaluated by ThreatEvaluation and search. Keeping this
        // formation-level term geometric avoids tracing paths for every pair in every node.
        score += matchup * proximity * 9f;
      }
    }

    score += ScoreSupportedPairs(state, team, friendly);
    score += ScoreAppliedAbilities(state, team, friendly);
    score -= ScoreAntiComboRisks(state, team, friendly, enemies);
    return score;
  }

  /// <summary>Small, immediate action adjustment used before beam pruning.</summary>
  public static float ScoreAction(CpuGameState state, ICpuGameAction action)
  {
    return action switch
    {
      AttackAction attack => ScoreAttackAction(state, attack),
      UseAbilityAction ability => ScoreAbilityAction(state, ability),
      PurchaseAction purchase => ScorePurchaseAction(state, purchase),
      MoveAction move => ScoreMoveAction(state, move),
      _ => 0f
    };
  }

  private static float ScoreAttackAction(CpuGameState state, AttackAction action)
  {
    NetworkPiece? attacker = state.Pieces.FirstOrDefault(piece => piece.Id == action.AttackerId);
    NetworkPiece? target = action.TargetPieceId is null ? null : state.Pieces.FirstOrDefault(piece => piece.Id == action.TargetPieceId);
    if (attacker is null || target is null) return 0f;

    float score = GetMatchupScore(state, attacker, target) * 14f;
    if (attacker.Type == "Cannon" && target.Health <= 5 &&
        !IsStrategicallyImportant(state, target))
    {
      // Cannon's damage is more valuable on a durable target unless this shot decides something.
      score -= 20f;
    }
    if (attacker.Type == "Bombard")
    {
      score += CountAdjacentTargets(state, target, action.Team) * 7f;
      score -= CountAdjacentFriendlies(state, target, action.Team) * 8f;
    }
    if (attacker.Type == "Ballista")
    {
      score += CountPiercedEnemies(state, attacker, target) * 9f;
    }
    return score;
  }

  private static float ScoreAbilityAction(CpuGameState state, UseAbilityAction action)
  {
    NetworkPiece? actor = state.Pieces.FirstOrDefault(piece => piece.Id == action.ActorId);
    NetworkPiece? target = action.TargetPieceId is null ? null : state.Pieces.FirstOrDefault(piece => piece.Id == action.TargetPieceId);
    if (actor is null) return 0f;

    if (actor.Type == "Spy" && action.Ability.Equals("Mark", StringComparison.OrdinalIgnoreCase) && target is not null)
    {
      NetworkPiece[] followUps = ActivePieces(state, action.Team)
        .Where(piece => piece.Id != actor.Id && piece.Type is "Cannon" or "Ballista" or "Crossbowman")
        .Where(piece => CpuGameRules.CanDirectlyAttack(state, piece, target))
        .ToArray();
      float score = followUps.Length == 0 ? -24f : 22f + followUps.Length * 18f;
      score += Math.Max(0f, GetMatchupScore(state, actor, target)) * 8f;
      return score;
    }

    if (actor.Type == "Guard" && action.Ability.Equals("Attach", StringComparison.OrdinalIgnoreCase) && target is not null)
    {
      // A Guard effectively buys the protected piece another health bar, so replacement cost is
      // the primary decision. Threat and the documented artillery/ranged pairings refine that
      // choice, but a cheap unit should not steal a Guard from a much more expensive asset.
      float score = GetGuardProtectionPriority(state, target);
      float bestAvailable = GetBestUnguardedGuardPriority(state, action.Team, actor.Id);
      if (bestAvailable > score)
      {
        // Penalise spending the one-shot attachment on a noticeably worse target. Keep the
        // penalty partial because the best board-wide target may not be in this Guard's range.
        score -= (bestAvailable - score) * 0.45f;
      }
      return score;
    }

    if (actor.Type == "Ox" && action.Ability.Equals("Attach", StringComparison.OrdinalIgnoreCase) && target is not null)
    {
      return Artillery.Contains(target.Type) ? 26f : UnitRules.TryGet(target.Type, out UnitRule rule) &&
        rule.Category == RuleCategory.Mechanical ? 18f : -5f;
    }

    if (actor.Type == "Engineer" && action.Ability is "Barrier" or "Mine" or "Road")
    {
      bool nearArtillery = ActivePieces(state, action.Team).Any(piece => Artillery.Contains(piece.Type) &&
        Distance((action.TargetX, action.TargetY), (piece.X, piece.Y)) <= 2);
      bool blocksDiver = state.Pieces.Where(piece => piece.Team != action.Team && FastDivers.Contains(piece.Type))
        .Any(piece => Distance((action.TargetX, action.TargetY), (piece.X, piece.Y)) <= 3);
      return (nearArtillery ? 16f : 0f) + (blocksDiver ? 8f : 0f) +
        (CreatesBombardCluster(state, action.Team, (action.TargetX, action.TargetY)) ? -16f : 0f);
    }

    return 0f;
  }

  private static float ScorePurchaseAction(CpuGameState state, PurchaseAction action)
  {
    if (!UnitRules.TryGet(action.UnitType, out UnitRule purchasedRule) || purchasedRule.Type == "Farm") return 0f;
    NetworkPiece prototype = new("strategy-prototype", purchasedRule.Type, action.Team, action.X, action.Y, purchasedRule.Health);
    float score = 0f;
    foreach (NetworkPiece enemy in state.Pieces.Where(piece => piece.Team != action.Team && piece.Team != NetworkTeam.Neutral &&
      piece.AttachedToId is null))
    {
      score += GetRecruitmentMatchupScore(state, prototype, enemy) * 10f;
    }

    score += action.UnitType switch
    {
      "Cannon" or "Catapult" or "Ballista" when ActivePieces(state, action.Team).Any(piece => piece.Type == "Engineer" || piece.Type == "Defender" || piece.Type == "Guard" || piece.Type == "Ox") => 12f,
      "Spy" when ActivePieces(state, action.Team).Any(piece => piece.Type is "Cannon" or "Ballista" or "Crossbowman") => 10f,
      "Guard" => Math.Clamp(GetBestUnguardedGuardPriority(state, action.Team) * 0.22f, 0f, 34f),
      "Defender" when ActivePieces(state, action.Team).Any(piece => ValuableRanged.Contains(piece.Type) || piece.Type is "Bombard" or "Sorceress") => 9f,
      "Peasant" when ActivePieces(state, action.Team).Any(piece => ValuableRanged.Contains(piece.Type)) => 6f,
      "Engineer" when ActivePieces(state, action.Team).Any(piece => Artillery.Contains(piece.Type) || piece.Type == "Bombard") => 9f,
      _ => 0f
    };
    return score;
  }

  private static float ScoreMoveAction(CpuGameState state, MoveAction action)
  {
    NetworkPiece? mover = state.Pieces.FirstOrDefault(piece => piece.Id == action.PieceId);
    if (mover is null) return 0f;
    (int x, int y) destination = (action.DestinationX, action.DestinationY);
    float score = 0f;
    foreach (NetworkPiece ally in ActivePieces(state, action.Team).Where(piece => piece.Id != mover.Id))
    {
      int before = Distance(mover, ally);
      int after = Distance(destination, (ally.X, ally.Y));
      if (Supports(mover.Type, ally.Type)) score += (before - after) * 2.5f;
    }
    if (mover.Type == "Guard")
    {
      score += ScoreGuardApproach(state, action.Team, mover, destination);
    }
    if (mover.Type == "Peasant" && IsBombardThreatenedCluster(state, action.Team, destination)) score -= 12f;
    if ((ValuableRanged.Contains(mover.Type) || IsMechanical(mover)) && IsWithinDiverReach(state, action.Team, destination)) score -= 10f;
    return score;
  }

  private static float ScoreSupportedPairs(CpuGameState state, NetworkTeam team, IReadOnlyList<NetworkPiece> friendly)
  {
    float score = 0f;
    for (int firstIndex = 0; firstIndex < friendly.Count; firstIndex++)
    {
      NetworkPiece first = friendly[firstIndex];
      for (int secondIndex = firstIndex + 1; secondIndex < friendly.Count; secondIndex++)
      {
        NetworkPiece second = friendly[secondIndex];
        int distance = Distance(first, second);
        if (distance > 3) continue;
        float closeness = distance <= 1 ? 1f : distance == 2 ? 0.65f : 0.3f;
        score += GetPairBonus(first.Type, second.Type) * closeness;
      }
    }

    // Baron and King operate through adjacency.  This evaluates the actual radius, not merely
    // ownership of the royal, and therefore encourages the stated local formations.
    foreach (NetworkPiece royal in friendly.Where(piece => piece.Type is "Baron" or "King" or "Sorceress"))
    {
      foreach (NetworkPiece ally in friendly.Where(piece => piece.Id != royal.Id && Distance(piece, royal) <= 1))
      {
        score += (royal.Type, ally.Type) switch
        {
          ("Baron", "Peasant") => 17f,
          ("Baron", "Defender") => 13f,
          ("Baron", "Swordsman") => 11f,
          ("King", "Defender") => 13f,
          ("King", "Guard") => 10f,
          ("Sorceress", "Defender") => 12f,
          _ => 0f
        };
      }
    }
    return score;
  }

  private static float ScoreAppliedAbilities(CpuGameState state, NetworkTeam team, IReadOnlyList<NetworkPiece> friendly)
  {
    float score = 0f;
    foreach (NetworkPiece spy in friendly.Where(piece => piece.Type == "Spy" && piece.MarkedTargetId is not null))
    {
      NetworkPiece? target = state.Pieces.FirstOrDefault(piece => piece.Id == spy.MarkedTargetId && piece.Team != team);
      if (target is null) continue;
      int hitters = friendly.Count(piece => piece.Id != spy.Id && piece.Type is "Cannon" or "Ballista" or "Crossbowman" &&
        CpuGameRules.CanDirectlyAttack(state, piece, target));
      score += hitters == 0 ? -12f : 18f + hitters * 16f;
    }

    foreach (NetworkPiece guard in state.Pieces.Where(piece => piece.Team == team && piece.AttachmentKind == NetworkAttachmentKind.Guard &&
      piece.AttachedToId is not null))
    {
      NetworkPiece? protectedPiece = state.Pieces.FirstOrDefault(piece => piece.Id == guard.AttachedToId);
      if (protectedPiece is null) continue;
      float guardHealth = UnitRules.TryGet(guard.Type, out UnitRule guardRule)
        ? Math.Clamp(guard.Health / (float)Math.Max(1, guardRule.Health), 0f, 1f)
        : 1f;
      // Once attached, value the real asset being protected rather than the Guard's own flat
      // material value. A damaged Guard still helps, but proportionally less.
      score += GetGuardProtectionPriority(state, protectedPiece) * 0.38f * (0.35f + guardHealth * 0.65f);
    }

    foreach (NetworkPiece ox in friendly.Where(piece => piece.Type == "Ox"))
    {
      NetworkPiece? cargo = state.Pieces.FirstOrDefault(piece => piece.AttachedToId == ox.Id && piece.AttachmentKind == NetworkAttachmentKind.Carried);
      if (cargo is not null) score += Artillery.Contains(cargo.Type) ? 23f : IsMechanical(cargo) ? 15f : 2f;
    }
    return score;
  }

  private static float ScoreAntiComboRisks(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<NetworkPiece> friendly,
    IReadOnlyList<NetworkPiece> enemies
  )
  {
    float risk = 0f;
    foreach (NetworkPiece bombard in enemies.Where(piece => piece.Type == "Bombard"))
    {
      float worstBlast = 0f;
      foreach (NetworkPiece target in friendly.Where(target => CpuGameRules.CanDirectlyAttack(state, bombard, target)))
      {
        float blast = CountAdjacentFriendlies(state, target, team) * 10f;
        blast += state.Pieces.Where(piece => piece.Team == team && piece.AttachedToId is null && Distance(piece, target) <= 1)
          .Sum(piece => IsHighValue(piece) ? 7f : piece.Type == "Peasant" ? 3f : 0f);
        worstBlast = Math.Max(worstBlast, blast);
      }
      risk += worstBlast;
    }

    foreach (NetworkPiece ballista in enemies.Where(piece => piece.Type == "Ballista"))
    {
      foreach (NetworkPiece target in friendly.Where(target => CpuGameRules.CanDirectlyAttack(state, ballista, target)))
      {
        risk += CountPiercedEnemies(state, ballista, target, team) * 12f;
      }
    }

    foreach (NetworkPiece piece in friendly.Where(piece => ValuableRanged.Contains(piece.Type) || IsMechanical(piece)))
    {
      if (IsWithinDiverReach(state, team, (piece.X, piece.Y))) risk += 13f;
    }

    foreach (NetworkPiece catapult in friendly.Where(piece => piece.Type == "Catapult"))
    {
      bool closeEnemy = enemies.Any(enemy => Distance(catapult, enemy) < UnitRules.GetRequired("Catapult").MinimumAttackRange + 1);
      bool screened = friendly.Any(ally => ally.Type is "Defender" or "Guard" && Distance(catapult, ally) <= 2);
      if (closeEnemy && !screened) risk += 15f;
    }

    int nearbyBarricades = state.Barricades.Keys.Count(position => friendly.Any(piece => piece.Type == "Engineer" &&
      Distance(position, (piece.X, piece.Y)) <= 3));
    if (nearbyBarricades >= 3 && enemies.Any(piece => piece.Type == "Bombard")) risk += (nearbyBarricades - 2) * 7f;
    float urgentGuardTarget = GetBestThreatenedUnguardedGuardPriority(state, team);
    if (urgentGuardTarget > 0f)
    {
      foreach (NetworkPiece guard in state.Pieces.Where(piece => piece.Team == team && piece.Type == "Guard" &&
        piece.AttachmentKind == NetworkAttachmentKind.Guard && piece.AttachedToId is not null))
      {
        NetworkPiece? attached = state.Pieces.FirstOrDefault(target => target.Id == guard.AttachedToId);
        if (attached is null) continue;
        float attachedPriority = GetGuardProtectionPriority(state, attached);
        if (urgentGuardTarget > attachedPriority + 18f)
        {
          // This primarily affects the branch where the Guard is choosing what to attach to: it
          // rejects sacrificing the attachment on a cheap piece while an expensive threatened
          // asset is still exposed.
          risk += Math.Clamp((urgentGuardTarget - attachedPriority) * 0.22f, 8f, 30f);
        }
      }
    }
    return risk;
  }

  private static bool IsListedGoodAgainst(CpuGameState state, NetworkPiece source, NetworkPiece target) =>
    GoodAgainst.TryGetValue(source.Type, out string[]? patterns) &&
    patterns.Any(pattern => MatchesPattern(state, source, target, pattern));

  private static bool IsListedBadAgainst(CpuGameState state, NetworkPiece source, NetworkPiece target) =>
    BadAgainst.TryGetValue(source.Type, out string[]? patterns) &&
    patterns.Any(pattern => MatchesPattern(state, source, target, pattern));

  private static bool MatchesPattern(CpuGameState state, NetworkPiece source, NetworkPiece target, string pattern)
  {
    if (string.Equals(pattern, target.Type, StringComparison.Ordinal)) return true;
    if (!UnitRules.TryGet(target.Type, out UnitRule targetRule)) return false;
    return pattern switch
    {
      "$Mechanical" => targetRule.Category == RuleCategory.Mechanical,
      "$Large" => targetRule.Width * targetRule.Height > 1,
      "#HighHealth" => target.Health >= 30,
      "#DamagedExpensive" => targetRule.Cost >= 35 && target.Health <= Math.Max(10, targetRule.Health / 2),
      "#Clustered" => CountAdjacentTargets(state, target, source.Team) >= 1,
      "#Aligned" => CountPiercedEnemies(state, source, target) >= 1,
      "#Barricade" => state.Barricades.Keys.Any(position => Distance((target.X, target.Y), position) <= 2),
      _ => false
    };
  }

  private static float GetPairBonus(string first, string second)
  {
    string left = string.CompareOrdinal(first, second) <= 0 ? first : second;
    string right = left == first ? second : first;
    return (left, right) switch
    {
      ("Cannon", "Spy") or ("Ballista", "Spy") => 18f,
      ("Crossbowman", "Spy") => 14f,
      ("Archer", "Defender") or ("Crossbowman", "Defender") or ("Cannon", "Defender") or
        ("Catapult", "Defender") or ("Ballista", "Defender") or ("Bombard", "Defender") => 12f,
      ("Catapult", "Guard") or ("Ballista", "Guard") or ("Cannon", "Guard") => 16f,
      ("Guard", "Spy") => 12f,
      ("Cannon", "Ox") or ("Catapult", "Ox") or ("Ballista", "Ox") => 13f,
      ("Cannon", "Engineer") or ("Catapult", "Engineer") or ("Ballista", "Engineer") or ("Bombard", "Engineer") => 13f,
      ("Bombard", "Elephant") => 10f,
      ("Archer", "Cavalier") or ("Cavalier", "Crossbowman") or ("Cannon", "Cavalier") or ("Cavalier", "Sorceress") => 8f,
      ("Archer", "Knight") => 10f,
      ("Crossbowman", "Peasant") or ("Cannon", "Peasant") or ("Catapult", "Peasant") or ("Ballista", "Peasant") => 7f,
      ("Archer", "Elephant") or ("Crossbowman", "Elephant") or ("Catapult", "Elephant") or ("Ballista", "Elephant") => 8f,
      _ => 0f
    };
  }

  private static bool Supports(string first, string second) => GetPairBonus(first, second) > 0f ||
    HasRoyalSupport(first, second) || HasRoyalSupport(second, first);

  private static bool HasRoyalSupport(string royal, string ally) =>
    (royal, ally) is ("Baron", "Peasant" or "Defender" or "Swordsman") or ("King", "Defender" or "Guard") or
      ("Sorceress", "Defender");

  private static IEnumerable<NetworkPiece> ActivePieces(CpuGameState state, NetworkTeam team) => state.Pieces.Where(piece =>
    piece.Team == team && piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out _));

  private static bool IsMechanical(NetworkPiece piece) => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    rule.Category == RuleCategory.Mechanical;

  private static bool IsHighValue(NetworkPiece piece) => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    (rule.Cost >= 45 || rule.Category == RuleCategory.Royal || ValuableRanged.Contains(piece.Type));

  private static bool IsStrategicallyImportant(CpuGameState state, NetworkPiece piece) => IsHighValue(piece) ||
    state.TreasureCarrierId == piece.Id || UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal;

  private static bool IsThreatenedNow(CpuGameState state, NetworkPiece target) => state.Pieces.Any(attacker => attacker.Team != target.Team &&
    attacker.Team != NetworkTeam.Neutral && CpuGameRules.CanDirectlyAttack(state, attacker, target));

  private static float GetGuardProtectionPriority(CpuGameState state, NetworkPiece target)
  {
    if (!UnitRules.TryGet(target.Type, out UnitRule rule) || rule.Category == RuleCategory.Royal || target.Type == "Guard")
    {
      return 0f;
    }

    // Cost intentionally dominates. The extra high-cost slope makes a 50-70 gold piece clearly
    // preferable to a 10-25 gold body even when the cheaper unit happens to be under light fire.
    float cost = Math.Max(0f, rule.Cost);
    float priority = cost * 1.65f + Math.Max(0f, cost - 30f) * 1.25f;
    if (GuardPriorities.Contains(target.Type)) priority += 9f;
    if (target.Type == "Farm") priority *= 0.55f;

    float healthFraction = Math.Clamp(target.Health / (float)Math.Max(1, rule.Health), 0f, 1f);
    if (healthFraction <= 0.5f) priority += 8f + cost * 0.15f;
    if (IsThreatenedNow(state, target)) priority += 22f + cost * 0.7f;
    if (IsWithinDiverReach(state, target.Team, (target.X, target.Y))) priority += 5f + cost * 0.12f;
    return priority;
  }

  private static IEnumerable<NetworkPiece> UnguardedGuardTargets(CpuGameState state, NetworkTeam team, string? excludingGuardId = null) =>
    ActivePieces(state, team).Where(piece => piece.Id != excludingGuardId && piece.Id != state.TreasureCarrierId &&
      piece.Type != "Guard" && UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category != RuleCategory.Royal &&
      !state.Pieces.Any(guard => guard.AttachmentKind == NetworkAttachmentKind.Guard && guard.AttachedToId == piece.Id));

  private static float GetBestUnguardedGuardPriority(CpuGameState state, NetworkTeam team, string? excludingGuardId = null) =>
    UnguardedGuardTargets(state, team, excludingGuardId)
      .Select(target => GetGuardProtectionPriority(state, target))
      .DefaultIfEmpty(0f)
      .Max();

  private static float GetBestThreatenedUnguardedGuardPriority(CpuGameState state, NetworkTeam team) =>
    UnguardedGuardTargets(state, team)
      .Where(target => IsThreatenedNow(state, target))
      .Select(target => GetGuardProtectionPriority(state, target))
      .DefaultIfEmpty(0f)
      .Max();

  private static float ScoreGuardApproach(
    CpuGameState state,
    NetworkTeam team,
    NetworkPiece guard,
    (int x, int y) destination
  )
  {
    float best = 0f;
    foreach (NetworkPiece target in UnguardedGuardTargets(state, team, guard.Id))
    {
      int progress = Distance((guard.X, guard.Y), (target.X, target.Y)) -
        Distance(destination, (target.X, target.Y));
      if (progress <= 0) continue;
      float targetPriority = GetGuardProtectionPriority(state, target);
      // A single step toward a premium target should survive candidate pruning, while movement
      // toward cheap pieces remains only a mild formation preference.
      best = Math.Max(best, progress * Math.Clamp(targetPriority * 0.16f, 2f, 24f));
    }
    return best;
  }

  private static bool HasThreatenedGuardPriority(CpuGameState state, NetworkTeam team) =>
    GetBestThreatenedUnguardedGuardPriority(state, team) > 0f;

  private static bool IsWithinDiverReach(CpuGameState state, NetworkTeam team, (int x, int y) position) => state.Pieces
    .Where(piece => piece.Team != team && FastDivers.Contains(piece.Type) && piece.AttachedToId is null)
    .Any(diver => UnitRules.TryGet(diver.Type, out UnitRule rule) && Distance((diver.X, diver.Y), position) <= rule.MoveRange + rule.AttackRange + 1);

  private static bool IsBombardThreatenedCluster(CpuGameState state, NetworkTeam team, (int x, int y) position) => state.Pieces
    .Where(piece => piece.Team != team && piece.Type == "Bombard" && piece.AttachedToId is null)
    .Any(bombard => Distance((bombard.X, bombard.Y), position) <= UnitRules.GetRequired("Bombard").AttackRange + 1 &&
      state.Pieces.Any(ally => ally.Team == team && ally.AttachedToId is null && Distance((ally.X, ally.Y), position) <= 1));

  private static bool CreatesBombardCluster(CpuGameState state, NetworkTeam team, (int x, int y) position) =>
    state.Pieces.Where(piece => piece.Team == team && piece.AttachedToId is null).Count(piece => Distance((piece.X, piece.Y), position) <= 1) >= 2 &&
    state.Pieces.Any(piece => piece.Team != team && piece.Type == "Bombard");

  private static int CountAdjacentTargets(CpuGameState state, NetworkPiece target, NetworkTeam attackingTeam) => state.Pieces.Count(piece =>
    piece.Id != target.Id && piece.Team != attackingTeam && piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null &&
    Distance(piece, target) <= 1);

  private static int CountAdjacentFriendlies(CpuGameState state, NetworkPiece target, NetworkTeam attackingTeam) => state.Pieces.Count(piece =>
    piece.Id != target.Id && piece.Team == attackingTeam && piece.AttachedToId is null && Distance(piece, target) <= 1);

  private static int CountPiercedEnemies(CpuGameState state, NetworkPiece ballista, NetworkPiece target, NetworkTeam? expectedTeam = null)
  {
    if (!UnitRules.TryGet(ballista.Type, out UnitRule rule) || ballista.Type != "Ballista") return 0;
    int count = 0;
    foreach ((int x, int y) position in AbilityRules.GetPiercingRay(rule, ballista.X, ballista.Y, target.X, target.Y))
    {
      if (!BoardRules.Contains(state.Board, position.x, position.y) || state.Terrain.IsForest(position) || state.Barricades.ContainsKey(position)) break;
      if (state.Pieces.Any(piece => piece.Id != ballista.Id && piece.Id != target.Id && piece.AttachedToId is null &&
        piece.Type != "Farm" && (expectedTeam is null ? piece.Team != ballista.Team : piece.Team == expectedTeam) && Occupies(piece, position)))
      {
        count++;
      }
    }
    return count;
  }

  private static bool Occupies(NetworkPiece piece, (int x, int y) position) => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    position.x >= piece.X && position.x < piece.X + rule.Width && position.y >= piece.Y && position.y < piece.Y + rule.Height;

  private static float GetEngagementProximity(NetworkPiece first, UnitRule firstRule, NetworkPiece second, UnitRule secondRule)
  {
    int distance = Distance(first, second);
    int reach = Math.Max(firstRule.MoveRange + Math.Max(1, firstRule.AttackRange), secondRule.MoveRange + Math.Max(1, secondRule.AttackRange));
    if (distance > reach + 4) return 0f;
    return Math.Clamp(1f - Math.Max(0, distance - 1) / (float)Math.Max(1, reach + 3), 0.15f, 1f);
  }

  private static int Distance(NetworkPiece first, NetworkPiece second) => Distance((first.X, first.Y), (second.X, second.Y));
  private static int Distance((int x, int y) first, (int x, int y) second) => Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);
}

/// <summary>Converts counter relationships, strong formations, and anti-combo risks into state utility.</summary>
public sealed class MatchupEvaluation : IEvaluationTerm
{
  public string Name => "Matchups";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float own = CpuStrategicHeuristics.ScoreTeam(state, perspective);
    float enemyAverage = TeamRules.GetActiveTeams(state.Configuration.PlayerCount)
      .Where(team => team != perspective)
      .Select(team => CpuStrategicHeuristics.ScoreTeam(state, team))
      .DefaultIfEmpty(0f)
      .Average();
    return own - enemyAverage;
  }
}
