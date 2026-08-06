#nullable enable

using System;
using System.Linq;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess.Campaign;

/// <summary>
/// The one bridge from serialisable campaign data to the live game's board and piece classes.
/// Editor previews, validation helpers, and test play all go through this bridge so unit stats,
/// footprints, and board geometry cannot drift from the main game.
/// </summary>
internal static class CampaignRuntimeFactory
{
  internal static Board CreateBoard(CampaignBoardDefinition board)
  {
    ArgumentNullException.ThrowIfNull(board);
    return new Board((board.Tiles ?? []).Where(tile => tile is not null).Select(tile => (tile.X, tile.Y)));
  }

  internal static bool TryGetPieceDefinition(string? unitType, out PieceDefinition definition)
  {
    PieceDefinition? candidate = PieceDefinitions.All.FirstOrDefault(candidate =>
      string.Equals(candidate.Type.ToString(), unitType, StringComparison.Ordinal));
    if (candidate is null)
    {
      definition = null!;
      return false;
    }
    definition = candidate;
    return true;
  }

  internal static bool TryCreatePiece(CampaignUnitDefinition unit, out Piece? piece)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (!TryGetPieceDefinition(unit.UnitType, out PieceDefinition definition))
    {
      piece = null;
      return false;
    }

    piece = new Piece(definition, (unit.Position.X, unit.Position.Y), unit.Team.ToTeamName())
    {
      NetworkId = unit.Id,
      CurrentHealth = unit.Health ?? definition.Health
    };
    return true;
  }
}
