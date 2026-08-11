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

if game_text != game_original:
    game_path.write_text(game_text, encoding="utf-8")
    print("Fixed Playing overlay handling")
else:
    print("Playing overlay handling already fixed")

myra_path = Path("Game1.MyraUi.cs")
myra_text = myra_path.read_text(encoding="utf-8")
myra_original = myra_text
# Let the HUD sidebars size to their content until they reach the viewport ceiling.
# A fixed full-height ScrollViewer visually covered playable board space even when empty.
myra_text = myra_text.replace("      Height = maximumPanelHeight,\n", "      MaxHeight = maximumPanelHeight,\n")

if myra_text != myra_original:
    myra_path.write_text(myra_text, encoding="utf-8")
    print("Made Myra HUD sidebars content-sized")
else:
    print("Myra HUD sidebar sizing already fixed")
