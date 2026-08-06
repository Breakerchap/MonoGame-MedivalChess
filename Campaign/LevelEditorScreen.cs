#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess.Campaign;

/// <summary>MonoGame editor surface backed exclusively by <see cref="LevelEditorState"/>.</summary>
internal sealed class LevelEditorScreen
{
  private enum TextField { None, Name, Author, Description, Dialogue }
  private enum PropertiesView { Scenario, Teams, Restrictions }
  private enum ObjectiveTargetMode { None, Unit, Location }

  private readonly UiRenderer _ui;
  private readonly SpriteBatch _spriteBatch;
  private readonly Texture2D _pixel;
  private readonly List<CampaignValidationProblem> _problems = [];
  // These are the exact board definitions used by normal local, CPU, and online matches.
  private static readonly string[] BoardBaseNames = ["Small", "Medium", "Large"];
  private int _unitPaletteIndex;
  private int _objectPaletteIndex;
  private int _boardBaseIndex;
  private CampaignTerrainType _terrainPaletteType;
  private NetworkTeam _placementTeam = NetworkTeam.Red;
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
  private string? _selectedObjectiveId;
  private string? _pendingObjectiveUnitId;
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
    _editingScenarioObjectives = false;
    _objectiveTargetMode = ObjectiveTargetMode.None;
    _selectedObjectiveId = null;
    _pendingObjectiveUnitId = null;
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
    bool wasLeftClick,
    bool isLeftHeld,
    bool wasRightClick,
    bool wasEscapePressed,
    Rectangle screen
  )
  {
    if (wasEscapePressed)
    {
      if (_textField != TextField.None) _textField = TextField.None;
      else RequestExit = true;
      return;
    }

    EditorLayout layout = new(screen);
    Point point = mouse.Position;
    UpdateKeyboardNavigation(keyboard, previousKeyboard);
    UpdateTextInput(keyboard, previousKeyboard);

    // Right click is deliberately immediate and context-free: it is the fast erase gesture.
    if (wasRightClick && layout.Viewport.Contains(point))
    {
      DeleteAt(GetPositionAt(layout, point));
      return;
    }

    if (wasLeftClick)
    {
      if (HandleHeaderClick(layout, point)) return;
      if (HandleToolClick(layout, point)) return;
      if (HandlePropertyClick(layout, point)) return;
    }

    // Painting continues while held for tools that do not have a one-click semantic.
    if (layout.Viewport.Contains(point) && (wasLeftClick || (isLeftHeld && CanPaintContinuously())))
    {
      HandleBoardClick(layout, point);
    }
  }

  internal void Draw(Rectangle screen)
  {
    EditorLayout layout = new(screen);
    _spriteBatch.Draw(_pixel, screen, UiTheme.MenuBackground);
    DrawHeader(layout);
    DrawToolPanel(layout);
    DrawBoard(layout);
    DrawProperties(layout);
    DrawProblems(layout);
  }

  private void DrawHeader(EditorLayout layout)
  {
    _ui.Panel(layout.Header, UiTheme.Panel, UiTheme.PanelBorder);
    _ui.TextFitted(State.Level.Metadata.Name, new Vector2(16, 14), Math.Max(160, layout.Header.Width - 720), UiTheme.TextPrimary, 1.2f);
    CampaignValidationResult validation = State.Validate();
    string state = validation.IsValid ? "READY TO PLAY" : $"{validation.Problems.Count(problem => problem.Severity == CampaignValidationSeverity.Error)} ISSUES";
    _ui.Text(state, new Vector2(layout.Header.Width - 700, 20), validation.IsValid ? UiTheme.Health : UiTheme.Attack, 0.78f);
    _ui.Button(layout.SaveButton, "SAVE", UiButtonTone.Primary);
    _ui.Button(layout.ExportButton, "EXPORT", UiButtonTone.Accent);
    _ui.Button(layout.ImportButton, "IMPORT", UiButtonTone.Neutral);
    _ui.Button(layout.BrowseButton, "LEVELS", UiButtonTone.Neutral);
    _ui.Button(layout.TestButton, "TEST PLAY", UiButtonTone.Primary);
    _ui.Button(layout.ExitButton, "EXIT", UiButtonTone.Danger);
  }

  private void DrawToolPanel(EditorLayout layout)
  {
    _ui.Panel(layout.Tools, UiTheme.Panel, UiTheme.PanelBorder);
    _ui.Text("TOOLS", new Vector2(layout.Tools.X + 12, layout.Tools.Y + 10), UiTheme.GoldBright, 0.85f);
    foreach ((EditorTool tool, string label, Rectangle bounds) in layout.ToolButtons)
    {
      _ui.Button(bounds, label, tool == EditorTool.Delete ? UiButtonTone.Danger : UiButtonTone.Neutral, State.ActiveTool == tool, 0.75f);
    }
    _ui.Button(layout.UndoButton, State.History.CanUndo ? "UNDO" : "UNDO", UiButtonTone.Neutral, false, 0.75f);
    _ui.Button(layout.RedoButton, State.History.CanRedo ? "REDO" : "REDO", UiButtonTone.Neutral, false, 0.75f);
    _ui.Button(layout.NewButton, "NEW LEVEL", UiButtonTone.Accent, false, 0.75f);
    _ui.Button(layout.BoardSmallerButton, "-", UiButtonTone.Neutral);
    _ui.Button(layout.BoardLargerButton, "+", UiButtonTone.Neutral);
    _ui.CenterText($"BOARD: {State.Level.Board.Width} x {State.Level.Board.Height}", layout.BoardSizeLabel, UiTheme.TextMuted, 0.6f);
    _ui.Button(layout.BoardShapeButton, $"SHAPE: {State.Level.Board.Shape}".ToUpperInvariant(), UiButtonTone.Neutral, false, 0.58f);
    _ui.Button(layout.BoardBasePrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.BoardBaseNext, ">", UiButtonTone.Neutral);
    _ui.CenterText($"BASE: {BoardBaseNames[_boardBaseIndex]}".ToUpperInvariant(), layout.BoardBaseLabel, UiTheme.TextMuted, 0.58f);
    _ui.Button(layout.BoardBaseApply, "USE DEFAULT BOARD", UiButtonTone.Accent, false, 0.55f);

    _ui.Divider(layout.Tools, layout.PaletteTop);
    UnitRule selectedUnit = UnitRules.All[_unitPaletteIndex % UnitRules.All.Count];
    _ui.Text("UNIT PALETTE", new Vector2(layout.Tools.X + 12, layout.PaletteTop + 10), UiTheme.GoldBright, 0.75f);
    _ui.Button(layout.UnitPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.UnitNext, ">", UiButtonTone.Neutral);
    _ui.PiecePreview(layout.UnitPreview, UiTheme.GetTeamColour(_placementTeam.ToTeamName()), selectedUnit.Type);
    _ui.CenterTextFitted(selectedUnit.Type.ToUpperInvariant(), layout.UnitName, UiTheme.TextPrimary, 0.7f, 0.5f, 1);
    _ui.Text($"{selectedUnit.Width}x{selectedUnit.Height}  {selectedUnit.Health} HP", new Vector2(layout.Tools.X + 12, layout.UnitName.Bottom + 3), UiTheme.TextMuted, 0.59f);
    _ui.Button(layout.TeamPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.TeamNext, ">", UiButtonTone.Neutral);
    _ui.CenterText($"FOR {_placementTeam}".ToUpperInvariant(), layout.TeamPreview, UiTheme.GetTeamColour(_placementTeam.ToTeamName()), 0.68f);

    CampaignBoardObjectType selectedObject = (CampaignBoardObjectType)(_objectPaletteIndex % Enum.GetValues<CampaignBoardObjectType>().Length);
    _ui.Text("OBJECT PALETTE", new Vector2(layout.Tools.X + 12, layout.ObjectPaletteTop + 4), UiTheme.GoldBright, 0.72f);
    _ui.Button(layout.ObjectPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.ObjectNext, ">", UiButtonTone.Neutral);
    _ui.CenterText(selectedObject.ToString().ToUpperInvariant(), layout.ObjectPreview, UiTheme.TextPrimary, 0.66f);
    _ui.Button(layout.TerrainButton, $"TERRAIN: {_terrainPaletteType}".ToUpperInvariant(), UiButtonTone.Accent, State.ActiveTool == EditorTool.Terrain, 0.62f);
  }

  private void DrawBoard(EditorLayout layout)
  {
    _ui.Panel(layout.Viewport, UiTheme.BoardBackground, UiTheme.PanelBorder);
    foreach (CampaignCoordinate tile in State.Level.Board.Tiles)
    {
      Rectangle bounds = GetTileBounds(layout, tile);
      if (!bounds.Intersects(layout.Viewport)) continue;
      Color colour = (tile.X + tile.Y) % 2 == 0 ? UiTheme.DarkBoardCell : UiTheme.LightBoardCell;
      CampaignTerrainTileDefinition? terrain = State.Level.Terrain.FirstOrDefault(entry => entry.Position == tile);
      if (terrain?.Type == CampaignTerrainType.Forest) colour = Color.Lerp(colour, UiTheme.Forest, 0.72f);
      if (terrain?.Type == CampaignTerrainType.Lake) colour = UiTheme.Lake;
      _spriteBatch.Draw(_pixel, bounds, colour);
      DrawOutline(bounds, UiTheme.PanelBorderSubtle, 1);
    }

    foreach (CampaignBoardObjectDefinition boardObject in State.Level.Objects)
    {
      Rectangle tile = GetTileBounds(layout, boardObject.Position);
      if (!tile.Intersects(layout.Viewport)) continue;
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
      _spriteBatch.Draw(_pixel, marker, colour);
    }

    foreach (CampaignUnitDefinition unit in State.Level.Units)
    {
      if (!UnitRules.TryGet(unit.UnitType, out UnitRule rule)) continue;
      Rectangle origin = GetTileBounds(layout, unit.Position);
      Rectangle bounds = new(origin.X + 4, origin.Y + 4, Math.Max(4, origin.Width * rule.Width - 8), Math.Max(4, origin.Height * rule.Height - 8));
      Color teamColour = UiTheme.GetTeamColour(unit.Team.ToTeamName());
      _spriteBatch.Draw(_pixel, bounds, Color.Lerp(teamColour, UiTheme.PanelRaised, 0.22f));
      DrawOutline(bounds, teamColour, 2);
      if (State.Selection.Kind == EditorSelectionKind.Unit && State.Selection.Id == unit.Id) DrawOutline(bounds, UiTheme.SelectionOutline, 3);
      _ui.CenterTextFitted(unit.UnitType.ToUpperInvariant(), bounds, UiTheme.TextPrimary, 0.57f, 0.42f, 3);
      int line = Math.Max(2, bounds.Width / 8);
      switch (unit.Rotation)
      {
        case CampaignUnitRotation.Degrees90: _spriteBatch.Draw(_pixel, new Rectangle(bounds.Right - line, bounds.Y, line, bounds.Height), UiTheme.GoldBright); break;
        case CampaignUnitRotation.Degrees180: _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Bottom - line, bounds.Width, line), UiTheme.GoldBright); break;
        case CampaignUnitRotation.Degrees270: _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, line, bounds.Height), UiTheme.GoldBright); break;
        default: _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, line), UiTheme.GoldBright); break;
      }
    }

    _ui.Text("DRAG TO PAINT    RIGHT CLICK: DELETE    WASD / ARROWS: PAN    Q/E: ZOOM", new Vector2(layout.Viewport.X + 10, layout.Viewport.Bottom - 24), UiTheme.TextMuted, 0.58f);
  }

  private void DrawProperties(EditorLayout layout)
  {
    _ui.Panel(layout.Properties, UiTheme.Panel, UiTheme.PanelBorder);
    _ui.Text("CAMPAIGN SETTINGS", new Vector2(layout.Properties.X + 12, layout.Properties.Y + 10), UiTheme.GoldBright, 0.82f);
    _ui.Button(layout.ScenarioTab, "SCENARIO", UiButtonTone.Neutral, _propertiesView == PropertiesView.Scenario, 0.6f);
    _ui.Button(layout.TeamsTab, "TEAMS", UiButtonTone.Neutral, _propertiesView == PropertiesView.Teams, 0.6f);
    _ui.Button(layout.RestrictionsTab, "RULES", UiButtonTone.Neutral, _propertiesView == PropertiesView.Restrictions, 0.6f);
    switch (_propertiesView)
    {
      case PropertiesView.Teams:
        DrawTeamSettings(layout);
        break;
      case PropertiesView.Restrictions:
        DrawRestrictionSettings(layout);
        break;
      default:
        DrawScenarioSettings(layout);
        break;
    }
    // Scenario editing uses the full panel for objectives. Selected-unit details remain available
    // in the compact Teams and Rules views instead of being hidden below the objective controls.
    if (_propertiesView != PropertiesView.Scenario) DrawSelectionProperties(layout);
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
    _ui.CenterText($"TURN LIMIT: {turns}", layout.TurnLimitLabel, UiTheme.TextMuted, 0.65f);
  }

  private void DrawObjectiveSettings(EditorLayout layout)
  {
    IReadOnlyList<CampaignObjectiveDefinition> objectives = _editingDefeatConditions
      ? State.Level.Scenario.DefeatConditions
      : State.Level.Scenario.VictoryConditions;
    CampaignObjectiveDefinition? selected = objectives.FirstOrDefault(objective => objective.Id == _selectedObjectiveId) ?? objectives.LastOrDefault();
    _ui.Divider(layout.Properties, layout.ObjectiveTop);
    _ui.Text(_editingDefeatConditions ? "LOSS CONDITIONS" : "WIN CONDITIONS", new Vector2(layout.Properties.X + 12, layout.ObjectiveTop + 8), UiTheme.GoldBright, 0.72f);
    _ui.Button(layout.ObjectiveOutcomeButton, _editingDefeatConditions ? "EDIT LOSS" : "EDIT WIN", UiButtonTone.Danger, false, 0.58f);
    _ui.Button(layout.ObjectivePrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.ObjectiveNext, ">", UiButtonTone.Neutral);
    _ui.CenterTextFitted(_objectivePaletteType.ToString().ToUpperInvariant(), layout.ObjectiveTypeLabel, UiTheme.TextPrimary, 0.55f, 0.42f, 2);
    _ui.Button(layout.ObjectiveTeamButton, $"FOR: {_objectiveTeam}".ToUpperInvariant(), UiButtonTone.Neutral, false, 0.56f);
    _ui.Button(layout.ObjectiveAddButton, "ADD CONDITION", UiButtonTone.Primary, false, 0.6f);
    _ui.Button(layout.ObjectiveRemoveButton, "REMOVE", UiButtonTone.Danger, false, 0.6f);
    if (selected is not null)
    {
      _ui.Button(layout.ObjectiveActiveLabel, $"ACTIVE: {selected.Type}  ({selected.RequiredAmount})  ›", UiButtonTone.Neutral, false, 0.52f);
      _ui.Button(layout.ObjectiveAmountDown, "-", UiButtonTone.Neutral);
      _ui.Button(layout.ObjectiveAmountUp, "+", UiButtonTone.Neutral);
      _ui.Button(layout.ObjectiveTargetButton, "PICK UNIT", UiButtonTone.Accent, _objectiveTargetMode == ObjectiveTargetMode.Unit, 0.57f);
      _ui.Button(layout.ObjectiveLocationButton, "PICK SQUARE", UiButtonTone.Accent, _objectiveTargetMode == ObjectiveTargetMode.Location, 0.57f);
    }
    else
    {
      _ui.Text("Choose a type and add a condition.", new Vector2(layout.Properties.X + 12, layout.ObjectiveTop + 154), UiTheme.TextMuted, 0.6f);
    }
  }

  private void DrawTeamSettings(EditorLayout layout)
  {
    CampaignTeamDefinition? team = State.Level.Teams.FirstOrDefault(candidate => candidate.Team == _settingsTeam);
    if (team is null) return;
    _ui.Text("TEAM CONFIGURATION", new Vector2(layout.Properties.X + 12, layout.SettingsTop + 8), UiTheme.GoldBright, 0.76f);
    _ui.Button(layout.SettingsTeamPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.SettingsTeamNext, ">", UiButtonTone.Neutral);
    _ui.CenterText(_settingsTeam.ToString().ToUpperInvariant(), layout.SettingsTeamLabel, UiTheme.GetTeamColour(_settingsTeam.ToTeamName()), 0.72f);
    _ui.Button(layout.ControllerButton, team.Controller == CampaignTeamController.Cpu ? "CPU CONTROLLED" : "HUMAN CONTROLLED", UiButtonTone.Neutral, false, 0.66f);
    _ui.Button(layout.MoneyDownButton, "-", UiButtonTone.Neutral);
    _ui.Button(layout.MoneyUpButton, "+", UiButtonTone.Neutral);
    _ui.CenterText($"STARTING GOLD: {team.StartingMoney}", layout.MoneyLabel, UiTheme.TextMuted, 0.65f);
    _ui.Button(layout.ActionsDownButton, "-", UiButtonTone.Neutral);
    _ui.Button(layout.ActionsUpButton, "+", UiButtonTone.Neutral);
    _ui.CenterText($"ACTIONS: {team.ActionsPerTurn}", layout.ActionsLabel, UiTheme.TextMuted, 0.65f);
    _ui.Button(layout.TeamPurchasesButton, team.PurchasesEnabled ? "TEAM BUYING: ON" : "TEAM BUYING: OFF", team.PurchasesEnabled ? UiButtonTone.Primary : UiButtonTone.Danger, false, 0.62f);
    UnitRule palette = UnitRules.Purchasable[_restrictionUnitIndex % UnitRules.Purchasable.Count];
    bool available = team.AvailableUnitTypes.Contains(palette.Type);
    _ui.Text("AVAILABLE UNITS", new Vector2(layout.Properties.X + 12, layout.TeamUnitTop), UiTheme.GoldBright, 0.68f);
    _ui.Button(layout.TeamUnitPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.TeamUnitNext, ">", UiButtonTone.Neutral);
    _ui.CenterTextFitted(palette.Type.ToUpperInvariant(), layout.TeamUnitLabel, UiTheme.TextPrimary, 0.62f, 0.45f, 2);
    _ui.Button(layout.TeamUnitToggle, available ? "UNIT AVAILABLE" : "UNIT RESTRICTED", available ? UiButtonTone.Primary : UiButtonTone.Danger, false, 0.61f);
    _ui.Button(layout.CpuDifficultyButton, $"CPU DIFFICULTY: {team.CpuProfile.Difficulty}".ToUpperInvariant(), UiButtonTone.Accent, false, 0.56f);
    _ui.Button(layout.CpuPersonalityButton, $"CPU STYLE: {team.CpuProfile.Personality}".ToUpperInvariant(), UiButtonTone.Accent, false, 0.56f);
  }

  private void DrawRestrictionSettings(EditorLayout layout)
  {
    CampaignRestrictionsDefinition rules = State.Level.Restrictions;
    _ui.Text("GLOBAL RULE RESTRICTIONS", new Vector2(layout.Properties.X + 12, layout.SettingsTop + 8), UiTheme.GoldBright, 0.75f);
    _ui.Button(layout.GlobalPurchasesButton, rules.PurchasesEnabled ? "GLOBAL BUYING: ON" : "GLOBAL BUYING: OFF", rules.PurchasesEnabled ? UiButtonTone.Primary : UiButtonTone.Danger, false, 0.62f);
    _ui.Button(layout.AbilitiesButton, rules.AbilitiesEnabled ? "ABILITIES: ON" : "ABILITIES: OFF", rules.AbilitiesEnabled ? UiButtonTone.Primary : UiButtonTone.Danger, false, 0.62f);
    UnitRule palette = UnitRules.Purchasable[_restrictionUnitIndex % UnitRules.Purchasable.Count];
    bool disabled = rules.DisabledUnitTypes.Contains(palette.Type);
    _ui.Text("GLOBAL UNIT RESTRICTION", new Vector2(layout.Properties.X + 12, layout.SettingsTop + 108), UiTheme.GoldBright, 0.67f);
    _ui.Button(layout.TeamUnitPrevious, "<", UiButtonTone.Neutral);
    _ui.Button(layout.TeamUnitNext, ">", UiButtonTone.Neutral);
    _ui.CenterTextFitted(palette.Type.ToUpperInvariant(), layout.TeamUnitLabel, UiTheme.TextPrimary, 0.62f, 0.45f, 2);
    _ui.Button(layout.TeamUnitToggle, disabled ? "UNIT DISABLED" : "UNIT ALLOWED", disabled ? UiButtonTone.Danger : UiButtonTone.Primary, false, 0.61f);
    _ui.TextWrapped("Team-level buying and available-unit lists override this global rule. Disable abilities or a unit here to create hard campaign restrictions.", layout.RulesHelp, UiTheme.TextMuted, 0.62f);
  }

  private void DrawSelectionProperties(EditorLayout layout)
  {
    _ui.Divider(layout.Properties, layout.SelectionTop);
    if (State.Selection.Kind == EditorSelectionKind.Unit && State.Selection.Id is string unitId)
    {
      CampaignUnitDefinition? unit = State.Level.Units.FirstOrDefault(candidate => candidate.Id == unitId);
      if (unit is not null)
      {
        _ui.Text("SELECTED UNIT", new Vector2(layout.Properties.X + 12, layout.SelectionTop + 10), UiTheme.GoldBright, 0.75f);
        _ui.Button(layout.SelectedRowOne, $"{unit.UnitType}  •  TEAM: {unit.Team}  (CHANGE)", UiButtonTone.Neutral, false, 0.58f);
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
        _ui.Text("SELECTED OBJECT", new Vector2(layout.Properties.X + 12, layout.SelectionTop + 10), UiTheme.GoldBright, 0.75f);
        _ui.LabelValueRow(layout.SelectedRowOne, boardObject.Type.ToString(), boardObject.Owner?.ToString() ?? "NEUTRAL", UiTheme.TextMuted);
        _ui.Button(layout.RotateButton, "DELETE OBJECT", UiButtonTone.Danger, false, 0.65f);
      }
      return;
    }
    _ui.Text("Select a unit or object to edit its properties.", new Vector2(layout.Properties.X + 12, layout.SelectionTop + 14), UiTheme.TextMuted, 0.68f);
  }

  private void DrawProblems(EditorLayout layout)
  {
    _ui.Panel(layout.Status, UiTheme.Panel, UiTheme.PanelBorderSubtle);
    _ui.TextFitted(_status, new Vector2(layout.Status.X + 12, layout.Status.Y + 8), layout.Status.Width - 24, UiTheme.TextPrimary, 0.68f, 0.55f);
    int x = layout.Status.X + 12;
    foreach (CampaignValidationProblem problem in _problems.Take(3))
    {
      string label = problem.Message;
      _ui.TextFitted(label, new Vector2(x, layout.Status.Y + 29), 270, problem.Severity == CampaignValidationSeverity.Error ? UiTheme.Attack : UiTheme.Move, 0.55f, 0.45f);
      x += 285;
    }
  }

  private void DrawTextField(Rectangle bounds, string label, string value, TextField field)
  {
    _ui.Panel(bounds, _textField == field ? UiTheme.PanelRaised : UiTheme.MenuBackground, _textField == field ? UiTheme.GoldBright : UiTheme.PanelBorderSubtle);
    _ui.Text(label, new Vector2(bounds.X + 6, bounds.Y + 4), UiTheme.TextDim, 0.53f);
    _ui.TextFitted(string.IsNullOrEmpty(value) ? "Click to edit" : value, new Vector2(bounds.X + 6, bounds.Y + 18), bounds.Width - 12, string.IsNullOrEmpty(value) ? UiTheme.TextDim : UiTheme.TextPrimary, 0.65f, 0.46f);
  }

  private bool HandleHeaderClick(EditorLayout layout, Point point)
  {
    if (layout.SaveButton.Contains(point))
    {
      string path = Path.Combine(CampaignLevelSerializer.LocalLevelDirectory, CreateSafeFileName(State.Level.Metadata.Name) + CampaignLevelFormat.Extension);
      ShowSaveResult(State.Save(path), $"Saved locally: {Path.GetFileName(path)}");
      return true;
    }
    if (layout.ExportButton.Contains(point))
    {
      string? path = LevelFilePicker.PickExportPath(CreateSafeFileName(State.Level.Metadata.Name) + CampaignLevelFormat.Extension);
      if (path is null) _status = "Export cancelled or no native file picker is available.";
      else ShowSaveResult(State.Save(path), $"Exported: {Path.GetFileName(path)}");
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
      _status = $"Using the shipped {BoardBaseNames[_boardBaseIndex]} board as this level's base.";
      return true;
    }
    if (layout.UnitPrevious.Contains(point)) { _unitPaletteIndex = (_unitPaletteIndex - 1 + UnitRules.All.Count) % UnitRules.All.Count; return true; }
    if (layout.UnitNext.Contains(point)) { _unitPaletteIndex = (_unitPaletteIndex + 1) % UnitRules.All.Count; return true; }
    if (layout.TeamPrevious.Contains(point)) { CyclePlacementTeam(-1); return true; }
    if (layout.TeamNext.Contains(point)) { CyclePlacementTeam(1); return true; }
    if (layout.ObjectPrevious.Contains(point)) { _objectPaletteIndex = (_objectPaletteIndex - 1 + Enum.GetValues<CampaignBoardObjectType>().Length) % Enum.GetValues<CampaignBoardObjectType>().Length; return true; }
    if (layout.ObjectNext.Contains(point)) { _objectPaletteIndex = (_objectPaletteIndex + 1) % Enum.GetValues<CampaignBoardObjectType>().Length; return true; }
    if (layout.TerrainButton.Contains(point)) { _terrainPaletteType = _terrainPaletteType == CampaignTerrainType.Forest ? CampaignTerrainType.Lake : CampaignTerrainType.Forest; return true; }
    return false;
  }

  private bool HandlePropertyClick(EditorLayout layout, Point point)
  {
    if (layout.ScenarioTab.Contains(point)) { _propertiesView = PropertiesView.Scenario; return true; }
    if (layout.TeamsTab.Contains(point)) { _propertiesView = PropertiesView.Teams; return true; }
    if (layout.RestrictionsTab.Contains(point)) { _propertiesView = PropertiesView.Restrictions; return true; }
    if (_propertiesView == PropertiesView.Teams && HandleTeamSettingsClick(layout, point)) return true;
    if (_propertiesView == PropertiesView.Restrictions && HandleRestrictionsClick(layout, point)) return true;
    if (_propertiesView == PropertiesView.Scenario)
    {
      if (layout.ScenarioDetailsButton.Contains(point)) { _editingScenarioObjectives = false; return true; }
      if (layout.ScenarioObjectivesButton.Contains(point)) { _editingScenarioObjectives = true; return true; }
      if (_editingScenarioObjectives) return HandleObjectiveClick(layout, point);
      if (layout.NameField.Contains(point)) { _textField = TextField.Name; return true; }
      if (layout.AuthorField.Contains(point)) { _textField = TextField.Author; return true; }
      if (layout.DescriptionField.Contains(point)) { _textField = TextField.Description; return true; }
      if (layout.DialogueField.Contains(point)) { _textField = TextField.Dialogue; return true; }
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
    if (layout.ObjectiveTeamButton.Contains(point)) { CycleObjectiveTeam(); return true; }
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
    if (layout.ObjectiveAmountDown.Contains(point)) { State.UpdateObjective(_editingDefeatConditions, selected.Id, objective => objective.RequiredAmount = Math.Max(1, objective.RequiredAmount - 1)); return true; }
    if (layout.ObjectiveAmountUp.Contains(point)) { State.UpdateObjective(_editingDefeatConditions, selected.Id, objective => objective.RequiredAmount = Math.Min(10_000, objective.RequiredAmount + 1)); return true; }
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
    if (layout.ControllerButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.Controller = value.Controller == CampaignTeamController.Cpu ? CampaignTeamController.Human : CampaignTeamController.Cpu); return true; }
    if (layout.MoneyDownButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.StartingMoney = Math.Max(0, value.StartingMoney - 50)); return true; }
    if (layout.MoneyUpButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.StartingMoney = Math.Min(1_000_000, value.StartingMoney + 50)); return true; }
    if (layout.ActionsDownButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.ActionsPerTurn = Math.Max(1, value.ActionsPerTurn - 1)); return true; }
    if (layout.ActionsUpButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.ActionsPerTurn = Math.Min(100, value.ActionsPerTurn + 1)); return true; }
    if (layout.TeamPurchasesButton.Contains(point)) { State.UpdateTeam(_settingsTeam, value => value.PurchasesEnabled = !value.PurchasesEnabled); return true; }
    if (layout.TeamUnitPrevious.Contains(point)) { CycleRestrictionUnit(-1); return true; }
    if (layout.TeamUnitNext.Contains(point)) { CycleRestrictionUnit(1); return true; }
    if (layout.TeamUnitToggle.Contains(point))
    {
      string type = UnitRules.Purchasable[_restrictionUnitIndex % UnitRules.Purchasable.Count].Type;
      State.UpdateTeam(_settingsTeam, value => ToggleUnit(value.AvailableUnitTypes, type));
      return true;
    }
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
      string type = UnitRules.Purchasable[_restrictionUnitIndex % UnitRules.Purchasable.Count].Type;
      State.UpdateScenario(_ => ToggleUnit(State.Level.Restrictions.DisabledUnitTypes, type));
      return true;
    }
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
      UnitRule unit = UnitRules.All[_unitPaletteIndex % UnitRules.All.Count];
      CampaignUnitDefinition candidate = new() { UnitType = unit.Type, Team = _placementTeam, Position = position };
      if (State.TryPlaceUnit(candidate, out string reason))
      {
        _status = $"Placed {unit.Type} for {_placementTeam}.";
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
    State.ActiveTool is EditorTool.Tile or EditorTool.Terrain or EditorTool.Object or EditorTool.Delete;

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
      _status = _pendingObjectiveUnitId is null ? "Objective unit selected." : "Now click PICK SQUARE, then choose this unit's destination.";
      return;
    }
    if (_objectiveTargetMode == ObjectiveTargetMode.Location && GetSelectedObjective() is CampaignObjectiveDefinition locationObjective)
    {
      State.UpdateObjective(_editingDefeatConditions, locationObjective.Id, value =>
      {
        if (value.Type == CampaignObjectiveType.GetUnitsToLocations && _pendingObjectiveUnitId is not null)
        {
          value.UnitLocationTargets.RemoveAll(target => target.UnitId == _pendingObjectiveUnitId);
          value.UnitLocationTargets.Add(new CampaignUnitLocationTargetDefinition { UnitId = _pendingObjectiveUnitId, Location = position });
          _pendingObjectiveUnitId = null;
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
      if (!UnitRules.TryGet(unit.UnitType, out UnitRule rule)) return;
      int health = unit.Health ?? rule.Health;
      unit.Health = Math.Clamp(health + delta, 1, rule.Health);
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
    _status = "Board resized. Validate to find any units that no longer fit.";
  }

  private CampaignObjectiveDefinition? GetSelectedObjective()
  {
    IReadOnlyList<CampaignObjectiveDefinition> objectives = _editingDefeatConditions
      ? State.Level.Scenario.DefeatConditions
      : State.Level.Scenario.VictoryConditions;
    return objectives.FirstOrDefault(objective => objective.Id == _selectedObjectiveId) ?? objectives.LastOrDefault();
  }

  private void CycleObjectiveTeam()
  {
    NetworkTeam[] teams = State.Level.Teams.Select(team => team.Team).ToArray();
    if (teams.Length == 0) return;
    int current = Array.IndexOf(teams, _objectiveTeam);
    _objectiveTeam = teams[(current + 1 + teams.Length) % teams.Length];
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
    if (Pressed(keyboard, previous, Keys.Home)) { _camera = Vector2.Zero; _zoom = 1f; }
  }

  private void UpdateTextInput(KeyboardState keyboard, KeyboardState previous)
  {
    if (_textField == TextField.None) return;
    foreach (Keys key in keyboard.GetPressedKeys())
    {
      if (!Pressed(keyboard, previous, key)) continue;
      if (key == Keys.Enter) { _textField = TextField.None; return; }
      string value = GetTextFieldValue();
      if (key == Keys.Back) SetTextFieldValue(value.Length == 0 ? value : value[..^1]);
      else if (TryGetCharacter(key, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift), out char character) && value.Length < 500)
        SetTextFieldValue(value + character);
    }
  }

  private string GetTextFieldValue() => _textField switch
  {
    TextField.Name => State.Level.Metadata.Name,
    TextField.Author => State.Level.Metadata.Author,
    TextField.Description => State.Level.Metadata.Description,
    TextField.Dialogue => State.Level.Metadata.CampaignDialogue ?? string.Empty,
    _ => string.Empty
  };

  private void SetTextFieldValue(string value)
  {
    State.EditMetadata(metadata =>
    {
      switch (_textField)
      {
        case TextField.Name: metadata.Name = value; break;
        case TextField.Author: metadata.Author = value; break;
        case TextField.Description: metadata.Description = value; break;
        case TextField.Dialogue: metadata.CampaignDialogue = value; break;
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
      (int)MathF.Round(layout.Viewport.Center.X + (position.X - centreX) * size),
      (int)MathF.Round(layout.Viewport.Center.Y + (position.Y - centreY) * size),
      Math.Max(4, (int)MathF.Ceiling(size)),
      Math.Max(4, (int)MathF.Ceiling(size)));
  }

  private CampaignCoordinate GetPositionAt(EditorLayout layout, Point point)
  {
    float size = 44f * _zoom;
    float centreX = State.Level.Board.OriginX + State.Level.Board.Width / 2f + _camera.X;
    float centreY = State.Level.Board.OriginY + State.Level.Board.Height / 2f + _camera.Y;
    return new CampaignCoordinate(
      (int)MathF.Floor((point.X - layout.Viewport.Center.X) / size + centreX),
      (int)MathF.Floor((point.Y - layout.Viewport.Center.Y) / size + centreY));
  }

  private static bool UnitOccupies(CampaignUnitDefinition unit, CampaignCoordinate position)
  {
    return UnitRules.TryGet(unit.UnitType, out UnitRule rule) && position.X >= unit.Position.X && position.X < unit.Position.X + rule.Width &&
      position.Y >= unit.Position.Y && position.Y < unit.Position.Y + rule.Height;
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
      SynchronisePlacementTeam();
    }
    _status = result.IsSuccess ? success : "Level was not imported. Review validation issues.";
  }

  private static string CreateSafeFileName(string name)
  {
    string safe = string.Concat((name ?? "Untitled").Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    return string.IsNullOrWhiteSpace(safe) ? "Untitled" : safe.Trim();
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
    internal Rectangle Properties { get; }
    internal Rectangle Status { get; }
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
    internal Rectangle NameField { get; }
    internal Rectangle AuthorField { get; }
    internal Rectangle DescriptionField { get; }
    internal Rectangle DialogueField { get; }
    internal Rectangle ModeButton { get; }
    internal Rectangle FirstTeamButton { get; }
    internal Rectangle TurnLimitDown { get; }
    internal Rectangle TurnLimitLabel { get; }
    internal Rectangle TurnLimitUp { get; }
    internal int SelectionTop { get; }
    internal Rectangle SelectedRowOne { get; }
    internal Rectangle SelectedPrevious { get; }
    internal Rectangle SelectedNext { get; }
    internal Rectangle RotateButton { get; }
    internal Rectangle ScenarioTab { get; }
    internal Rectangle TeamsTab { get; }
    internal Rectangle RestrictionsTab { get; }
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
    internal Rectangle ObjectiveAmountDown { get; }
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

    internal EditorLayout(Rectangle screen)
    {
      int toolsWidth = 212;
      int propertiesWidth = 320;
      Header = new Rectangle(0, 0, screen.Width, 64);
      Status = new Rectangle(0, Math.Max(64, screen.Height - 66), screen.Width, 66);
      Tools = new Rectangle(0, 64, toolsWidth, Math.Max(1, Status.Y - 64));
      Properties = new Rectangle(Math.Max(toolsWidth + 1, screen.Width - propertiesWidth), 64, propertiesWidth, Math.Max(1, Status.Y - 64));
      Viewport = new Rectangle(toolsWidth + 1, 64, Math.Max(1, Properties.X - toolsWidth - 2), Math.Max(1, Status.Y - 64));
      int buttonX = screen.Width - 520;
      SaveButton = new Rectangle(buttonX, 12, 70, 38);
      ExportButton = new Rectangle(buttonX + 76, 12, 80, 38);
      ImportButton = new Rectangle(buttonX + 162, 12, 80, 38);
      BrowseButton = new Rectangle(buttonX + 248, 12, 76, 38);
      TestButton = new Rectangle(buttonX + 330, 12, 104, 38);
      ExitButton = new Rectangle(buttonX + 440, 12, 70, 38);
      (EditorTool tool, string label)[] tools =
      [
        (EditorTool.Select, "SELECT"), (EditorTool.Tile, "PAINT TILE"), (EditorTool.Unit, "PLACE UNIT"),
        (EditorTool.Move, "MOVE UNIT"), (EditorTool.Terrain, "TERRAIN"), (EditorTool.Object, "OBJECT"),
        (EditorTool.Delete, "DELETE")
      ];
      ToolButtons = tools.Select((tool, index) => (
        tool.tool,
        tool.label,
        new Rectangle(
          index == tools.Length - 1 && index % 2 == 0 ? 10 : 10 + index % 2 * 100,
          94 + index / 2 * 34,
          index == tools.Length - 1 && index % 2 == 0 ? 192 : 92,
          28
        )
      )).ToArray();
      UndoButton = new Rectangle(10, 232, 92, 30);
      RedoButton = new Rectangle(110, 232, 92, 30);
      NewButton = new Rectangle(10, 270, 192, 30);
      BoardSmallerButton = new Rectangle(10, 310, 30, 28);
      BoardSizeLabel = new Rectangle(44, 310, 124, 28);
      BoardLargerButton = new Rectangle(172, 310, 30, 28);
      BoardShapeButton = new Rectangle(10, 344, 192, 28);
      BoardBasePrevious = new Rectangle(10, 378, 30, 28);
      BoardBaseLabel = new Rectangle(44, 378, 124, 28);
      BoardBaseNext = new Rectangle(172, 378, 30, 28);
      BoardBaseApply = new Rectangle(10, 412, 192, 28);
      PaletteTop = 448;
      UnitPrevious = new Rectangle(10, 470, 34, 34);
      UnitNext = new Rectangle(168, 470, 34, 34);
      UnitPreview = new Rectangle(50, 470, 112, 34);
      UnitName = new Rectangle(10, 507, 192, 18);
      TeamPrevious = new Rectangle(10, 538, 34, 28);
      TeamNext = new Rectangle(168, 538, 34, 28);
      TeamPreview = new Rectangle(50, 538, 112, 28);
      ObjectPaletteTop = 572;
      ObjectPrevious = new Rectangle(10, 590, 34, 28);
      ObjectNext = new Rectangle(168, 590, 34, 28);
      ObjectPreview = new Rectangle(50, 590, 112, 28);
      TerrainButton = new Rectangle(10, 624, 192, 28);
      int propertyX = Properties.X + 12;
      int fieldWidth = Properties.Width - 24;
      ScenarioTab = new Rectangle(propertyX, 88, (fieldWidth - 8) / 3, 28);
      TeamsTab = new Rectangle(propertyX + (fieldWidth + 4) / 3, 88, (fieldWidth - 8) / 3, 28);
      RestrictionsTab = new Rectangle(propertyX + (fieldWidth + 4) * 2 / 3, 88, (fieldWidth - 8) / 3, 28);
      ScenarioDetailsButton = new Rectangle(propertyX, 124, (fieldWidth - 4) / 2, 28);
      ScenarioObjectivesButton = new Rectangle(propertyX + (fieldWidth + 4) / 2, 124, (fieldWidth - 4) / 2, 28);
      NameField = new Rectangle(propertyX, 160, fieldWidth, 38);
      AuthorField = new Rectangle(propertyX, 204, fieldWidth, 38);
      DescriptionField = new Rectangle(propertyX, 248, fieldWidth, 48);
      DialogueField = new Rectangle(propertyX, 302, fieldWidth, 40);
      ModeButton = new Rectangle(propertyX, 348, fieldWidth, 30);
      FirstTeamButton = new Rectangle(propertyX, 384, fieldWidth, 30);
      TurnLimitDown = new Rectangle(propertyX, 422, 32, 28);
      TurnLimitLabel = new Rectangle(propertyX + 36, 422, fieldWidth - 72, 28);
      TurnLimitUp = new Rectangle(propertyX + fieldWidth - 32, 422, 32, 28);
      ObjectiveTop = 160;
      ObjectiveOutcomeButton = new Rectangle(propertyX, 186, fieldWidth, 28);
      ObjectivePrevious = new Rectangle(propertyX, 220, 30, 28);
      ObjectiveTypeLabel = new Rectangle(propertyX + 34, 220, fieldWidth - 68, 28);
      ObjectiveNext = new Rectangle(propertyX + fieldWidth - 30, 220, 30, 28);
      ObjectiveTeamButton = new Rectangle(propertyX, 254, fieldWidth, 28);
      ObjectiveAddButton = new Rectangle(propertyX, 288, (fieldWidth - 6) / 2, 28);
      ObjectiveRemoveButton = new Rectangle(propertyX + (fieldWidth + 6) / 2, 288, (fieldWidth - 6) / 2, 28);
      ObjectiveActiveLabel = new Rectangle(propertyX, 322, fieldWidth, 26);
      ObjectiveAmountDown = new Rectangle(propertyX, 352, 32, 28);
      ObjectiveAmountUp = new Rectangle(propertyX + fieldWidth - 32, 352, 32, 28);
      ObjectiveTargetButton = new Rectangle(propertyX, 386, (fieldWidth - 6) / 2, 28);
      ObjectiveLocationButton = new Rectangle(propertyX + (fieldWidth + 6) / 2, 386, (fieldWidth - 6) / 2, 28);
      SettingsTop = 124;
      SettingsTeamPrevious = new Rectangle(propertyX, 150, 30, 28);
      SettingsTeamLabel = new Rectangle(propertyX + 34, 150, fieldWidth - 68, 28);
      SettingsTeamNext = new Rectangle(propertyX + fieldWidth - 30, 150, 30, 28);
      ControllerButton = new Rectangle(propertyX, 186, fieldWidth, 30);
      MoneyDownButton = new Rectangle(propertyX, 224, 32, 28);
      MoneyLabel = new Rectangle(propertyX + 36, 224, fieldWidth - 72, 28);
      MoneyUpButton = new Rectangle(propertyX + fieldWidth - 32, 224, 32, 28);
      ActionsDownButton = new Rectangle(propertyX, 260, 32, 28);
      ActionsLabel = new Rectangle(propertyX + 36, 260, fieldWidth - 72, 28);
      ActionsUpButton = new Rectangle(propertyX + fieldWidth - 32, 260, 32, 28);
      TeamPurchasesButton = new Rectangle(propertyX, 296, fieldWidth, 30);
      TeamUnitTop = 342;
      TeamUnitPrevious = new Rectangle(propertyX, 362, 30, 28);
      TeamUnitLabel = new Rectangle(propertyX + 34, 362, fieldWidth - 68, 28);
      TeamUnitNext = new Rectangle(propertyX + fieldWidth - 30, 362, 30, 28);
      TeamUnitToggle = new Rectangle(propertyX, 398, fieldWidth, 30);
      CpuDifficultyButton = new Rectangle(propertyX, 434, fieldWidth, 30);
      CpuPersonalityButton = new Rectangle(propertyX, 470, fieldWidth, 30);
      GlobalPurchasesButton = new Rectangle(propertyX, 150, fieldWidth, 30);
      AbilitiesButton = new Rectangle(propertyX, 188, fieldWidth, 30);
      RulesHelp = new Rectangle(propertyX, 434, fieldWidth, 60);
      SelectionTop = 510;
      SelectedRowOne = new Rectangle(propertyX, 537, fieldWidth, 26);
      SelectedPrevious = new Rectangle(propertyX, 571, (fieldWidth - 6) / 2, 30);
      SelectedNext = new Rectangle(propertyX + (fieldWidth + 6) / 2, 571, (fieldWidth - 6) / 2, 30);
      RotateButton = new Rectangle(propertyX, 609, fieldWidth, 30);
    }
  }
}
