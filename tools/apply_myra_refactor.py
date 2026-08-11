from pathlib import Path

path = Path("Game1.cs")
text = path.read_text(encoding="utf-8-sig")
original = text


def replace_once(old: str, new: str) -> None:
    global text
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one integration point, found {count}: {old[:80]!r}")
    text = text.replace(old, new, 1)


replace_once(
    "internal sealed class Game1 : Game",
    "internal sealed partial class Game1 : Game",
)

replace_once(
    "    _levelEditor = new LevelEditorScreen(_ui, _spriteBatch, _pixel);\n  }",
    "    _levelEditor = new LevelEditorScreen(_ui, _spriteBatch, _pixel);\n    InitializeMyraUi();\n  }",
)

replace_once(
    """    if (_screen == Screen.CustomLevels)\n    {\n      UpdateCustomLevels(mouse, wasLeftClick, wasEscapePressed);\n      _previousMouseState = mouse;\n      _previousKeyboardState = keyboard;\n      base.Update(gameTime);\n      return;\n    }\n\n    if (_screen == Screen.EditorDiscardConfirm)\n    {\n      UpdateEditorDiscardConfirmation(mouse, wasLeftClick, wasEscapePressed);\n      _previousMouseState = mouse;\n      _previousKeyboardState = keyboard;\n      base.Update(gameTime);\n      return;\n    }""",
    """    if (_screen is Screen.CustomLevels or Screen.EditorDiscardConfirm)\n    {\n      UpdateMyraUi(keyboard, wasEscapePressed);\n      _previousMouseState = mouse;\n      _previousKeyboardState = keyboard;\n      base.Update(gameTime);\n      return;\n    }""",
)

replace_once(
    """    if (_screen != Screen.Playing)\n    {\n      UpdateMenu(keyboard, mouse, wasLeftClick, wasEscapePressed);\n      _previousMouseState = mouse;\n      _previousKeyboardState = keyboard;\n      base.Update(gameTime);\n      return;\n    }""",
    """    if (_screen != Screen.Playing)\n    {\n      if (UsesMyraUi(_screen))\n      {\n        UpdateMyraUi(keyboard, wasEscapePressed);\n      }\n      else\n      {\n        UpdateMenu(keyboard, mouse, wasLeftClick, wasEscapePressed);\n      }\n      _previousMouseState = mouse;\n      _previousKeyboardState = keyboard;\n      base.Update(gameTime);\n      return;\n    }""",
)

replace_once(
    """    bool clickedPurchasePanel =\n      _royalAwaitingPlacement is null && wasLeftClick && HandlePurchasePanelClick(ToUiPoint(mouse.Position));\n    bool clickedInitialBuyStop =\n      wasLeftClick && HandleInitialBuyStopClick(ToUiPoint(mouse.Position));\n    bool clickedSkipTurn =\n      wasLeftClick && HandleSkipTurnClick(ToUiPoint(mouse.Position));\n    bool clickedDebugTeamSwitch =\n      wasLeftClick && HandleDebugTeamSwitchClick(ToUiPoint(mouse.Position));\n    bool clickedEngineerPanel =\n      wasLeftClick && HandleEngineerAbilityClick(ToUiPoint(mouse.Position));\n    bool clickedOxCarryPanel =\n      wasLeftClick && HandleOxCarryPanelClick(ToUiPoint(mouse.Position));\n    bool clickedMercenaryPanel =\n      wasLeftClick && HandleMercenaryPanelClick(ToUiPoint(mouse.Position));\n\n    if (!planningInput && !clickedPurchasePanel && !clickedInitialBuyStop && !clickedSkipTurn && !clickedDebugTeamSwitch && !clickedEngineerPanel && !clickedOxCarryPanel && !clickedMercenaryPanel && (wasLeftClick || wasRightClick))""",
    """    bool clickedMyraHud =\n      (wasLeftClick || wasRightClick) && IsPointerOverMyraPlayingHud(ToUiPoint(mouse.Position));\n\n    if (!planningInput && !clickedMyraHud && (wasLeftClick || wasRightClick))""",
)

