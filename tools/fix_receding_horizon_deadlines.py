from pathlib import Path

# Tighten the two remaining inner-loop checks to the current receding-horizon slice.
path = Path('MedivalChess.CPU/CpuSearch.cs')
text = path.read_text()
start = text.index('  private SearchIterationResult RunSearchIteration(')
end = text.index('  private List<SearchNode> ContinueRecedingHorizon(', start)
segment = text[start:end]
segment = segment.replace(
'''if (ShouldStop(stopwatch, profile.Search, nodesGenerated + pending.Count, cancellationToken,
            out timedOut, out nodeBudgetReached, out cancelled))''',
'''if (ShouldStop(stopwatch, profile.Search, nodesGenerated + pending.Count, cancellationToken, softDeadlineMilliseconds,
            out timedOut, out nodeBudgetReached, out cancelled))''')
segment = segment.replace(
'''if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken,
          out timedOut, out nodeBudgetReached, out cancelled))''',
'''if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken, softDeadlineMilliseconds,
          out timedOut, out nodeBudgetReached, out cancelled))''')
text = text[:start] + segment + text[end:]
path.write_text(text)

# Normal farm purchase clustering should use the same forward-protection baseline as farm scoring.
path = Path('MedivalChess.CPU/CpuActionGenerator.cs')
text = path.read_text()
old = '''    int furthestForwardProjection = openingFarmPlacement
      ? CpuPlacementHeuristics.GetFurthestForwardProjection(state, team)
      : 0;
'''
new = '''    int furthestForwardProjection = state.Configuration.FarmsEnabled
      ? CpuPlacementHeuristics.GetFurthestForwardProjection(state, team)
      : 0;
'''
if old not in text:
  raise SystemExit('farm projection anchor not found')
path.write_text(text.replace(old, new, 1))
