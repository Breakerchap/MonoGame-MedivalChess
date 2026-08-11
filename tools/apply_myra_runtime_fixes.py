from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        return text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one {label} integration point, found {count}")
    return text.replace(old, new, 1)


game_path = Path("Game1.cs")
game_text = game_path.read_text(encoding="utf-8-sig")
game_original = game_text

game_text = replace_once(
    game_text,
    """    }\n    else\n    {\n      Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);\n      _spriteBatch.Draw(_pixel, viewport, new Color(5, 9, 14, 176));\n      if (!UsesMyraUi(_screen)) DrawMenuScreen();\n    }\n\n    _spriteBatch.End();""",
    """    }\n    else if (_screen != Screen.Playing)\n    {\n      Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);\n      _spriteBatch.Draw(_pixel, viewport, new Color(5, 9, 14, 176));\n      if (!UsesMyraUi(_screen)) DrawMenuScreen();\n    }\n\n    _spriteBatch.End();""",
    "playing overlay",
)

game_text = replace_once(
    game_text,
    """    UiLayout.Scale = _uiScale;\n    _ui.InputScale = _uiScale;\n  }\n\n  private Point ToUiPoint""",
    """    UiLayout.Scale = _uiScale;\n    _ui.InputScale = _uiScale;\n    ApplyMyraUiScale();\n  }\n\n  private Point ToUiPoint""",
    "Myra HUD scale hook",
)

if game_text != game_original:
    game_path.write_text(game_text, encoding="utf-8")
    print("Applied Game1 Myra runtime hooks")
else:
    print("Game1 Myra runtime hooks already applied")

myra_path = Path("Game1.MyraUi.cs")
myra_text = myra_path.read_text(encoding="utf-8")
myra_original = myra_text

if "using Myra.Graphics2D.Brushes;\n" not in myra_text:
    myra_text = myra_text.replace(
        "using Myra.Graphics2D;\n",
        "using Myra.Graphics2D;\nusing Myra.Graphics2D.Brushes;\n",
        1,
    )

# Let the HUD sidebars size to their content until they reach the viewport ceiling.
# A fixed full-height ScrollViewer visually covered playable board space even when empty.
myra_text = myra_text.replace("      Height = maximumPanelHeight,\n", "      MaxHeight = maximumPanelHeight,\n")

old_initialise = """    MyraEnvironment.Game = this;\n    _myraDesktop = new Desktop();\n    _myraDirty = true;\n"""
new_initialise = """    MyraEnvironment.Game = this;\n    _myraDesktop = new Desktop\n    {\n      Background = new SolidBrush(Color.Transparent),\n      TransformOrigin = Vector2.Zero,\n      BoundsFetcher = GetMyraBounds\n    };\n    ApplyMyraUiScale();\n    _myraDirty = true;\n"""
if old_initialise in myra_text:
    myra_text = myra_text.replace(old_initialise, new_initialise, 1)
elif new_initialise not in myra_text:
    raise RuntimeError("Could not find Myra initialisation point")

if "private Rectangle GetMyraBounds()" not in myra_text:
    scale_methods = r'''

  private Rectangle GetMyraBounds() => new(
    0,
    0,
    Math.Max(1, (int)MathF.Ceiling(GraphicsDevice.Viewport.Width / _uiScale)),
    Math.Max(1, (int)MathF.Ceiling(GraphicsDevice.Viewport.Height / _uiScale))
  );

  private void ApplyMyraUiScale()
  {
    if (_myraDesktop is null) return;
    _myraDesktop.Scale = new Vector2(_uiScale);
    _myraDesktop.TransformOrigin = Vector2.Zero;
    MarkMyraDirty();
  }
'''
    marker = "  private void MarkMyraDirty() => _myraDirty = true;\n"
    if marker not in myra_text:
        raise RuntimeError("Could not find Myra scale method insertion point")
    myra_text = myra_text.replace(marker, marker + scale_methods, 1)

if "private readonly Dictionary<TeamName, Label> _myraClockLabels" not in myra_text:
    myra_text = myra_text.replace(
        "  private string _myraStatusSnapshot = string.Empty;\n",
        "  private string _myraStatusSnapshot = string.Empty;\n  private readonly Dictionary<TeamName, Label> _myraClockLabels = [];\n",
        1,
    )

old_clock_snapshot = """    string clocks = _chessTimerEnabled\n      ? string.Join(\",\", Team.ActiveTeams.Select(team => $\"{team}:{FormatClock(team)}\"))\n      : \"off\";\n\n"""
myra_text = myra_text.replace(old_clock_snapshot, "")
myra_text = myra_text.replace("|clock:{clocks}|mode:", "|mode:")

