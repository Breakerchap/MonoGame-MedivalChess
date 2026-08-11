#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using MedivalChess.GameBoard;
using MedivalChess.Shared;

namespace MedivalChess.Campaign;

internal sealed partial class LevelEditorScreen
{
  private Desktop? _editorMyraDesktop;
  private Rectangle _editorMyraScreen;
  private bool _editorMyraDirty = true;
  private string _editorMyraSnapshot = string.Empty;

  private void EnsureMyraEditor(Rectangle screen)
  {
    _editorMyraScreen = screen;
    if (_editorMyraDesktop is not null)
    {
      _editorMyraDesktop.Scale = new Vector2(UiLayout.Scale);
      return;
    }

    _editorMyraDesktop = new Desktop
    {
      Background = new SolidBrush(Color.Transparent),
      TransformOrigin = Vector2.Zero,
      Scale = new Vector2(UiLayout.Scale),
      BoundsFetcher = () => _editorMyraScreen
    };
    _editorMyraDirty = true;
  }

  private bool IsMyraEditorTextInputFocused() => _editorMyraDesktop?.FocusedKeyboardWidget is TextBox;

  private bool HandleMyraEditorEscape()
  {
    if (_editorMyraDesktop?.FocusedKeyboardWidget is not TextBox) return false;
    _editorMyraDesktop.FocusedKeyboardWidget = null;
    return true;
  }

  private bool IsMyraEditorChromePoint(EditorLayout layout, Point point) =>
    layout.Header.Contains(point) || layout.Tools.Contains(point) || layout.Properties.Contains(point) || layout.Status.Contains(point);

  private void MarkEditorMyraDirty() => _editorMyraDirty = true;

  private string GetEditorMyraSnapshot()
  {
    CampaignValidationResult validation = State.Validate();
    string selection = $"{State.Selection.Kind}:{State.Selection.Id}";
    string teams = string.Join(',', State.Level.Teams.Select(team =>
      $"{team.Team}:{team.Controller}:{team.StartingMoney}:{team.ActionsPerTurn}:{team.PurchaseListMode}:{team.CpuProfile.Difficulty}:{team.CpuProfile.Personality}"));
    return $"{_propertiesView}|{_editingScenarioObjectives}|{_editingDefeatConditions}|{_selectedObjectiveId}|{_objectiveTargetMode}|" +
      $"{State.ActiveTool}|{selection}|{_unitPaletteIndex}|{_objectPaletteIndex}|{_terrainPaletteType}|{_placementTeam}|{_territoryOwner}|" +
      $"{_boardBaseIndex}|{_settingsTeam}|{_restrictionUnitIndex}|{_expandedCatalogueUnitId}|{_unitCataloguePage}|" +
      $"{State.Level.Board.Width}x{State.Level.Board.Height}:{State.Level.Board.Shape}|{State.Level.Scenario.GameMode}:{State.Level.Scenario.FirstTeam}:{State.Level.Scenario.TurnLimit}|" +
      $"{State.Level.Restrictions.PurchasesEnabled}:{State.Level.Restrictions.AbilitiesEnabled}:{string.Join(',', State.Level.Restrictions.DisabledUnitTypes)}|" +
      $"{State.Level.Units.Count}:{State.Level.Objects.Count}:{State.Level.Terrain.Count}:{teams}|{validation.IsValid}:{validation.Problems.Count}|{_status}";
  }

  private void RenderMyraEditor(EditorLayout layout)
  {
    EnsureMyraEditor(_editorMyraScreen);
    if (_editorMyraDesktop is null) return;

    string snapshot = GetEditorMyraSnapshot();
    if (!IsMyraEditorTextInputFocused() && snapshot != _editorMyraSnapshot)
    {
      _editorMyraDirty = true;
    }

    if (_editorMyraDirty)
    {
      _editorMyraDesktop.Root = BuildMyraEditor(layout);
      _editorMyraSnapshot = GetEditorMyraSnapshot();
      _editorMyraDirty = false;
    }

    _editorMyraDesktop.Scale = new Vector2(UiLayout.Scale);
    _editorMyraDesktop.Render();
  }

  private Widget BuildMyraEditor(EditorLayout layout)
  {
    Panel root = new()
    {
      Width = layout.Screen.Width,
      Height = layout.Screen.Height,
      Background = new SolidBrush(Color.Transparent),
      HorizontalAlignment = HorizontalAlignment.Left,
      VerticalAlignment = VerticalAlignment.Top
    };

    AddBackground(root, layout.Header, UiTheme.Panel);
    AddBackground(root, layout.Tools, UiTheme.Panel);
    AddBackground(root, layout.Properties, UiTheme.Panel);
    AddBackground(root, layout.Status, UiTheme.Panel);

    BuildMyraEditorHeader(root, layout);
    BuildMyraEditorTools(root, layout);
    BuildMyraEditorInspector(root, layout);
    BuildMyraEditorStatus(root, layout);
    return root;
  }

