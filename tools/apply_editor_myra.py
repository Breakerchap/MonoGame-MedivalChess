from pathlib import Path

path = Path("Campaign/LevelEditorScreen.cs")
text = path.read_text(encoding="utf-8-sig")
original = text


def replace_once(old: str, new: str, label: str) -> None:
    global text
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one {label} integration point, found {count}")
    text = text.replace(old, new, 1)


replace_once(
    "internal sealed class LevelEditorScreen",
    "internal sealed partial class LevelEditorScreen",
    "partial class",
)

replace_once(
    """    if (wasEscapePressed)\n    {\n      if (_textField != TextField.None)""",
    """    if (wasEscapePressed)\n    {\n      if (HandleMyraEditorEscape()) return;\n      if (_textField != TextField.None)""",
    "escape handling",
)

replace_once(
    """    EditorLayout layout = new(screen, State.Level.Teams.Count);\n    Point point = pointer;\n    UpdateKeyboardNavigation(keyboard, previousKeyboard);\n    UpdateTextInput(keyboard, previousKeyboard);""",
    """    EditorLayout layout = new(screen, State.Level.Teams.Count);\n    Point point = pointer;\n    EnsureMyraEditor(screen);\n    if (!IsMyraEditorTextInputFocused()) UpdateKeyboardNavigation(keyboard, previousKeyboard);\n    UpdateTextInput(keyboard, previousKeyboard);""",
    "editor input setup",
)

replace_once(
    """    if (wasLeftClick)\n    {\n      CommitAndEndTextEdit();\n      if (HandleHeaderClick(layout, point)) return;\n      if (HandleToolClick(layout, point)) return;\n      if (HandlePropertyClick(layout, point)) return;\n    }""",
    """    if (wasLeftClick && IsMyraEditorChromePoint(layout, point))\n    {\n      CommitAndEndTextEdit();\n      return;\n    }\n\n    if (wasLeftClick) CommitAndEndTextEdit();""",
    "Myra chrome click routing",
)

replace_once(
    """    if (layout.Canvas.Contains(point) && (wasLeftClick || (isLeftHeld && CanPaintContinuously())))\n    {\n      HandleBoardClick(layout, point);\n    }""",
    """    if (layout.Canvas.Contains(point) && (wasLeftClick || (isLeftHeld && CanPaintContinuously())))\n    {\n      HandleBoardClick(layout, point);\n      MarkEditorMyraDirty();\n    }""",
    "board chrome refresh",
)

replace_once(
    """    _spriteBatch.Draw(_pixel, screen, UiTheme.MenuBackground);\n    DrawHeader(layout);\n    DrawBoard(layout);\n    DrawToolPanel(layout);\n    DrawProperties(layout);\n    DrawProblems(layout);""",
    """    _spriteBatch.Draw(_pixel, screen, UiTheme.MenuBackground);\n    DrawBoard(layout);\n\n    // Game1 owns the surrounding SpriteBatch. Pause it while Myra renders the editor\n    // chrome, then restore the same logical UI transform for the caller's final End().\n    _spriteBatch.End();\n    EnsureMyraEditor(screen);\n    RenderMyraEditor(layout);\n    _spriteBatch.Begin(transformMatrix: Matrix.CreateScale(UiLayout.Scale));""",
    "editor draw handoff",
)

if text != original:
    path.write_text(text, encoding="utf-8")
    print("Applied Myra editor integration")
else:
    print("Myra editor integration already applied")

# The Myra partial uses the current logical screen rectangle rather than relying on
# EditorLayout exposing it as a public property.
myra_path = Path("Campaign/LevelEditorScreen.Myra.cs")
myra = myra_path.read_text(encoding="utf-8")
myra_original = myra
myra = myra.replace(
    "      Width = layout.Screen.Width,\n      Height = layout.Screen.Height,",
    "      Width = _editorMyraScreen.Width,\n      Height = _editorMyraScreen.Height,",
)
if myra != myra_original:
    myra_path.write_text(myra, encoding="utf-8")
    print("Adjusted Myra editor root sizing")
