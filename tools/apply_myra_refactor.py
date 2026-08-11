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
    """    if (!drawsGameView)\n    {\n      _spriteBatch.Begin(transformMatrix: Matrix.CreateScale(_uiScale));\n      DrawMenuScreen();\n      _spriteBatch.End();\n      base.Draw(gameTime);\n      return;\n    }""",
    """    if (!drawsGameView)\n    {\n      if (UsesMyraUi(_screen))\n      {\n        RenderMyraUi();\n      }\n      else\n      {\n        _spriteBatch.Begin(transformMatrix: Matrix.CreateScale(_uiScale));\n        DrawMenuScreen();\n        _spriteBatch.End();\n      }\n      base.Draw(gameTime);\n      return;\n    }""",
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