  private void BuildMyraEditorHeader(Panel root, EditorLayout layout)
  {
    CampaignValidationResult validation = State.Validate();
    int errors = validation.Problems.Count(problem => problem.Severity == CampaignValidationSeverity.Error);
    AddLabel(root, layout.TitleBounds, State.Level.Metadata.Name, UiTheme.TextPrimary, HorizontalAlignment.Left);
    AddLabel(root, layout.ValidationBounds, validation.IsValid ? "READY" : $"{errors} ISSUE{(errors == 1 ? string.Empty : "S")}", validation.IsValid ? UiTheme.Health : UiTheme.Attack);

    AddButton(root, layout.SaveButton, "SAVE", SaveMyraEditor, UiTheme.PrimaryButton);
    AddButton(root, layout.ExportButton, "EXPORT", ExportMyraEditor, UiTheme.AccentButton);
    AddButton(root, layout.ImportButton, "OPEN", ImportMyraEditor);
    AddButton(root, layout.BrowseButton, "LEVELS", () => RequestBrowse = true);
    AddButton(root, layout.TestButton, "TEST PLAY", TestMyraEditor, UiTheme.PrimaryButton);
    AddButton(root, layout.ExitButton, "EXIT", () => RequestExit = true, UiTheme.DangerButton);
  }

  private void BuildMyraEditorTools(Panel root, EditorLayout layout)
  {
    AddLabel(root, new Rectangle(layout.Tools.X + layout.PanelPadding, layout.Tools.Y + 6, layout.Tools.Width - layout.PanelPadding * 2, 24), "BUILD", UiTheme.GoldBright, HorizontalAlignment.Left);
    foreach ((EditorTool tool, string label, Rectangle bounds) in layout.ToolButtons)
    {
      EditorTool captured = tool;
      AddButton(root, bounds, label, () => SelectMyraEditorTool(captured), captured == EditorTool.Delete ? UiTheme.DangerButton : UiTheme.NeutralButton, State.ActiveTool == captured);
    }

    AddButton(root, layout.UndoButton, "UNDO", () => _status = State.Undo() ? "Undid last editor operation." : "Nothing to undo.");
    AddButton(root, layout.RedoButton, "REDO", () => _status = State.Redo() ? "Redid editor operation." : "Nothing to redo.");
    AddButton(root, layout.NewButton, "NEW LEVEL", () => RequestNew = true, UiTheme.AccentButton);
    AddButton(root, layout.BoardSmallerButton, "-", () => ResizeBoard(-1));
    AddButton(root, layout.BoardLargerButton, "+", () => ResizeBoard(1));
    AddLabel(root, layout.BoardSizeLabel, $"BOARD: {State.Level.Board.Width} × {State.Level.Board.Height}", UiTheme.TextMuted);
    AddButton(root, layout.BoardShapeButton, $"SHAPE: {State.Level.Board.Shape}".ToUpperInvariant(), () =>
      State.SetBoardShape(State.Level.Board.Shape == CampaignBoardShape.Rectangle ? CampaignBoardShape.Custom : CampaignBoardShape.Rectangle));
    AddButton(root, layout.BoardBasePrevious, "<", () => _boardBaseIndex = (_boardBaseIndex - 1 + BoardBaseNames.Length) % BoardBaseNames.Length);
    AddButton(root, layout.BoardBaseNext, ">", () => _boardBaseIndex = (_boardBaseIndex + 1) % BoardBaseNames.Length);
    AddLabel(root, layout.BoardBaseLabel, $"BASE: {BoardBaseNames[_boardBaseIndex]}".ToUpperInvariant(), UiTheme.TextMuted);
    AddButton(root, layout.BoardBaseApply, "USE DEFAULT BOARD", () =>
    {
      State.UseBoardBase(BoardRules.GetBoard(BoardBaseNames[_boardBaseIndex]));
      _fitBoardRequested = true;
      _status = $"Using the shipped {BoardBaseNames[_boardBaseIndex]} board as this level's base.";
    }, UiTheme.AccentButton);
    AddButton(root, layout.FitBoardButton, "FIT BOARD", () =>
    {
      _fitBoardRequested = true;
      _status = "Board fitted to the canvas.";
    });

    IReadOnlyList<(string identifier, PieceDefinition definition)> unitPalette = GetUnitPalette();
    if (unitPalette.Count > 0)
    {
      _unitPaletteIndex = Math.Clamp(_unitPaletteIndex, 0, unitPalette.Count - 1);
      (string _, PieceDefinition selectedUnit) = unitPalette[_unitPaletteIndex];
      AddLabel(root, new Rectangle(layout.Tools.X + layout.PanelPadding, layout.PaletteTop + 1, layout.Tools.Width - layout.PanelPadding * 2, 20), "UNIT", UiTheme.GoldBright, HorizontalAlignment.Left);
      AddButton(root, layout.UnitPrevious, "<", () => _unitPaletteIndex = (_unitPaletteIndex - 1 + unitPalette.Count) % unitPalette.Count);
      AddButton(root, layout.UnitNext, ">", () => _unitPaletteIndex = (_unitPaletteIndex + 1) % unitPalette.Count);
      AddLabel(root, layout.UnitPreview, selectedUnit.DisplayName.ToUpperInvariant(), UiTheme.GetTeamColour(_placementTeam.ToTeamName()));
      AddButton(root, layout.TeamPrevious, "<", () => CyclePlacementTeam(-1));
      AddButton(root, layout.TeamNext, ">", () => CyclePlacementTeam(1));
      AddLabel(root, layout.TeamPreview, $"PLACE FOR {_placementTeam}".ToUpperInvariant(), UiTheme.GetTeamColour(_placementTeam.ToTeamName()));
    }

    CampaignBoardObjectType[] objectTypes = Enum.GetValues<CampaignBoardObjectType>();
    CampaignBoardObjectType selectedObject = objectTypes[_objectPaletteIndex % objectTypes.Length];
    AddLabel(root, new Rectangle(layout.Tools.X + layout.PanelPadding, layout.ObjectPaletteTop + 1, layout.Tools.Width - layout.PanelPadding * 2, 20), "OBJECT / TERRAIN", UiTheme.GoldBright, HorizontalAlignment.Left);
    AddButton(root, layout.ObjectPrevious, "<", () => _objectPaletteIndex = (_objectPaletteIndex - 1 + objectTypes.Length) % objectTypes.Length);
    AddButton(root, layout.ObjectNext, ">", () => _objectPaletteIndex = (_objectPaletteIndex + 1) % objectTypes.Length);
    AddLabel(root, layout.ObjectPreview, selectedObject.ToString().ToUpperInvariant());
    AddButton(root, layout.TerrainButton, $"PAINT {_terrainPaletteType}".ToUpperInvariant(), () =>
    {
      _terrainPaletteType = _terrainPaletteType == CampaignTerrainType.Forest ? CampaignTerrainType.Lake : CampaignTerrainType.Forest;
      State.ActiveTool = EditorTool.Terrain;
      _status = $"Terrain brush: {_terrainPaletteType}. Drag across playable tiles to paint it.";
    }, UiTheme.AccentButton, State.ActiveTool == EditorTool.Terrain);

    NetworkTeam[] owners = [NetworkTeam.Neutral, .. State.Level.Teams.Select(team => team.Team)];
    for (int index = 0; index < Math.Min(owners.Length, layout.TerritoryButtons.Count); index++)
    {
      NetworkTeam owner = owners[index];
      NetworkTeam? team = owner == NetworkTeam.Neutral ? null : owner;
      AddButton(root, layout.TerritoryButtons[index], CampaignTerritoryRules.GetAreaLabel(team), () =>
      {
        _territoryOwner = owner;
        State.ActiveTool = EditorTool.Territory;
        _status = $"Territory brush: {CampaignTerritoryRules.GetAreaLabel(team)}. Drag across the board to paint it.";
      }, team is null ? UiTheme.NoMansLand : UiTheme.GetTeamColour(team.Value.ToTeamName()), State.ActiveTool == EditorTool.Territory && _territoryOwner == owner);
    }
    AddButton(root, layout.ResetTerritoryButton, "RESET AREAS", () =>
    {
      State.UseAutomaticTerritories();
      State.ActiveTool = EditorTool.Territory;
      _territoryOwner = NetworkTeam.Neutral;
      _status = "Areas reset to the game's automatic territories. Paint a square to begin a custom map.";
    });
  }

