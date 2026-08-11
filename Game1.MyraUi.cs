using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D.UI;
using MedivalChess.Campaign;
using MedivalChess.CPU;
using MedivalChess.Shared;
using System;
using System.Linq;

namespace MedivalChess;

internal sealed partial class Game1
{
  private Desktop _myraDesktop = null!;
  private Screen? _myraBuiltScreen;
  private SetupStage? _myraBuiltSetupStage;
  private bool _myraDirty = true;
  private bool _myraPresetBrowserOpen;
  private string _myraStatusSnapshot = string.Empty;

  private void InitializeMyraUi()
  {
    MyraEnvironment.Game = this;
    _myraDesktop = new Desktop();
    _myraDirty = true;
    RebuildMyraUi();
  }

  private bool UsesMyraUi(Screen screen) => screen is
    Screen.Title or Screen.OnlineLobby or Screen.OnlineJoin or Screen.OnlineWaiting or
    Screen.OnlineRoyalSelection or Screen.Settings or Screen.Setup or Screen.Pause or
    Screen.Encyclopedia or Screen.GameOver or Screen.CustomLevels or Screen.EditorDiscardConfirm;

  private void MarkMyraDirty() => _myraDirty = true;

  private void UpdateMyraUi(KeyboardState keyboard, bool wasEscapePressed)
  {
    if (_myraDesktop is null) return;

    if (_bindingToChange.HasValue)
    {
      if (wasEscapePressed)
      {
        _bindingToChange = null;
        MarkMyraDirty();
      }
      else
      {
        foreach (Keys key in keyboard.GetPressedKeys())
        {
          if (_previousKeyboardState.IsKeyDown(key)) continue;
          SetBinding(_bindingToChange.Value, key);
          _bindingToChange = null;
          MarkMyraDirty();
          break;
        }
      }
    }
    else if (wasEscapePressed)
    {
      HandleMyraEscape();
    }

    if (_myraBuiltScreen != _screen || (_screen == Screen.Setup && _myraBuiltSetupStage != _setupStage) || _myraStatusSnapshot != GetMyraStatusSnapshot())
    {
      MarkMyraDirty();
    }

    if (_myraDirty) RebuildMyraUi();
  }

  private void RenderMyraUi()
  {
    if (_myraDesktop is null) return;
    if (_myraDirty) RebuildMyraUi();
    _myraDesktop.Render();
  }

  private string GetMyraStatusSnapshot() => $"{_screen}|{_onlineStatus}|{_onlineError}|{_onlineRoyalChoicePending}|{_bindingToChange}";

  private void HandleMyraEscape()
  {
    if (_myraPresetBrowserOpen)
    {
      _myraPresetBrowserOpen = false;
      MarkMyraDirty();
      return;
    }

    switch (_screen)
    {
      case Screen.Pause: _screen = Screen.Playing; break;
      case Screen.Encyclopedia: _screen = Screen.Pause; break;
      case Screen.Settings: _screen = _settingsReturnScreen; break;
      case Screen.OnlineJoin: _screen = Screen.OnlineLobby; break;
      case Screen.OnlineLobby: _screen = Screen.Title; break;
      case Screen.OnlineWaiting:
      case Screen.OnlineRoyalSelection: ReturnToTitle(); break;
      case Screen.Setup: NavigateSetupBack(); break;
      case Screen.CustomLevels:
      case Screen.EditorDiscardConfirm: _screen = Screen.LevelEditor; break;
    }
    MarkMyraDirty();
  }

  private void RebuildMyraUi()
  {
    if (_myraDesktop is null || !UsesMyraUi(_screen)) return;

    Widget root = _screen switch
    {
      Screen.Title => BuildMyraTitle(),
      Screen.OnlineLobby => BuildMyraOnlineLobby(),
      Screen.OnlineJoin => BuildMyraOnlineJoin(),
      Screen.OnlineWaiting => BuildMyraOnlineWaiting(),
      Screen.OnlineRoyalSelection => BuildMyraOnlineRoyalSelection(),
      Screen.Settings => BuildMyraSettings(),
      Screen.Setup => BuildMyraSetup(),
      Screen.Pause => BuildMyraPause(),
      Screen.Encyclopedia => BuildMyraEncyclopedia(),
      Screen.GameOver => BuildMyraGameOver(),
      Screen.CustomLevels => BuildMyraCustomLevels(),
      Screen.EditorDiscardConfirm => BuildMyraEditorDiscardConfirmation(),
      _ => new Label { Text = string.Empty }
    };

    _myraDesktop.Root = root;
    _myraBuiltScreen = _screen;
    _myraBuiltSetupStage = _setupStage;
    _myraStatusSnapshot = GetMyraStatusSnapshot();
    _myraDirty = false;
  }