myra_text = replace_once(
    myra_text,
    """    if (_myraDirty) RebuildMyraUi();\n    _myraDesktop.Render();\n  }\n\n  private string GetMyraStatusSnapshot()""",
    """    if (_myraDirty) RebuildMyraUi();\n    UpdateMyraPlayingClockLabels();\n    _myraDesktop.Render();\n  }\n\n  private string GetMyraStatusSnapshot()""",
    "dynamic Myra clock update",
)

myra_text = replace_once(
    myra_text,
    """  private Widget BuildMyraPlayingHud()\n  {\n    Grid root = new()""",
    """  private Widget BuildMyraPlayingHud()\n  {\n    _myraClockLabels.Clear();\n    Grid root = new()""",
    "playing HUD clock reset",
)

old_left_scroll = """      HorizontalAlignment = HorizontalAlignment.Left,\n      VerticalAlignment = VerticalAlignment.Top,\n      Margin = new Thickness(16)\n    };\n    root.Widgets.Add(leftScroll);"""
new_left_scroll = """      HorizontalAlignment = HorizontalAlignment.Left,\n      VerticalAlignment = VerticalAlignment.Top,\n      Margin = new Thickness(16),\n      Padding = new Thickness(UiTheme.SpaceMd),\n      Background = new SolidBrush(UiTheme.Panel)\n    };\n    root.Widgets.Add(leftScroll);"""
if old_left_scroll in myra_text:
    myra_text = myra_text.replace(old_left_scroll, new_left_scroll, 1)
elif new_left_scroll not in myra_text:
    raise RuntimeError("Could not find Myra left HUD panel styling point")

old_purchase_scroll = """        HorizontalAlignment = HorizontalAlignment.Right,\n        VerticalAlignment = VerticalAlignment.Top,\n        Margin = new Thickness(16)\n      };\n      root.Widgets.Add(purchaseScroll);"""
new_purchase_scroll = """        HorizontalAlignment = HorizontalAlignment.Right,\n        VerticalAlignment = VerticalAlignment.Top,\n        Margin = new Thickness(16),\n        Padding = new Thickness(UiTheme.SpaceMd),\n        Background = new SolidBrush(UiTheme.Panel)\n      };\n      root.Widgets.Add(purchaseScroll);"""
if old_purchase_scroll in myra_text:
    myra_text = myra_text.replace(old_purchase_scroll, new_purchase_scroll, 1)
elif new_purchase_scroll not in myra_text:
    raise RuntimeError("Could not find Myra purchase HUD panel styling point")

old_clock_widgets = """      foreach (TeamName team in Team.ActiveTeams)\n      {\n        clocks.Widgets.Add(MyraInfo($\"{UiText.GetTeamDisplayName(team).ToUpperInvariant()} {FormatClock(team)}\", UiTheme.GetTeamColour(team)));\n      }\n"""
new_clock_widgets = """      foreach (TeamName team in Team.ActiveTeams)\n      {\n        Label clockLabel = MyraInfo($\"{UiText.GetTeamDisplayName(team).ToUpperInvariant()} {FormatClock(team)}\", UiTheme.GetTeamColour(team));\n        _myraClockLabels[team] = clockLabel;\n        clocks.Widgets.Add(clockLabel);\n      }\n"""
if old_clock_widgets in myra_text:
    myra_text = myra_text.replace(old_clock_widgets, new_clock_widgets, 1)
elif new_clock_widgets not in myra_text:
    raise RuntimeError("Could not find Myra clock widget integration point")

if "private void UpdateMyraPlayingClockLabels()" not in myra_text:
    clock_method = r'''

  private void UpdateMyraPlayingClockLabels()
  {
    if (_screen != Screen.Playing || !_chessTimerEnabled) return;
    foreach ((TeamName team, Label label) in _myraClockLabels)
    {
      label.Text = $"{UiText.GetTeamDisplayName(team).ToUpperInvariant()} {FormatClock(team)}";
    }
  }
'''
    stripped = myra_text.rstrip()
    if not stripped.endswith("}"):
        raise RuntimeError("Game1.MyraUi.cs did not end with the partial class brace")
    myra_text = stripped[:-1] + clock_method + "}\n"

if myra_text != myra_original:
    myra_path.write_text(myra_text, encoding="utf-8")
    print("Applied Myra runtime/UI fixes")
else:
    print("Myra runtime/UI fixes already applied")