  private void BuildMyraEditorInspector(Panel root, EditorLayout layout)
  {
    AddLabel(root, new Rectangle(layout.Properties.X + layout.PanelPadding, layout.Properties.Y + 8, layout.Properties.Width - layout.PanelPadding * 2, 24), "INSPECTOR", UiTheme.GoldBright, HorizontalAlignment.Left);
    AddButton(root, layout.ScenarioTab, "SCENARIO", () => _propertiesView = PropertiesView.Scenario, UiTheme.NeutralButton, _propertiesView == PropertiesView.Scenario);
    AddButton(root, layout.TeamsTab, "TEAMS", () => _propertiesView = PropertiesView.Teams, UiTheme.NeutralButton, _propertiesView == PropertiesView.Teams);
    AddButton(root, layout.RestrictionsTab, "RULES", () => _propertiesView = PropertiesView.Restrictions, UiTheme.NeutralButton, _propertiesView == PropertiesView.Restrictions);
    AddButton(root, layout.UnitsTab, "UNITS", () => _propertiesView = PropertiesView.Units, UiTheme.NeutralButton, _propertiesView == PropertiesView.Units);

    switch (_propertiesView)
    {
      case PropertiesView.Teams: BuildMyraTeamInspector(root, layout); break;
      case PropertiesView.Restrictions: BuildMyraRestrictionInspector(root, layout); break;
      case PropertiesView.Units: BuildMyraUnitInspector(root, layout); break;
      default: BuildMyraScenarioInspector(root, layout); break;
    }

    if (_propertiesView is not PropertiesView.Scenario and not PropertiesView.Units && layout.ShowSelectionProperties)
    {
      BuildMyraSelectionInspector(root, layout);
    }
  }

  private void BuildMyraScenarioInspector(Panel root, EditorLayout layout)
  {
    AddButton(root, layout.ScenarioDetailsButton, "DETAILS", () => _editingScenarioObjectives = false, UiTheme.NeutralButton, !_editingScenarioObjectives);
    AddButton(root, layout.ScenarioObjectivesButton, "OBJECTIVES", () => _editingScenarioObjectives = true, UiTheme.NeutralButton, _editingScenarioObjectives);
    if (_editingScenarioObjectives)
    {
      BuildMyraObjectiveInspector(root, layout);
      return;
    }

    AddTextBox(root, layout.NameField, State.Level.Metadata.Name, "Level name", text => State.EditMetadata(metadata => metadata.Name = text));
    AddTextBox(root, layout.AuthorField, State.Level.Metadata.Author, "Author", text => State.EditMetadata(metadata => metadata.Author = text));
    AddTextBox(root, layout.DescriptionField, State.Level.Metadata.Description, "Description", text => State.EditMetadata(metadata => metadata.Description = text), multiline: true);
    AddTextBox(root, layout.DialogueField, State.Level.Metadata.CampaignDialogue ?? string.Empty, "Campaign dialogue", text => State.EditMetadata(metadata => metadata.CampaignDialogue = text), multiline: true);
    AddButton(root, layout.ModeButton, $"MODE: {State.Level.Scenario.GameMode.ToUpperInvariant()}", CycleGameMode);
    AddButton(root, layout.FirstTeamButton, $"FIRST: {State.Level.Scenario.FirstTeam.ToString().ToUpperInvariant()}", CycleFirstTeam);
    AddButton(root, layout.TurnLimitDown, "-", () => State.UpdateScenario(scenario => scenario.TurnLimit = Math.Max(1, (scenario.TurnLimit ?? 11) - 1)));
    AddButton(root, layout.TurnLimitUp, "+", () => State.UpdateScenario(scenario => scenario.TurnLimit = Math.Min(10_000, (scenario.TurnLimit ?? 0) + 1)));
    AddLabel(root, layout.TurnLimitLabel, $"TURN LIMIT: {State.Level.Scenario.TurnLimit?.ToString() ?? "NONE"}", UiTheme.TextMuted);
  }