  private ScrollViewer MyraPage(string title, string? subtitle, out VerticalStackPanel content, int width = 680)
  {
    content = new VerticalStackPanel
    {
      Width = width,
      Spacing = 10,
      HorizontalAlignment = HorizontalAlignment.Center,
      VerticalAlignment = VerticalAlignment.Center
    };
    content.Widgets.Add(new Label
    {
      Text = title,
      TextColor = UiTheme.GoldBright,
      HorizontalAlignment = HorizontalAlignment.Center
    });
    if (!string.IsNullOrWhiteSpace(subtitle))
    {
      content.Widgets.Add(new Label
      {
        Text = subtitle,
        TextColor = UiTheme.TextMuted,
        HorizontalAlignment = HorizontalAlignment.Center
      });
    }
    content.Widgets.Add(new HorizontalSeparator());
    return new ScrollViewer { Content = content };
  }

  private Button MyraButton(string text, Action action, bool enabled = true, int width = 560)
  {
    Button button = Button.CreateTextButton(text);
    button.Width = width;
    button.Height = 44;
    button.Enabled = enabled;
    button.HorizontalAlignment = HorizontalAlignment.Center;
    button.Click += (_, _) =>
    {
      action();
      MarkMyraDirty();
    };
    return button;
  }

  private Label MyraInfo(string text, Color? colour = null)
  {
    return new Label
    {
      Text = text,
      TextColor = colour ?? UiTheme.TextPrimary,
      HorizontalAlignment = HorizontalAlignment.Center
    };
  }

  private HorizontalStackPanel MyraRow(params Widget[] widgets)
  {
    HorizontalStackPanel row = new()
    {
      Spacing = 8,
      HorizontalAlignment = HorizontalAlignment.Center
    };
    foreach (Widget widget in widgets) row.Widgets.Add(widget);
    return row;
  }

  private HorizontalStackPanel MyraStepper(string label, string value, Action decrease, Action increase)
  {
    Label text = new()
    {
      Text = $"{label}: {value}",
      Width = 350,
      HorizontalAlignment = HorizontalAlignment.Center,
      TextColor = UiTheme.TextPrimary
    };
    return MyraRow(
      MyraButton("-", decrease, width: 70),
      text,
      MyraButton("+", increase, width: 70)
    );
  }

  private Widget BuildMyraTitle()
  {
    ScrollViewer page = MyraPage("CROWN & SIEGE", "A medieval strategy game", out VerticalStackPanel content, 620);
    content.Widgets.Add(MyraButton("PLAY LOCAL", () => BeginMatchSetup()));
    content.Widgets.Add(MyraButton("PLAY VS CPU", () => BeginMatchSetup(cpuOpponent: true)));
    content.Widgets.Add(MyraButton("ONLINE MULTIPLAYER", () => _screen = Screen.OnlineLobby));
    content.Widgets.Add(MyraButton("CAMPAIGN LEVEL BUILDER", () =>
    {
      _levelEditor ??= new LevelEditorScreen(_ui, _spriteBatch, _pixel);
      _screen = Screen.LevelEditor;
    }));
    content.Widgets.Add(MyraButton("SETTINGS", () =>
    {
      _settingsReturnScreen = Screen.Title;
      _screen = Screen.Settings;
    }));
    content.Widgets.Add(MyraButton("QUIT GAME", Exit));
    return page;
  }

  private Widget BuildMyraOnlineLobby()
  {
    ScrollViewer page = MyraPage("ONLINE MULTIPLAYER", "Host a private room or join with a room code.", out VerticalStackPanel content);
    TextBox server = new() { Text = _onlineServerUrl, HintText = "Server URL", Width = 560, Height = 38, HorizontalAlignment = HorizontalAlignment.Center };
    content.Widgets.Add(MyraInfo("SERVER"));
    content.Widgets.Add(server);
    if (!string.IsNullOrWhiteSpace(_onlineError)) content.Widgets.Add(MyraInfo(_onlineError, UiTheme.Attack));
    content.Widgets.Add(MyraButton("HOST PRIVATE MATCH", () =>
    {
      _onlineServerUrl = server.Text?.Trim() ?? string.Empty;
      BeginMatchSetup(onlineHost: true);
    }));
    content.Widgets.Add(MyraButton("JOIN MATCH", () =>
    {
      _onlineServerUrl = server.Text?.Trim() ?? string.Empty;
      _onlineJoinCode = string.Empty;
      _screen = Screen.OnlineJoin;
    }));
    content.Widgets.Add(MyraButton("BACK", () => _screen = Screen.Title));
    return page;
  }

