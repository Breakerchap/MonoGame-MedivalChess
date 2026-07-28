namespace MedivalChess.Shared;

/// <summary>Creates the deterministic terrain used by local and authoritative online matches.</summary>
public static class TerrainRules
{
  public static BattlefieldTerrain Create(
    Board board,
    int seed,
    string forestDensity,
    string waterwayDensity
  )
  {
    TerrainGenerationSettings settings = new()
    {
      MinimumForestGroups = forestDensity switch { "Light" => 2, "Heavy" => 6, _ => 4 },
      MaximumForestGroups = forestDensity switch { "Light" => 3, "Heavy" => 8, _ => 6 },
      MinimumForestClusterSize = forestDensity == "Light" ? 2 : 3,
      MaximumForestClusterSize = forestDensity switch { "Light" => 4, "Heavy" => 8, _ => 6 },
      LargeBoardCellCount = waterwayDensity switch { "Light" => int.MaxValue, "Heavy" => 0, _ => 300 },
      AdditionalRiverChance = waterwayDensity switch { "Light" => 0, "Heavy" => 1, _ => 0.4 }
    };
    return BattlefieldTerrain.CreateRandom(board, seed, settings);
  }
}