  private void BuildMyraObjectiveInspector(Panel root, EditorLayout layout)
  {
    IReadOnlyList<CampaignObjectiveDefinition> objectives = _editingDefeatConditions
      ? State.Level.Scenario.DefeatConditions
      : State.Level.Scenario.VictoryConditions;
    CampaignObjectiveDefinition? selected = objectives.FirstOrDefault(objective => objective.Id == _selectedObjectiveId) ?? objectives.LastOrDefault();

    AddButton(root, layout.ObjectiveOutcomeButton, _editingDefeatConditions ? "SWITCH TO VICTORY" : "SWITCH TO DEFEAT", () =>
    {
      _editingDefeatConditions = !_editingDefeatConditions;
      _selectedObjectiveId = null;
      _objectiveTargetMode = ObjectiveTargetMode.None;
    }, _editingDefeatConditions ? UiTheme.DangerButton : UiTheme.PrimaryButton);

    CampaignObjectiveType[] types = Enum.GetValues<CampaignObjectiveType>();
    AddButton(root, layout.ObjectivePrevious, "<", () => _objectivePaletteType = types[(Array.IndexOf(types, _objectivePaletteType) - 1 + types.Length) % types.Length]);
    AddButton(root, layout.ObjectiveNext, ">", () => _objectivePaletteType = types[(Array.IndexOf(types, _objectivePaletteType) + 1) % types.Length]);
    AddLabel(root, layout.ObjectiveTypeLabel, GetObjectiveTitle(_objectivePaletteType).ToUpperInvariant());
    AddButton(root, layout.ObjectiveTeamButton, $"TEAM: {(selected?.Team ?? _objectiveTeam)}".ToUpperInvariant(), () =>
    {
      if (selected is null) CycleObjectiveTeam();
      else
      {
        NetworkTeam next = GetNextObjectiveTeam(selected.Team ?? _objectiveTeam);
        State.UpdateObjective(_editingDefeatConditions, selected.Id, objective => objective.Team = next);
        _objectiveTeam = next;
      }
    });
    AddButton(root, layout.ObjectiveAddButton, "ADD", () =>
    {
      CampaignObjectiveDefinition objective = State.AddObjective(_editingDefeatConditions, _objectivePaletteType, _objectiveTeam);
      _selectedObjectiveId = objective.Id;
      _status = "Condition added. Pick a unit or square when this condition needs one.";
    }, UiTheme.PrimaryButton);
    AddButton(root, layout.ObjectiveRemoveButton, "REMOVE", () =>
    {
      if (selected is null) return;
      State.RemoveObjective(_editingDefeatConditions, selected.Id);
      _selectedObjectiveId = null;
    }, UiTheme.DangerButton, enabled: selected is not null);

    if (selected is null)
    {
      AddLabel(root, layout.ObjectiveHelp, "Choose a win or defeat condition, then press Add.", UiTheme.TextMuted, HorizontalAlignment.Left);
      return;
    }

    int selectedIndex = objectives.ToList().FindIndex(objective => objective.Id == selected.Id) + 1;
    AddButton(root, layout.ObjectiveActiveLabel, $"CONDITION {selectedIndex}/{objectives.Count}: {GetObjectiveTitle(selected.Type)}", () =>
    {
      int current = objectives.ToList().FindIndex(objective => objective.Id == selected.Id);
      _selectedObjectiveId = objectives[(current + 1) % objectives.Count].Id;
    });
    AddLabel(root, layout.ObjectiveHelp, GetObjectiveExplanation(selected), UiTheme.TextMuted, HorizontalAlignment.Left);
    AddButton(root, layout.ObjectiveAmountDown, "-", () =>
    {
      if (ObjectiveUsesAmount(selected.Type)) State.UpdateObjective(_editingDefeatConditions, selected.Id, objective => objective.RequiredAmount = Math.Max(1, objective.RequiredAmount - 1));
    }, enabled: ObjectiveUsesAmount(selected.Type));
    AddButton(root, layout.ObjectiveAmountUp, "+", () =>
    {
      if (ObjectiveUsesAmount(selected.Type)) State.UpdateObjective(_editingDefeatConditions, selected.Id, objective => objective.RequiredAmount = Math.Min(10_000, objective.RequiredAmount + 1));
    }, enabled: ObjectiveUsesAmount(selected.Type));
    AddLabel(root, layout.ObjectiveAmountLabel, GetObjectiveAmountLabel(selected));
    AddButton(root, layout.ObjectiveTargetButton, GetObjectiveUnitButtonLabel(selected), () =>
    {
      _objectiveTargetMode = ObjectiveTargetMode.Unit;
      _status = "Click a starting unit on the board.";
    }, UiTheme.AccentButton, _objectiveTargetMode == ObjectiveTargetMode.Unit);
    AddButton(root, layout.ObjectiveLocationButton, GetObjectiveLocationButtonLabel(selected), () =>
    {
      _objectiveTargetMode = ObjectiveTargetMode.Location;
      _status = "Click a playable square on the board.";
    }, UiTheme.AccentButton, _objectiveTargetMode == ObjectiveTargetMode.Location);
  }