  private Widget BuildMyraOnlineJoin()
  {
    ScrollViewer page = MyraPage("JOIN ONLINE MATCH", "Paste the room code from the host.", out VerticalStackPanel content);
    TextBox server = new() { Text = _onlineServerUrl, HintText = "Server URL", Width = 560, Height = 38, HorizontalAlignment = HorizontalAlignment.Center };
    TextBox code = new() { Text = _onlineJoinCode, HintText = "Room code", Width = 560, Height = 44, HorizontalAlignment = HorizontalAlignment.Center };
    content.Widgets.Add(MyraInfo("SERVER"));
    content.Widgets.Add(server);
    content.Widgets.Add(MyraInfo("ROOM CODE"));
    content.Widgets.Add(code);
    if (!string.IsNullOrWhiteSpace(_onlineError)) content.Widgets.Add(MyraInfo(_onlineError, UiTheme.Attack));
    content.Widgets.Add(MyraButton("JOIN", () =>
    {
      _onlineServerUrl = server.Text?.Trim() ?? string.Empty;
      _onlineJoinCode = (code.Text ?? string.Empty).Trim().ToUpperInvariant();
      if (_onlineJoinCode.Length > 0) _ = JoinOnlineMatchAsync(_onlineJoinCode);
    }));
    content.Widgets.Add(MyraButton("BACK", () => _screen = Screen.OnlineLobby));
    return page;
  }

  private Widget BuildMyraOnlineWaiting()
  {
    string code = string.IsNullOrWhiteSpace(_onlineClient?.JoinCode) ? "CONNECTING" : _onlineClient.JoinCode;
    string team = _onlineClient?.Team?.ToString() ?? "UNASSIGNED";
    ScrollViewer page = MyraPage("WAITING FOR PLAYERS", _onlineStatus, out VerticalStackPanel content);
    content.Widgets.Add(MyraInfo($"ROOM CODE: {code}", UiTheme.GoldBright));
    content.Widgets.Add(MyraInfo($"YOUR TEAM: {team}"));
    if (!string.IsNullOrWhiteSpace(_onlineError)) content.Widgets.Add(MyraInfo(_onlineError, UiTheme.Attack));
    content.Widgets.Add(MyraButton("CANCEL", ReturnToTitle));
    return page;
  }

  private Widget BuildMyraOnlineRoyalSelection()
  {
    PieceDefinition royal = PieceDefinitions.Royals[_selectedRoyalIndex];
    ScrollViewer page = MyraPage(_onlineRoyalChoicePending ? "ROYAL CHOSEN" : "CHOOSE YOUR ROYAL", _onlineStatus, out VerticalStackPanel content);
    AddRoyalSummary(content, royal);
    content.Widgets.Add(MyraRow(
      MyraButton("<", () => _selectedRoyalIndex = (_selectedRoyalIndex - 1 + PieceDefinitions.Royals.Length) % PieceDefinitions.Royals.Length, !_onlineRoyalChoicePending, 80),
      MyraButton(">", () => _selectedRoyalIndex = (_selectedRoyalIndex + 1) % PieceDefinitions.Royals.Length, !_onlineRoyalChoicePending, 80)
    ));
    if (IsDebugOnlineMatch) content.Widgets.Add(MyraButton(GetDebugTeamSwitchLabel(), () => _ = SwitchDebugTeamAsync()));
    content.Widgets.Add(MyraButton(_onlineRoyalChoicePending ? "WAITING..." : "CONFIRM ROYAL", BeginOnlineRoyalPlacement, !_onlineRoyalChoicePending));
    content.Widgets.Add(MyraButton("BACK", ReturnToTitle));
    return page;
  }

  private Widget BuildMyraSettings()
  {
    ScrollViewer page = MyraPage("SETTINGS", "Click a control, then press the new key.", out VerticalStackPanel content, 760);
    foreach (BindingAction action in Enum.GetValues<BindingAction>())
    {
      string value = _bindingToChange == action ? "PRESS A KEY..." : GetBinding(action).ToString();
      content.Widgets.Add(MyraButton($"{GetBindingLabel(action)}    [{value}]", () => _bindingToChange = action, width: 650));
    }
    content.Widgets.Add(new HorizontalSeparator());
    content.Widgets.Add(MyraStepper("HUD SCALE", $"{MathF.Round(_uiScale * 100f):0}%", () => AdjustUiScale(-1), () => AdjustUiScale(1)));
    content.Widgets.Add(MyraButton(_zoomTowardsMouse ? "ZOOM ANCHOR: MOUSE" : "ZOOM ANCHOR: CAMERA CENTRE", () => _zoomTowardsMouse = !_zoomTowardsMouse));
    content.Widgets.Add(MyraButton($"FRAME CAP: {GetFpsCapLabel()}", CycleFpsCap));
    content.Widgets.Add(MyraButton($"RESOLUTION: {GetResolutionLabel()}", CycleResolution));
    content.Widgets.Add(MyraButton(_rotateBoard ? "BOARD ROTATION: 90 DEG" : "BOARD ROTATION: 0 DEG", () => _rotateBoard = !_rotateBoard));
    content.Widgets.Add(MyraButton(_settingsReturnScreen == Screen.Pause ? "BACK TO PAUSE" : "BACK", () => _screen = _settingsReturnScreen));
    return page;
  }

