#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess.Campaign;

/// <summary>MonoGame editor surface backed exclusively by <see cref="LevelEditorState"/>.</summary>
internal sealed class LevelEditorScreen
{
  private enum TextField
  {
    None, Name, Author, Description, Dialogue,
    UnitName, UnitAbbreviation, UnitMoveRange, UnitHealth, UnitAttack,
    UnitWidth, UnitHeight, UnitMinimumRange, UnitMaximumRange, UnitCost
  }
  private enum PropertiesView { Scenario, Teams, Restrictions, Units }
  private enum ObjectiveTargetMode { None, Unit, Location }
  private enum UnitCatalogueDropdown { None, MoveShape, AttackShape, Ability }

  private sealed record UnitCatalogueEntry(
    string Identifier,
    bool IsCustom,
    string AbilitySource,
    bool Purchasable,
    PieceDefinition Definition
  );

  private sealed record UnitCatalogueLayout(
    Rectangle AddButton,
    Rectangle PreviousButton,
    Rectangle PageLabel,
    Rectangle NextButton,
    IReadOnlyList<Rectangle> Headers,
    Rectangle Details,
    int PageSize
  );

  private sealed record UnitCatalogueDetails(
    Rectangle Name,
    Rectangle Abbreviation,
    Rectangle Cost,
    Rectangle MoveRange,
    Rectangle MoveShape,
    Rectangle Health,
    Rectangle Attack,
    Rectangle Width,
    Rectangle Height,
    Rectangle MinimumRange,
    Rectangle MaximumRange,
    Rectangle AttackShape,
    Rectangle Ability,
    Rectangle Place,
    Rectangle Buy,
    Rectangle Remove
  );

  private sealed record UnitDropdownLayout(
    Rectangle Title,
    Rectangle Previous,
    Rectangle Page,
    Rectangle Next,
    IReadOnlyList<Rectangle> Options,
    int PageSize
  );

  private readonly UiRenderer _ui;
  private readonly SpriteBatch _spriteBatch;
  private readonly Texture2D _pixel;
  private readonly List<CampaignValidationProblem> _problems = [];
  // These are the exact board definitions used by normal local, CPU, and online matches.
  private static readonly string[] BoardBaseNames = ["Small", "Medium", "Large"];
  private int _unitPaletteIndex;
  private int _objectPaletteIndex;
  private int _boardBaseIndex = 1;
  private CampaignTerrainType _terrainPaletteType;
  private NetworkTeam _placementTeam = NetworkTeam.Red;
  // Neutral is the editor-friendly representation for No-Man's-Land.
  private NetworkTeam _territoryOwner = NetworkTeam.Neutral;
  private Vector2 _camera;
  private float _zoom = 1f;
  private TextField _textField;
  private PropertiesView _propertiesView;
  private bool _editingScenarioObjectives;
  private ObjectiveTargetMode _objectiveTargetMode;
  private bool _editingDefeatConditions;
  private CampaignObjectiveType _objectivePaletteType = CampaignObjectiveType.DefeatEnemyRoyal;
  private NetworkTeam _objectiveTeam = NetworkTeam.Red;
  private NetworkTeam _settingsTeam = NetworkTeam.Red;
  private int _restrictionUnitIndex;
  private bool _buyListDropdownOpen;
  private string? _expandedCatalogueUnitId;
  private int _unitCataloguePage;
  private string _textBuffer = string.Empty;
  private string? _textUnitIdentifier;
  private bool _replaceTextOnNextCharacter;
  private UnitCatalogueDropdown _unitCatalogueDropdown;
  private string? _unitDropdownIdentifier;
  private int _unitDropdownPage;
  private string? _selectedObjectiveId;
  private string? _pendingObjectiveUnitId;
  private bool _fitBoardRequested = true;
  private Point _lastCanvasSize;
  private string _status = "Build a board, place units, then validate before test play.";

  internal LevelEditorState State { get; private set; }
  internal bool RequestExit { get; private set; }
  internal bool RequestNew { get; private set; }
  internal bool RequestBrowse { get; private set; }
  internal bool RequestTestPlay { get; private set; }

  internal LevelEditorScreen(UiRenderer ui, SpriteBatch spriteBatch, Texture2D pixel, LevelEditorState? state = null)
  {
    _ui = ui;
    _spriteBatch = spriteBatch;
    _pixel = pixel;
    State = state ?? LevelEditorState.CreateNew();
    SynchronisePlacementTeam();
  }

  internal void ReplaceState(LevelEditorState state)
  {
    State = state;
    _camera = Vector2.Zero;
    _zoom = 1f;
    _textField = TextField.None;
    _textBuffer = string.Empty;
    _textUnitIdentifier = null;
    _replaceTextOnNextCharacter = false;
    _buyListDropdownOpen = false;
    _expandedCatalogueUnitId = null;
    _unitCataloguePage = 0;
    _unitCatalogueDropdown = UnitCatalogueDropdown.None;
    _unitDropdownIdentifier = null;
    _unitDropdownPage = 0;
    _editingScenarioObjectives = false;
    _objectiveTargetMode = ObjectiveTargetMode.None;
    _selectedObjectiveId = null;
    _pendingObjectiveUnitId = null;
    _fitBoardRequested = true;
    _lastCanvasSize = Point.Zero;
    _problems.Clear();
    _status = "Level opened in the editor.";
    SynchronisePlacementTeam();
  }

  internal void ClearRequests()
  {
    RequestExit = false;
    RequestNew = false;
    RequestBrowse = false;
    RequestTestPlay = false;
  }

  internal void Update(
    KeyboardState keyboard,
    KeyboardState previousKeyboard,
    MouseState mouse,
    Point pointer,
    bool wasLeftClick,
    bool isLeftHeld,
    bool wasRightClick,
    bool wasEscapePressed,
    Rectangle screen
  )
  {
    if (wasEscapePressed)
    {
      if (_textField != TextField.None)
      {
        _textField = TextField.None;
        _textBuffer = string.Empty;
        _textUnitIdentifier = null;
        _replaceTextOnNextCharacter = false;
      }
      else if (_unitCatalogueDropdown != UnitCatalogueDropdown.None)
      {
        _unitCatalogueDropdown = UnitCatalogueDropdown.None;
        _unitDropdownIdentifier = null;
      }
      else RequestExit = true;
      return;
    }

    EditorLayout layout = new(screen, State.Level.Teams.Count);
    Point point = pointer;
    UpdateKeyboardNavigation(keyboard, previousKeyboard);
    UpdateTextInput(keyboard, previousKeyboard);

    // Right click is deliberately immediate and context-free: it is the fast erase gesture.
    if (wasRightClick && layout.Canvas.Contains(point))
    {
      DeleteAt(GetPositionAt(layout, point));
      return;
    }

    if (wasLeftClick)
    {
      CommitAndEndTextEdit();
      if (HandleHeaderClick(layout, point)) return;
      if (HandleToolClick(layout, point)) return;
      if (HandlePropertyClick(layout, point)) return;
    }

    // Painting continues while held for tools that do not have a one-click semantic.
    if (layout.Canvas.Contains(point) && (wasLeftClick || (isLeftHeld && CanPaintContinuously())))
    {
      HandleBoardClick(layout, point);
    }
  }

  internal void Draw(Rectangle screen)
  {
    EditorLayout layout = new(screen, State.Level.Teams.Count);
    if (_fitBoardRequested || _lastCanvasSize != layout.Canvas.Size)
    {
      FitBoardToCanvas(layout);
      _fitBoardRequested = false;
      _lastCanvasSize = layout.Canvas.Size;
    }
    _spriteBatch.Draw(_pixel, screen, UiTheme.MenuBackground);
    DrawHeader(layout);
    DrawBoard(layout);
    DrawToolPanel(layout);
    DrawProperties(layout);
    DrawProblems(layout);
  }

  private void DrawHeader(EditorLayout layout)
  {
    _ui.Panel(layout.Header, UiTheme.Panel, UiTheme.PanelBorder);
    _ui.TextFitted(State.Level.Metadata.Name, new Vector2(layout.TitleBounds.X, layout.TitleBounds.Y), layout.TitleBounds.Width, UiTheme.TextPrimary, layout.IsCompact ? 0.82f : 1.05f, 0.52f);
    CampaignValidationResult validation = State.Validate();
    int errors = validation.Problems.Count(problem => problem.Severity == CampaignValidationSeverity.Error);
    string state = validation.IsValid ? "READY" : $"{errors} ISSUE{(errors == 1 ? string.Empty : "S")}";
    _ui.CenterTextFitted(state, layout.ValidationBounds, validation.IsValid ? UiTheme.Health : UiTheme.Attack, layout.IsCompact ? 0.52f : 0.68f, 0.42f, 2);
    _ui.Button(layout.SaveButton, "SAVE", UiButtonTone.Primary, false, layout.HeaderButtonScale);
    _ui.Button(layout.ExportButton, layout.IsCompact ? "EXPORT" : "EXPORT", UiButtonTone.Accent, false, layout.HeaderButtonScale);
    _ui.Button(layout.ImportButton, layout.IsCompact ? "OPEN" : "IMPORT", UiButtonTone.Neutral, false, layout.HeaderButtonScale);
    _ui.Button(layout.BrowseButton, layout.IsCompact ? "LEVELS" : "LEVELS", UiButtonTone.Neutral, false, layout.HeaderButtonScale);
    _ui.Button(layout.TestButton, layout.IsCompact ? "PLAY" : "TEST PLAY", UiButtonTone.Primary, false, layout.HeaderButtonScale);
    _ui.Button(layout.ExitButton, "EXIT", UiButtonTone.Danger);
  }

