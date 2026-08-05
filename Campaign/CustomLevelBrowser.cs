#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedivalChess.Shared;

namespace MedivalChess.Campaign;

public sealed record CustomLevelSummary(
  string Path,
  string FileName,
  string Name,
  string Author,
  string Description,
  string Difficulty,
  int? FormatVersion,
  bool IsValid,
  IReadOnlyList<CampaignValidationProblem> Problems,
  IReadOnlyList<CampaignCoordinate> BoardPreview
);

/// <summary>Lists locally saved levels and validates each file before it can be launched.</summary>
public static class CustomLevelBrowser
{
  public static IReadOnlyList<CustomLevelSummary> Browse(string? directory = null)
  {
    string targetDirectory = directory ?? CampaignLevelSerializer.LocalLevelDirectory;
    if (!Directory.Exists(targetDirectory)) return [];

    List<CustomLevelSummary> levels = [];
    foreach (string path in Directory.EnumerateFiles(targetDirectory, $"*{CampaignLevelFormat.Extension}", SearchOption.TopDirectoryOnly)
      .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
    {
      CampaignLevelLoadResult result = CampaignLevelSerializer.Load(path);
      CampaignLevelDefinition? level = result.Level;
      levels.Add(new CustomLevelSummary(
        path,
        Path.GetFileName(path),
        level?.Metadata?.Name ?? Path.GetFileNameWithoutExtension(path),
        level?.Metadata?.Author ?? string.Empty,
        level?.Metadata?.Description ?? string.Empty,
        level?.Metadata?.Difficulty ?? string.Empty,
        level?.FormatVersion,
        result.IsSuccess,
        result.Problems,
        level?.Board?.Tiles?.Take(CampaignLevelFormat.MaximumBoardTiles).ToArray() ?? []
      ));
    }

    return levels;
  }
}