  private Widget BuildMyraPause()
  {
    ScrollViewer page = MyraPage("PAUSED", "Match controls", out VerticalStackPanel content, 600);
    content.Widgets.Add(MyraButton("RESUME", () => _screen = Screen.Playing));
    content.Widgets.Add(MyraButton("SETTINGS", () =>
    {
      _settingsReturnScreen = Screen.Pause;
      _screen = Screen.Settings;
    }));
    content.Widgets.Add(MyraButton("ENCYCLOPAEDIA", () => _screen = Screen.Encyclopedia));
    content.Widgets.Add(MyraButton("RETURN TO TITLE", ReturnToTitle));
    return page;
  }

  private Widget BuildMyraEncyclopedia()
  {
    PieceDefinition piece = PieceDefinitions.Encyclopedia[_encyclopediaIndex];
    ScrollViewer page = MyraPage($"ENCYCLOPAEDIA — {piece.DisplayName.ToUpperInvariant()}", $"{_encyclopediaIndex + 1} / {PieceDefinitions.Encyclopedia.Length}", out VerticalStackPanel content, 760);
    content.Widgets.Add(MyraInfo($"TYPE: {piece.Category}"));
    content.Widgets.Add(MyraInfo($"MOVE: {piece.Movement.range} {piece.Movement.shape}    ATTACK: {piece.Attack}    HEALTH: {piece.Health}"));
    content.Widgets.Add(MyraInfo($"SIZE: {piece.Size.x} x {piece.Size.y}    RANGE: {piece.AttackRange.Minimum}-{piece.AttackRange.Maximum} {piece.AttackPattern}"));
    content.Widgets.Add(MyraInfo($"COST: {piece.Cost}"));
    if (!string.IsNullOrWhiteSpace(piece.AbilityDescription)) content.Widgets.Add(MyraInfo(piece.AbilityDescription, UiTheme.GoldBright));
    content.Widgets.Add(MyraRow(
      MyraButton("< PREVIOUS", () => _encyclopediaIndex = (_encyclopediaIndex - 1 + PieceDefinitions.Encyclopedia.Length) % PieceDefinitions.Encyclopedia.Length, width: 210),
      MyraButton("NEXT >", () => _encyclopediaIndex = (_encyclopediaIndex + 1) % PieceDefinitions.Encyclopedia.Length, width: 210)
    ));
    content.Widgets.Add(MyraButton("BACK", () => _screen = Screen.Pause));
    return page;
  }

  private Widget BuildMyraGameOver()
  {
    string winner = _winningTeam.HasValue ? $"{UiText.GetTeamDisplayName(_winningTeam.Value)} WINS" : "MATCH OVER";
    ScrollViewer page = MyraPage(winner, "The battle is over.", out VerticalStackPanel content, 600);
    content.Widgets.Add(MyraButton(_campaignTestPlay ? "RETURN TO LEVEL EDITOR" : "RETURN TO TITLE", () =>
    {
      if (_campaignTestPlay) ReturnToEditorFromTestPlay();
      else ReturnToTitle();
    }));
    content.Widgets.Add(MyraButton("QUIT GAME", Exit));
    return page;
  }

  private Widget BuildMyraCustomLevels()
  {
    ScrollViewer page = MyraPage("CUSTOM CAMPAIGN LEVELS", "Open one of your locally saved levels.", out VerticalStackPanel content, 760);
    if (_customLevels.Count == 0)
    {
      content.Widgets.Add(MyraInfo($"No local levels yet. Save one to {CampaignLevelSerializer.LocalLevelDirectory}", UiTheme.TextMuted));
    }
    foreach (CustomLevelSummary summary in _customLevels.Take(12))
    {
      string validity = summary.IsValid ? "" : "  [INVALID]";
      content.Widgets.Add(MyraButton($"{summary.Name} — {summary.Author}{validity}", () =>
      {
        CampaignLevelLoadResult result = CampaignLevelSerializer.Load(summary.Path);
        if (!result.IsSuccess || result.Level is null) return;
        _levelEditor.ReplaceState(new LevelEditorState(result.Level, summary.Path));
        _screen = Screen.LevelEditor;
      }, summary.IsValid, 680));
    }
    content.Widgets.Add(MyraButton("BACK TO EDITOR", () => _screen = Screen.LevelEditor));
    return page;
  }