  private void DrawToolPanel(EditorLayout layout)
  {
    _ui.Panel(layout.Tools, UiTheme.Panel, UiTheme.PanelBorder);
    _ui.TextFitted("BUILD", new Vector2(layout.Tools.X + layout.PanelPadding, layout.Tools.Y + 9), Math.Max(1, layout.Tools.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.76f, 0.44f);
    foreach ((EditorTool tool, string label, Rectangle bounds) in layout.ToolButtons)
    {
      _ui.Button(bounds, label, tool == EditorTool.Delete ? UiButtonTone.Danger : UiButtonTone.Neutral, State.ActiveTool == tool, layout.ToolButtonScale);
    }
    _ui.Button(layout.UndoButton, "UNDO", UiButtonTone.Neutral, false, layout.ToolButtonScale);
    _ui.Button(layout.RedoButton, "REDO", UiButtonTone.Neutral, false, layout.ToolButtonScale);
    _ui.Button(layout.NewButton, "NEW LEVEL", UiButtonTone.Accent, false, layout.ToolButtonScale);
    _ui.Button(layout.BoardSmallerButton, "-", UiButtonTone.Neutral);
    _ui.Button(layout.BoardLargerButton, "+", UiButtonTone.Neutral);
    _ui.CenterTextFitted($"BOARD: {State.Level.Board.Width} x {State.Level.Board.Height}", layout.BoardSizeLabel, UiTheme.TextMuted, layout.SmallControlScale, 0.42f, 2);
    _ui.Button(layout.BoardShapeButton, $"SHAPE: {State.Level.Board.Shape}".ToUpperInvariant(), UiButtonTone.Neutral, false, layout.SmallControlScale);
    _ui.Button(layout.BoardBasePrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.BoardBaseNext, ">", UiButtonTone.Neutral);
    _ui.CenterTextFitted($"BASE: {BoardBaseNames[_boardBaseIndex]}".ToUpperInvariant(), layout.BoardBaseLabel, UiTheme.TextMuted, layout.SmallControlScale, 0.42f, 2);
    _ui.Button(layout.BoardBaseApply, "USE DEFAULT BOARD", UiButtonTone.Accent, false, layout.SmallControlScale);
    _ui.Button(layout.FitBoardButton, "FIT BOARD", UiButtonTone.Neutral, false, layout.SmallControlScale);

    _ui.Divider(layout.Tools, layout.PaletteTop);
    IReadOnlyList<(string identifier, PieceDefinition definition)> unitPalette = GetUnitPalette();
    (string selectedUnitId, PieceDefinition selectedUnit) = unitPalette[_unitPaletteIndex % unitPalette.Count];
    _ui.TextFitted("UNIT", new Vector2(layout.Tools.X + layout.PanelPadding, layout.PaletteTop + 4), Math.Max(1, layout.Tools.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.65f, 0.42f);
    _ui.Button(layout.UnitPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.UnitNext, ">", UiButtonTone.Neutral);
    _ui.PiecePreview(layout.UnitPreview, UiTheme.GetTeamColour(_placementTeam.ToTeamName()), selectedUnit.DisplayName);
    _ui.Button(layout.TeamPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.TeamNext, ">", UiButtonTone.Neutral);
    _ui.CenterTextFitted($"PLACE FOR {_placementTeam}".ToUpperInvariant(), layout.TeamPreview, UiTheme.GetTeamColour(_placementTeam.ToTeamName()), layout.SmallControlScale, 0.42f, 2);

    CampaignBoardObjectType selectedObject = (CampaignBoardObjectType)(_objectPaletteIndex % Enum.GetValues<CampaignBoardObjectType>().Length);
    _ui.TextFitted("OBJECT / TERRAIN", new Vector2(layout.Tools.X + layout.PanelPadding, layout.ObjectPaletteTop + 4), Math.Max(1, layout.Tools.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.62f, 0.40f);
    _ui.Button(layout.ObjectPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.ObjectNext, ">", UiButtonTone.Neutral);
    _ui.CenterTextFitted(selectedObject.ToString().ToUpperInvariant(), layout.ObjectPreview, UiTheme.TextPrimary, layout.SmallControlScale, 0.42f, 2);
    _ui.Button(layout.TerrainButton, $"PAINT {_terrainPaletteType}".ToUpperInvariant(), UiButtonTone.Accent, State.ActiveTool == EditorTool.Terrain, layout.SmallControlScale);
    _ui.TextFitted("PAINT AREAS", new Vector2(layout.Tools.X + layout.PanelPadding, layout.TerritoryTop), Math.Max(1, layout.Tools.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.62f, 0.40f);
    NetworkTeam[] areaOwners = [NetworkTeam.Neutral, .. State.Level.Teams.Select(team => team.Team)];
    for (int index = 0; index < Math.Min(areaOwners.Length, layout.TerritoryButtons.Count); index++)
    {
      NetworkTeam owner = areaOwners[index];
      NetworkTeam? team = owner == NetworkTeam.Neutral ? null : owner;
      string label = CampaignTerritoryRules.GetAreaLabel(team);
      Color toneColour = team is null ? UiTheme.NoMansLand : UiTheme.GetTeamColour(team.Value.ToTeamName());
      _ui.Button(layout.TerritoryButtons[index], label, UiButtonTone.Neutral, State.ActiveTool == EditorTool.Territory && _territoryOwner == owner, layout.SmallControlScale);
      DrawOutline(layout.TerritoryButtons[index], toneColour, 1);
    }
    _ui.Button(layout.ResetTerritoryButton, "RESET AREAS", UiButtonTone.Neutral, false, layout.SmallControlScale);
  }

  private void DrawBoard(EditorLayout layout)
  {
    _ui.Panel(layout.Viewport, UiTheme.BoardBackground, UiTheme.PanelBorder);
    _spriteBatch.Draw(_pixel, layout.Canvas, UiTheme.BoardBackground);
    Board? board = null;
    try { board = CampaignRuntimeFactory.CreateBoard(State.Level.Board); }
    catch (ArgumentException) { }
    if (board is null) return;
    CampaignTerritoryMap territories = CampaignTerritoryRules.CreateMap(State.Level.Scenario);

    foreach ((int x, int y) cell in board.Cells)
    {
      CampaignCoordinate tile = new(cell.x, cell.y);
      Rectangle bounds = GetTileBounds(layout, tile);
      if (!bounds.Intersects(layout.Canvas)) continue;
      Color colour = (tile.X + tile.Y) % 2 == 0 ? UiTheme.DarkBoardCell : UiTheme.LightBoardCell;
      NetworkTeam? territoryOwner = territories.GetSquareOwner(board, (tile.X, tile.Y), State.Level.Teams.Count);
      Color territoryColour = territoryOwner.HasValue
        ? UiTheme.GetTeamColour(territoryOwner.Value.ToTeamName())
        : UiTheme.NoMansLand;
      colour = Color.Lerp(colour, territoryColour, 0.24f);
      CampaignTerrainTileDefinition? terrain = State.Level.Terrain.FirstOrDefault(entry => entry.Position == tile);
      if (terrain?.Type == CampaignTerrainType.Forest) colour = Color.Lerp(colour, UiTheme.Forest, 0.72f);
      if (terrain?.Type == CampaignTerrainType.Lake) colour = UiTheme.Lake;
      DrawCanvasRectangle(layout, bounds, colour);
      DrawCanvasOutline(layout, bounds, UiTheme.PanelBorderSubtle, 1);

      if (State.ActiveTool == EditorTool.Territory && _territoryOwner == (territoryOwner ?? NetworkTeam.Neutral))
      {
        DrawCanvasOutline(layout, bounds, UiTheme.SelectionOutline, 2);
      }
    }

    foreach (CampaignBoardObjectDefinition boardObject in State.Level.Objects)
    {
      Rectangle tile = GetTileBounds(layout, boardObject.Position);
      if (!tile.Intersects(layout.Canvas)) continue;
      Color colour = boardObject.Type switch
      {
        CampaignBoardObjectType.Road => UiTheme.Road,
        CampaignBoardObjectType.Barrier => UiTheme.Barricade,
        CampaignBoardObjectType.Mine => UiTheme.Attack,
        CampaignBoardObjectType.Bridge => UiTheme.Bridge,
        CampaignBoardObjectType.Treasure => UiTheme.GoldBright,
        _ => UiTheme.TextMuted
      };
      Rectangle marker = new(tile.Center.X - Math.Max(3, tile.Width / 5), tile.Center.Y - Math.Max(3, tile.Height / 5), Math.Max(6, tile.Width * 2 / 5), Math.Max(6, tile.Height * 2 / 5));
      DrawCanvasRectangle(layout, marker, colour);
    }

    foreach (CampaignUnitDefinition unit in State.Level.Units)
    {
      if (!CampaignRuntimeFactory.TryCreatePiece(State.Level, unit, out Piece? piece) || piece is null) continue;
      Rectangle origin = GetTileBounds(layout, new CampaignCoordinate(piece.Position.x, piece.Position.y));
      Rectangle bounds = new(origin.X + 4, origin.Y + 4, Math.Max(4, origin.Width * piece.Definition.Size.x - 8), Math.Max(4, origin.Height * piece.Definition.Size.y - 8));
      if (!bounds.Intersects(layout.Canvas)) continue;
      Color teamColour = UiTheme.GetTeamColour(piece.Team);
      DrawCanvasRectangle(layout, bounds, Color.Lerp(teamColour, UiTheme.PanelRaised, 0.22f));
      DrawCanvasOutline(layout, bounds, teamColour, 2);
      if (State.Selection.Kind == EditorSelectionKind.Unit && State.Selection.Id == unit.Id) DrawCanvasOutline(layout, bounds, UiTheme.SelectionOutline, 3);
      if (layout.Canvas.Contains(bounds)) _ui.CenterTextFitted(UiText.BuildPieceLabel(piece.Definition), bounds, UiTheme.TextPrimary, 0.57f, 0.42f, 3);
      int line = Math.Max(2, bounds.Width / 8);
      switch (unit.Rotation)
      {
        case CampaignUnitRotation.Degrees90: DrawCanvasRectangle(layout, new Rectangle(bounds.Right - line, bounds.Y, line, bounds.Height), UiTheme.GoldBright); break;
        case CampaignUnitRotation.Degrees180: DrawCanvasRectangle(layout, new Rectangle(bounds.X, bounds.Bottom - line, bounds.Width, line), UiTheme.GoldBright); break;
        case CampaignUnitRotation.Degrees270: DrawCanvasRectangle(layout, new Rectangle(bounds.X, bounds.Y, line, bounds.Height), UiTheme.GoldBright); break;
        default: DrawCanvasRectangle(layout, new Rectangle(bounds.X, bounds.Y, bounds.Width, line), UiTheme.GoldBright); break;
      }
    }

    DrawObjectiveGuides(layout);
    string help = State.ActiveTool == EditorTool.Territory
      ? $"Painting: {CampaignTerritoryRules.GetAreaLabel(_territoryOwner == NetworkTeam.Neutral ? null : _territoryOwner)}. Drag to paint; right-click erases."
      : "Drag to paint. Right-click deletes. WASD / arrows pan. Q / E zoom. Home or Fit Board recentres.";
    Rectangle helpBounds = new(layout.Canvas.X + 8, layout.Canvas.Bottom - Math.Min(42, layout.Canvas.Height / 4), Math.Max(1, layout.Canvas.Width - 16), Math.Min(34, layout.Canvas.Height / 4));
    _spriteBatch.Draw(_pixel, helpBounds, new Color(8, 13, 20, 196));
    _ui.TextWrapped(help, new Rectangle(helpBounds.X + 5, helpBounds.Y + 3, Math.Max(1, helpBounds.Width - 10), Math.Max(1, helpBounds.Height - 6)), UiTheme.TextMuted, 0.52f);
  }

  private void DrawProperties(EditorLayout layout)
  {
    _ui.Panel(layout.Properties, UiTheme.Panel, UiTheme.PanelBorder);
    _ui.TextFitted("INSPECTOR", new Vector2(layout.Properties.X + layout.PanelPadding, layout.Properties.Y + 14), Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.76f, 0.44f);
    _ui.Button(layout.ScenarioTab, "SCENARIO", UiButtonTone.Neutral, _propertiesView == PropertiesView.Scenario, layout.SmallControlScale);
    _ui.Button(layout.TeamsTab, "TEAMS", UiButtonTone.Neutral, _propertiesView == PropertiesView.Teams, layout.SmallControlScale);
    _ui.Button(layout.RestrictionsTab, "RULES", UiButtonTone.Neutral, _propertiesView == PropertiesView.Restrictions, layout.SmallControlScale);
    _ui.Button(layout.UnitsTab, "UNITS", UiButtonTone.Neutral, _propertiesView == PropertiesView.Units, layout.SmallControlScale);
    switch (_propertiesView)
    {
      case PropertiesView.Teams:
        DrawTeamSettings(layout);
        break;
      case PropertiesView.Restrictions:
        DrawRestrictionSettings(layout);
        break;
      case PropertiesView.Units:
        DrawUnitStatSettings(layout);
        break;
      default:
        DrawScenarioSettings(layout);
        break;
    }
    // Scenario editing uses the full panel for objectives. Selected-unit details remain available
    // in the compact Teams and Rules views instead of being hidden below the objective controls.
    if (_propertiesView is not PropertiesView.Scenario and not PropertiesView.Units && layout.ShowSelectionProperties) DrawSelectionProperties(layout);
  }

  private void DrawScenarioSettings(EditorLayout layout)
  {
    _ui.Button(layout.ScenarioDetailsButton, "DETAILS", UiButtonTone.Neutral, !_editingScenarioObjectives, 0.58f);
    _ui.Button(layout.ScenarioObjectivesButton, "OBJECTIVES", UiButtonTone.Neutral, _editingScenarioObjectives, 0.58f);
    if (_editingScenarioObjectives)
    {
      DrawObjectiveSettings(layout);
      return;
    }
    DrawTextField(layout.NameField, "NAME", State.Level.Metadata.Name, TextField.Name);
    DrawTextField(layout.AuthorField, "AUTHOR", State.Level.Metadata.Author, TextField.Author);
    DrawTextField(layout.DescriptionField, "DESCRIPTION", State.Level.Metadata.Description, TextField.Description);
    DrawTextField(layout.DialogueField, "DIALOGUE", State.Level.Metadata.CampaignDialogue ?? string.Empty, TextField.Dialogue);
    _ui.Button(layout.ModeButton, $"MODE: {State.Level.Scenario.GameMode.ToUpperInvariant()}", UiButtonTone.Neutral, false, 0.62f);
    _ui.Button(layout.FirstTeamButton, $"FIRST: {State.Level.Scenario.FirstTeam.ToString().ToUpperInvariant()}", UiButtonTone.Neutral, false, 0.62f);
    string turns = State.Level.Scenario.TurnLimit?.ToString() ?? "NONE";
    _ui.Button(layout.TurnLimitDown, "-", UiButtonTone.Neutral);
    _ui.Button(layout.TurnLimitUp, "+", UiButtonTone.Neutral);
    _ui.CenterTextFitted($"TURN LIMIT: {turns}", layout.TurnLimitLabel, UiTheme.TextMuted, layout.SmallControlScale, 0.42f, 2);
  }

  private void DrawObjectiveSettings(EditorLayout layout)
  {
    IReadOnlyList<CampaignObjectiveDefinition> objectives = _editingDefeatConditions
      ? State.Level.Scenario.DefeatConditions
      : State.Level.Scenario.VictoryConditions;
    CampaignObjectiveDefinition? selected = objectives.FirstOrDefault(objective => objective.Id == _selectedObjectiveId) ?? objectives.LastOrDefault();
    _ui.Divider(layout.Properties, layout.ObjectiveTop);
    _ui.TextFitted(_editingDefeatConditions ? "DEFEAT CONDITIONS" : "VICTORY CONDITIONS", new Vector2(layout.Properties.X + layout.PanelPadding, layout.ObjectiveTop + 7), Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.68f, 0.44f);
    _ui.Button(layout.ObjectiveOutcomeButton, _editingDefeatConditions ? "SWITCH TO VICTORY" : "SWITCH TO DEFEAT", _editingDefeatConditions ? UiButtonTone.Danger : UiButtonTone.Primary, false, layout.SmallControlScale);
    _ui.Button(layout.ObjectivePrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.ObjectiveNext, ">", UiButtonTone.Neutral);
    _ui.CenterTextFitted(GetObjectiveTitle(_objectivePaletteType).ToUpperInvariant(), layout.ObjectiveTypeLabel, UiTheme.TextPrimary, layout.SmallControlScale, 0.42f, 2);
    _ui.Button(layout.ObjectiveTeamButton, $"TEAM: {(selected?.Team ?? _objectiveTeam)}".ToUpperInvariant(), UiButtonTone.Neutral, false, layout.SmallControlScale);
    _ui.Button(layout.ObjectiveAddButton, "ADD", UiButtonTone.Primary, false, layout.SmallControlScale);
    _ui.Button(layout.ObjectiveRemoveButton, "REMOVE", UiButtonTone.Danger, false, layout.SmallControlScale);
    if (selected is not null)
    {
      int selectedIndex = objectives.ToList().FindIndex(objective => objective.Id == selected.Id) + 1;
      _ui.Button(layout.ObjectiveActiveLabel, $"CONDITION {selectedIndex} OF {objectives.Count}: {GetObjectiveTitle(selected.Type)}", UiButtonTone.Neutral, false, layout.SmallControlScale);
      _ui.TextWrapped(GetObjectiveExplanation(selected), layout.ObjectiveHelp, UiTheme.TextMuted, 0.55f);
      _ui.Button(layout.ObjectiveAmountDown, "-", UiButtonTone.Neutral);
      _ui.Button(layout.ObjectiveAmountUp, "+", UiButtonTone.Neutral);
      _ui.CenterTextFitted(GetObjectiveAmountLabel(selected), layout.ObjectiveAmountLabel, UiTheme.TextPrimary, layout.SmallControlScale, 0.42f, 2);
      _ui.Button(layout.ObjectiveTargetButton, GetObjectiveUnitButtonLabel(selected), UiButtonTone.Accent, _objectiveTargetMode == ObjectiveTargetMode.Unit, layout.SmallControlScale);
      _ui.Button(layout.ObjectiveLocationButton, GetObjectiveLocationButtonLabel(selected), UiButtonTone.Accent, _objectiveTargetMode == ObjectiveTargetMode.Location, layout.SmallControlScale);
    }
    else
    {
      _ui.TextWrapped("Choose a clear win or defeat condition, then press Add. Conditions on the same side are all required.", layout.ObjectiveHelp, UiTheme.TextMuted, 0.58f);
    }
  }

  private void DrawTeamSettings(EditorLayout layout)
  {
    CampaignTeamDefinition? team = State.Level.Teams.FirstOrDefault(candidate => candidate.Team == _settingsTeam);
    if (team is null) return;
    _ui.TextFitted("TEAM CONFIGURATION", new Vector2(layout.Properties.X + layout.PanelPadding, layout.SettingsTop + 8), Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.76f, 0.44f);
    _ui.Button(layout.SettingsTeamPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.SettingsTeamNext, ">", UiButtonTone.Neutral);
    _ui.CenterTextFitted(_settingsTeam.ToString().ToUpperInvariant(), layout.SettingsTeamLabel, UiTheme.GetTeamColour(_settingsTeam.ToTeamName()), layout.SmallControlScale, 0.42f, 2);
    _ui.Button(layout.ControllerButton, team.Controller == CampaignTeamController.Cpu ? "CONTROLLER: CPU" : "CONTROLLER: PLAYER", UiButtonTone.Neutral, false, 0.66f);
    _ui.Button(layout.MoneyDownButton, "-", UiButtonTone.Neutral);
    _ui.Button(layout.MoneyUpButton, "+", UiButtonTone.Neutral);
    _ui.CenterTextFitted($"STARTING GOLD: {team.StartingMoney}", layout.MoneyLabel, UiTheme.TextMuted, layout.SmallControlScale, 0.42f, 2);
    _ui.Button(layout.ActionsDownButton, "-", UiButtonTone.Neutral);
    _ui.Button(layout.ActionsUpButton, "+", UiButtonTone.Neutral);
    _ui.CenterTextFitted($"ACTIONS: {team.ActionsPerTurn}", layout.ActionsLabel, UiTheme.TextMuted, layout.SmallControlScale, 0.42f, 2);
    _ui.Button(layout.TeamPurchasesButton, $"BUY LIST: {GetBuyListMode(team)} v", UiButtonTone.Neutral, _buyListDropdownOpen, layout.SmallControlScale);
    _ui.TextFitted("BUY UNITS", new Vector2(layout.Properties.X + layout.PanelPadding, layout.TeamUnitTop), Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.68f, 0.44f);
    _ui.Button(layout.TeamUnitToggle, "MANAGE IN UNITS TAB", UiButtonTone.Neutral, false, layout.SmallControlScale);
    _ui.Button(layout.CpuDifficultyButton, $"CPU DIFFICULTY: {team.CpuProfile.Difficulty}".ToUpperInvariant(), UiButtonTone.Accent, false, 0.56f);
    _ui.Button(layout.CpuPersonalityButton, $"CPU STYLE: {team.CpuProfile.Personality}".ToUpperInvariant(), UiButtonTone.Accent, false, 0.56f);
    _ui.TextWrapped("Set both teams to PLAYER for a local two-player match. CPU teams use the difficulty and style below during test play.", new Rectangle(layout.Properties.X + 12, layout.CpuPersonalityButton.Bottom + 7, layout.Properties.Width - 24, 44), UiTheme.TextMuted, 0.55f);
    if (_buyListDropdownOpen)
    {
      _ui.Panel(layout.TeamBuyListMenu, UiTheme.PanelRaised, UiTheme.Gold);
      CampaignPurchaseListMode[] modes = Enum.GetValues<CampaignPurchaseListMode>();
      for (int index = 0; index < modes.Length; index++)
      {
        CampaignPurchaseListMode mode = modes[index];
        _ui.Button(layout.TeamBuyListOptions[index], mode.ToString().ToUpperInvariant(), UiButtonTone.Neutral, team.PurchaseListMode == mode, layout.SmallControlScale);
      }
    }
  }

  private void DrawRestrictionSettings(EditorLayout layout)
  {
    CampaignRestrictionsDefinition rules = State.Level.Restrictions;
    _ui.TextFitted("GLOBAL RULE RESTRICTIONS", new Vector2(layout.Properties.X + layout.PanelPadding, layout.SettingsTop + 8), Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.75f, 0.44f);
    _ui.Button(layout.GlobalPurchasesButton, rules.PurchasesEnabled ? "GLOBAL BUYING: ON" : "GLOBAL BUYING: OFF", rules.PurchasesEnabled ? UiButtonTone.Primary : UiButtonTone.Danger, false, 0.62f);
    _ui.Button(layout.AbilitiesButton, rules.AbilitiesEnabled ? "ABILITIES: ON" : "ABILITIES: OFF", rules.AbilitiesEnabled ? UiButtonTone.Primary : UiButtonTone.Danger, false, 0.62f);
    IReadOnlyList<(string identifier, PieceDefinition definition)> buyPalette = GetPurchasableUnitPalette();
    (string unitId, PieceDefinition palette) = buyPalette[_restrictionUnitIndex % buyPalette.Count];
    bool disabled = rules.DisabledUnitTypes.Contains(unitId);
    _ui.TextFitted("GLOBAL UNIT RESTRICTION", new Vector2(layout.Properties.X + layout.PanelPadding, layout.SettingsTop + 108), Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.67f, 0.44f);
    _ui.Button(layout.TeamUnitPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.TeamUnitNext, ">", UiButtonTone.Neutral);
    _ui.CenterTextFitted(palette.DisplayName.ToUpperInvariant(), layout.TeamUnitLabel, UiTheme.TextPrimary, 0.62f, 0.45f, 2);
    _ui.Button(layout.TeamUnitToggle, disabled ? "UNIT DISABLED" : "UNIT ALLOWED", disabled ? UiButtonTone.Danger : UiButtonTone.Primary, false, 0.61f);
    _ui.TextWrapped("Team-level buying and available-unit lists override this global rule. Disable abilities or a unit here to create hard campaign restrictions.", layout.RulesHelp, UiTheme.TextMuted, 0.62f);
  }

  private void DrawUnitStatSettings(EditorLayout layout)
  {
    IReadOnlyList<UnitCatalogueEntry> units = GetUnitCatalogue();
    UnitCatalogueLayout catalogue = CreateUnitCatalogueLayout(layout);
    int pageCount = Math.Max(1, (units.Count + catalogue.PageSize - 1) / catalogue.PageSize);
    _unitCataloguePage = Math.Clamp(_unitCataloguePage, 0, pageCount - 1);
    _ui.TextFitted("UNIT CATALOGUE", new Vector2(layout.Properties.X + layout.PanelPadding, layout.SettingsTop + 8), Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.75f, 0.44f);
    _ui.Button(catalogue.AddButton, "+ NEW UNIT", UiButtonTone.Accent, false, layout.SmallControlScale);
    _ui.Button(catalogue.PreviousButton, "<", UiButtonTone.Neutral);
    _ui.CenterTextFitted($"{_unitCataloguePage + 1}/{pageCount}  {units.Count} UNITS", catalogue.PageLabel, UiTheme.TextMuted, layout.SmallControlScale, 0.42f, 2);
    _ui.Button(catalogue.NextButton, ">", UiButtonTone.Neutral);

    int first = _unitCataloguePage * catalogue.PageSize;
    for (int slot = 0; slot < catalogue.Headers.Count && first + slot < units.Count; slot++)
    {
      UnitCatalogueEntry entry = units[first + slot];
      bool expanded = entry.Identifier == _expandedCatalogueUnitId;
      string marker = expanded ? "v" : ">";
      string label = $"{marker} {entry.Definition.DisplayName.ToUpperInvariant()}  [{UiText.BuildPieceLabel(entry.Definition)}]";
      _ui.Button(catalogue.Headers[slot], label, expanded ? UiButtonTone.Accent : UiButtonTone.Neutral, expanded, layout.SmallControlScale);
    }

    UnitCatalogueEntry? selected = units.FirstOrDefault(entry => entry.Identifier == _expandedCatalogueUnitId);
    if (selected is null)
    {
      _ui.TextWrapped("Click a unit to expand its editable card. Base and custom units share this catalogue; changing a base unit creates a level-only override.", catalogue.Details, UiTheme.TextMuted, 0.58f);
      return;
    }
    DrawExpandedUnitCard(selected, CreateUnitCatalogueDetails(catalogue.Details), layout);
  }

  private void DrawExpandedUnitCard(UnitCatalogueEntry entry, UnitCatalogueDetails fields, EditorLayout layout)
  {
    if (_unitCatalogueDropdown != UnitCatalogueDropdown.None && _unitDropdownIdentifier == entry.Identifier)
    {
      DrawUnitDropdown(entry, fields, layout);
      return;
    }
    PieceDefinition unit = entry.Definition;
    _ui.Panel(fields.Name, UiTheme.PanelRaised, UiTheme.Gold);
    DrawUnitCatalogueField(fields.Name, "NAME", unit.DisplayName, TextField.UnitName, false, layout);
    DrawUnitCatalogueField(fields.Abbreviation, "ABBREVIATION", UiText.BuildPieceLabel(unit), TextField.UnitAbbreviation, false, layout);
    DrawUnitCatalogueField(fields.Cost, "COST", unit.Cost.ToString(), TextField.UnitCost, false, layout);
    DrawUnitCatalogueField(fields.MoveRange, "MOVEMENT", unit.Movement.range.ToString(), TextField.UnitMoveRange, false, layout);
    DrawUnitCatalogueField(fields.MoveShape, "MOVE STYLE", unit.Movement.shape.ToString(), TextField.None, true, layout);
    DrawUnitCatalogueField(fields.Health, "HEALTH", unit.Health.ToString(), TextField.UnitHealth, false, layout);
    DrawUnitCatalogueField(fields.Attack, "ATTACK", unit.Attack.ToString(), TextField.UnitAttack, false, layout);
    DrawUnitCatalogueField(fields.Width, "SIZE X", unit.Size.x.ToString(), TextField.UnitWidth, false, layout);
    DrawUnitCatalogueField(fields.Height, "SIZE Y", unit.Size.y.ToString(), TextField.UnitHeight, false, layout);
    DrawUnitCatalogueField(fields.MinimumRange, "MIN RANGE", unit.AttackRange.Minimum.ToString(), TextField.UnitMinimumRange, false, layout);
    DrawUnitCatalogueField(fields.MaximumRange, "MAX RANGE", unit.AttackRange.Maximum.ToString(), TextField.UnitMaximumRange, false, layout);
    DrawUnitCatalogueField(fields.AttackShape, "RANGE STYLE", unit.AttackPattern.ToString(), TextField.None, true, layout);
    DrawUnitCatalogueField(fields.Ability, "ABILITY", GetAbilityLabel(entry.AbilitySource), TextField.None, true, layout);
    _ui.Button(fields.Place, "PLACE", UiButtonTone.Primary, false, layout.SmallControlScale);
    CampaignTeamDefinition? team = State.Level.Teams.FirstOrDefault(candidate => candidate.Team == _settingsTeam);
    bool buyable = entry.IsCustom || PieceDefinitions.Purchasable.Any(candidate => candidate.Identifier == entry.Identifier);
    bool allowedForTeam = buyable && entry.Purchasable && (team?.PurchaseListMode == CampaignPurchaseListMode.All || team?.AvailableUnitTypes.Contains(entry.Identifier) == true);
    _ui.Button(fields.Buy, buyable ? allowedForTeam ? $"BUY {_settingsTeam}: ON" : $"BUY {_settingsTeam}: OFF" : "NOT BUYABLE", allowedForTeam ? UiButtonTone.Primary : UiButtonTone.Danger, false, layout.SmallControlScale);
    string removeLabel = entry.IsCustom ? "DELETE UNIT" : buyable ? entry.Purchasable ? "REMOVE FROM BUY" : "RESTORE BUY" : "BASE UNIT";
    _ui.Button(fields.Remove, removeLabel, entry.IsCustom || (buyable && entry.Purchasable) ? UiButtonTone.Danger : UiButtonTone.Neutral, false, layout.SmallControlScale);
  }

  private void DrawUnitDropdown(UnitCatalogueEntry entry, UnitCatalogueDetails fields, EditorLayout layout)
  {
    IReadOnlyList<string> options = GetUnitDropdownOptions();
    Rectangle bounds = new(fields.Name.X, fields.Name.Y, fields.Name.Width, fields.Remove.Bottom - fields.Name.Y);
    UnitDropdownLayout dropdown = CreateUnitDropdownLayout(bounds);
    int pageCount = Math.Max(1, (options.Count + dropdown.PageSize - 1) / dropdown.PageSize);
    _unitDropdownPage = Math.Clamp(_unitDropdownPage, 0, pageCount - 1);
    string title = _unitCatalogueDropdown switch
    {
      UnitCatalogueDropdown.MoveShape => "MOVEMENT STYLE",
      UnitCatalogueDropdown.AttackShape => "RANGE STYLE",
      _ => "ABILITY SOURCE"
    };
    _ui.Panel(bounds, UiTheme.PanelRaised, UiTheme.Gold);
    _ui.CenterTextFitted(title, dropdown.Title, UiTheme.GoldBright, layout.SmallControlScale, 0.42f, 2);
    _ui.Button(dropdown.Previous, "<", UiButtonTone.Neutral);
    _ui.CenterTextFitted($"{_unitDropdownPage + 1}/{pageCount}", dropdown.Page, UiTheme.TextMuted, layout.SmallControlScale, 0.42f, 2);
    _ui.Button(dropdown.Next, ">", UiButtonTone.Neutral);
    int first = _unitDropdownPage * dropdown.PageSize;
    for (int index = 0; index < dropdown.Options.Count && first + index < options.Count; index++)
    {
      string option = options[first + index];
      bool selected = string.Equals(option, GetSelectedDropdownValue(entry), StringComparison.OrdinalIgnoreCase);
      _ui.Button(dropdown.Options[index], option.ToUpperInvariant(), UiButtonTone.Neutral, selected, layout.SmallControlScale);
    }
  }

  private void DrawUnitCatalogueField(Rectangle bounds, string label, string value, TextField field, bool dropdown, EditorLayout layout)
  {
    bool editing = field != TextField.None && _textField == field;
    string shown = editing ? _textBuffer : value;
    int labelWidth = Math.Min(Math.Max(42, bounds.Width / 2), 84);
    Rectangle valueBounds = new(bounds.X + labelWidth, bounds.Y + 2, Math.Max(1, bounds.Width - labelWidth - 2), Math.Max(1, bounds.Height - 4));
    _ui.TextFitted(label + ":", new Vector2(bounds.X + 3, bounds.Y + (bounds.Height - 12) / 2), Math.Max(1, labelWidth - 6), UiTheme.TextMuted, 0.52f, 0.40f);
    _ui.Panel(valueBounds, editing ? UiTheme.PanelRaised : UiTheme.MenuBackground, editing ? UiTheme.GoldBright : dropdown ? UiTheme.Gold : UiTheme.PanelBorderSubtle);
    _ui.CenterTextFitted(dropdown ? $"{shown} v" : shown, valueBounds, dropdown ? UiTheme.GoldBright : UiTheme.TextPrimary, layout.SmallControlScale, 0.42f, 2);
  }

  private void DrawSelectionProperties(EditorLayout layout)
  {
    _ui.Divider(layout.Properties, layout.SelectionTop);
    if (State.Selection.Kind == EditorSelectionKind.Unit && State.Selection.Id is string unitId)
    {
      CampaignUnitDefinition? unit = State.Level.Units.FirstOrDefault(candidate => candidate.Id == unitId);
      if (unit is not null)
      {
        _ui.TextFitted("SELECTED UNIT", new Vector2(layout.Properties.X + layout.PanelPadding, layout.SelectionTop + 10), Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.75f, 0.44f);
        _ui.Button(layout.SelectedRowOne, $"{unit.UnitType} - TEAM: {unit.Team} (CHANGE)", UiButtonTone.Neutral, false, 0.58f);
        _ui.Button(layout.SelectedPrevious, "HEALTH -", UiButtonTone.Neutral, false, 0.65f);
        _ui.Button(layout.SelectedNext, "HEALTH +", UiButtonTone.Neutral, false, 0.65f);
        _ui.Button(layout.RotateButton, "ROTATE", UiButtonTone.Accent, false, 0.7f);
      }
      return;
    }
    if (State.Selection.Kind == EditorSelectionKind.Object && State.Selection.Id is string objectId)
    {
      CampaignBoardObjectDefinition? boardObject = State.Level.Objects.FirstOrDefault(candidate => candidate.Id == objectId);
      if (boardObject is not null)
      {
        _ui.TextFitted("SELECTED OBJECT", new Vector2(layout.Properties.X + layout.PanelPadding, layout.SelectionTop + 10), Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), UiTheme.GoldBright, 0.75f, 0.44f);
        _ui.LabelValueRow(layout.SelectedRowOne, boardObject.Type.ToString(), boardObject.Owner?.ToString() ?? "NEUTRAL", UiTheme.TextMuted);
        _ui.Button(layout.RotateButton, "DELETE OBJECT", UiButtonTone.Danger, false, 0.65f);
      }
      return;
    }
    _ui.TextWrapped("Select a unit or object to edit its properties.", new Rectangle(layout.Properties.X + layout.PanelPadding, layout.SelectionTop + 10, Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2), 46), UiTheme.TextMuted, 0.58f);
  }

  private void DrawProblems(EditorLayout layout)
  {
    _ui.Panel(layout.Status, UiTheme.Panel, UiTheme.PanelBorderSubtle);
    int padding = Math.Min(12, Math.Max(6, layout.Status.Width / 45));
    int messageWidth = Math.Max(1, layout.Status.Width - padding * 2);
    int messageHeight = Math.Max(18, layout.Status.Height / 2 - 3);
    _ui.TextWrapped(_status, new Rectangle(layout.Status.X + padding, layout.Status.Y + 5, messageWidth, messageHeight), UiTheme.TextPrimary, 0.59f);
    CampaignValidationProblem? firstProblem = _problems.FirstOrDefault() ?? State.Validate().Problems.FirstOrDefault();
    if (firstProblem is not null)
    {
      string label = firstProblem.Severity == CampaignValidationSeverity.Error ? "FIX BEFORE TEST PLAY: " : "CHECK: ";
      _ui.TextWrapped(label + firstProblem.Message, new Rectangle(layout.Status.X + padding, layout.Status.Y + messageHeight + 7, messageWidth, Math.Max(1, layout.Status.Bottom - (layout.Status.Y + messageHeight + 11))), firstProblem.Severity == CampaignValidationSeverity.Error ? UiTheme.Attack : UiTheme.Move, 0.53f);
    }
  }

  private void DrawTextField(Rectangle bounds, string label, string value, TextField field)
  {
    _ui.Panel(bounds, _textField == field ? UiTheme.PanelRaised : UiTheme.MenuBackground, _textField == field ? UiTheme.GoldBright : UiTheme.PanelBorderSubtle);
    _ui.TextFitted(label, new Vector2(bounds.X + 6, bounds.Y + 4), Math.Max(1, bounds.Width - 12), UiTheme.TextDim, 0.53f, 0.40f);
    string shown = _textField == field ? _textBuffer : value;
    _ui.TextFitted(string.IsNullOrEmpty(shown) ? "Click to edit" : shown, new Vector2(bounds.X + 6, bounds.Y + 18), bounds.Width - 12, string.IsNullOrEmpty(shown) ? UiTheme.TextDim : UiTheme.TextPrimary, 0.65f, 0.46f);
  }

  private bool HandleHeaderClick(EditorLayout layout, Point point)
  {
    if (layout.SaveButton.Contains(point))
    {
      string path = Path.Combine(CampaignLevelSerializer.LocalLevelDirectory, LevelFilePicker.CreateSafeLevelFileName(State.Level.Metadata.Name));
      ShowSaveResult(State.Save(path), $"Saved locally: {Path.GetFileName(path)}");
      return true;
    }
    if (layout.ExportButton.Contains(point))
    {
      string? path = LevelFilePicker.PickExportPath(LevelFilePicker.CreateSafeLevelFileName(State.Level.Metadata.Name));
      if (path is null) _status = "Export cancelled.";
      else ShowSaveResult(State.Save(path), $"Exported to: {path}");
      return true;
    }
    if (layout.ImportButton.Contains(point))
    {
      string? path = LevelFilePicker.PickImportPath();
      if (path is null) _status = "Import cancelled or no native file picker is available. Use LEVELS for local files.";
      else ShowLoadResult(State.Import(path), "Imported level.");
      return true;
    }
    if (layout.BrowseButton.Contains(point))
    {
      RequestBrowse = true;
      return true;
    }
    if (layout.TestButton.Contains(point))
    {
      CampaignLevelLoadResult snapshot = State.CreateTestPlaySnapshot();
      _problems.Clear();
      _problems.AddRange(snapshot.Problems);
      if (snapshot.IsSuccess) RequestTestPlay = true;
      else _status = "Fix the listed validation issues before test play.";
      return true;
    }
    if (layout.ExitButton.Contains(point))
    {
      RequestExit = true;
      return true;
    }
    return false;
  }

  private bool HandleToolClick(EditorLayout layout, Point point)
  {
    foreach ((EditorTool tool, _, Rectangle bounds) in layout.ToolButtons)
    {
      if (!bounds.Contains(point)) continue;
      State.ActiveTool = tool;
      _status = tool switch
      {
        EditorTool.Unit => "Choose a unit and team, then click a valid tile to place it.",
        EditorTool.Move => "Select a unit, then click a valid destination to move it.",
        EditorTool.Tile => "Drag across empty space to paint playable tiles. Right-click erases.",
        EditorTool.Terrain => "Choose Forest or Lake, then drag across the board to paint terrain.",
        EditorTool.Object => "Choose an object, then click or drag to place it.",
        EditorTool.Territory => "Choose No-Man's-Land or a team area, then paint the playable board.",
        EditorTool.Delete => "Drag across content to erase it. Right-click also erases.",
        _ => "Click a unit, terrain tile, or object to inspect it."
      };
      return true;
    }
    if (layout.UndoButton.Contains(point)) { _status = State.Undo() ? "Undid last editor operation." : "Nothing to undo."; return true; }
    if (layout.RedoButton.Contains(point)) { _status = State.Redo() ? "Redid editor operation." : "Nothing to redo."; return true; }
    if (layout.NewButton.Contains(point)) { RequestNew = true; return true; }
    if (layout.BoardSmallerButton.Contains(point)) { ResizeBoard(-1); return true; }
    if (layout.BoardLargerButton.Contains(point)) { ResizeBoard(1); return true; }
    if (layout.BoardShapeButton.Contains(point)) { State.SetBoardShape(State.Level.Board.Shape == CampaignBoardShape.Rectangle ? CampaignBoardShape.Custom : CampaignBoardShape.Rectangle); return true; }
    if (layout.BoardBasePrevious.Contains(point)) { _boardBaseIndex = (_boardBaseIndex - 1 + BoardBaseNames.Length) % BoardBaseNames.Length; return true; }
    if (layout.BoardBaseNext.Contains(point)) { _boardBaseIndex = (_boardBaseIndex + 1) % BoardBaseNames.Length; return true; }
    if (layout.BoardBaseApply.Contains(point))
    {
      State.UseBoardBase(BoardRules.GetBoard(BoardBaseNames[_boardBaseIndex]));
      _fitBoardRequested = true;
      _status = $"Using the shipped {BoardBaseNames[_boardBaseIndex]} board as this level's base.";
      return true;
    }
    if (layout.FitBoardButton.Contains(point)) { _fitBoardRequested = true; _status = "Board fitted to the canvas."; return true; }
    int paletteCount = GetUnitPalette().Count;
    if (layout.UnitPrevious.Contains(point)) { _unitPaletteIndex = (_unitPaletteIndex - 1 + paletteCount) % paletteCount; return true; }
    if (layout.UnitNext.Contains(point)) { _unitPaletteIndex = (_unitPaletteIndex + 1) % paletteCount; return true; }
    if (layout.TeamPrevious.Contains(point)) { CyclePlacementTeam(-1); return true; }
    if (layout.TeamNext.Contains(point)) { CyclePlacementTeam(1); return true; }
    if (layout.ObjectPrevious.Contains(point)) { _objectPaletteIndex = (_objectPaletteIndex - 1 + Enum.GetValues<CampaignBoardObjectType>().Length) % Enum.GetValues<CampaignBoardObjectType>().Length; return true; }
    if (layout.ObjectNext.Contains(point)) { _objectPaletteIndex = (_objectPaletteIndex + 1) % Enum.GetValues<CampaignBoardObjectType>().Length; return true; }
    if (layout.TerrainButton.Contains(point))
    {
      _terrainPaletteType = _terrainPaletteType == CampaignTerrainType.Forest ? CampaignTerrainType.Lake : CampaignTerrainType.Forest;
      State.ActiveTool = EditorTool.Terrain;
      _status = $"Terrain brush: {_terrainPaletteType}. Drag across playable tiles to paint it.";
      return true;
    }
    NetworkTeam[] areaOwners = [NetworkTeam.Neutral, .. State.Level.Teams.Select(team => team.Team)];
    for (int index = 0; index < Math.Min(areaOwners.Length, layout.TerritoryButtons.Count); index++)
    {
      if (!layout.TerritoryButtons[index].Contains(point)) continue;
      _territoryOwner = areaOwners[index];
      State.ActiveTool = EditorTool.Territory;
      _status = $"Territory brush: {CampaignTerritoryRules.GetAreaLabel(_territoryOwner == NetworkTeam.Neutral ? null : _territoryOwner)}. Drag across the board to paint it.";
      return true;
    }
    if (layout.ResetTerritoryButton.Contains(point))
    {
      State.UseAutomaticTerritories();
      State.ActiveTool = EditorTool.Territory;
      _territoryOwner = NetworkTeam.Neutral;
      _status = "Areas reset to the game's automatic territories. Paint a square to begin a custom map.";
      return true;
    }
    return false;
  }

  private bool HandlePropertyClick(EditorLayout layout, Point point)
  {
    if (layout.ScenarioTab.Contains(point)) { _propertiesView = PropertiesView.Scenario; return true; }
    if (layout.TeamsTab.Contains(point)) { _propertiesView = PropertiesView.Teams; return true; }
    if (layout.RestrictionsTab.Contains(point)) { _propertiesView = PropertiesView.Restrictions; return true; }
    if (layout.UnitsTab.Contains(point)) { _propertiesView = PropertiesView.Units; return true; }
    if (_propertiesView == PropertiesView.Teams && HandleTeamSettingsClick(layout, point)) return true;
    if (_propertiesView == PropertiesView.Restrictions && HandleRestrictionsClick(layout, point)) return true;
    if (_propertiesView == PropertiesView.Units && HandleUnitStatClick(layout, point)) return true;
    if (_propertiesView == PropertiesView.Scenario)
    {
      if (layout.ScenarioDetailsButton.Contains(point)) { _editingScenarioObjectives = false; return true; }
      if (layout.ScenarioObjectivesButton.Contains(point)) { _editingScenarioObjectives = true; return true; }
      if (_editingScenarioObjectives) return HandleObjectiveClick(layout, point);
      if (layout.NameField.Contains(point)) { BeginTextEdit(TextField.Name, State.Level.Metadata.Name); return true; }
      if (layout.AuthorField.Contains(point)) { BeginTextEdit(TextField.Author, State.Level.Metadata.Author); return true; }
      if (layout.DescriptionField.Contains(point)) { BeginTextEdit(TextField.Description, State.Level.Metadata.Description); return true; }
      if (layout.DialogueField.Contains(point)) { BeginTextEdit(TextField.Dialogue, State.Level.Metadata.CampaignDialogue ?? string.Empty); return true; }
      if (layout.ModeButton.Contains(point)) { CycleGameMode(); return true; }
      if (layout.FirstTeamButton.Contains(point)) { CycleFirstTeam(); return true; }
      if (layout.TurnLimitDown.Contains(point)) { State.UpdateScenario(scenario => scenario.TurnLimit = Math.Max(1, (scenario.TurnLimit ?? 11) - 1)); return true; }
      if (layout.TurnLimitUp.Contains(point)) { State.UpdateScenario(scenario => scenario.TurnLimit = Math.Min(10_000, (scenario.TurnLimit ?? 0) + 1)); return true; }
    }
    if (_propertiesView != PropertiesView.Scenario && State.Selection.Kind == EditorSelectionKind.Unit && State.Selection.Id is string unitId)
    {
      if (layout.SelectedRowOne.Contains(point)) { CycleSelectedUnitTeam(unitId); return true; }
      if (layout.SelectedPrevious.Contains(point)) { AdjustSelectedHealth(unitId, -1); return true; }
      if (layout.SelectedNext.Contains(point)) { AdjustSelectedHealth(unitId, 1); return true; }
      if (layout.RotateButton.Contains(point)) { State.RotateUnit(unitId); return true; }
    }
    if (_propertiesView != PropertiesView.Scenario && State.Selection.Kind == EditorSelectionKind.Object && State.Selection.Id is string objectId && layout.RotateButton.Contains(point))
    {
      State.DeleteObject(objectId);
      return true;
    }
    return false;
  }

  private bool HandleObjectiveClick(EditorLayout layout, Point point)
  {
    if (layout.ObjectiveOutcomeButton.Contains(point))
    {
      _editingDefeatConditions = !_editingDefeatConditions;
      _selectedObjectiveId = null;
      _objectiveTargetMode = ObjectiveTargetMode.None;
      return true;
    }
    CampaignObjectiveType[] types = Enum.GetValues<CampaignObjectiveType>();
    if (layout.ObjectivePrevious.Contains(point)) { _objectivePaletteType = types[(Array.IndexOf(types, _objectivePaletteType) - 1 + types.Length) % types.Length]; return true; }
    if (layout.ObjectiveNext.Contains(point)) { _objectivePaletteType = types[(Array.IndexOf(types, _objectivePaletteType) + 1) % types.Length]; return true; }
    if (layout.ObjectiveTeamButton.Contains(point))
    {
      CampaignObjectiveDefinition? teamObjective = GetSelectedObjective();
      if (teamObjective is null)
      {
        CycleObjectiveTeam();
      }
      else
      {
        NetworkTeam nextTeam = GetNextObjectiveTeam(teamObjective.Team ?? _objectiveTeam);
        State.UpdateObjective(_editingDefeatConditions, teamObjective.Id, objective => objective.Team = nextTeam);
        _objectiveTeam = nextTeam;
        _status = $"Condition assigned to {nextTeam}.";
      }
      return true;
    }
    if (layout.ObjectiveAddButton.Contains(point))
    {
      CampaignObjectiveDefinition objective = State.AddObjective(_editingDefeatConditions, _objectivePaletteType, _objectiveTeam);
      _selectedObjectiveId = objective.Id;
      _status = "Condition added. Pick a unit or square when this condition needs one.";
      return true;
    }
    CampaignObjectiveDefinition? selected = GetSelectedObjective();
    if (selected is null) return false;
    if (layout.ObjectiveActiveLabel.Contains(point))
    {
      IReadOnlyList<CampaignObjectiveDefinition> objectives = _editingDefeatConditions
        ? State.Level.Scenario.DefeatConditions
        : State.Level.Scenario.VictoryConditions;
      int current = objectives.ToList().FindIndex(objective => objective.Id == selected.Id);
      _selectedObjectiveId = objectives[(current + 1 + objectives.Count) % objectives.Count].Id;
      _status = "Switched active condition.";
      return true;
    }
    if (layout.ObjectiveRemoveButton.Contains(point))
    {
      State.RemoveObjective(_editingDefeatConditions, selected.Id);
      _selectedObjectiveId = null;
      return true;
    }
    if (layout.ObjectiveAmountDown.Contains(point))
    {
      if (!ObjectiveUsesAmount(selected.Type)) { _status = "This condition does not use an amount."; return true; }
      State.UpdateObjective(_editingDefeatConditions, selected.Id, objective => objective.RequiredAmount = Math.Max(1, objective.RequiredAmount - 1));
      return true;
    }
    if (layout.ObjectiveAmountUp.Contains(point))
    {
      if (!ObjectiveUsesAmount(selected.Type)) { _status = "This condition does not use an amount."; return true; }
      State.UpdateObjective(_editingDefeatConditions, selected.Id, objective => objective.RequiredAmount = Math.Min(10_000, objective.RequiredAmount + 1));
      return true;
    }
    if (layout.ObjectiveTargetButton.Contains(point)) { _objectiveTargetMode = ObjectiveTargetMode.Unit; _status = "Click a starting unit on the board."; return true; }
    if (layout.ObjectiveLocationButton.Contains(point)) { _objectiveTargetMode = ObjectiveTargetMode.Location; _status = "Click a playable square on the board."; return true; }
    return false;
  }

  private bool HandleTeamSettingsClick(EditorLayout layout, Point point)
  {
    if (layout.SettingsTeamPrevious.Contains(point)) { CycleSettingsTeam(-1); return true; }
    if (layout.SettingsTeamNext.Contains(point)) { CycleSettingsTeam(1); return true; }
    CampaignTeamDefinition? team = State.Level.Teams.FirstOrDefault(candidate => candidate.Team == _settingsTeam);
    if (team is null) return false;
    if (_buyListDropdownOpen)
    {
      CampaignPurchaseListMode[] modes = Enum.GetValues<CampaignPurchaseListMode>();
      for (int index = 0; index < modes.Length; index++)
      {
        if (!layout.TeamBuyListOptions[index].Contains(point)) continue;
        SetTeamBuyListMode(modes[index]);
        _buyListDropdownOpen = false;
        return true;
      }
      _buyListDropdownOpen = false;
      return true;
    }
    if (layout.ControllerButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.Controller = value.Controller == CampaignTeamController.Cpu ? CampaignTeamController.Human : CampaignTeamController.Cpu); return true; }
    if (layout.MoneyDownButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.StartingMoney = Math.Max(0, value.StartingMoney - 50)); return true; }
    if (layout.MoneyUpButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.StartingMoney = Math.Min(1_000_000, value.StartingMoney + 50)); return true; }
    if (layout.ActionsDownButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.ActionsPerTurn = Math.Max(1, value.ActionsPerTurn - 1)); return true; }
    if (layout.ActionsUpButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.ActionsPerTurn = Math.Min(100, value.ActionsPerTurn + 1)); return true; }
    if (layout.TeamPurchasesButton.Contains(point)) { _buyListDropdownOpen = true; return true; }
    if (layout.TeamUnitToggle.Contains(point)) { _propertiesView = PropertiesView.Units; _status = "Manage this team's buy list from a unit card."; return true; }
    if (layout.CpuDifficultyButton.Contains(point))
    {
      State.UpdateTeam(_settingsTeam, value =>
      {
        value.CpuProfile.Difficulty = value.CpuProfile.Difficulty switch
        {
          "Easy" => "Medium",
          "Medium" or "Normal" => "Hard",
          "Hard" => "Best",
          _ => "Easy"
        };
      });
      return true;
    }
    if (layout.CpuPersonalityButton.Contains(point))
    {
      State.UpdateTeam(_settingsTeam, value =>
      {
        string[] personalities = ["Balanced", "Aggressive", "Defensive", "Greedy", "Reckless", "ObjectiveFocused", "Swarmer"];
        int current = Array.IndexOf(personalities, value.CpuProfile.Personality);
        value.CpuProfile.Personality = personalities[(current + 1 + personalities.Length) % personalities.Length];
      });
      return true;
    }
    return false;
  }

  private bool HandleRestrictionsClick(EditorLayout layout, Point point)
  {
    if (layout.GlobalPurchasesButton.Contains(point)) { State.UpdateScenario(_ => State.Level.Restrictions.PurchasesEnabled = !State.Level.Restrictions.PurchasesEnabled); return true; }
    if (layout.AbilitiesButton.Contains(point)) { State.UpdateScenario(_ => State.Level.Restrictions.AbilitiesEnabled = !State.Level.Restrictions.AbilitiesEnabled); return true; }
    if (layout.TeamUnitPrevious.Contains(point)) { CycleRestrictionUnit(-1); return true; }
    if (layout.TeamUnitNext.Contains(point)) { CycleRestrictionUnit(1); return true; }
    if (layout.TeamUnitToggle.Contains(point))
    {
      IReadOnlyList<(string identifier, PieceDefinition definition)> buyPalette = GetPurchasableUnitPalette();
      string type = buyPalette[_restrictionUnitIndex % buyPalette.Count].identifier;
      State.UpdateScenario(_ => ToggleUnit(State.Level.Restrictions.DisabledUnitTypes, type));
      return true;
    }
    return false;
  }

  private bool HandleUnitStatClick(EditorLayout layout, Point point)
  {
    IReadOnlyList<UnitCatalogueEntry> units = GetUnitCatalogue();
    UnitCatalogueLayout catalogue = CreateUnitCatalogueLayout(layout);
    int pageCount = Math.Max(1, (units.Count + catalogue.PageSize - 1) / catalogue.PageSize);
    if (catalogue.AddButton.Contains(point))
    {
      CampaignCustomUnitDefinition created = State.AddCustomUnit();
      _expandedCatalogueUnitId = created.Id;
      _unitCatalogueDropdown = UnitCatalogueDropdown.None;
      _unitDropdownIdentifier = null;
      int createdIndex = GetUnitCatalogue().ToList().FindIndex(entry => entry.Identifier == created.Id);
      _unitCataloguePage = Math.Max(0, createdIndex / catalogue.PageSize);
      _status = $"Created {created.Name}. Edit its card, then press PLACE.";
      return true;
    }
    if (catalogue.PreviousButton.Contains(point)) { _unitCataloguePage = (_unitCataloguePage - 1 + pageCount) % pageCount; return true; }
    if (catalogue.NextButton.Contains(point)) { _unitCataloguePage = (_unitCataloguePage + 1) % pageCount; return true; }
    int first = _unitCataloguePage * catalogue.PageSize;
    for (int slot = 0; slot < catalogue.Headers.Count && first + slot < units.Count; slot++)
    {
      if (!catalogue.Headers[slot].Contains(point)) continue;
      _expandedCatalogueUnitId = _expandedCatalogueUnitId == units[first + slot].Identifier ? null : units[first + slot].Identifier;
      _textField = TextField.None;
      _textUnitIdentifier = null;
      _replaceTextOnNextCharacter = false;
      _unitCatalogueDropdown = UnitCatalogueDropdown.None;
      _unitDropdownIdentifier = null;
      return true;
    }

    UnitCatalogueEntry? entry = units.FirstOrDefault(unit => unit.Identifier == _expandedCatalogueUnitId);
    if (entry is null) return false;
    UnitCatalogueDetails fields = CreateUnitCatalogueDetails(catalogue.Details);
    if (_unitCatalogueDropdown != UnitCatalogueDropdown.None && _unitDropdownIdentifier == entry.Identifier)
    {
      return HandleUnitDropdownClick(entry, fields, point);
    }
    if (fields.Name.Contains(point)) { BeginUnitTextEdit(TextField.UnitName, entry, entry.Definition.DisplayName); return true; }
    if (fields.Abbreviation.Contains(point)) { BeginUnitTextEdit(TextField.UnitAbbreviation, entry, UiText.BuildPieceLabel(entry.Definition)); return true; }
    if (fields.Cost.Contains(point)) { BeginUnitTextEdit(TextField.UnitCost, entry, entry.Definition.Cost.ToString()); return true; }
    if (fields.MoveRange.Contains(point)) { BeginUnitTextEdit(TextField.UnitMoveRange, entry, entry.Definition.Movement.range.ToString()); return true; }
    if (fields.Health.Contains(point)) { BeginUnitTextEdit(TextField.UnitHealth, entry, entry.Definition.Health.ToString()); return true; }
    if (fields.Attack.Contains(point)) { BeginUnitTextEdit(TextField.UnitAttack, entry, entry.Definition.Attack.ToString()); return true; }
    if (fields.Width.Contains(point)) { BeginUnitTextEdit(TextField.UnitWidth, entry, entry.Definition.Size.x.ToString()); return true; }
    if (fields.Height.Contains(point)) { BeginUnitTextEdit(TextField.UnitHeight, entry, entry.Definition.Size.y.ToString()); return true; }
    if (fields.MinimumRange.Contains(point)) { BeginUnitTextEdit(TextField.UnitMinimumRange, entry, entry.Definition.AttackRange.Minimum.ToString()); return true; }
    if (fields.MaximumRange.Contains(point)) { BeginUnitTextEdit(TextField.UnitMaximumRange, entry, entry.Definition.AttackRange.Maximum.ToString()); return true; }
    if (fields.MoveShape.Contains(point)) { OpenUnitDropdown(entry, UnitCatalogueDropdown.MoveShape); return true; }
    if (fields.AttackShape.Contains(point)) { OpenUnitDropdown(entry, UnitCatalogueDropdown.AttackShape); return true; }
    if (fields.Ability.Contains(point)) { OpenUnitDropdown(entry, UnitCatalogueDropdown.Ability); return true; }
    if (fields.Place.Contains(point)) { SelectCatalogueUnitForPlacement(entry.Identifier); return true; }
    if (fields.Buy.Contains(point)) { ToggleCatalogueBuying(entry); return true; }
    if (fields.Remove.Contains(point)) { RemoveCatalogueUnit(entry); return true; }
    return false;
  }

  private void HandleBoardClick(EditorLayout layout, Point point)
  {
    CampaignCoordinate position = GetPositionAt(layout, point);
    bool hasTile = State.Level.Board.Tiles.Any(tile => tile == position);
    if (State.ActiveTool == EditorTool.Tile)
    {
      State.AddTile(position);
      return;
    }
    if (!hasTile) { _status = "Add a playable tile before placing content here."; return; }
    if (_objectiveTargetMode != ObjectiveTargetMode.None) { SelectAt(position); return; }
    if (State.ActiveTool == EditorTool.Select) { SelectAt(position); return; }
    if (State.ActiveTool == EditorTool.Delete) { DeleteAt(position); return; }
    if (State.ActiveTool == EditorTool.Unit)
    {
      (string unitType, PieceDefinition unit) = GetUnitPalette()[_unitPaletteIndex % GetUnitPalette().Count];
      CampaignUnitDefinition candidate = new() { UnitType = unitType, Team = _placementTeam, Position = position };
      if (State.TryPlaceUnit(candidate, out string reason))
      {
        _status = $"Placed {unit.DisplayName} for {_placementTeam}.";
      }
      else _status = reason;
      return;
    }
    if (State.ActiveTool == EditorTool.Move)
    {
      if (State.Selection.Kind != EditorSelectionKind.Unit || State.Selection.Id is not string selectedUnitId)
      {
        _status = "Select a unit first, then use MOVE UNIT.";
        return;
      }
      if (State.TryMoveUnit(selectedUnitId, position, out string reason))
      {
        _status = "Moved selected unit.";
      }
      else _status = reason;
      return;
    }
    if (State.ActiveTool == EditorTool.Terrain)
    {
      State.PaintTerrain(_terrainPaletteType, position);
      return;
    }
    if (State.ActiveTool == EditorTool.Territory)
    {
      State.PaintTerritory(_territoryOwner == NetworkTeam.Neutral ? null : _territoryOwner, position);
      _status = $"Painted {CampaignTerritoryRules.GetAreaLabel(_territoryOwner == NetworkTeam.Neutral ? null : _territoryOwner)}.";
      return;
    }
    if (State.ActiveTool == EditorTool.Object)
    {
      CampaignBoardObjectType type = (CampaignBoardObjectType)(_objectPaletteIndex % Enum.GetValues<CampaignBoardObjectType>().Length);
      if (State.Level.Objects.Any(boardObject => boardObject.Type == type && boardObject.Position == position)) return;
      State.AddObject(new CampaignBoardObjectDefinition
      {
        Type = type,
        Position = position,
        Owner = type == CampaignBoardObjectType.Mine ? _placementTeam : null,
        Health = type == CampaignBoardObjectType.Barrier ? 20 : null
      });
    }
  }

  private bool CanPaintContinuously() =>
    _objectiveTargetMode == ObjectiveTargetMode.None &&
    State.ActiveTool is EditorTool.Tile or EditorTool.Terrain or EditorTool.Object or EditorTool.Territory or EditorTool.Delete;

  private void SelectAt(CampaignCoordinate position)
  {
    CampaignUnitDefinition? unit = State.Level.Units.LastOrDefault(candidate => UnitOccupies(candidate, position));
    if (_objectiveTargetMode == ObjectiveTargetMode.Unit && unit is not null && GetSelectedObjective() is CampaignObjectiveDefinition objective)
    {
      State.UpdateObjective(_editingDefeatConditions, objective.Id, value =>
      {
        if (value.Type == CampaignObjectiveType.GetUnitsToLocations) _pendingObjectiveUnitId = unit.Id;
        else value.TargetUnitId = unit.Id;
      });
      _objectiveTargetMode = ObjectiveTargetMode.None;
      _status = _pendingObjectiveUnitId is null ? "Objective unit selected." : "Unit selected. Press DESTINATION, then click that unit's target square.";
      return;
    }
    if (_objectiveTargetMode == ObjectiveTargetMode.Location && GetSelectedObjective() is CampaignObjectiveDefinition locationObjective)
    {
      if (locationObjective.Type == CampaignObjectiveType.GetUnitsToLocations && _pendingObjectiveUnitId is null)
      {
        _objectiveTargetMode = ObjectiveTargetMode.None;
        _status = "Choose the unit first, then choose its destination.";
        return;
      }
      State.UpdateObjective(_editingDefeatConditions, locationObjective.Id, value =>
      {
        if (value.Type == CampaignObjectiveType.GetUnitsToLocations && _pendingObjectiveUnitId is not null)
        {
          value.UnitLocationTargets.RemoveAll(target => target.UnitId == _pendingObjectiveUnitId);
          value.UnitLocationTargets.Add(new CampaignUnitLocationTargetDefinition { UnitId = _pendingObjectiveUnitId, Location = position });
          _pendingObjectiveUnitId = null;
        }
        else if (value.Type == CampaignObjectiveType.CaptureLocations)
        {
          if (!value.Locations.Contains(position)) value.Locations.Add(position);
        }
        else
        {
          value.Locations = [position];
        }
      });
      _objectiveTargetMode = ObjectiveTargetMode.None;
      _status = "Objective destination selected.";
      return;
    }
    if (unit is not null)
    {
      State.SelectUnit(unit.Id);
      _status = $"Selected {unit.UnitType}. Use MOVE UNIT to reposition it; edit its details in TEAMS or RULES.";
      return;
    }
    CampaignBoardObjectDefinition? boardObject = State.Level.Objects.LastOrDefault(candidate => candidate.Position == position);
    if (boardObject is not null)
    {
      State.SelectObject(boardObject.Id);
      _status = $"Selected {boardObject.Type}. Its inspector is available in TEAMS or RULES.";
      return;
    }
    if (State.Level.Terrain.Any(terrain => terrain.Position == position))
    {
      State.SelectTerrain(position);
      _status = "Selected terrain. Use TERRAIN to repaint it or DELETE to remove it.";
      return;
    }
    State.SelectUnit(null);
    _status = "Nothing selected.";
  }

  private void DeleteAt(CampaignCoordinate position)
  {
    CampaignUnitDefinition? unit = State.Level.Units.LastOrDefault(candidate => UnitOccupies(candidate, position));
    if (unit is not null) { State.DeleteUnit(unit.Id); return; }
    CampaignBoardObjectDefinition? boardObject = State.Level.Objects.LastOrDefault(candidate => candidate.Position == position);
    if (boardObject is not null) { State.DeleteObject(boardObject.Id); return; }
    if (State.Level.Terrain.Any(terrain => terrain.Position == position)) { State.DeleteTerrain(position); return; }
    State.RemoveTile(position);
  }

  private void AdjustSelectedHealth(string unitId, int delta)
  {
    State.UpdateUnit(unitId, unit =>
    {
      if (!CampaignRuntimeFactory.TryGetPieceDefinition(State.Level, unit.UnitType, unit.StatOverrides, out PieceDefinition definition)) return;
      int health = unit.Health ?? definition.Health;
      unit.Health = Math.Clamp(health + delta, 1, definition.Health);
    });
  }

  private void CycleGameMode()
  {
    string[] modes = ["Regicide", "Conquest", "Escort", "Dominion", "Plunder"];
    int current = Array.IndexOf(modes, State.Level.Scenario.GameMode);
    State.UpdateScenario(scenario => scenario.GameMode = modes[(current + 1 + modes.Length) % modes.Length]);
  }

  private void CycleFirstTeam()
  {
    NetworkTeam[] teams = State.Level.Teams.Select(team => team.Team).ToArray();
    if (teams.Length == 0) return;
    int current = Array.IndexOf(teams, State.Level.Scenario.FirstTeam);
    State.UpdateScenario(scenario => scenario.FirstTeam = teams[(current + 1 + teams.Length) % teams.Length]);
  }

  private void CyclePlacementTeam(int direction)
  {
    NetworkTeam[] teams = State.Level.Teams.Select(team => team.Team).ToArray();
    if (teams.Length == 0) return;
    int current = Array.IndexOf(teams, _placementTeam);
    _placementTeam = teams[(current + direction + teams.Length) % teams.Length];
    _status = $"New units will be placed for {_placementTeam}.";
  }

  private void CycleTerritoryOwner(int direction)
  {
    NetworkTeam[] areas = [NetworkTeam.Neutral, .. State.Level.Teams.Select(team => team.Team)];
    if (areas.Length == 0) return;
    int current = Array.IndexOf(areas, _territoryOwner);
    _territoryOwner = areas[(Math.Max(0, current) + direction + areas.Length) % areas.Length];
    _status = $"Territory brush: {CampaignTerritoryRules.GetAreaLabel(_territoryOwner == NetworkTeam.Neutral ? null : _territoryOwner)}.";
  }

  private void CycleSelectedUnitTeam(string unitId)
  {
    NetworkTeam[] teams = State.Level.Teams.Select(team => team.Team).ToArray();
    CampaignUnitDefinition? unit = State.Level.Units.FirstOrDefault(candidate => candidate.Id == unitId);
    if (unit is null || teams.Length == 0) return;
    int current = Array.IndexOf(teams, unit.Team);
    NetworkTeam next = teams[(current + 1 + teams.Length) % teams.Length];
    State.UpdateUnit(unitId, value => value.Team = next);
    _status = $"Selected unit assigned to {next}.";
  }

  private void ResizeBoard(int adjustment)
  {
    int width = Math.Clamp(State.Level.Board.Width + adjustment, 1, CampaignLevelFormat.MaximumBoardDimension);
    int height = Math.Clamp(State.Level.Board.Height + adjustment, 1, CampaignLevelFormat.MaximumBoardDimension);
    State.SetBoardSize(width, height);
    _fitBoardRequested = true;
    _status = "Board resized. Validate to find any units that no longer fit.";
  }

  private CampaignObjectiveDefinition? GetSelectedObjective()
  {
    IReadOnlyList<CampaignObjectiveDefinition> objectives = _editingDefeatConditions
      ? State.Level.Scenario.DefeatConditions
      : State.Level.Scenario.VictoryConditions;
    return objectives.FirstOrDefault(objective => objective.Id == _selectedObjectiveId) ?? objectives.LastOrDefault();
  }

  private static string GetObjectiveTitle(CampaignObjectiveType type) => type switch
  {
    CampaignObjectiveType.DefeatEnemyRoyal => "Defeat enemy royal",
    CampaignObjectiveType.Conquest => "Win by conquest",
    CampaignObjectiveType.EscapeRoyal => "Escape with royal",
    CampaignObjectiveType.Dominion => "Control dominion points",
    CampaignObjectiveType.Plunder => "Return treasure",
    CampaignObjectiveType.EliminateEnemies => "Eliminate all enemies",
    CampaignObjectiveType.SurviveTurns => "Survive turns",
    CampaignObjectiveType.CaptureLocations => "Capture locations",
    CampaignObjectiveType.EscortUnit => "Escort a unit",
    CampaignObjectiveType.GetUnitsToLocations => "Move units to squares",
    CampaignObjectiveType.ProtectUnit => "Protect a unit",
    CampaignObjectiveType.PreventEscape => "Prevent an escape",
    CampaignObjectiveType.Score => "Reach a score",
    CampaignObjectiveType.ReachCash => "Reach gold amount",
    _ => type.ToString()
  };

  private static string GetObjectiveExplanation(CampaignObjectiveDefinition objective)
  {
    string team = objective.Team?.ToString() ?? "the assigned side";
    return objective.Type switch
    {
      CampaignObjectiveType.DefeatEnemyRoyal => $"{team} wins after the opposing royal is defeated.",
      CampaignObjectiveType.Conquest => $"{team} wins by meeting the configured conquest goal.",
      CampaignObjectiveType.EscapeRoyal => $"{team} wins when its royal escapes through the objective edge.",
      CampaignObjectiveType.Dominion => $"{team} wins by scoring at dominion control points.",
      CampaignObjectiveType.Plunder => $"{team} wins by returning enough treasure to its own territory.",
      CampaignObjectiveType.EliminateEnemies => $"{team} wins when no opposing units remain.",
      CampaignObjectiveType.SurviveTurns => $"{team} wins after surviving {objective.RequiredAmount} turn{(objective.RequiredAmount == 1 ? string.Empty : "s")}.",
      CampaignObjectiveType.CaptureLocations => objective.Locations.Count == 0
        ? "Choose the squares that must be captured."
        : $"Capture the {objective.Locations.Count} marked square{(objective.Locations.Count == 1 ? string.Empty : "s")}.",
      CampaignObjectiveType.EscortUnit => string.IsNullOrWhiteSpace(objective.TargetUnitId)
        ? "Choose the unit to escort, then choose its destination square."
        : objective.Locations.Count == 0 ? "Choose the destination square for the escorted unit." : "Escort the marked unit to the gold square.",
      CampaignObjectiveType.GetUnitsToLocations => objective.UnitLocationTargets.Count == 0
        ? "Choose a unit, then choose the square it must reach."
        : $"Move {objective.UnitLocationTargets.Count} marked unit{(objective.UnitLocationTargets.Count == 1 ? string.Empty : "s")} to their gold square{(objective.UnitLocationTargets.Count == 1 ? string.Empty : "s")}.",
      CampaignObjectiveType.ProtectUnit => string.IsNullOrWhiteSpace(objective.TargetUnitId) ? "Choose the unit that must survive." : "Keep the marked unit alive.",
      CampaignObjectiveType.PreventEscape => "Stop the opposing side from escaping.",
      CampaignObjectiveType.Score => $"Reach a score of {objective.RequiredAmount}.",
      CampaignObjectiveType.ReachCash => $"Accumulate {objective.RequiredAmount} gold.",
      _ => "Configure this condition for the selected team."
    };
  }

  private static string GetObjectiveAmountLabel(CampaignObjectiveDefinition objective) => objective.Type switch
  {
    CampaignObjectiveType.SurviveTurns => $"TURNS: {objective.RequiredAmount}",
    CampaignObjectiveType.ReachCash => $"GOLD: {objective.RequiredAmount}",
    CampaignObjectiveType.Score => $"SCORE: {objective.RequiredAmount}",
    _ => "NO AMOUNT NEEDED"
  };

  private static bool ObjectiveUsesAmount(CampaignObjectiveType type) => type is
    CampaignObjectiveType.SurviveTurns or CampaignObjectiveType.ReachCash or CampaignObjectiveType.Score;

  private static string GetObjectiveUnitButtonLabel(CampaignObjectiveDefinition objective) => objective.Type switch
  {
    CampaignObjectiveType.EscortUnit or CampaignObjectiveType.ProtectUnit => "CHOOSE UNIT",
    CampaignObjectiveType.GetUnitsToLocations => "CHOOSE UNIT",
    _ => "MARK UNIT"
  };

  private static string GetObjectiveLocationButtonLabel(CampaignObjectiveDefinition objective) => objective.Type switch
  {
    CampaignObjectiveType.CaptureLocations => "CHOOSE SQUARE",
    CampaignObjectiveType.EscortUnit => "DESTINATION",
    CampaignObjectiveType.GetUnitsToLocations => "DESTINATION",
    _ => "MARK SQUARE"
  };

  private void CycleObjectiveTeam()
  {
    _objectiveTeam = GetNextObjectiveTeam(_objectiveTeam);
  }

  private NetworkTeam GetNextObjectiveTeam(NetworkTeam current)
  {
    NetworkTeam[] teams = State.Level.Teams.Select(team => team.Team).ToArray();
    if (teams.Length == 0) return current;
    int index = Array.IndexOf(teams, current);
    return teams[(Math.Max(0, index) + 1) % teams.Length];
  }

  private void CycleSettingsTeam(int direction)
  {
    NetworkTeam[] teams = State.Level.Teams.Select(team => team.Team).ToArray();
    if (teams.Length == 0) return;
    int current = Array.IndexOf(teams, _settingsTeam);
    _settingsTeam = teams[(current + direction + teams.Length) % teams.Length];
  }

  private void CycleRestrictionUnit(int direction)
  {
    _restrictionUnitIndex = (_restrictionUnitIndex + direction + UnitRules.Purchasable.Count) % UnitRules.Purchasable.Count;
  }

  private IReadOnlyList<(string identifier, PieceDefinition definition)> GetUnitPalette()
  {
    return GetUnitCatalogue().Select(entry => (entry.Identifier, entry.Definition)).ToArray();
  }

  private IReadOnlyList<UnitCatalogueEntry> GetUnitCatalogue()
  {
    List<UnitCatalogueEntry> catalogue = [];
    foreach (PieceDefinition native in PieceDefinitions.All)
    {
      if (!CampaignUnitResolver.TryResolve(State.Level, native.Identifier, null, out PieceDefinition definition)) continue;
      CampaignUnitTemplateOverrideDefinition? unitOverride = State.Level.UnitOverrides.FirstOrDefault(entry => entry.UnitType == native.Identifier);
      catalogue.Add(new UnitCatalogueEntry(
        native.Identifier,
        false,
        unitOverride?.AbilitySourceUnitType ?? native.Identifier,
        unitOverride?.Purchasable ?? native.Category != PieceCategory.Royal,
        definition
      ));
    }
    foreach (CampaignCustomUnitDefinition custom in State.Level.CustomUnits)
    {
      if (!CampaignUnitResolver.TryResolve(State.Level, custom.Id, null, out PieceDefinition definition)) continue;
      catalogue.Add(new UnitCatalogueEntry(custom.Id, true, custom.AbilitySourceUnitType, custom.Purchasable, definition));
    }
    return catalogue;
  }

  private UnitCatalogueLayout CreateUnitCatalogueLayout(EditorLayout layout)
  {
    int x = layout.Properties.X + layout.PanelPadding;
    int width = Math.Max(1, layout.Properties.Width - layout.PanelPadding * 2);
    int gap = 4;
    int row = Math.Clamp(layout.Properties.Height / 28, 20, 26);
    int top = layout.SettingsTop + 30;
    Rectangle add = new(x, top, width, row);
    int arrowWidth = Math.Clamp(width / 6, 22, 34);
    Rectangle previous = new(x, add.Bottom + gap, arrowWidth, row);
    Rectangle next = new(x + width - arrowWidth, previous.Y, arrowWidth, row);
    Rectangle page = new(previous.Right + gap, previous.Y, Math.Max(1, next.X - previous.Right - gap * 2), row);
    int pageSize = layout.Properties.Height < 520 ? 3 : 5;
    List<Rectangle> headers = [];
    int headerY = previous.Bottom + gap;
    for (int index = 0; index < pageSize; index++)
    {
      headers.Add(new Rectangle(x, headerY + index * (row + 2), width, row));
    }
    int detailsY = headers[^1].Bottom + gap;
    Rectangle details = new(x, detailsY, width, Math.Max(1, layout.Properties.Bottom - detailsY - 7));
    return new UnitCatalogueLayout(add, previous, page, next, headers, details, pageSize);
  }

  private static UnitCatalogueDetails CreateUnitCatalogueDetails(Rectangle bounds)
  {
    int gap = 3;
    int rows = 9;
    int rowHeight = Math.Max(16, (bounds.Height - gap * (rows - 1)) / rows);
    Rectangle Row(int index) => new(bounds.X, bounds.Y + index * (rowHeight + gap), bounds.Width, Math.Max(1, rowHeight));
    Rectangle[] Split(Rectangle row, int count) => Enumerable.Range(0, count)
      .Select(index => UiLayout.HorizontalSlot(row, count, index, gap)).ToArray();
    Rectangle[] name = Split(Row(0), 1);
    Rectangle[] abbrCost = Split(Row(1), 2);
    Rectangle[] movement = Split(Row(2), 2);
    Rectangle[] healthAttack = Split(Row(3), 2);
    Rectangle[] size = Split(Row(4), 2);
    Rectangle[] range = Split(Row(5), 2);
    Rectangle[] attackShape = Split(Row(6), 1);
    Rectangle[] ability = Split(Row(7), 1);
    Rectangle[] actions = Split(Row(8), 3);
    return new UnitCatalogueDetails(name[0], abbrCost[0], abbrCost[1], movement[0], movement[1], healthAttack[0], healthAttack[1], size[0], size[1], range[0], range[1], attackShape[0], ability[0], actions[0], actions[1], actions[2]);
  }

  private static UnitDropdownLayout CreateUnitDropdownLayout(Rectangle bounds)
  {
    int gap = 3;
    int row = Math.Max(18, bounds.Height / 9);
    int arrowWidth = Math.Clamp(bounds.Width / 6, 20, 34);
    Rectangle title = new(bounds.X + 3, bounds.Y + 3, Math.Max(1, bounds.Width - 6), row);
    Rectangle previous = new(bounds.X + 3, title.Bottom + gap, arrowWidth, row);
    Rectangle next = new(bounds.Right - arrowWidth - 3, previous.Y, arrowWidth, row);
    Rectangle page = new(previous.Right + gap, previous.Y, Math.Max(1, next.X - previous.Right - gap * 2), row);
    const int pageSize = 6;
    List<Rectangle> options = [];
    for (int index = 0; index < pageSize; index++)
    {
      options.Add(new Rectangle(bounds.X + 3, previous.Bottom + gap + index * (row + gap), Math.Max(1, bounds.Width - 6), row));
    }
    return new UnitDropdownLayout(title, previous, page, next, options, pageSize);
  }

  private void OpenUnitDropdown(UnitCatalogueEntry entry, UnitCatalogueDropdown dropdown)
  {
    _unitCatalogueDropdown = dropdown;
    _unitDropdownIdentifier = entry.Identifier;
    _unitDropdownPage = 0;
    _textField = TextField.None;
    _textUnitIdentifier = null;
  }

  private bool HandleUnitDropdownClick(UnitCatalogueEntry entry, UnitCatalogueDetails fields, Point point)
  {
    Rectangle bounds = new(fields.Name.X, fields.Name.Y, fields.Name.Width, fields.Remove.Bottom - fields.Name.Y);
    UnitDropdownLayout dropdown = CreateUnitDropdownLayout(bounds);
    IReadOnlyList<string> options = GetUnitDropdownOptions();
    int pageCount = Math.Max(1, (options.Count + dropdown.PageSize - 1) / dropdown.PageSize);
    if (dropdown.Previous.Contains(point)) { _unitDropdownPage = (_unitDropdownPage - 1 + pageCount) % pageCount; return true; }
    if (dropdown.Next.Contains(point)) { _unitDropdownPage = (_unitDropdownPage + 1) % pageCount; return true; }
    int first = _unitDropdownPage * dropdown.PageSize;
    for (int index = 0; index < dropdown.Options.Count && first + index < options.Count; index++)
    {
      if (!dropdown.Options[index].Contains(point)) continue;
      string selected = options[first + index];
      switch (_unitCatalogueDropdown)
      {
        case UnitCatalogueDropdown.MoveShape:
          UpdateCatalogueStats(entry, value => value.MovePattern = Enum.Parse<Shape>(selected));
          break;
        case UnitCatalogueDropdown.AttackShape:
          UpdateCatalogueStats(entry, value => value.AttackPattern = Enum.Parse<Shape>(selected));
          break;
        case UnitCatalogueDropdown.Ability:
          SetCatalogueAbility(entry, selected);
          break;
      }
      _unitCatalogueDropdown = UnitCatalogueDropdown.None;
      _unitDropdownIdentifier = null;
      return true;
    }
    _unitCatalogueDropdown = UnitCatalogueDropdown.None;
    _unitDropdownIdentifier = null;
    return true;
  }

  private IReadOnlyList<string> GetUnitDropdownOptions() => _unitCatalogueDropdown switch
  {
    UnitCatalogueDropdown.MoveShape or UnitCatalogueDropdown.AttackShape =>
      [Shape.Any.ToString(), Shape.Straight.ToString(), Shape.Line.ToString(), Shape.Forward.ToString(), Shape.AbsoluteStraightOrDiagonal.ToString(), Shape.ForwardOrForwardDiagonal.ToString(), Shape.None.ToString()],
    UnitCatalogueDropdown.Ability => ["None", .. PieceDefinitions.All.Where(unit => !string.IsNullOrWhiteSpace(unit.AbilityDescription)).Select(unit => unit.Identifier)],
    _ => []
  };

  private string GetSelectedDropdownValue(UnitCatalogueEntry entry) => _unitCatalogueDropdown switch
  {
    UnitCatalogueDropdown.MoveShape => entry.Definition.Movement.shape.ToString(),
    UnitCatalogueDropdown.AttackShape => entry.Definition.AttackPattern.ToString(),
    UnitCatalogueDropdown.Ability => GetAbilityLabel(entry.AbilitySource) == "NONE" ? "None" : entry.AbilitySource,
    _ => string.Empty
  };

  private void BeginUnitTextEdit(TextField field, UnitCatalogueEntry entry, string initialValue)
  {
    _textField = field;
    _textUnitIdentifier = entry.Identifier;
    _textBuffer = initialValue;
    _replaceTextOnNextCharacter = true;
  }

  private void UpdateCatalogueStats(UnitCatalogueEntry entry, Action<CampaignUnitStatOverrides> update)
  {
    if (entry.IsCustom)
    {
      State.UpdateCustomUnit(entry.Identifier, value =>
      {
        value.StatOverrides ??= new CampaignUnitStatOverrides();
        update(value.StatOverrides);
      });
    }
    else
    {
      State.UpdateBuiltInUnit(entry.Identifier, value => update(value.StatOverrides));
    }
  }

  private void UpdateCatalogueName(UnitCatalogueEntry entry, string name)
  {
    if (entry.IsCustom) State.UpdateCustomUnit(entry.Identifier, value => value.Name = name);
    else State.UpdateBuiltInUnit(entry.Identifier, value => value.Name = name);
  }

  private void UpdateCatalogueAbbreviation(UnitCatalogueEntry entry, string abbreviation)
  {
    if (entry.IsCustom) State.UpdateCustomUnit(entry.Identifier, value => value.Abbreviation = abbreviation);
    else State.UpdateBuiltInUnit(entry.Identifier, value => value.Abbreviation = abbreviation);
  }

  private void SetCatalogueAbility(UnitCatalogueEntry entry, string source)
  {
    if (entry.IsCustom) State.UpdateCustomUnit(entry.Identifier, value => value.AbilitySourceUnitType = source);
    else State.UpdateBuiltInUnit(entry.Identifier, value => value.AbilitySourceUnitType = source);
    _status = $"{entry.Definition.DisplayName} ability source: {GetAbilityLabel(source)}.";
  }

  private void SelectCatalogueUnitForPlacement(string identifier)
  {
    int index = GetUnitPalette().ToList().FindIndex(entry => entry.identifier == identifier);
    if (index < 0) return;
    _unitPaletteIndex = index;
    State.ActiveTool = EditorTool.Unit;
    _status = "Unit selected. Click a playable board square to place it.";
  }

  private void ToggleCatalogueBuying(UnitCatalogueEntry entry)
  {
    if (!entry.IsCustom && !PieceDefinitions.Purchasable.Any(unit => unit.Identifier == entry.Identifier))
    {
      _status = "Royals are placed from the catalogue but cannot be bought.";
      return;
    }
    CampaignTeamDefinition? selectedTeam = State.Level.Teams.FirstOrDefault(team => team.Team == _settingsTeam);
    bool currentlyAllowed = entry.Purchasable && (selectedTeam?.PurchaseListMode == CampaignPurchaseListMode.All || selectedTeam?.AvailableUnitTypes.Contains(entry.Identifier) == true);
    bool shouldAllow = !currentlyAllowed;
    if (!entry.Purchasable && shouldAllow)
    {
      if (entry.IsCustom) State.UpdateCustomUnit(entry.Identifier, value => value.Purchasable = true);
      else State.UpdateBuiltInUnit(entry.Identifier, value => value.Purchasable = true);
    }
    State.UpdateTeam(_settingsTeam, team =>
    {
      if (team.PurchaseListMode != CampaignPurchaseListMode.Custom)
      {
        team.PurchaseListMode = CampaignPurchaseListMode.Custom;
        team.AvailableUnitTypes = [.. CampaignUnitResolver.GetPurchasableIdentifiers(State.Level)];
      }
      if (shouldAllow)
      {
        if (!team.AvailableUnitTypes.Contains(entry.Identifier)) team.AvailableUnitTypes.Add(entry.Identifier);
      }
      else
      {
        team.AvailableUnitTypes.RemoveAll(identifier => identifier == entry.Identifier);
      }
    });
    _status = $"Updated {_settingsTeam}'s buy list for {entry.Definition.DisplayName}.";
  }

  private void RemoveCatalogueUnit(UnitCatalogueEntry entry)
  {
    if (entry.IsCustom)
    {
      State.DeleteCustomUnit(entry.Identifier);
      _expandedCatalogueUnitId = null;
      _status = "Custom unit deleted, including its placed copies.";
      return;
    }
    if (!PieceDefinitions.Purchasable.Any(unit => unit.Identifier == entry.Identifier))
    {
      _status = "Royals are fixed game pieces. Use the card's stats and PLACE controls instead.";
      return;
    }
    State.UpdateBuiltInUnit(entry.Identifier, value => value.Purchasable = !entry.Purchasable);
    if (entry.Purchasable)
    {
      foreach (NetworkTeam team in State.Level.Teams.Select(team => team.Team).ToArray())
      {
        State.UpdateTeam(team, value => value.AvailableUnitTypes.RemoveAll(type => type == entry.Identifier));
      }
    }
    _status = entry.Purchasable ? "Removed from the buy menu." : "Restored to the buy menu.";
  }

  private static string GetAbilityLabel(string source)
  {
    if (string.Equals(source, "None", StringComparison.OrdinalIgnoreCase) || !UnitRules.TryGet(source, out UnitRule rule) || string.IsNullOrWhiteSpace(rule.AbilityDescription)) return "NONE";
    return source.ToUpperInvariant();
  }

  private IReadOnlyList<(string identifier, PieceDefinition definition)> GetPurchasableUnitPalette() =>
    CampaignUnitResolver.GetPurchasableIdentifiers(State.Level)
      .Select(identifier => CampaignUnitResolver.TryResolve(State.Level, identifier, null, out PieceDefinition definition)
        ? (identifier, definition)
        : ((string identifier, PieceDefinition definition)?)null)
      .Where(entry => entry.HasValue)
      .Select(entry => entry!.Value)
      .ToArray();

  private static string GetBuyListMode(CampaignTeamDefinition team) => team.PurchaseListMode.ToString().ToUpperInvariant();

  private void SetTeamBuyListMode(CampaignPurchaseListMode mode)
  {
    State.UpdateTeam(_settingsTeam, value =>
    {
      value.PurchaseListMode = mode;
      if (mode == CampaignPurchaseListMode.All)
      {
        value.AvailableUnitTypes = [.. CampaignUnitResolver.GetPurchasableIdentifiers(State.Level)];
      }
    });
    _status = mode switch
    {
      CampaignPurchaseListMode.All => "All purchasable units are allowed for this team.",
      CampaignPurchaseListMode.Custom => "Custom buy list selected. Use the unit selector below to allow or block individual units.",
      _ => "No units are purchasable for this team."
    };
  }

  private static void ToggleUnit(List<string> values, string type)
  {
    if (!values.Remove(type)) values.Add(type);
  }

  private void SynchronisePlacementTeam()
  {
    if (!State.Level.Teams.Any(team => team.Team == _placementTeam))
    {
      _placementTeam = State.Level.Teams.FirstOrDefault()?.Team ?? NetworkTeam.Red;
    }
    if (!State.Level.Teams.Any(team => team.Team == _settingsTeam)) _settingsTeam = _placementTeam;
    if (!State.Level.Teams.Any(team => team.Team == _objectiveTeam)) _objectiveTeam = _placementTeam;
  }

  private void UpdateKeyboardNavigation(KeyboardState keyboard, KeyboardState previous)
  {
    if (_textField != TextField.None) return;
    Vector2 direction = Vector2.Zero;
    if (keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A)) direction.X -= 1;
    if (keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D)) direction.X += 1;
    if (keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.W)) direction.Y -= 1;
    if (keyboard.IsKeyDown(Keys.Down) || keyboard.IsKeyDown(Keys.S)) direction.Y += 1;
    if (direction != Vector2.Zero) _camera += Vector2.Normalize(direction) * 0.16f / _zoom;
    if (keyboard.IsKeyDown(Keys.Q)) _zoom = MathHelper.Clamp(_zoom * 0.98f, 0.35f, 2.8f);
    if (keyboard.IsKeyDown(Keys.E)) _zoom = MathHelper.Clamp(_zoom * 1.02f, 0.35f, 2.8f);
    if (Pressed(keyboard, previous, Keys.Home)) _fitBoardRequested = true;
  }

  private void UpdateTextInput(KeyboardState keyboard, KeyboardState previous)
  {
    if (_textField == TextField.None) return;
    foreach (Keys key in keyboard.GetPressedKeys())
    {
      if (!Pressed(keyboard, previous, key)) continue;
      if (key == Keys.Enter)
      {
        CommitAndEndTextEdit();
        return;
      }
      if (key == Keys.Back)
      {
        _replaceTextOnNextCharacter = false;
        _textBuffer = _textBuffer.Length == 0 ? _textBuffer : _textBuffer[..^1];
      }
      else if (key == Keys.Delete)
      {
        _replaceTextOnNextCharacter = false;
        _textBuffer = string.Empty;
      }
      else if (TryGetCharacter(key, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift), out char character) && _textBuffer.Length < 500)
      {
        if (_replaceTextOnNextCharacter) _textBuffer = string.Empty;
        _replaceTextOnNextCharacter = false;
        _textBuffer += character;
      }
    }
  }

  private void BeginTextEdit(TextField field, string value)
  {
    _textField = field;
    _textBuffer = value;
    _textUnitIdentifier = null;
    _replaceTextOnNextCharacter = true;
  }

  private void CommitAndEndTextEdit()
  {
    if (_textField == TextField.None) return;
    CommitTextField();
    _textField = TextField.None;
    _textBuffer = string.Empty;
    _textUnitIdentifier = null;
    _replaceTextOnNextCharacter = false;
  }

  private void CommitTextField()
  {
    if (_textField is >= TextField.UnitName and <= TextField.UnitCost)
    {
      CommitUnitTextField();
      return;
    }
    State.EditMetadata(metadata =>
    {
      switch (_textField)
      {
        case TextField.Name: metadata.Name = _textBuffer; break;
        case TextField.Author: metadata.Author = _textBuffer; break;
        case TextField.Description: metadata.Description = _textBuffer; break;
        case TextField.Dialogue: metadata.CampaignDialogue = _textBuffer; break;
      }
    });
  }

  private void CommitUnitTextField()
  {
    UnitCatalogueEntry? entry = GetUnitCatalogue().FirstOrDefault(unit => unit.Identifier == _textUnitIdentifier);
    if (entry is null) return;
    if (_textField == TextField.UnitName)
    {
      UpdateCatalogueName(entry, _textBuffer.Trim());
      return;
    }
    if (_textField == TextField.UnitAbbreviation)
    {
      UpdateCatalogueAbbreviation(entry, _textBuffer.Trim());
      return;
    }
    if (!int.TryParse(_textBuffer, out int value))
    {
      _status = "Enter a whole number for that unit stat.";
      return;
    }
    PieceDefinition definition = entry.Definition;
    UpdateCatalogueStats(entry, overrides =>
    {
      switch (_textField)
      {
        case TextField.UnitMoveRange: overrides.MoveRange = Math.Clamp(value, 0, 32); break;
        case TextField.UnitHealth: overrides.Health = Math.Clamp(value, 1, 10_000); break;
        case TextField.UnitAttack: overrides.Attack = Math.Clamp(value, 0, 10_000); break;
        case TextField.UnitWidth: overrides.Width = Math.Clamp(value, 1, 8); break;
        case TextField.UnitHeight: overrides.Height = Math.Clamp(value, 1, 8); break;
        case TextField.UnitMinimumRange: overrides.MinimumAttackRange = Math.Clamp(value, 0, definition.AttackRange.Maximum); break;
        case TextField.UnitMaximumRange: overrides.MaximumAttackRange = Math.Clamp(value, definition.AttackRange.Minimum, 32); break;
        case TextField.UnitCost: overrides.Cost = Math.Clamp(value, 0, 1_000_000); break;
      }
    });
  }

  private static bool Pressed(KeyboardState current, KeyboardState previous, Keys key) =>
    current.IsKeyDown(key) && !previous.IsKeyDown(key);

  private static bool TryGetCharacter(Keys key, bool shift, out char character)
  {
    if (key is >= Keys.A and <= Keys.Z) { character = (char)((shift ? 'A' : 'a') + (key - Keys.A)); return true; }
    if (key is >= Keys.D0 and <= Keys.D9) { character = (char)('0' + (key - Keys.D0)); return true; }
    character = key switch
    {
      Keys.Space => ' ', Keys.OemPeriod => '.', Keys.OemComma => ',', Keys.OemMinus => '-', Keys.OemPlus => '+', Keys.OemQuestion => '?', Keys.OemSemicolon => ':', _ => '\0'
    };
    return character != '\0';
  }

  private Rectangle GetTileBounds(EditorLayout layout, CampaignCoordinate position)
  {
    float size = 44f * _zoom;
    float centreX = State.Level.Board.OriginX + State.Level.Board.Width / 2f + _camera.X;
    float centreY = State.Level.Board.OriginY + State.Level.Board.Height / 2f + _camera.Y;
    return new Rectangle(
      (int)MathF.Round(layout.Canvas.Center.X + (position.X - centreX) * size),
      (int)MathF.Round(layout.Canvas.Center.Y + (position.Y - centreY) * size),
      Math.Max(4, (int)MathF.Ceiling(size)),
      Math.Max(4, (int)MathF.Ceiling(size)));
  }

  private CampaignCoordinate GetPositionAt(EditorLayout layout, Point point)
  {
    float size = 44f * _zoom;
    float centreX = State.Level.Board.OriginX + State.Level.Board.Width / 2f + _camera.X;
    float centreY = State.Level.Board.OriginY + State.Level.Board.Height / 2f + _camera.Y;
    return new CampaignCoordinate(
      (int)MathF.Floor((point.X - layout.Canvas.Center.X) / size + centreX),
      (int)MathF.Floor((point.Y - layout.Canvas.Center.Y) / size + centreY));
  }

  private void FitBoardToCanvas(EditorLayout layout)
  {
    IReadOnlyList<CampaignCoordinate> tiles = State.Level.Board.Tiles;
    if (tiles.Count == 0 || layout.Canvas.Width < 8 || layout.Canvas.Height < 8)
    {
      _camera = Vector2.Zero;
      _zoom = 1f;
      return;
    }

    int minX = tiles.Min(tile => tile.X);
    int maxX = tiles.Max(tile => tile.X);
    int minY = tiles.Min(tile => tile.Y);
    int maxY = tiles.Max(tile => tile.Y);
    int spanX = Math.Max(1, maxX - minX + 1);
    int spanY = Math.Max(1, maxY - minY + 1);
    const int canvasPadding = 32;
    float widthZoom = Math.Max(0.08f, (layout.Canvas.Width - canvasPadding) / (spanX * 44f));
    float heightZoom = Math.Max(0.08f, (layout.Canvas.Height - canvasPadding) / (spanY * 44f));
    _zoom = MathHelper.Clamp(Math.Min(widthZoom, heightZoom), 0.20f, 2.8f);
    _camera = new Vector2(
      (minX + maxX + 1) / 2f - (State.Level.Board.OriginX + State.Level.Board.Width / 2f),
      (minY + maxY + 1) / 2f - (State.Level.Board.OriginY + State.Level.Board.Height / 2f)
    );
  }

  private void DrawCanvasRectangle(EditorLayout layout, Rectangle bounds, Color colour)
  {
    Rectangle clipped = Rectangle.Intersect(bounds, layout.Canvas);
    if (clipped.Width > 0 && clipped.Height > 0) _spriteBatch.Draw(_pixel, clipped, colour);
  }

  private void DrawCanvasOutline(EditorLayout layout, Rectangle bounds, Color colour, int thickness)
  {
    DrawCanvasRectangle(layout, new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), colour);
    DrawCanvasRectangle(layout, new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), colour);
    DrawCanvasRectangle(layout, new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), colour);
    DrawCanvasRectangle(layout, new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), colour);
  }

  private void DrawObjectiveGuides(EditorLayout layout)
  {
    if (!_editingScenarioObjectives || GetSelectedObjective() is not CampaignObjectiveDefinition objective) return;

    foreach (CampaignCoordinate location in objective.Locations.Concat(objective.UnitLocationTargets.Select(target => target.Location)))
    {
      Rectangle marker = GetTileBounds(layout, location);
      DrawCanvasRectangle(layout, new Rectangle(marker.Center.X - Math.Max(3, marker.Width / 5), marker.Center.Y - Math.Max(3, marker.Height / 5), Math.Max(6, marker.Width * 2 / 5), Math.Max(6, marker.Height * 2 / 5)), UiTheme.GoldBright);
      DrawCanvasOutline(layout, marker, UiTheme.GoldBright, 2);
    }

    if (!string.IsNullOrWhiteSpace(objective.TargetUnitId))
    {
      CampaignUnitDefinition? unit = State.Level.Units.FirstOrDefault(candidate => candidate.Id == objective.TargetUnitId);
      if (unit is not null && CampaignRuntimeFactory.TryCreatePiece(State.Level, unit, out Piece? piece) && piece is not null)
      {
        Rectangle origin = GetTileBounds(layout, new CampaignCoordinate(piece.Position.x, piece.Position.y));
        Rectangle bounds = new(origin.X + 2, origin.Y + 2, Math.Max(4, origin.Width * piece.Definition.Size.x - 4), Math.Max(4, origin.Height * piece.Definition.Size.y - 4));
        DrawCanvasOutline(layout, bounds, UiTheme.GoldBright, 3);
      }
    }
  }

  private bool UnitOccupies(CampaignUnitDefinition unit, CampaignCoordinate position)
  {
    return CampaignRuntimeFactory.TryCreatePiece(State.Level, unit, out Piece? piece) && piece is not null && piece.Occupies((position.X, position.Y));
  }

  private void ShowSaveResult(CampaignLevelSaveResult result, string success)
  {
    _problems.Clear();
    _problems.AddRange(result.Problems);
    _status = result.IsSuccess ? success : "Level was not saved. Review validation issues.";
  }

  private void ShowLoadResult(CampaignLevelLoadResult result, string success)
  {
    _problems.Clear();
    _problems.AddRange(result.Problems);
    if (result.IsSuccess)
    {
      _objectiveTargetMode = ObjectiveTargetMode.None;
      _selectedObjectiveId = null;
      _pendingObjectiveUnitId = null;
      _unitCatalogueDropdown = UnitCatalogueDropdown.None;
      _unitDropdownIdentifier = null;
      SynchronisePlacementTeam();
    }
    _status = result.IsSuccess ? success : "Level was not imported. Review validation issues.";
  }

  private void DrawOutline(Rectangle bounds, Color colour, int thickness)
  {
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), colour);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), colour);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), colour);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), colour);
  }

  private sealed class EditorLayout
  {
    internal Rectangle Header { get; }
    internal Rectangle Tools { get; }
    internal Rectangle Viewport { get; }
    internal Rectangle Canvas { get; }
    internal Rectangle Properties { get; }
    internal Rectangle Status { get; }
    internal Rectangle TitleBounds { get; }
    internal Rectangle ValidationBounds { get; }
    internal bool IsCompact { get; }
    internal int PanelPadding { get; }
    internal float HeaderButtonScale { get; }
    internal float ToolButtonScale { get; }
    internal float SmallControlScale { get; }
    internal Rectangle SaveButton { get; }
    internal Rectangle ExportButton { get; }
    internal Rectangle ImportButton { get; }
    internal Rectangle BrowseButton { get; }
    internal Rectangle TestButton { get; }
    internal Rectangle ExitButton { get; }
    internal IReadOnlyList<(EditorTool tool, string label, Rectangle bounds)> ToolButtons { get; }
    internal Rectangle UndoButton { get; }
    internal Rectangle RedoButton { get; }
    internal Rectangle NewButton { get; }
    internal Rectangle BoardSmallerButton { get; }
    internal Rectangle BoardSizeLabel { get; }
    internal Rectangle BoardLargerButton { get; }
    internal Rectangle BoardShapeButton { get; }
    internal Rectangle BoardBasePrevious { get; }
    internal Rectangle BoardBaseLabel { get; }
    internal Rectangle BoardBaseNext { get; }
    internal Rectangle BoardBaseApply { get; }
    internal Rectangle FitBoardButton { get; }
    internal int PaletteTop { get; }
    internal Rectangle UnitPrevious { get; }
    internal Rectangle UnitNext { get; }
    internal Rectangle UnitPreview { get; }
    internal Rectangle UnitName { get; }
    internal Rectangle TeamPrevious { get; }
    internal Rectangle TeamNext { get; }
    internal Rectangle TeamPreview { get; }
    internal int ObjectPaletteTop { get; }
    internal Rectangle ObjectPrevious { get; }
    internal Rectangle ObjectNext { get; }
    internal Rectangle ObjectPreview { get; }
    internal Rectangle TerrainButton { get; }
    internal int TerritoryTop { get; }
    internal IReadOnlyList<Rectangle> TerritoryButtons { get; }
    internal Rectangle ResetTerritoryButton { get; }
    internal Rectangle NameField { get; }
    internal Rectangle AuthorField { get; }
    internal Rectangle DescriptionField { get; }
    internal Rectangle DialogueField { get; }
    internal Rectangle ModeButton { get; }
    internal Rectangle FirstTeamButton { get; }
    internal Rectangle TurnLimitDown { get; }
    internal Rectangle TurnLimitLabel { get; }
    internal Rectangle TurnLimitUp { get; }
    internal bool ShowSelectionProperties { get; }
    internal int SelectionTop { get; }
    internal Rectangle SelectedRowOne { get; }
    internal Rectangle SelectedPrevious { get; }
    internal Rectangle SelectedNext { get; }
    internal Rectangle RotateButton { get; }
    internal Rectangle ScenarioTab { get; }
    internal Rectangle TeamsTab { get; }
    internal Rectangle RestrictionsTab { get; }
    internal Rectangle UnitsTab { get; }
    internal Rectangle ScenarioDetailsButton { get; }
    internal Rectangle ScenarioObjectivesButton { get; }
    internal int ObjectiveTop { get; }
    internal Rectangle ObjectiveOutcomeButton { get; }
    internal Rectangle ObjectivePrevious { get; }
    internal Rectangle ObjectiveTypeLabel { get; }
    internal Rectangle ObjectiveNext { get; }
    internal Rectangle ObjectiveTeamButton { get; }
    internal Rectangle ObjectiveAddButton { get; }
    internal Rectangle ObjectiveRemoveButton { get; }
    internal Rectangle ObjectiveActiveLabel { get; }
    internal Rectangle ObjectiveHelp { get; }
    internal Rectangle ObjectiveAmountDown { get; }
    internal Rectangle ObjectiveAmountLabel { get; }
    internal Rectangle ObjectiveAmountUp { get; }
    internal Rectangle ObjectiveTargetButton { get; }
    internal Rectangle ObjectiveLocationButton { get; }
    internal int SettingsTop { get; }
    internal Rectangle SettingsTeamPrevious { get; }
    internal Rectangle SettingsTeamLabel { get; }
    internal Rectangle SettingsTeamNext { get; }
    internal Rectangle ControllerButton { get; }
    internal Rectangle MoneyDownButton { get; }
    internal Rectangle MoneyLabel { get; }
    internal Rectangle MoneyUpButton { get; }
    internal Rectangle ActionsDownButton { get; }
    internal Rectangle ActionsLabel { get; }
    internal Rectangle ActionsUpButton { get; }
    internal Rectangle TeamPurchasesButton { get; }
    internal Rectangle TeamBuyListMenu { get; }
    internal IReadOnlyList<Rectangle> TeamBuyListOptions { get; }
    internal int TeamUnitTop { get; }
    internal Rectangle TeamUnitPrevious { get; }
    internal Rectangle TeamUnitLabel { get; }
    internal Rectangle TeamUnitNext { get; }
    internal Rectangle TeamUnitToggle { get; }
    internal Rectangle CpuDifficultyButton { get; }
    internal Rectangle CpuPersonalityButton { get; }
    internal Rectangle GlobalPurchasesButton { get; }
    internal Rectangle AbilitiesButton { get; }
    internal Rectangle RulesHelp { get; }
    internal Rectangle UnitStatPrevious { get; }
    internal Rectangle UnitStatLabel { get; }
    internal Rectangle UnitStatNext { get; }
    internal Rectangle UnitStatOverrideButton { get; }
    internal Rectangle UnitStatDown { get; }
    internal Rectangle UnitStatValue { get; }
    internal Rectangle UnitStatUp { get; }
    internal Rectangle UnitAbilityPrevious { get; }
    internal Rectangle UnitAbilityLabel { get; }
    internal Rectangle UnitAbilityNext { get; }
    internal Rectangle CreateCustomUnitButton { get; }
    internal Rectangle UnitStatsHelp { get; }

    internal EditorLayout(Rectangle screen, int teamCount)
    {
      IsCompact = screen.Width < 1100 || screen.Height < 720;
      PanelPadding = Math.Clamp(screen.Width / 110, 6, 12);
      HeaderButtonScale = IsCompact ? 0.54f : 0.68f;
      ToolButtonScale = IsCompact ? 0.54f : 0.64f;
      SmallControlScale = IsCompact ? 0.50f : 0.60f;
      int gutter = Math.Clamp(screen.Width / 250, 2, 8);
      int headerHeight = screen.Height >= 620 ? 64 : Math.Clamp(screen.Height / 10, 46, 60);
      int statusHeight = Math.Clamp(screen.Height / 9, 54, 82);
      Header = new Rectangle(screen.X, screen.Y, Math.Max(1, screen.Width), headerHeight);
      Status = new Rectangle(screen.X, Math.Max(Header.Bottom, screen.Bottom - statusHeight), Math.Max(1, screen.Width), Math.Max(1, screen.Bottom - Math.Max(Header.Bottom, screen.Bottom - statusHeight)));

      int desiredCanvas = Math.Clamp(screen.Width / 3, 120, 420);
      int toolsWidth = Math.Clamp((int)MathF.Round(screen.Width * 0.18f), 100, 236);
      int propertiesWidth = Math.Clamp((int)MathF.Round(screen.Width * 0.23f), 145, 350);
      int availableSideWidth = Math.Max(2, screen.Width - desiredCanvas - gutter * 2);
      if (toolsWidth + propertiesWidth > availableSideWidth)
      {
        toolsWidth = Math.Max(72, availableSideWidth * 43 / 100);
        propertiesWidth = Math.Max(96, availableSideWidth - toolsWidth);
      }
      toolsWidth = Math.Min(toolsWidth, Math.Max(1, screen.Width - gutter * 2 - propertiesWidth));
      propertiesWidth = Math.Min(propertiesWidth, Math.Max(1, screen.Width - gutter * 2 - toolsWidth));
      int bodyHeight = Math.Max(1, Status.Y - Header.Bottom);
      Tools = new Rectangle(screen.X, Header.Bottom, toolsWidth, bodyHeight);
      Properties = new Rectangle(Math.Max(Tools.Right + gutter, screen.Right - propertiesWidth), Header.Bottom, propertiesWidth, bodyHeight);
      Viewport = new Rectangle(Tools.Right + gutter, Header.Bottom, Math.Max(1, Properties.X - Tools.Right - gutter * 2), bodyHeight);
      Canvas = UiLayout.Inset(Viewport, Math.Min(3, Math.Max(1, Viewport.Width / 40)));

      int buttonGap = 4;
      int headerPadding = Math.Min(12, Math.Max(5, screen.Width / 80));
      int buttonWidth = Math.Clamp((screen.Width - headerPadding * 2 - 150 - buttonGap * 5) / 6, 32, 90);
      int buttonHeight = Math.Max(22, Header.Height - 22);
      int actionWidth = buttonWidth * 6 + buttonGap * 5;
      int actionX = Math.Max(Header.X + headerPadding, Header.Right - headerPadding - actionWidth);
      int titleAvailable = Math.Max(1, actionX - Header.X - headerPadding);
      int validationWidth = screen.Width >= 900 ? Math.Min(140, titleAvailable / 4) : 0;
      int titleWidth = Math.Max(1, titleAvailable - validationWidth - (validationWidth > 0 ? buttonGap : 0));
      TitleBounds = new Rectangle(Header.X + headerPadding, Header.Y + (Header.Height - buttonHeight) / 2, titleWidth, buttonHeight);
      ValidationBounds = new Rectangle(TitleBounds.Right + buttonGap, TitleBounds.Y, Math.Max(1, validationWidth), buttonHeight);
      int buttonY = Header.Y + (Header.Height - buttonHeight) / 2;
      SaveButton = new Rectangle(actionX, buttonY, buttonWidth, buttonHeight);
      ExportButton = new Rectangle(SaveButton.Right + buttonGap, buttonY, buttonWidth, buttonHeight);
      ImportButton = new Rectangle(ExportButton.Right + buttonGap, buttonY, buttonWidth, buttonHeight);
      BrowseButton = new Rectangle(ImportButton.Right + buttonGap, buttonY, buttonWidth, buttonHeight);
      TestButton = new Rectangle(BrowseButton.Right + buttonGap, buttonY, buttonWidth, buttonHeight);
      ExitButton = new Rectangle(TestButton.Right + buttonGap, buttonY, buttonWidth, buttonHeight);

      int innerX = Tools.X + PanelPadding;
      int innerWidth = Math.Max(1, Tools.Width - PanelPadding * 2);
      int territoryRows = (Math.Max(1, teamCount) + 2) / 2;
      int rowHeight = Math.Clamp((Tools.Height - 116) / Math.Max(1, 16 + territoryRows), 16, 24);
      int toolGap = Math.Max(2, rowHeight / 7);
      int columnGap = Math.Max(2, PanelPadding / 2);
      int columnWidth = Math.Max(1, (innerWidth - columnGap) / 2);
      int y = Tools.Y + 32;
      (EditorTool tool, string label)[] tools =
      [
        (EditorTool.Select, "SELECT"), (EditorTool.Tile, "PAINT TILE"), (EditorTool.Unit, "PLACE UNIT"),
        (EditorTool.Move, "MOVE UNIT"), (EditorTool.Terrain, "TERRAIN"), (EditorTool.Object, "OBJECT"),
        (EditorTool.Territory, "PAINT AREAS"), (EditorTool.Delete, "DELETE")
      ];
      ToolButtons = tools.Select((tool, index) =>
      {
        int x = innerX + index % 2 * (columnWidth + columnGap);
        int width = index % 2 == 0 ? columnWidth : Math.Max(1, innerX + innerWidth - x);
        return (tool.tool, tool.label, new Rectangle(x, y + index / 2 * (rowHeight + toolGap), width, rowHeight));
      }).ToArray();
      y += rowHeight * 4 + toolGap * 3 + 6;
      UndoButton = new Rectangle(innerX, y, columnWidth, rowHeight);
      RedoButton = new Rectangle(UndoButton.Right + columnGap, y, Math.Max(1, innerX + innerWidth - UndoButton.Right - columnGap), rowHeight);
      y += rowHeight + toolGap;
      NewButton = new Rectangle(innerX, y, innerWidth, rowHeight);
      y += rowHeight + toolGap;
      int arrowWidth = Math.Clamp(innerWidth / 6, 16, 28);
      BoardSmallerButton = new Rectangle(innerX, y, arrowWidth, rowHeight);
      BoardSizeLabel = new Rectangle(BoardSmallerButton.Right + columnGap, y, Math.Max(1, innerWidth - arrowWidth * 2 - columnGap * 2), rowHeight);
      BoardLargerButton = new Rectangle(BoardSizeLabel.Right + columnGap, y, Math.Max(1, innerX + innerWidth - BoardSizeLabel.Right - columnGap), rowHeight);
      y += rowHeight + toolGap;
      BoardShapeButton = new Rectangle(innerX, y, innerWidth, rowHeight);
      y += rowHeight + toolGap;
      BoardBasePrevious = new Rectangle(innerX, y, arrowWidth, rowHeight);
      BoardBaseLabel = new Rectangle(BoardBasePrevious.Right + columnGap, y, Math.Max(1, innerWidth - arrowWidth * 2 - columnGap * 2), rowHeight);
      BoardBaseNext = new Rectangle(BoardBaseLabel.Right + columnGap, y, Math.Max(1, innerX + innerWidth - BoardBaseLabel.Right - columnGap), rowHeight);
      y += rowHeight + toolGap;
      BoardBaseApply = new Rectangle(innerX, y, innerWidth, rowHeight);
      y += rowHeight + toolGap;
      FitBoardButton = new Rectangle(innerX, y, innerWidth, rowHeight);
      y += rowHeight + 5;
      PaletteTop = y;
      y += 15;
      UnitPrevious = new Rectangle(innerX, y, arrowWidth, rowHeight);
      UnitPreview = new Rectangle(UnitPrevious.Right + columnGap, y, Math.Max(1, innerWidth - arrowWidth * 2 - columnGap * 2), rowHeight);
      UnitName = UnitPreview;
      UnitNext = new Rectangle(UnitPreview.Right + columnGap, y, Math.Max(1, innerX + innerWidth - UnitPreview.Right - columnGap), rowHeight);
      y += rowHeight + toolGap;
      TeamPrevious = new Rectangle(innerX, y, arrowWidth, rowHeight);
      TeamPreview = new Rectangle(TeamPrevious.Right + columnGap, y, Math.Max(1, innerWidth - arrowWidth * 2 - columnGap * 2), rowHeight);
      TeamNext = new Rectangle(TeamPreview.Right + columnGap, y, Math.Max(1, innerX + innerWidth - TeamPreview.Right - columnGap), rowHeight);
      y += rowHeight + 5;
      ObjectPaletteTop = y;
      y += 15;
      ObjectPrevious = new Rectangle(innerX, y, arrowWidth, rowHeight);
      ObjectPreview = new Rectangle(ObjectPrevious.Right + columnGap, y, Math.Max(1, innerWidth - arrowWidth * 2 - columnGap * 2), rowHeight);
      ObjectNext = new Rectangle(ObjectPreview.Right + columnGap, y, Math.Max(1, innerX + innerWidth - ObjectPreview.Right - columnGap), rowHeight);
      y += rowHeight + toolGap;
      TerrainButton = new Rectangle(innerX, y, innerWidth, rowHeight);
      y += rowHeight + 5;
      TerritoryTop = y;
      y += 15;
      List<Rectangle> territoryButtons = [];
      for (int index = 0; index < teamCount + 1; index++)
      {
        int x = innerX + index % 2 * (columnWidth + columnGap);
        int width = index % 2 == 0 ? columnWidth : Math.Max(1, innerX + innerWidth - x);
        territoryButtons.Add(new Rectangle(x, y + index / 2 * (rowHeight + toolGap), width, rowHeight));
      }
      TerritoryButtons = territoryButtons;
      y += territoryRows * rowHeight + Math.Max(0, territoryRows - 1) * toolGap + toolGap;
      ResetTerritoryButton = new Rectangle(innerX, y, innerWidth, rowHeight);

      int propertyX = Properties.X + PanelPadding;
      int fieldWidth = Math.Max(1, Properties.Width - PanelPadding * 2);
      int propertyRow = Math.Clamp(bodyHeight / 22, 22, 28);
      int propertyGap = 5;
      int propertyContentY = Properties.Y + 32;
      int tabWidth = Math.Max(1, (fieldWidth - propertyGap * 3) / 4);
      ScenarioTab = new Rectangle(propertyX, propertyContentY, tabWidth, propertyRow);
      TeamsTab = new Rectangle(ScenarioTab.Right + propertyGap, propertyContentY, tabWidth, propertyRow);
      RestrictionsTab = new Rectangle(TeamsTab.Right + propertyGap, propertyContentY, tabWidth, propertyRow);
      UnitsTab = new Rectangle(RestrictionsTab.Right + propertyGap, propertyContentY, Math.Max(1, propertyX + fieldWidth - RestrictionsTab.Right - propertyGap), propertyRow);
      int scenarioY = ScenarioTab.Bottom + 8;
      int halfWidth = Math.Max(1, (fieldWidth - propertyGap) / 2);
      ScenarioDetailsButton = new Rectangle(propertyX, scenarioY, halfWidth, propertyRow);
      ScenarioObjectivesButton = new Rectangle(ScenarioDetailsButton.Right + propertyGap, scenarioY, Math.Max(1, propertyX + fieldWidth - ScenarioDetailsButton.Right - propertyGap), propertyRow);
      int fieldY = ScenarioDetailsButton.Bottom + propertyGap;
      NameField = new Rectangle(propertyX, fieldY, fieldWidth, 34);
      AuthorField = new Rectangle(propertyX, NameField.Bottom + propertyGap, fieldWidth, 34);
      DescriptionField = new Rectangle(propertyX, AuthorField.Bottom + propertyGap, fieldWidth, 42);
      DialogueField = new Rectangle(propertyX, DescriptionField.Bottom + propertyGap, fieldWidth, 34);
      ModeButton = new Rectangle(propertyX, DialogueField.Bottom + propertyGap, fieldWidth, propertyRow);
      FirstTeamButton = new Rectangle(propertyX, ModeButton.Bottom + propertyGap, fieldWidth, propertyRow);
      TurnLimitDown = new Rectangle(propertyX, FirstTeamButton.Bottom + 7, Math.Min(32, Math.Max(16, fieldWidth / 5)), propertyRow);
      TurnLimitLabel = new Rectangle(TurnLimitDown.Right + propertyGap, TurnLimitDown.Y, Math.Max(1, fieldWidth - TurnLimitDown.Width * 2 - propertyGap * 2), propertyRow);
      TurnLimitUp = new Rectangle(TurnLimitLabel.Right + propertyGap, TurnLimitDown.Y, Math.Max(1, propertyX + fieldWidth - TurnLimitLabel.Right - propertyGap), propertyRow);

      ObjectiveTop = ScenarioDetailsButton.Bottom + 8;
      ObjectiveOutcomeButton = new Rectangle(propertyX, ObjectiveTop + 20, fieldWidth, propertyRow);
      ObjectivePrevious = new Rectangle(propertyX, ObjectiveOutcomeButton.Bottom + propertyGap, Math.Min(30, Math.Max(16, fieldWidth / 5)), propertyRow);
      ObjectiveTypeLabel = new Rectangle(ObjectivePrevious.Right + propertyGap, ObjectivePrevious.Y, Math.Max(1, fieldWidth - ObjectivePrevious.Width * 2 - propertyGap * 2), propertyRow);
      ObjectiveNext = new Rectangle(ObjectiveTypeLabel.Right + propertyGap, ObjectivePrevious.Y, Math.Max(1, propertyX + fieldWidth - ObjectiveTypeLabel.Right - propertyGap), propertyRow);
      ObjectiveTeamButton = new Rectangle(propertyX, ObjectivePrevious.Bottom + propertyGap, fieldWidth, propertyRow);
      ObjectiveAddButton = new Rectangle(propertyX, ObjectiveTeamButton.Bottom + propertyGap, halfWidth, propertyRow);
      ObjectiveRemoveButton = new Rectangle(ObjectiveAddButton.Right + propertyGap, ObjectiveAddButton.Y, Math.Max(1, propertyX + fieldWidth - ObjectiveAddButton.Right - propertyGap), propertyRow);
      ObjectiveActiveLabel = new Rectangle(propertyX, ObjectiveAddButton.Bottom + propertyGap, fieldWidth, propertyRow);
      ObjectiveHelp = new Rectangle(propertyX, ObjectiveActiveLabel.Bottom + 4, fieldWidth, Math.Max(30, propertyRow * 2));
      ObjectiveAmountDown = new Rectangle(propertyX, ObjectiveHelp.Bottom + 4, Math.Min(32, Math.Max(16, fieldWidth / 5)), propertyRow);
      ObjectiveAmountLabel = new Rectangle(ObjectiveAmountDown.Right + propertyGap, ObjectiveAmountDown.Y, Math.Max(1, fieldWidth - ObjectiveAmountDown.Width * 2 - propertyGap * 2), propertyRow);
      ObjectiveAmountUp = new Rectangle(ObjectiveAmountLabel.Right + propertyGap, ObjectiveAmountDown.Y, Math.Max(1, propertyX + fieldWidth - ObjectiveAmountLabel.Right - propertyGap), propertyRow);
      ObjectiveTargetButton = new Rectangle(propertyX, ObjectiveAmountDown.Bottom + propertyGap, halfWidth, propertyRow);
      ObjectiveLocationButton = new Rectangle(ObjectiveTargetButton.Right + propertyGap, ObjectiveTargetButton.Y, Math.Max(1, propertyX + fieldWidth - ObjectiveTargetButton.Right - propertyGap), propertyRow);

      SettingsTop = scenarioY;
      // Leave a real visual gap below each tab title before controls begin.
      SettingsTeamPrevious = new Rectangle(propertyX, SettingsTop + 26, Math.Min(30, Math.Max(16, fieldWidth / 5)), propertyRow);
      SettingsTeamLabel = new Rectangle(SettingsTeamPrevious.Right + propertyGap, SettingsTeamPrevious.Y, Math.Max(1, fieldWidth - SettingsTeamPrevious.Width * 2 - propertyGap * 2), propertyRow);
      SettingsTeamNext = new Rectangle(SettingsTeamLabel.Right + propertyGap, SettingsTeamPrevious.Y, Math.Max(1, propertyX + fieldWidth - SettingsTeamLabel.Right - propertyGap), propertyRow);
      ControllerButton = new Rectangle(propertyX, SettingsTeamPrevious.Bottom + propertyGap, fieldWidth, propertyRow);
      MoneyDownButton = new Rectangle(propertyX, ControllerButton.Bottom + propertyGap, Math.Min(32, Math.Max(16, fieldWidth / 5)), propertyRow);
      MoneyLabel = new Rectangle(MoneyDownButton.Right + propertyGap, MoneyDownButton.Y, Math.Max(1, fieldWidth - MoneyDownButton.Width * 2 - propertyGap * 2), propertyRow);
      MoneyUpButton = new Rectangle(MoneyLabel.Right + propertyGap, MoneyDownButton.Y, Math.Max(1, propertyX + fieldWidth - MoneyLabel.Right - propertyGap), propertyRow);
      ActionsDownButton = new Rectangle(propertyX, MoneyDownButton.Bottom + propertyGap, MoneyDownButton.Width, propertyRow);
      ActionsLabel = new Rectangle(ActionsDownButton.Right + propertyGap, ActionsDownButton.Y, MoneyLabel.Width, propertyRow);
      ActionsUpButton = new Rectangle(ActionsLabel.Right + propertyGap, ActionsDownButton.Y, Math.Max(1, propertyX + fieldWidth - ActionsLabel.Right - propertyGap), propertyRow);
      TeamPurchasesButton = new Rectangle(propertyX, ActionsDownButton.Bottom + propertyGap, fieldWidth, propertyRow);
      TeamBuyListMenu = new Rectangle(propertyX, TeamPurchasesButton.Bottom + 2, fieldWidth, propertyRow * 3 + propertyGap * 2 + 4);
      TeamBuyListOptions = Enumerable.Range(0, 3).Select(index => new Rectangle(
        TeamBuyListMenu.X + 2,
        TeamBuyListMenu.Y + 2 + index * (propertyRow + propertyGap),
        Math.Max(1, TeamBuyListMenu.Width - 4),
        propertyRow
      )).ToArray();
      TeamUnitTop = TeamPurchasesButton.Bottom + 8;
      TeamUnitPrevious = new Rectangle(propertyX, TeamUnitTop + 18, SettingsTeamPrevious.Width, propertyRow);
      TeamUnitLabel = new Rectangle(TeamUnitPrevious.Right + propertyGap, TeamUnitPrevious.Y, SettingsTeamLabel.Width, propertyRow);
      TeamUnitNext = new Rectangle(TeamUnitLabel.Right + propertyGap, TeamUnitPrevious.Y, Math.Max(1, propertyX + fieldWidth - TeamUnitLabel.Right - propertyGap), propertyRow);
      TeamUnitToggle = new Rectangle(propertyX, TeamUnitPrevious.Bottom + propertyGap, fieldWidth, propertyRow);
      CpuDifficultyButton = new Rectangle(propertyX, TeamUnitToggle.Bottom + propertyGap, fieldWidth, propertyRow);
      CpuPersonalityButton = new Rectangle(propertyX, CpuDifficultyButton.Bottom + propertyGap, fieldWidth, propertyRow);
      GlobalPurchasesButton = new Rectangle(propertyX, SettingsTeamPrevious.Y, fieldWidth, propertyRow);
      AbilitiesButton = new Rectangle(propertyX, GlobalPurchasesButton.Bottom + propertyGap, fieldWidth, propertyRow);
      RulesHelp = new Rectangle(propertyX, CpuDifficultyButton.Y, fieldWidth, Math.Max(34, propertyRow * 2));
      UnitStatPrevious = SettingsTeamPrevious;
      UnitStatLabel = SettingsTeamLabel;
      UnitStatNext = SettingsTeamNext;
      UnitStatOverrideButton = ControllerButton;
      UnitStatDown = MoneyDownButton;
      UnitStatValue = MoneyLabel;
      UnitStatUp = MoneyUpButton;
      UnitAbilityPrevious = ActionsDownButton;
      UnitAbilityLabel = ActionsLabel;
      UnitAbilityNext = ActionsUpButton;
      CreateCustomUnitButton = TeamPurchasesButton;
      UnitStatsHelp = RulesHelp;

      ShowSelectionProperties = bodyHeight >= 500;
      SelectionTop = ShowSelectionProperties ? Properties.Bottom - 128 : Properties.Bottom;
      SelectedRowOne = new Rectangle(propertyX, SelectionTop + 26, fieldWidth, propertyRow);
      SelectedPrevious = new Rectangle(propertyX, SelectedRowOne.Bottom + propertyGap, halfWidth, propertyRow);
      SelectedNext = new Rectangle(SelectedPrevious.Right + propertyGap, SelectedPrevious.Y, Math.Max(1, propertyX + fieldWidth - SelectedPrevious.Right - propertyGap), propertyRow);
      RotateButton = new Rectangle(propertyX, SelectedPrevious.Bottom + propertyGap, fieldWidth, propertyRow);
    }
  }
}