  private void BuildMyraTeamInspector(Panel root, EditorLayout layout)
  {
    CampaignTeamDefinition? team = State.Level.Teams.FirstOrDefault(candidate => candidate.Team == _settingsTeam);
    if (team is null) return;
    AddButton(root, layout.SettingsTeamPrevious, "<", () => CycleSettingsTeam(-1));
    AddButton(root, layout.SettingsTeamNext, ">", () => CycleSettingsTeam(1));
    AddLabel(root, layout.SettingsTeamLabel, _settingsTeam.ToString().ToUpperInvariant(), UiTheme.GetTeamColour(_settingsTeam.ToTeamName()));
    AddButton(root, layout.ControllerButton, team.Controller == CampaignTeamController.Cpu ? "CONTROLLER: CPU" : "CONTROLLER: PLAYER", () =>
      State.UpdateTeam(_settingsTeam, value => value.Controller = value.Controller == CampaignTeamController.Cpu ? CampaignTeamController.Human : CampaignTeamController.Cpu));
    AddButton(root, layout.MoneyDownButton, "-", () => State.UpdateTeam(_settingsTeam, value => value.StartingMoney = Math.Max(0, value.StartingMoney - 50)));
    AddButton(root, layout.MoneyUpButton, "+", () => State.UpdateTeam(_settingsTeam, value => value.StartingMoney = Math.Min(1_000_000, value.StartingMoney + 50)));
    AddLabel(root, layout.MoneyLabel, $"STARTING GOLD: {team.StartingMoney}", UiTheme.TextMuted);
    AddButton(root, layout.ActionsDownButton, "-", () => State.UpdateTeam(_settingsTeam, value => value.ActionsPerTurn = Math.Max(1, value.ActionsPerTurn - 1)));
    AddButton(root, layout.ActionsUpButton, "+", () => State.UpdateTeam(_settingsTeam, value => value.ActionsPerTurn = Math.Min(100, value.ActionsPerTurn + 1)));
    AddLabel(root, layout.ActionsLabel, $"ACTIONS: {team.ActionsPerTurn}", UiTheme.TextMuted);
    AddButton(root, layout.TeamPurchasesButton, $"BUY LIST: {GetBuyListMode(team)}", () =>
    {
      CampaignPurchaseListMode[] modes = Enum.GetValues<CampaignPurchaseListMode>();
      int index = Array.IndexOf(modes, team.PurchaseListMode);
      SetTeamBuyListMode(modes[(index + 1 + modes.Length) % modes.Length]);
    });
    AddButton(root, layout.TeamUnitToggle, "MANAGE IN UNITS TAB", () => _propertiesView = PropertiesView.Units);
    AddButton(root, layout.CpuDifficultyButton, $"CPU DIFFICULTY: {team.CpuProfile.Difficulty}".ToUpperInvariant(), () => State.UpdateTeam(_settingsTeam, value =>
    {
      value.CpuProfile.Difficulty = value.CpuProfile.Difficulty switch
      {
        "Easy" => "Medium", "Medium" or "Normal" => "Hard", "Hard" => "Best", _ => "Easy"
      };
    }), UiTheme.AccentButton);
    AddButton(root, layout.CpuPersonalityButton, $"CPU STYLE: {team.CpuProfile.Personality}".ToUpperInvariant(), () => State.UpdateTeam(_settingsTeam, value =>
    {
      string[] personalities = ["Balanced", "Aggressive", "Defensive", "Greedy", "Reckless", "ObjectiveFocused", "Swarmer"];
      int current = Array.IndexOf(personalities, value.CpuProfile.Personality);
      value.CpuProfile.Personality = personalities[(current + 1 + personalities.Length) % personalities.Length];
    }), UiTheme.AccentButton);
  }