  private Widget BuildMyraEditorDiscardConfirmation()
  {
    string action = _editorConfirmAction == EditorConfirmAction.New ? "start a new level" : "leave the editor";
    ScrollViewer page = MyraPage("UNSAVED CHANGES", $"Discard your changes and {action}?", out VerticalStackPanel content, 620);
    content.Widgets.Add(MyraButton("KEEP EDITING", () => _screen = Screen.LevelEditor));
    content.Widgets.Add(MyraButton("DISCARD CHANGES", () =>
    {
      if (_editorConfirmAction == EditorConfirmAction.New)
      {
        _levelEditor.ReplaceState(LevelEditorState.CreateNew());
        _screen = Screen.LevelEditor;
      }
      else
      {
        _screen = Screen.Title;
      }
    }));
    return page;
  }

  private Widget BuildMyraSetup()
  {
    if (_myraPresetBrowserOpen) return BuildMyraTerrainPresetBrowser();
    return _setupStage switch
    {
      SetupStage.Mode => BuildMyraModeSetup(),
      SetupStage.Battlefield => BuildMyraBattlefieldSetup(),
      SetupStage.Economy => BuildMyraEconomySetup(),
      SetupStage.ModeSettings => BuildMyraModeSettingsSetup(),
      _ => BuildMyraRoyalSetup()
    };
  }

  private void AddSetupProgress(VerticalStackPanel content)
  {
    string[] names = ["MODE", "MAP", "ECONOMY", "RULES", "ROYAL"];
    content.Widgets.Add(MyraInfo(string.Join("  ›  ", names.Select((name, index) => index == (int)_setupStage ? $"[{name}]" : name)), UiTheme.TextMuted));
  }

  private Widget BuildMyraModeSetup()
  {
    ScrollViewer page = MyraPage("MATCH SETUP", _onlineHostingSetup ? "Configure the private room." : "Choose how this battle is won.", out VerticalStackPanel content, 760);
    AddSetupProgress(content);
    foreach (GameMode mode in Enum.GetValues<GameMode>())
    {
      string marker = mode == _gameMode ? "✓ " : string.Empty;
      content.Widgets.Add(MyraButton(marker + mode.ToString().ToUpperInvariant(), () => _gameMode = mode, width: 620));
    }
    content.Widgets.Add(MyraInfo(GetMyraModeDescription(_gameMode), UiTheme.TextMuted));
    content.Widgets.Add(MyraStepper("PLAYERS", _playerCount.ToString(), () =>
    {
      SetPlayerCount(_playerCount - 1);
      if (_cpuOpponentSetup) ConfigureCpuOpponents();
    }, () =>
    {
      SetPlayerCount(_playerCount + 1);
      if (_cpuOpponentSetup) ConfigureCpuOpponents();
    }));
    if (_cpuOpponentSetup)
    {
      content.Widgets.Add(MyraButton($"CPU DIFFICULTY: {_selectedCpuDifficulty}", CycleMyraCpuDifficulty));
      content.Widgets.Add(MyraButton($"CPU STYLE: {GetCpuPersonalityName(_selectedCpuPersonality)}", CycleMyraCpuPersonality));
    }
    content.Widgets.Add(MyraRow(
      MyraButton("BACK", NavigateSetupBack, width: 200),
      MyraButton("CONTINUE", () => _setupStage = SetupStage.Battlefield, width: 300)
    ));
    return page;
  }

  private string GetMyraModeDescription(GameMode mode) => mode switch
  {
    GameMode.Conquest => $"Hold the centre objective and push control to {_conquestWinScore}.",
    GameMode.Escort => $"Get your royal to the enemy edge. Royals start at {_escortRoyalHealthPercent}% health.",
    GameMode.Dominion => $"Hold control points to score. First to {_dominionWinScore} wins.",
    GameMode.Plunder => $"Carry the central treasure home. Deliveries score {_plunderDeliveryScore}; first to {_plunderWinScore} wins.",
    _ => "Destroy the opposing royal to win."
  };

  private void CycleMyraCpuDifficulty()
  {
    CpuDifficultyLevel[] values = [CpuDifficultyLevel.Easy, CpuDifficultyLevel.Medium, CpuDifficultyLevel.Hard, CpuDifficultyLevel.Best];
    int index = Array.IndexOf(values, _selectedCpuDifficulty);
    _selectedCpuDifficulty = values[(Math.Max(0, index) + 1) % values.Length];
    ConfigureCpuOpponents();
  }

  private void CycleMyraCpuPersonality()
  {
    CpuPersonality[] values = [CpuPersonality.Balanced, CpuPersonality.Aggressive, CpuPersonality.Defensive, CpuPersonality.Greedy, CpuPersonality.Reckless, CpuPersonality.ObjectiveFocused, CpuPersonality.Swarmer];
    int index = Array.FindIndex(values, value => ReferenceEquals(value, _selectedCpuPersonality));
    _selectedCpuPersonality = values[(Math.Max(0, index) + 1) % values.Length];
    ConfigureCpuOpponents();
  }

