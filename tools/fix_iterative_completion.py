from pathlib import Path

path = Path('MedivalChess.CPU/CpuSearch.cs')
text = path.read_text()
old = '''        IReadOnlyList<EvaluatedSearchExpansion> evaluated = EvaluatePendingBranches(
          batch, team, profile, intents, context, evaluatedStates, parallelism, stopwatch, profile.Search,
          cancellationToken, ref evaluationCacheHits);
        foreach (EvaluatedSearchExpansion branch in evaluated)
'''
new = '''        IReadOnlyList<EvaluatedSearchExpansion> evaluated = EvaluatePendingBranches(
          batch, team, profile, intents, context, evaluatedStates, parallelism, stopwatch, profile.Search,
          cancellationToken, ref evaluationCacheHits);
        if (evaluated.Count < batch.Length)
        {
          cancelled = cancellationToken.IsCancellationRequested;
          timedOut = !cancelled && stopwatch.ElapsedMilliseconds >= Math.Max(1, profile.Search.MaxSearchMilliseconds);
          return new SearchIterationResult(beam, false, fallbackAction, rootLegalActionCount, pvPromotions, macrosGenerated);
        }
        foreach (EvaluatedSearchExpansion branch in evaluated)
'''
if old not in text:
    raise SystemExit('Expected iterative evaluation block not found')
path.write_text(text.replace(old, new, 1))