  private void BuildMyraRestrictionInspector(Panel root, EditorLayout layout)
  {
    CampaignRestrictionsDefinition rules = State.Level.Restrictions;
    AddButton(root, layout.GlobalPurchasesButton, rules.PurchasesEnabled ? "GLOBAL BUYING: ON" : "GLOBAL BUYING: OFF", () =>
      State.UpdateScenario(_ => State.Level.Restrictions.PurchasesEnabled = !State.Level.Restrictions.PurchasesEnabled), rules.PurchasesEnabled ? UiTheme.PrimaryButton : UiTheme.DangerButton);
    AddButton(root, layout.AbilitiesButton, rules.AbilitiesEnabled ? "ABILITIES: ON" : "ABILITIES: OFF", () =>
      State.UpdateScenario(_ => State.Level.Restrictions.AbilitiesEnabled = !State.Level.Restrictions.AbilitiesEnabled), rules.AbilitiesEnabled ? UiTheme.PrimaryButton : UiTheme.DangerButton);

    IReadOnlyList<(string identifier, PieceDefinition definition)> buyPalette = GetPurchasableUnitPalette();
    if (buyPalette.Count == 0) return;
    _restrictionUnitIndex = Math.Clamp(_restrictionUnitIndex, 0, buyPalette.Count - 1);
    (string unitId, PieceDefinition definition) = buyPalette[_restrictionUnitIndex];
    AddButton(root, layout.TeamUnitPrevious, "<", () => _restrictionUnitIndex = (_restrictionUnitIndex - 1 + buyPalette.Count) % buyPalette.Count);
    AddButton(root, layout.TeamUnitNext, ">", () => _restrictionUnitIndex = (_restrictionUnitIndex + 1) % buyPalette.Count);
    AddLabel(root, layout.TeamUnitLabel, definition.DisplayName.ToUpperInvariant());
    bool disabled = rules.DisabledUnitTypes.Contains(unitId);
    AddButton(root, layout.TeamUnitToggle, disabled ? "UNIT DISABLED" : "UNIT ALLOWED", () =>
      State.UpdateScenario(_ => ToggleUnit(State.Level.Restrictions.DisabledUnitTypes, unitId)), disabled ? UiTheme.DangerButton : UiTheme.PrimaryButton);
    AddLabel(root, layout.RulesHelp, "Team buy lists can further narrow these global rules.", UiTheme.TextMuted, HorizontalAlignment.Left);
  }

  private void BuildMyraUnitInspector(Panel root, EditorLayout layout)
  {
    IReadOnlyList<UnitCatalogueEntry> units = GetUnitCatalogue();
    UnitCatalogueLayout catalogue = CreateUnitCatalogueLayout(layout);
    int pageCount = Math.Max(1, (units.Count + catalogue.PageSize - 1) / catalogue.PageSize);
    _unitCataloguePage = Math.Clamp(_unitCataloguePage, 0, pageCount - 1);

    AddButton(root, catalogue.AddButton, "+ NEW UNIT", () =>
    {
      CampaignCustomUnitDefinition created = State.AddCustomUnit();
      _expandedCatalogueUnitId = created.Id;
      int createdIndex = GetUnitCatalogue().ToList().FindIndex(entry => entry.Identifier == created.Id);
      _unitCataloguePage = Math.Max(0, createdIndex / catalogue.PageSize);
      _status = $"Created {created.Name}. Edit its card, then press PLACE.";
    }, UiTheme.AccentButton);
    AddButton(root, catalogue.PreviousButton, "<", () => _unitCataloguePage = (_unitCataloguePage - 1 + pageCount) % pageCount);
    AddButton(root, catalogue.NextButton, ">", () => _unitCataloguePage = (_unitCataloguePage + 1) % pageCount);
    AddLabel(root, catalogue.PageLabel, $"{_unitCataloguePage + 1}/{pageCount}  •  {units.Count} UNITS", UiTheme.TextMuted);

    int first = _unitCataloguePage * catalogue.PageSize;
    for (int slot = 0; slot < catalogue.Headers.Count && first + slot < units.Count; slot++)
    {
      UnitCatalogueEntry entry = units[first + slot];
      bool expanded = _expandedCatalogueUnitId == entry.Identifier;
      string prefix = entry.IsCustom ? "CUSTOM • " : string.Empty;
      AddButton(root, catalogue.Headers[slot], prefix + entry.Definition.DisplayName.ToUpperInvariant(), () =>
        _expandedCatalogueUnitId = expanded ? null : entry.Identifier, UiTheme.NeutralButton, expanded);
    }

    UnitCatalogueEntry? selected = units.FirstOrDefault(entry => entry.Identifier == _expandedCatalogueUnitId);
    if (selected is null)
    {
      AddLabel(root, catalogue.Details, "Select a unit above to edit stats, placement and buying.", UiTheme.TextMuted, HorizontalAlignment.Left);
      return;
    }

    UnitCatalogueDetails fields = CreateUnitCatalogueDetails(catalogue.Details);
    AddUnitTextBox(root, fields.Name, selected, TextField.UnitName, selected.Definition.DisplayName, "Name");
    AddUnitTextBox(root, fields.Abbreviation, selected, TextField.UnitAbbreviation, UiText.BuildPieceLabel(selected.Definition), "Abbreviation");
    AddUnitTextBox(root, fields.Cost, selected, TextField.UnitCost, selected.Definition.Cost.ToString(), "Cost");
    AddUnitTextBox(root, fields.MoveRange, selected, TextField.UnitMoveRange, selected.Definition.Movement.range.ToString(), "Move");
    AddButton(root, fields.MoveShape, $"MOVE: {selected.Definition.Movement.shape}", () => CycleMyraUnitShape(selected, true));
    AddUnitTextBox(root, fields.Health, selected, TextField.UnitHealth, selected.Definition.Health.ToString(), "Health");
    AddUnitTextBox(root, fields.Attack, selected, TextField.UnitAttack, selected.Definition.Attack.ToString(), "Attack");
    AddUnitTextBox(root, fields.Width, selected, TextField.UnitWidth, selected.Definition.Size.x.ToString(), "Width");
    AddUnitTextBox(root, fields.Height, selected, TextField.UnitHeight, selected.Definition.Size.y.ToString(), "Height");
    AddUnitTextBox(root, fields.MinimumRange, selected, TextField.UnitMinimumRange, selected.Definition.AttackRange.Minimum.ToString(), "Min range");
    AddUnitTextBox(root, fields.MaximumRange, selected, TextField.UnitMaximumRange, selected.Definition.AttackRange.Maximum.ToString(), "Max range");
    AddButton(root, fields.AttackShape, $"ATTACK: {selected.Definition.AttackPattern}", () => CycleMyraUnitShape(selected, false));
    AddButton(root, fields.Ability, $"ABILITY: {GetAbilityLabel(selected.AbilitySource)}", () => CycleMyraUnitAbility(selected));
    AddButton(root, fields.Place, "PLACE", () => SelectCatalogueUnitForPlacement(selected.Identifier), UiTheme.PrimaryButton);
    AddButton(root, fields.Buy, selected.Purchasable ? "BUYING: ON" : "BUYING: OFF", () => ToggleCatalogueBuying(selected), selected.Purchasable ? UiTheme.PrimaryButton : UiTheme.NeutralButton);
    AddButton(root, fields.Remove, selected.IsCustom ? "DELETE CUSTOM UNIT" : (selected.Purchasable ? "REMOVE FROM BUY MENU" : "RESTORE BUY MENU"), () => RemoveCatalogueUnit(selected), UiTheme.DangerButton);
  }