  private Widget BuildMyraBattlefieldSetup()
  {
    ScrollViewer page = MyraPage("BATTLEFIELD", "Choose board size and terrain before economy settings.", out VerticalStackPanel content, 760);
    AddSetupProgress(content);
    content.Widgets.Add(MyraStepper("BOARD", _selectedBoardSize.ToString(), () => ChangeMyraBoardSize(-1), () => ChangeMyraBoardSize(1)));
    content.Widgets.Add(MyraStepper("TERRAIN SOURCE", _terrainSource.ToString(), () => ChangeMyraTerrainSource(-1), () => ChangeMyraTerrainSource(1)));
    content.Widgets.Add(MyraStepper("FORESTS", _forestDensity.ToString(), () => ChangeMyraForestDensity(-1), () => ChangeMyraForestDensity(1)));
    content.Widgets.Add(MyraStepper("WATERWAYS", _waterwayDensity.ToString(), () => ChangeMyraWaterDensity(-1), () => ChangeMyraWaterDensity(1)));
    if (!string.IsNullOrWhiteSpace(_selectedTerrainPresetId)) content.Widgets.Add(MyraInfo($"PRESET: {_selectedTerrainPresetName ?? _selectedTerrainPresetId}", UiTheme.GoldBright));
    content.Widgets.Add(MyraButton($"BROWSE {_selectedBoardSize.ToString().ToUpperInvariant()} PRESETS", () =>
    {
      OpenTerrainPresetBrowser();
      _terrainPresetBrowserOpen = false;
      _myraPresetBrowserOpen = true;
    }));
    content.Widgets.Add(MyraRow(
      MyraButton("BACK", NavigateSetupBack, width: 200),
      MyraButton("CONTINUE", () =>
      {
        ApplyBattlefieldSetup();
        _setupStage = SetupStage.Economy;
      }, width: 300)
    ));
    return page;
  }

  private Widget BuildMyraTerrainPresetBrowser()
  {
    ScrollViewer page = MyraPage($"{_selectedBoardSize.ToString().ToUpperInvariant()} TERRAIN PRESETS", "Select an authored terrain layout.", out VerticalStackPanel content, 780);
    if (_terrainPresetBrowserPresets.Count == 0) content.Widgets.Add(MyraInfo("No authored maps are available for this board size.", UiTheme.TextMuted));
    foreach (BattlefieldTerrainPreset preset in _terrainPresetBrowserPresets)
    {
      string selected = string.Equals(preset.Id, _selectedTerrainPresetId, StringComparison.Ordinal) ? "✓ " : string.Empty;
      content.Widgets.Add(MyraButton($"{selected}{preset.Name} — FOREST {preset.ForestDensity}, WATER {preset.WaterwayDensity}", () =>
      {
        SelectTerrainPreset(preset);
        _myraPresetBrowserOpen = false;
      }, width: 700));
    }
    content.Widgets.Add(MyraButton("BACK", () => _myraPresetBrowserOpen = false));
    return page;
  }

  private void ChangeMyraBoardSize(int delta)
  {
    BoardSize next = (BoardSize)Math.Clamp((int)_selectedBoardSize + delta, (int)BoardSize.Small, (int)BoardSize.Large);
    if (next == _selectedBoardSize) return;
    _selectedBoardSize = next;
    ClearTerrainPresetSelection();
  }

  private void ChangeMyraTerrainSource(int delta) => _terrainSource = (TerrainSource)Math.Clamp((int)_terrainSource + delta, (int)TerrainSource.Preset, (int)TerrainSource.None);
  private void ChangeMyraForestDensity(int delta)
  {
    TerrainDensity next = (TerrainDensity)Math.Clamp((int)_forestDensity + delta, (int)TerrainDensity.Light, (int)TerrainDensity.Heavy);
    if (next == _forestDensity) return;
    _forestDensity = next;
    ClearTerrainPresetSelection();
  }
  private void ChangeMyraWaterDensity(int delta)
  {
    TerrainDensity next = (TerrainDensity)Math.Clamp((int)_waterwayDensity + delta, (int)TerrainDensity.Light, (int)TerrainDensity.Heavy);
    if (next == _waterwayDensity) return;
    _waterwayDensity = next;
    ClearTerrainPresetSelection();
  }

