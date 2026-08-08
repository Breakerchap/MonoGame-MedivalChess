namespace MedivalChess.Shared;

/// <summary>Creates the deterministic terrain used by local and authoritative online matches.</summary>
public static class TerrainRules
{
  public static BattlefieldTerrain Create(
    Board board,
    int seed,
    string forestDensity,
    string waterwayDensity,
    int playerCount = 2,
    string terrainSource = "Procedural",
    string boardSize = "Medium"
  )
  {
    if (terrainSource == "None")
    {
      return new BattlefieldTerrain();
    }

    TerrainGenerationSettings settings = new()
    {
      MinimumForestGroups = forestDensity switch { "Light" => 2, "Heavy" => 6, _ => 4 },
      MaximumForestGroups = forestDensity switch { "Light" => 3, "Heavy" => 8, _ => 6 },
      MinimumForestClusterSize = forestDensity == "Light" ? 2 : 3,
      MaximumForestClusterSize = forestDensity switch { "Light" => 4, "Heavy" => 8, _ => 6 },
      RiverCount = waterwayDensity switch { "Light" => 1, "Heavy" => 3, _ => 2 }
    };

    if (terrainSource == "Preset" &&
        BattlefieldTerrain.TryCreateRandomPreset(board, seed, boardSize, out BattlefieldTerrain preset))
    {
      return preset;
    }

    return BattlefieldTerrain.CreateRandom(board, seed, settings, playerCount);
  }
}