  private void BuildMyraSelectionInspector(Panel root, EditorLayout layout)
  {
    if (State.Selection.Kind == EditorSelectionKind.Unit && State.Selection.Id is string unitId)
    {
      CampaignUnitDefinition? unit = State.Level.Units.FirstOrDefault(candidate => candidate.Id == unitId);
      if (unit is null) return;
      AddButton(root, layout.SelectedRowOne, $"{unit.UnitType} • TEAM {unit.Team}", () => CycleSelectedUnitTeam(unitId));
      AddButton(root, layout.SelectedPrevious, "HEALTH -", () => AdjustSelectedHealth(unitId, -1));
      AddButton(root, layout.SelectedNext, "HEALTH +", () => AdjustSelectedHealth(unitId, 1));
      AddButton(root, layout.RotateButton, "ROTATE", () => State.RotateUnit(unitId), UiTheme.AccentButton);
      return;
    }
    if (State.Selection.Kind == EditorSelectionKind.Object && State.Selection.Id is string objectId)
    {
      CampaignBoardObjectDefinition? boardObject = State.Level.Objects.FirstOrDefault(candidate => candidate.Id == objectId);
      if (boardObject is null) return;
      AddLabel(root, layout.SelectedRowOne, $"{boardObject.Type} • {boardObject.Owner?.ToString() ?? "NEUTRAL"}", UiTheme.TextMuted);
      AddButton(root, layout.RotateButton, "DELETE OBJECT", () => State.DeleteObject(objectId), UiTheme.DangerButton);
    }
  }

  private void BuildMyraEditorStatus(Panel root, EditorLayout layout)
  {
    int padding = Math.Min(12, Math.Max(6, layout.Status.Width / 45));
    int messageHeight = Math.Max(18, layout.Status.Height / 2 - 3);
    AddLabel(root, new Rectangle(layout.Status.X + padding, layout.Status.Y + 3, layout.Status.Width - padding * 2, messageHeight), _status, UiTheme.TextPrimary, HorizontalAlignment.Left);
    CampaignValidationProblem? firstProblem = _problems.FirstOrDefault() ?? State.Validate().Problems.FirstOrDefault();
    if (firstProblem is null) return;
    string prefix = firstProblem.Severity == CampaignValidationSeverity.Error ? "FIX BEFORE TEST PLAY: " : "CHECK: ";
    AddLabel(root, new Rectangle(layout.Status.X + padding, layout.Status.Y + messageHeight + 4, layout.Status.Width - padding * 2, Math.Max(16, layout.Status.Height - messageHeight - 7)), prefix + firstProblem.Message, firstProblem.Severity == CampaignValidationSeverity.Error ? UiTheme.Attack : UiTheme.Move, HorizontalAlignment.Left);
  }

  private void SaveMyraEditor()
  {
    string path = Path.Combine(CampaignLevelSerializer.LocalLevelDirectory, LevelFilePicker.CreateSafeLevelFileName(State.Level.Metadata.Name));
    ShowSaveResult(State.Save(path), $"Saved locally: {Path.GetFileName(path)}");
  }

  private void ExportMyraEditor()
  {
    string? path = LevelFilePicker.PickExportPath(LevelFilePicker.CreateSafeLevelFileName(State.Level.Metadata.Name));
    if (path is null) _status = "Export cancelled.";
    else ShowSaveResult(State.Save(path), $"Exported to: {path}");
  }

  private void ImportMyraEditor()
  {
    string? path = LevelFilePicker.PickImportPath();
    if (path is null) _status = "Import cancelled or no native file picker is available. Use LEVELS for local files.";
    else ShowLoadResult(State.Import(path), "Imported level.");
  }

  private void TestMyraEditor()
  {
    CampaignLevelLoadResult snapshot = State.CreateTestPlaySnapshot();
    _problems.Clear();
    _problems.AddRange(snapshot.Problems);
    if (snapshot.IsSuccess) RequestTestPlay = true;
    else _status = "Fix the listed validation issues before test play.";
  }

  private void SelectMyraEditorTool(EditorTool tool)
  {
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
  }

  private void CycleMyraUnitShape(UnitCatalogueEntry entry, bool movement)
  {
    Shape[] options = [Shape.Any, Shape.Straight, Shape.Line, Shape.Forward, Shape.AbsoluteStraightOrDiagonal, Shape.ForwardOrForwardDiagonal, Shape.None];
    Shape current = movement ? entry.Definition.Movement.shape : entry.Definition.AttackPattern;
    int index = Array.IndexOf(options, current);
    Shape next = options[(Math.Max(0, index) + 1) % options.Length];
    UpdateCatalogueStats(entry, value =>
    {
      if (movement) value.MovePattern = next;
      else value.AttackPattern = next;
    });
  }