  private Widget BuildMyraEconomySetup()
  {
    ScrollViewer page = MyraPage("MATCH ECONOMY", "Tune the opening economy. Values are explicit and reversible.", out VerticalStackPanel content, 780);
    AddSetupProgress(content);
    content.Widgets.Add(MyraStepper("STARTING GOLD", _startingCash.ToString(), () => _startingCash = Math.Max(0, AdjustInteger(_startingCash, -15)), () => _startingCash = AdjustInteger(_startingCash, 15)));
    content.Widgets.Add(MyraStepper("KILLER REFUND", $"{TruncateRefundMultiplier(_killerRefundMultiplier):0.##}x", () => _killerRefundMultiplier = AdjustRefundMultiplier(_killerRefundMultiplier, -0.1f), () => _killerRefundMultiplier = AdjustRefundMultiplier(_killerRefundMultiplier, 0.1f)));
    content.Widgets.Add(MyraStepper("DEFEATED TEAM REFUND", $"{TruncateRefundMultiplier(_defeatedTeamRefundMultiplier):0.##}x", () => _defeatedTeamRefundMultiplier = AdjustRefundMultiplier(_defeatedTeamRefundMultiplier, -0.1f), () => _defeatedTeamRefundMultiplier = AdjustRefundMultiplier(_defeatedTeamRefundMultiplier, 0.1f)));
    content.Widgets.Add(MyraStepper("BUYS PER BUY TURN", _initialBuysPerTurn.ToString(), () => _initialBuysPerTurn = Math.Max(1, _initialBuysPerTurn - 1), () => _initialBuysPerTurn++));
    content.Widgets.Add(MyraStepper("BUY TURNS PER TEAM", _initialBuyTurnsPerTeam.ToString(), () => _initialBuyTurnsPerTeam = Math.Max(1, _initialBuyTurnsPerTeam - 1), () => _initialBuyTurnsPerTeam++));
    content.Widgets.Add(MyraButton(_farmsEnabled ? "OPENING FARMS: ON" : "OPENING FARMS: OFF", () =>
    {
      _farmsEnabled = !_farmsEnabled;
      EnsurePurchaseSelectionIsValid();
    }));
    content.Widgets.Add(MyraStepper("FARM INCOME", _farmIncomePerTurn.ToString(), () => _farmIncomePerTurn = AdjustInteger(_farmIncomePerTurn, -1), () => _farmIncomePerTurn = AdjustInteger(_farmIncomePerTurn, 1)));
    content.Widgets.Add(MyraStepper("UNIT PRICE", $"{_unitPricePercent}%", () => _unitPricePercent = AdjustInteger(_unitPricePercent, -10), () => _unitPricePercent = AdjustInteger(_unitPricePercent, 10)));
    content.Widgets.Add(MyraButton(_interestEnabled ? "INTEREST: ON" : "INTEREST: OFF", () => _interestEnabled = !_interestEnabled));
    content.Widgets.Add(MyraStepper("INTEREST RATE", $"{_interestPercent}%", () => _interestPercent = Math.Max(-100, _interestPercent - 5), () => _interestPercent = Math.Min(200, _interestPercent + 5)));
    content.Widgets.Add(MyraButton("RESET ECONOMY", ResetMatchConfigurationValues));
    content.Widgets.Add(MyraRow(
      MyraButton("BACK", NavigateSetupBack, width: 200),
      MyraButton("CONTINUE", () => _setupStage = SetupStage.ModeSettings, width: 300)
    ));
    return page;
  }

