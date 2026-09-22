using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedivalChess.Shared;

/// <summary>Loads static unit statistics from the versioned source-of-truth roster.</summary>
internal static class AuthoritativeUnitCatalogue
{

  private static readonly Dictionary<string, PieceType> TypeAliases = new(StringComparer.Ordinal)
  {
    ["norse.beserker"] = PieceType.Beserker
  };

  private static Dictionary<string, bool>? _royals;

  internal static PieceDefinition[] Load(IReadOnlyList<PieceDefinition> legacy)
  {
    string resourceName = typeof(AuthoritativeUnitCatalogue).Assembly.GetManifestResourceNames()
      .Single(name => name.EndsWith("units_codex.json", StringComparison.OrdinalIgnoreCase));
    using Stream stream = typeof(AuthoritativeUnitCatalogue).Assembly.GetManifestResourceStream(resourceName)!;
    Specification? specification = JsonSerializer.Deserialize<Specification>(stream, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });
    if (specification?.Units is null) throw new InvalidOperationException("The embedded unit specification is invalid.");

    Dictionary<PieceType, PieceDefinition> fallback = legacy.ToDictionary(unit => unit.Type);
    _royals = specification.Units.ToDictionary(unit => unit.UnitId, unit => unit.IsRoyal, StringComparer.Ordinal);
    List<PieceDefinition> definitions = [];
    foreach (Unit unit in specification.Units)
    {
      if (!unit.DataReady || !TryGetType(unit.UnitId, out PieceType type) || !HasConcreteCoreStats(unit)) continue;
      definitions.Add(new PieceDefinition(
        type,
        unit.Abbreviation ?? string.Empty,
        ParsePack(unit.PackId),
        new MovementDefinition(unit.Move.Min ?? 0, unit.Move.Max ?? 0, ParseShape(unit.Move.Pattern)),
        ResolveAttack(unit, type),
        ResolveHealth(unit, type, fallback),
        ResolveSize(unit, type),
        new AttackRange(unit.AttackRange.Min ?? 0, unit.AttackRange.Max ?? 0),
        ParseShape(unit.AttackRange.Pattern),
        ResolveCost(unit, type),
        unit.Ability.Text ?? string.Empty,
        type.ToString(),
        unit.Name,
        unit.UnitId,
        !unit.Name.Contains("Unchoosable", StringComparison.OrdinalIgnoreCase),
        unit.IsRoyal ? PieceCategory.Royal : null));
    }
    return [.. definitions];
  }

  internal static bool IsRoyal(string unitId) => _royals?.TryGetValue(unitId, out bool royal) == true && royal;

  private static bool HasConcreteCoreStats(Unit unit) =>
    unit.UnitId is "dynasty.qilin" or "norse.serpent" ||
    unit.Attack.Value.HasValue && unit.Health.Value.HasValue && unit.Size.Width.HasValue && unit.Size.Height.HasValue &&
    unit.Move.Min.HasValue && unit.Move.Max.HasValue && unit.AttackRange.Pattern is not null &&
    (unit.Cost.Value.HasValue || unit.IsRoyal || unit.Name.Contains("Unchoosable", StringComparison.OrdinalIgnoreCase));

  // Qilin's legal purchase range starts at X=40. The runtime may replace these baseline stats
  // with another legal X value at purchase time. Serpent represents one 1x1 segment; the
  // formation service owns its two companion segments.
  private static int ResolveAttack(Unit unit, PieceType type) => unit.Attack.Value ?? type switch
  {
    PieceType.Qilin => 20,
    _ => 0
  };

  private static int ResolveHealth(Unit unit, PieceType type, IReadOnlyDictionary<PieceType, PieceDefinition> fallback) =>
    unit.Health.Value ?? type switch
    {
      PieceType.Qilin => 40,
      PieceType.Serpent => 40,
      PieceType.ChessKing => 1,
      _ when fallback.TryGetValue(type, out PieceDefinition? definition) => definition.Health,
      _ => 0
    };

  private static (int x, int y) ResolveSize(Unit unit, PieceType type) =>
    (unit.Size.Width, unit.Size.Height) switch
    {
      (int width, int height) => (width, height),
      _ when type == PieceType.Serpent => (1, 1),
      _ => (1, 1)
    };

  private static int ResolveCost(Unit unit, PieceType type) => unit.Cost.Value ?? (type == PieceType.Qilin ? 40 : 0);

  private static bool TryGetType(string id, out PieceType type) =>
    TypeAliases.TryGetValue(id, out type) || Enum.TryParse(ToTypeName(id), out type);

  private static string ToTypeName(string id) => string.Concat(id[(id.IndexOf('.') + 1)..]
    .Split('_', StringSplitOptions.RemoveEmptyEntries)
    .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

  private static Pack ParsePack(string id) => id switch
  {
    "wild_west" => Pack.WildWest,
    "angels_demons" => Pack.AngelsDemons,
    "legacy" => Pack.Legacy,
    _ => Enum.Parse<Pack>(char.ToUpperInvariant(id[0]) + id[1..])
  };

  private static Shape ParseShape(string? pattern) => pattern switch
  {
    "Square" => Shape.Any,
    "Diamond" => Shape.Straight,
    "Circle" => Shape.Circle,
    "Line" => Shape.Line,
    "Diagonal" => Shape.Diagonal,
    "Line OR Diagonal" => Shape.LineOrDiagonal,
    "Forward" => Shape.Forward,
    "ForwardDiagonal" => Shape.ForwardDiagonal,
    "ForwardLine" => Shape.ForwardLine,
    "ChessKnight" => Shape.ChessKnight,
    "NA" => Shape.MoveOnEnemy,
    _ => Shape.None
  };

  private sealed class Specification { public List<Unit>? Units { get; set; } }
  private sealed class Unit
  {
    [JsonPropertyName("unit_id")]
    public string UnitId { get; set; } = "";
    [JsonPropertyName("pack_id")]
    public string PackId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Abbreviation { get; set; }
    [JsonPropertyName("is_royal")]
    public bool IsRoyal { get; set; }
    [JsonPropertyName("data_ready")]
    public bool DataReady { get; set; }
    public Range Move { get; set; } = new();
    public Number Attack { get; set; } = new();
    public Number Health { get; set; } = new();
    public Size Size { get; set; } = new();
    [JsonPropertyName("attack_range")]
    public Range AttackRange { get; set; } = new();
    public Number Cost { get; set; } = new();
    public Ability Ability { get; set; } = new();
  }
  private sealed class Range { public int? Min { get; set; } public int? Max { get; set; } public string? Pattern { get; set; } }
  private sealed class Number { public int? Value { get; set; } }
  private sealed class Size { public int? Width { get; set; } public int? Height { get; set; } }
  private sealed class Ability { public string? Text { get; set; } }
}