  private void CycleMyraUnitAbility(UnitCatalogueEntry entry)
  {
    string[] options = ["None", .. PieceDefinitions.All.Where(unit => !string.IsNullOrWhiteSpace(unit.AbilityDescription)).Select(unit => unit.Identifier)];
    string current = GetAbilityLabel(entry.AbilitySource) == "NONE" ? "None" : entry.AbilitySource;
    int index = Array.IndexOf(options, current);
    SetCatalogueAbility(entry, options[(Math.Max(0, index) + 1) % options.Length]);
  }

  private void AddUnitTextBox(Panel root, Rectangle bounds, UnitCatalogueEntry entry, TextField field, string value, string hint)
  {
    AddTextBox(root, bounds, value, hint, text =>
    {
      if (field == TextField.UnitName)
      {
        UpdateCatalogueName(entry, text.Trim());
        return;
      }
      if (field == TextField.UnitAbbreviation)
      {
        UpdateCatalogueAbbreviation(entry, text.Trim());
        return;
      }
      if (!int.TryParse(text, out int number)) return;
      PieceDefinition definition = entry.Definition;
      UpdateCatalogueStats(entry, overrides =>
      {
        switch (field)
        {
          case TextField.UnitMoveRange: overrides.MoveRange = Math.Clamp(number, 0, 32); break;
          case TextField.UnitHealth: overrides.Health = Math.Clamp(number, 1, 10_000); break;
          case TextField.UnitAttack: overrides.Attack = Math.Clamp(number, 0, 10_000); break;
          case TextField.UnitWidth: overrides.Width = Math.Clamp(number, 1, 8); break;
          case TextField.UnitHeight: overrides.Height = Math.Clamp(number, 1, 8); break;
          case TextField.UnitMinimumRange: overrides.MinimumAttackRange = Math.Clamp(number, 0, definition.AttackRange.Maximum); break;
          case TextField.UnitMaximumRange: overrides.MaximumAttackRange = Math.Clamp(number, definition.AttackRange.Minimum, 32); break;
          case TextField.UnitCost: overrides.Cost = Math.Clamp(number, 0, 1_000_000); break;
        }
      });
    });
  }

  private void AddBackground(Panel root, Rectangle bounds, Color colour)
  {
    Panel panel = new()
    {
      Background = new SolidBrush(colour),
      Border = new SolidBrush(UiTheme.PanelBorder),
      BorderThickness = new Thickness(1)
    };
    Place(root, panel, bounds);
  }

  private Button AddButton(Panel root, Rectangle bounds, string text, Action action, Color? colour = null, bool selected = false, bool enabled = true)
  {
    Color baseColour = colour ?? UiTheme.NeutralButton;
    if (selected) baseColour = Color.Lerp(baseColour, UiTheme.Gold, 0.32f);
    Button button = Button.CreateTextButton(text);
    button.Enabled = enabled;
    button.Background = new SolidBrush(baseColour);
    button.OverBackground = new SolidBrush(Color.Lerp(baseColour, Color.White, 0.12f));
    button.PressedBackground = new SolidBrush(Color.Lerp(baseColour, Color.Black, 0.18f));
    button.Border = new SolidBrush(selected ? UiTheme.GoldBright : UiTheme.PanelBorderSubtle);
    button.OverBorder = new SolidBrush(UiTheme.Gold);
    button.FocusedBorder = new SolidBrush(UiTheme.GoldBright);
    button.BorderThickness = new Thickness(selected ? 2 : 1);
    button.Padding = new Thickness(5);
    button.Click += (_, _) =>
    {
      action();
      MarkEditorMyraDirty();
    };
    Place(root, button, bounds);
    return button;
  }

  private Label AddLabel(Panel root, Rectangle bounds, string text, Color? colour = null, HorizontalAlignment alignment = HorizontalAlignment.Center)
  {
    Label label = new()
    {
      Text = text,
      TextColor = colour ?? UiTheme.TextPrimary,
      HorizontalAlignment = alignment,
      VerticalAlignment = VerticalAlignment.Center,
      Wrap = true
    };
    Place(root, label, bounds);
    return label;
  }

  private TextBox AddTextBox(Panel root, Rectangle bounds, string text, string hint, Action<string> changed, bool multiline = false)
  {
    TextBox box = new()
    {
      Text = text,
      HintText = hint,
      Multiline = multiline,
      Wrap = multiline,
      TextColor = UiTheme.TextPrimary,
      HorizontalAlignment = HorizontalAlignment.Left,
      VerticalAlignment = VerticalAlignment.Top
    };
    box.TextChangedByUser += (_, _) => changed(box.Text ?? string.Empty);
    Place(root, box, bounds);
    return box;
  }

  private static void Place(Panel root, Widget widget, Rectangle bounds)
  {
    widget.Left = bounds.X;
    widget.Top = bounds.Y;
    widget.Width = Math.Max(1, bounds.Width);
    widget.Height = Math.Max(1, bounds.Height);
    widget.HorizontalAlignment = HorizontalAlignment.Left;
    widget.VerticalAlignment = VerticalAlignment.Top;
    root.Widgets.Add(widget);
  }
}