replace_once(
    """    if (!drawsGameView)\n    {\n      _spriteBatch.Begin(transformMatrix: Matrix.CreateScale(_uiScale));\n      DrawMenuScreen();\n      _spriteBatch.End();\n      base.Draw(gameTime);\n      return;\n    }""",
    """    if (!drawsGameView)\n    {\n      if (UsesMyraUi(_screen))\n      {\n        RenderMyraUi();\n      }\n      else\n      {\n        _spriteBatch.Begin(transformMatrix: Matrix.CreateScale(_uiScale));\n        DrawMenuScreen();\n        _spriteBatch.End();\n      }\n      base.Draw(gameTime);\n      return;\n    }""",
)

replace_once(
    """    if (_screen == Screen.Playing)\n    {\n      DrawStatusPanel();""",
    """    if (_screen == Screen.Playing && !UsesMyraUi(_screen))\n    {\n      DrawStatusPanel();""",
)

replace_once(
    """    else\n    {\n      Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);\n      _spriteBatch.Draw(_pixel, viewport, new Color(5, 9, 14, 176));\n      DrawMenuScreen();\n    }\n\n    _spriteBatch.End();\n\n    base.Draw(gameTime);""",
    """    else\n    {\n      Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);\n      _spriteBatch.Draw(_pixel, viewport, new Color(5, 9, 14, 176));\n      if (!UsesMyraUi(_screen)) DrawMenuScreen();\n    }\n\n    _spriteBatch.End();\n\n    if (UsesMyraUi(_screen)) RenderMyraUi();\n\n    base.Draw(gameTime);""",
)

if text != original:
    path.write_text(text, encoding="utf-8")
    print("Applied Myra integration to Game1.cs")
else:
    print("Game1.cs already integrated")

myra_path = Path("Game1.MyraUi.cs")
myra_text = myra_path.read_text(encoding="utf-8")
myra_original = myra_text

if "using Myra.Graphics2D;\n" not in myra_text:
    myra_text = myra_text.replace("using Myra;\n", "using Myra;\nusing Myra.Graphics2D;\n", 1)

myra_text = myra_text.replace("    _myraDesktop.UpdateInput();\n", "")

myra_text = myra_text.replace(
    "Screen.OnlineRoyalSelection or Screen.Settings or Screen.Setup or Screen.Pause or",
    "Screen.OnlineRoyalSelection or Screen.Settings or Screen.Setup or Screen.Playing or Screen.Pause or",
    1,
)

myra_text = myra_text.replace(
    "      Screen.Setup => BuildMyraSetup(),\n      Screen.Pause => BuildMyraPause(),",
    "      Screen.Setup => BuildMyraSetup(),\n      Screen.Playing => BuildMyraPlayingHud(),\n      Screen.Pause => BuildMyraPause(),",
    1,
)

old_render = """  private void RenderMyraUi()\n  {\n    if (_myraDesktop is null) return;\n    if (_myraDirty) RebuildMyraUi();\n    _myraDesktop.Render();\n  }\n"""
new_render = """  private void RenderMyraUi()\n  {\n    if (_myraDesktop is null) return;\n    if (_myraBuiltScreen != _screen ||\n        (_screen == Screen.Setup && _myraBuiltSetupStage != _setupStage) ||\n        _myraStatusSnapshot != GetMyraStatusSnapshot())\n    {\n      MarkMyraDirty();\n    }\n    if (_myraDirty) RebuildMyraUi();\n    _myraDesktop.Render();\n  }\n"""
if old_render in myra_text:
    myra_text = myra_text.replace(old_render, new_render, 1)
elif new_render not in myra_text:
    raise RuntimeError("Could not find Myra render integration point")