  private Widget BuildMyraModeSettingsSetup()
  {
    ScrollViewer page = MyraPage($"{_gameMode.ToString().ToUpperInvariant()} RULES", GetMyraModeDescription(_gameMode), out VerticalStackPanel content, 780);
    AddSetupProgress(content);
    switch (_gameMode)
    {
      case GameMode.Conquest:
        content.Widgets.Add(MyraStepper("CONTROL TO WIN", _conquestWinScore.ToString(), () => _conquestWinScore = Math.Max(1, _conquestWinScore - 1), () => _conquestWinScore++));
        break;
      case GameMode.Escort:
        content.Widgets.Add(MyraStepper("ROYAL STARTING HEALTH", $"{_escortRoyalHealthPercent}%", () => _escortRoyalHealthPercent = Math.Max(1, _escortRoyalHealthPercent - 5), () => _escortRoyalHealthPercent = Math.Min(100, _escortRoyalHealthPercent + 5)));
        break;
      case GameMode.Dominion:
        content.Widgets.Add(MyraStepper("SCORE TO WIN", _dominionWinScore.ToString(), () => _dominionWinScore = Math.Max(1, _dominionWinScore - 1), () => _dominionWinScore++));
        break;
      case GameMode.Plunder:
        content.Widgets.Add(MyraStepper("SCORE TO WIN", _plunderWinScore.ToString(), () => _plunderWinScore = Math.Max(1, _plunderWinScore - 1), () => _plunderWinScore++));
        content.Widgets.Add(MyraStepper("POINTS PER DELIVERY", _plunderDeliveryScore.ToString(), () => _plunderDeliveryScore = Math.Max(1, _plunderDeliveryScore - 1), () => _plunderDeliveryScore++));
        content.Widgets.Add(MyraStepper("ROYAL KILL PENALTY", _plunderRoyalKillPenalty.ToString(), () => _plunderRoyalKillPenalty = Math.Max(0, _plunderRoyalKillPenalty - 1), () => _plunderRoyalKillPenalty++));
        break;
    }
    content.Widgets.Add(new HorizontalSeparator());
    content.Widgets.Add(MyraButton(_chessTimerEnabled ? "CHESS CLOCK: ON" : "CHESS CLOCK: OFF", () => _chessTimerEnabled = !_chessTimerEnabled));
    content.Widgets.Add(MyraStepper("MINUTES", _chessTimerMinutes.ToString(), () => _chessTimerMinutes = Math.Max(0, _chessTimerMinutes - 1), () => _chessTimerMinutes = Math.Min(180, _chessTimerMinutes + 1)));
    content.Widgets.Add(MyraStepper("SECONDS", _chessTimerSeconds.ToString(), () => _chessTimerSeconds = Math.Max(0, _chessTimerSeconds - 1), () => _chessTimerSeconds = Math.Min(59, _chessTimerSeconds + 1)));
    content.Widgets.Add(MyraStepper("INCREMENT", $"{_chessTimerIncrementSeconds}s", () => _chessTimerIncrementSeconds = Math.Max(0, _chessTimerIncrementSeconds - 1), () => _chessTimerIncrementSeconds = Math.Min(120, _chessTimerIncrementSeconds + 1)));
    content.Widgets.Add(MyraRow(
      MyraButton("BACK", NavigateSetupBack, width: 200),
      MyraButton(_onlineHostingSetup ? "HOST ROOM" : "CONTINUE", ContinueMyraModeSettings, width: 300)
    ));
    return page;
  }

  private void ContinueMyraModeSettings()
  {
    if (_chessTimerEnabled && _chessTimerMinutes == 0 && _chessTimerSeconds == 0) _chessTimerSeconds = 1;
    foreach (Player.Team team in _teams)
    {
      team.Money = _startingCash;
      team.ActionPoints = team.ActionLimit;
    }
    if (_onlineHostingSetup)
    {
      _onlineMatchConfiguration = BuildOnlineMatchConfiguration();
      PrepareOnlineRoom();
      _onlineHostingSetup = false;
      _ = HostOnlineMatchAsync(_onlineMatchConfiguration);
    }
    else
    {
      _setupStage = SetupStage.RoyalSelection;
    }
  }

  private Widget BuildMyraRoyalSetup()
  {
    PieceDefinition royal = PieceDefinitions.Royals[_selectedRoyalIndex];
    ScrollViewer page = MyraPage($"{UiText.GetTeamDisplayName(_setupTeam)} — CHOOSE YOUR ROYAL", "Confirm a royal, then place it on your territory.", out VerticalStackPanel content, 760);
    AddSetupProgress(content);
    AddRoyalSummary(content, royal);
    content.Widgets.Add(MyraRow(
      MyraButton("<", () => _selectedRoyalIndex = GetNextSelectableRoyalIndex(_selectedRoyalIndex, -1), width: 80),
      MyraButton(">", () => _selectedRoyalIndex = GetNextSelectableRoyalIndex(_selectedRoyalIndex, 1), width: 80)
    ));
    content.Widgets.Add(MyraRow(
      MyraButton("BACK", NavigateSetupBack, width: 200),
      MyraButton("CONFIRM", () =>
      {
        PieceDefinition selected = PieceDefinitions.Royals[_selectedRoyalIndex];
        if (_gameMode == GameMode.Escort && selected.Type == PieceType.Palace)
        {
          _selectedRoyalIndex = GetNextSelectableRoyalIndex(_selectedRoyalIndex, 1);
          return;
        }
        BeginRoyalPlacement(selected);
      }, width: 300)
    ));
    return page;
  }

  private void AddRoyalSummary(VerticalStackPanel content, PieceDefinition royal)
  {
    content.Widgets.Add(MyraInfo(royal.DisplayName.ToUpperInvariant(), UiTheme.GoldBright));
    content.Widgets.Add(MyraInfo($"HEALTH {royal.Health}    ATTACK {royal.Attack}    SIZE {royal.Size.x} x {royal.Size.y}"));
    content.Widgets.Add(MyraInfo($"MOVE {royal.Movement.range} {royal.Movement.shape}    RANGE {royal.AttackRange.Minimum}-{royal.AttackRange.Maximum} {royal.AttackPattern}"));
    if (!string.IsNullOrWhiteSpace(royal.AbilityDescription)) content.Widgets.Add(MyraInfo(royal.AbilityDescription, UiTheme.TextMuted));
  }
}
