from pathlib import Path

path = Path('MedivalChess.CPU/CpuActionGenerator.cs')
text = path.read_text()

text = text.replace(
'''    NetworkTeam owner = MatchRules.GetSquareOwner(
      state.Board, state.Configuration.GameMode, position, state.Configuration.PlayerCount);
    int territoryBand = owner == team ? 0 : owner == NetworkTeam.Neutral ? 1 : 2;''',
'''    NetworkTeam? owner = MatchRules.GetSquareOwner(
      state.Board, state.Configuration.GameMode, position, state.Configuration.PlayerCount);
    int territoryBand = owner == team ? 0 : owner is null || owner == NetworkTeam.Neutral ? 1 : 2;''')

text = text.replace(
'''      positions.UnionWith(state.Board.Cells.Where(MatchRules.IsConquestSquare));''',
'''      positions.UnionWith(state.Board.Cells.Where(position => MatchRules.IsConquestSquare(state.Board, position)));''')

needle = '''  private static bool OverlapsExistingPiece(CpuGameState state, UnitRule rule, int x, int y) => state.Pieces.Any(piece =>'''
replacement = '''  private static int Distance((int x, int y) first, (int x, int y) second) =>
    Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);

  private static bool OverlapsExistingPiece(CpuGameState state, UnitRule rule, int x, int y) => state.Pieces.Any(piece =>'''
if needle not in text:
    raise SystemExit('Distance insertion point not found')
text = text.replace(needle, replacement, 1)

path.write_text(text)