old_snapshot = """  private string GetMyraStatusSnapshot() => $\"{_screen}|{_onlineStatus}|{_onlineError}|{_onlineRoyalChoicePending}|{_bindingToChange}\";\n"""
new_snapshot = """  private string GetMyraStatusSnapshot()\n  {\n    string common = $\"{_screen}|{_onlineStatus}|{_onlineError}|{_onlineRoyalChoicePending}|{_bindingToChange}\";\n    if (_screen != Screen.Playing) return common;\n\n    Team? currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);\n    string selected = selectedPiece is null\n      ? \"none\"\n      : $\"{selectedPiece.NetworkId}:{selectedPiece.CurrentHealth}:{selectedPiece.Team}:{selectedPiece.HasMovedThisTurn}:{selectedPiece.HasAttackedThisTurn}\";\n    string initialBuy = _initialBuyPhase is null\n      ? \"none\"\n      : $\"{_initialBuyPhase.CurrentTeam}:{_initialBuyPhase.PurchasesThisTurn}:{_initialBuyPhase.PurchasesPerTurn}:{_initialBuyPhase.IsFarmPlacementPhase}:{_initialBuyPhase.CanStopCurrentBuyer}\";\n    string clocks = _chessTimerEnabled\n      ? string.Join(\",\", Team.ActiveTeams.Select(team => $\"{team}:{FormatClock(team)}\"))\n      : \"off\";\n\n    return $\"{common}|turn:{Team.CurrentTurn}|money:{currentTeam?.Money}|actions:{currentTeam?.ActionPoints}|selected:{selected}|buy:{_isPurchaseMode}:{_selectedPurchaseIndex}:{_isPurchaseUnitListExpanded}|initial:{initialBuy}|engineer:{_selectedEngineerAbility}|clock:{clocks}|mode:{GetMyraModeScoreText()}|royal:{_royalAwaitingPlacement?.Identifier}|debug:{_debugTeamSwitchPending}\";\n  }\n"""
if old_snapshot in myra_text:
    myra_text = myra_text.replace(old_snapshot, new_snapshot, 1)
elif new_snapshot not in myra_text:
    raise RuntimeError("Could not find Myra snapshot integration point")

