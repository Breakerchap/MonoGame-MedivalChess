using System.Globalization;
using System.Text;

namespace MedivalChess.CPU;

/// <summary>Converts CPU search diagnostics into deterministic, concise text for consoles and debug overlays.</summary>
public static class CpuDebugFormatter
{
  public static string FormatDecision(CpuDecisionReport report, int maximumChoices = 3)
  {
    ArgumentNullException.ThrowIfNull(report);
    StringBuilder builder = new();
    builder.Append(report.ProfileName)
      .Append(" (").Append(report.Difficulty).Append(") searched ")
      .Append(report.NodesEvaluated.ToString(CultureInfo.InvariantCulture)).Append(" nodes in ")
      .Append(report.SearchTime.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)).Append(" ms")
      .Append("; ").Append(report.RootLegalActionCount.ToString(CultureInfo.InvariantCulture)).Append(" root actions")
      .Append("; state ").Append(report.InitialStateHash.ToString("X16", CultureInfo.InvariantCulture));

    CpuPersonality personality = report.Personality;
    builder.AppendLine().Append("Personality: aggression ").Append(personality.Aggression.ToString("0.00", CultureInfo.InvariantCulture))
      .Append(", caution ").Append(personality.Caution.ToString("0.00", CultureInfo.InvariantCulture))
      .Append(", objective ").Append(personality.ObjectiveFocus.ToString("0.00", CultureInfo.InvariantCulture))
      .Append(", economy ").Append(personality.EconomyFocus.ToString("0.00", CultureInfo.InvariantCulture))
      .Append(", royal protection ").Append(personality.RoyalProtection.ToString("0.00", CultureInfo.InvariantCulture));

    if (report.TimedOut) builder.Append("; time limit reached");
    if (report.Cancelled) builder.Append("; cancelled");
    if (report.Intentions.Count > 0)
    {
      builder.AppendLine().Append("Intentions: ")
        .Append(string.Join(", ", report.Intentions.Select(intent => $"{intent.Type} ({intent.Priority:0})")));
    }

    int count = Math.Min(Math.Max(0, maximumChoices), report.TopChoices.Count);
    for (int index = 0; index < count; index++)
    {
      CpuChoiceReport choice = report.TopChoices[index];
      builder.AppendLine().Append('#').Append(index + 1).Append(' ')
        .Append(string.Join(" -> ", choice.Actions))
        .Append(" | score ").Append(choice.FinalScore.ToString("0.0", CultureInfo.InvariantCulture))
        .Append(" | ").Append(choice.Reason);
      if (choice.OpponentResponsePenalty > 0f)
      {
        builder.Append(" | opponent response -")
          .Append(choice.OpponentResponsePenalty.ToString("0.0", CultureInfo.InvariantCulture));
      }
      if (choice.EvaluationTerms.Count > 0)
      {
        builder.AppendLine().Append("  Terms: ").Append(string.Join(", ", choice.EvaluationTerms
          .OrderBy(pair => pair.Key, StringComparer.Ordinal)
          .Select(pair => $"{pair.Key}={pair.Value:0.0}")));
      }
    }

    return builder.ToString();
  }

  public static string FormatThreatMap(CpuThreatMap map)
  {
    ArgumentNullException.ThrowIfNull(map);
    if (map.ThreatsByPiece.Count == 0)
    {
      return $"{map.AttackingTeam}: no immediate attacks.";
    }

    return $"{map.AttackingTeam}: " + string.Join("; ", map.ThreatsByPiece.Values
      .OrderBy(threat => threat.PieceId, StringComparer.Ordinal)
      .Select(threat => $"{threat.PieceId} takes {threat.TotalExpectedDamage} from {threat.AttackerCount} attacker(s)" +
        (threat.IsLethal ? " [lethal]" : string.Empty)));
  }
}