if "private Widget BuildMyraPlayingHud()" not in myra_text:
    hud_block = r'''

  private Widget BuildMyraPlayingHud()
  {
    Grid root = new()
    {
      HorizontalAlignment = HorizontalAlignment.Stretch,
      VerticalAlignment = VerticalAlignment.Stretch
    };

    int maximumPanelHeight = Math.Max(320, GraphicsDevice.Viewport.Height - 32);

    VerticalStackPanel left = new()
    {
      Width = 336,
      Spacing = 8,
      HorizontalAlignment = HorizontalAlignment.Center
    };
    Team? currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);
    Color turnColour = UiTheme.GetTeamColour(Team.CurrentTurn);
    left.Widgets.Add(MyraInfo($"{UiText.GetTeamDisplayName(Team.CurrentTurn).ToUpperInvariant()} TURN", turnColour));
    if (currentTeam is not null)
    {
      left.Widgets.Add(MyraInfo($"GOLD  {currentTeam.Money}"));
      if (Globals.ActionLimitsEnabled)
      {
        left.Widgets.Add(MyraInfo($"ACTIONS  {currentTeam.ActionPoints}/{currentTeam.ActionLimit}"));
      }
    }
    left.Widgets.Add(MyraInfo(GetMyraModeScoreText(), UiTheme.GoldBright));

    if (_initialBuyPhase is not null)
    {
      if (_initialBuyPhase.IsFarmPlacementPhase)
      {
        left.Widgets.Add(MyraInfo($"OPENING FARMS  {_initialBuyPhase.GetFarmsPlaced(Team.CurrentTurn)}/2", UiTheme.TextMuted));
        left.Widgets.Add(MyraInfo("Select Farm, then click your territory to place it.", UiTheme.TextMuted));
      }
      else
      {
        left.Widgets.Add(MyraInfo($"BUY TURN  {_initialBuyPhase.PurchasesThisTurn}/{_initialBuyPhase.PurchasesPerTurn}", UiTheme.TextMuted));
        left.Widgets.Add(MyraButton(
          "STOP BUYING",
          StopMyraInitialBuying,
          _initialBuyPhase.CanStopCurrentBuyer && IsOnlineLocalTurn() && !IsCpuTurn(),
          320
        ));
      }
    }
    else if (_royalAwaitingPlacement is not null)
    {
      left.Widgets.Add(MyraInfo($"PLACE {_royalAwaitingPlacement.DisplayName.ToUpperInvariant()}", UiTheme.GoldBright));
      left.Widgets.Add(MyraInfo("Click a valid square in your territory.", UiTheme.TextMuted));
    }
    else
    {
      bool canEndTurn = currentTeam is not null && CanSkipCurrentTurn(currentTeam) &&
        IsOnlineLocalTurn() && !IsCpuTurn();
      left.Widgets.Add(MyraButton("END TURN", TrySkipCurrentTurn, canEndTurn, 320));
    }

    if (IsDebugOnlineMatch)
    {
      left.Widgets.Add(MyraButton(GetDebugTeamSwitchLabel(), () => _ = SwitchDebugTeamAsync(), !_debugTeamSwitchPending, 320));
    }
    if (_onlineClient is not null)
    {
      left.Widgets.Add(MyraInfo(_onlineStatus, UiTheme.TextMuted));
    }
    if (!string.IsNullOrWhiteSpace(_onlineError))
    {
      left.Widgets.Add(MyraInfo(_onlineError, UiTheme.Attack));
    }

    left.Widgets.Add(new HorizontalSeparator());
    if (selectedPiece is null)
    {
      left.Widgets.Add(MyraInfo("NO PIECE SELECTED", UiTheme.TextMuted));
      left.Widgets.Add(MyraInfo("Left-click a unit to inspect or move it.", UiTheme.TextMuted));
    }
    else
    {
      Piece inspected = selectedPiece;
      left.Widgets.Add(MyraInfo(inspected.Definition.DisplayName.ToUpperInvariant(), UiTheme.GoldBright));
      left.Widgets.Add(MyraInfo($"{UiText.GetTeamDisplayName(inspected.Team)}  •  HP {inspected.CurrentHealth}/{inspected.Definition.Health}"));
      left.Widgets.Add(MyraInfo($"MOVE {inspected.Definition.Movement.range} {inspected.Definition.Movement.shape}  •  ATK {inspected.Definition.Attack}"));
      left.Widgets.Add(MyraInfo($"RANGE {inspected.Definition.AttackRange.Minimum}-{inspected.Definition.AttackRange.Maximum} {inspected.Definition.AttackPattern}"));
      left.Widgets.Add(MyraInfo(GetSelectedPieceControlHint(inspected), UiTheme.TextMuted));

      if (inspected.Definition.Type == PieceType.Engineer)
      {
        Label engineerMode = MyraInfo($"ENGINEER: {_selectedEngineerAbility}", UiTheme.GoldBright);
        engineerMode.Width = 180;
        left.Widgets.Add(MyraRow(
          MyraButton("<", () => CycleEngineerAbility(-1), width: 58),
          engineerMode,
          MyraButton(">", () => CycleEngineerAbility(1), width: 58)
        ));
      }
      else if (inspected.Definition.Type == PieceType.Ox)
      {
        Piece? cargo = GetOxCargo(inspected);
        if (cargo is not null)
        {
          left.Widgets.Add(MyraButton($"SELECT CARGO: {cargo.Definition.DisplayName.ToUpperInvariant()}", () => SelectPiece(cargo, true), width: 320));
        }
      }
      else if (inspected.Definition.Type == PieceType.Guard && inspected.AttachedTo is not null)
      {
        left.Widgets.Add(MyraInfo($"PROTECTING: {inspected.AttachedTo.Definition.DisplayName.ToUpperInvariant()}", UiTheme.GoldBright));
      }
      else if (inspected.Definition.Type == PieceType.Mercenary)
      {
        left.Widgets.Add(MyraButton("FIRE MERCENARY", FireMyraSelectedMercenary, CanFireSelectedMercenary(), 320));
      }
    }

    ScrollViewer leftScroll = new()
    {
      Content = left,
      Width = 368,
      Height = maximumPanelHeight,
      HorizontalAlignment = HorizontalAlignment.Left,
      VerticalAlignment = VerticalAlignment.Top,
      Margin = new Thickness(16)
    };
    root.Widgets.Add(leftScroll);

    if (_royalAwaitingPlacement is null)
    {
      VerticalStackPanel purchase = BuildMyraPurchaseHud();
      ScrollViewer purchaseScroll = new()
      {
        Content = purchase,
        Width = 390,
        Height = maximumPanelHeight,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(16)
      };
      root.Widgets.Add(purchaseScroll);
    }

    if (_chessTimerEnabled)
    {
      HorizontalStackPanel clocks = new()
      {
        Spacing = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(16)
      };
      foreach (TeamName team in Team.ActiveTeams)
      {
        clocks.Widgets.Add(MyraInfo($"{UiText.GetTeamDisplayName(team).ToUpperInvariant()} {FormatClock(team)}", UiTheme.GetTeamColour(team)));
      }
      root.Widgets.Add(clocks);
    }

    return root;
  }

  private VerticalStackPanel BuildMyraPurchaseHud()
  {
    VerticalStackPanel purchase = new()
    {
      Width = 352,
      Spacing = 8,
      HorizontalAlignment = HorizontalAlignment.Center
    };
    purchase.Widgets.Add(MyraInfo("PURCHASE", UiTheme.GoldBright));

    IReadOnlyList<PieceDefinition> purchasable = GetPurchasablePieces();
    if (purchasable.Count == 0)
    {
      purchase.Widgets.Add(MyraInfo("No units are available to buy.", UiTheme.TextMuted));
      return purchase;
    }

    _selectedPurchaseIndex = Math.Clamp(_selectedPurchaseIndex, 0, purchasable.Count - 1);
    PieceDefinition selected = purchasable[_selectedPurchaseIndex];
    bool openingFarm = _initialBuyPhase?.IsFarmPlacementPhase == true && selected.Type == PieceType.Farm;
    string price = openingFarm ? "FREE OPENING PLACEMENT" : $"{GetUnitPrice(selected)} GOLD";
    purchase.Widgets.Add(MyraInfo(selected.DisplayName.ToUpperInvariant(), UiTheme.GoldBright));
    purchase.Widgets.Add(MyraInfo(price));
    purchase.Widgets.Add(MyraInfo($"HP {selected.Health}  •  ATK {selected.Attack}  •  MOVE {selected.Movement.range}"));
    purchase.Widgets.Add(MyraRow(
      MyraButton("<", () => CyclePurchaseSelection(-1), width: 68),
      MyraButton(">", () => CyclePurchaseSelection(1), width: 68)
    ));

    if (_initialBuyPhase is null)
    {
      bool canToggle = IsOnlineLocalTurn() && !IsCpuTurn();
      purchase.Widgets.Add(MyraButton(
        _isPurchaseMode ? "CANCEL PLACEMENT" : "BUY / PLACE UNIT",
        ToggleMyraPurchaseMode,
        canToggle,
        336
      ));
    }
    else
    {
      purchase.Widgets.Add(MyraInfo("PLACEMENT MODE ACTIVE — CLICK THE BOARD", UiTheme.TextMuted));
    }

    purchase.Widgets.Add(MyraButton(
      _isPurchaseUnitListExpanded ? "HIDE UNIT LIST" : "SHOW UNIT LIST",
      () => _isPurchaseUnitListExpanded = !_isPurchaseUnitListExpanded,
      width: 336
    ));

    if (_isPurchaseUnitListExpanded)
    {
      purchase.Widgets.Add(new HorizontalSeparator());
      for (int index = 0; index < purchasable.Count; index++)
      {
        int purchaseIndex = index;
        PieceDefinition definition = purchasable[purchaseIndex];
        bool selectable = !(_initialBuyPhase?.IsFarmPlacementPhase == true && definition.Type != PieceType.Farm) &&
          !(_initialBuyPhase is not null && definition.Type == PieceType.Mercenary);
        string marker = purchaseIndex == _selectedPurchaseIndex ? "✓ " : string.Empty;
        string itemPrice = _initialBuyPhase?.IsFarmPlacementPhase == true && definition.Type == PieceType.Farm
          ? "FREE"
          : $"{GetUnitPrice(definition)}G";
        purchase.Widgets.Add(MyraButton(
          $"{marker}{definition.DisplayName.ToUpperInvariant()} — {itemPrice}",
          () => TrySelectPurchaseIndex(purchaseIndex),
          selectable,
          336
        ));
      }
    }

    return purchase;
  }

  private void ToggleMyraPurchaseMode()
  {
    if (_initialBuyPhase is not null || _royalAwaitingPlacement is not null || !IsOnlineLocalTurn() || IsCpuTurn())
    {
      return;
    }
    _isPurchaseMode = !_isPurchaseMode;
    selectedPiece = null;
  }

  private void StopMyraInitialBuying()
  {
    if (_initialBuyPhase is null || !_initialBuyPhase.CanStopCurrentBuyer || IsCpuTurn()) return;
    if (_onlineClient is not null)
    {
      if (!IsOnlineLocalTurn())
      {
        _onlineError = "It is not your initial buy turn.";
        return;
      }
      _ = SendOnlineStopInitialBuyingAsync();
      return;
    }

    _initialBuyPhase.StopCurrentBuyer();
    UpdateInitialBuyPhaseState();
  }

  private void FireMyraSelectedMercenary()
  {
    if (selectedPiece?.Definition.Type != PieceType.Mercenary || !CanFireSelectedMercenary()) return;
    Piece mercenary = selectedPiece;
    bool fired = _onlineClient is null
      ? TryUseSpecialAbility(mercenary, mercenary.Position, mercenary, Keyboard.GetState())
      : TrySendOnlineSpecialAbility(mercenary, mercenary.Position, mercenary);
    if (fired) selectedPiece = null;
  }

  private string GetMyraModeScoreText()
  {
    return _gameMode switch
    {
      GameMode.Conquest when _playerCount == 2 =>
        $"CONQUEST  {UiText.GetTeamDisplayName(TeamName.Red)} {Math.Max(0, -_conquestScore)}/{_conquestWinScore}  •  {UiText.GetTeamDisplayName(TeamName.Blue)} {Math.Max(0, _conquestScore)}/{_conquestWinScore}",
      GameMode.Conquest => "CONQUEST  " + string.Join("  •  ", Team.ActiveTeams.Select(team =>
        $"{UiText.GetTeamDisplayName(team)} {_conquestScores.GetValueOrDefault(team)}/{_conquestWinScore}")),
      GameMode.Dominion => "DOMINION  " + string.Join("  •  ", Team.ActiveTeams.Select(team =>
        $"{UiText.GetTeamDisplayName(team)} {_modeScores.GetValueOrDefault(team)}/{_dominionWinScore}")),
      GameMode.Plunder => "PLUNDER  " + string.Join("  •  ", Team.ActiveTeams.Select(team =>
        $"{UiText.GetTeamDisplayName(team)} {_modeScores.GetValueOrDefault(team)}/{_plunderWinScore}")),
      GameMode.Escort => "ESCORT — GET YOUR ROYAL TO THE ENEMY EDGE",
      _ => "REGICIDE — DESTROY THE ENEMY ROYAL"
    };
  }

  private bool IsPointerOverMyraPlayingHud(Point position)
  {
    if (_screen != Screen.Playing) return false;
    if (GetStatusPanelBounds().Contains(position) || GetSelectedPiecePanelBounds().Contains(position)) return true;
    if (_chessTimerEnabled && GetChessClockPanelBounds().Contains(position)) return true;
    return _royalAwaitingPlacement is null && IsPointerOverPurchaseMenu(position);
  }
'''
    stripped = myra_text.rstrip()
    if not stripped.endswith("}"):
        raise RuntimeError("Game1.MyraUi.cs did not end with the partial class brace")
    myra_text = stripped[:-1] + hud_block + "}\n"

if myra_text != myra_original:
    myra_path.write_text(myra_text, encoding="utf-8")
    print("Applied Myra UI source updates")
else:
    print("Myra UI source already updated")
