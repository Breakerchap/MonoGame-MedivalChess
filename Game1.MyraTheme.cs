using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI.Styles;

namespace MedivalChess;

internal sealed partial class Game1
{
  private bool _myraThemeInitialised;

  protected override void BeginRun()
  {
    // Initialize() has completed by this point, which means LoadContent() has run
    // and InitializeMyraUi() has already assigned MyraEnvironment.Game.
    InitialiseMyraTheme();
    MarkMyraDirty();
    base.BeginRun();
  }

  private void InitialiseMyraTheme()
  {
    if (_myraThemeInitialised) return;

    // Stylesheet.Current lazily loads Myra's default assets and therefore requires
    // MyraEnvironment.Game to have already been assigned by InitializeMyraUi().
    Stylesheet stylesheet = Stylesheet.Current;

    ButtonStyle button = stylesheet.ButtonStyle;
    button.Background = Brush(UiTheme.NeutralButton);
    button.OverBackground = Brush(Lighten(UiTheme.NeutralButton, 1.20f));
    button.FocusedBackground = Brush(Lighten(UiTheme.NeutralButton, 1.12f));
    button.PressedBackground = Brush(Darken(UiTheme.NeutralButton, 0.78f));
    button.DisabledBackground = Brush(new Color(UiTheme.NeutralButton, 0.42f));
    button.Border = Brush(UiTheme.PanelBorderSubtle);
    button.OverBorder = Brush(UiTheme.Gold);
    button.FocusedBorder = Brush(UiTheme.GoldBright);
    button.PressedBorder = Brush(UiTheme.Gold);
    button.DisabledBorder = Brush(new Color(UiTheme.PanelBorderSubtle, 0.45f));
    button.BorderThickness = new Thickness(1);
    button.Padding = new Thickness(UiTheme.SpaceSm, UiTheme.SpaceXs);

    WidgetStyle panel = stylesheet.PanelStyle;
    panel.Background = Brush(UiTheme.Panel);
    panel.Border = Brush(UiTheme.PanelBorder);
    panel.BorderThickness = new Thickness(1);

    TextBoxStyle textBox = stylesheet.TextBoxStyle;
    textBox.Background = Brush(UiTheme.MenuBackground);
    textBox.FocusedBackground = Brush(UiTheme.PanelRaised);
    textBox.Border = Brush(UiTheme.PanelBorderSubtle);
    textBox.FocusedBorder = Brush(UiTheme.GoldBright);
    textBox.OverBorder = Brush(UiTheme.Gold);
    textBox.BorderThickness = new Thickness(1);
    textBox.Padding = new Thickness(UiTheme.SpaceSm, UiTheme.SpaceXs);

    _myraThemeInitialised = true;
  }

  private static SolidBrush Brush(Color colour) => new(colour);

  private static Color Lighten(Color colour, float factor) => new(
    (byte)Math.Clamp((int)MathF.Round(colour.R * factor), 0, 255),
    (byte)Math.Clamp((int)MathF.Round(colour.G * factor), 0, 255),
    (byte)Math.Clamp((int)MathF.Round(colour.B * factor), 0, 255),
    colour.A
  );

  private static Color Darken(Color colour, float factor) => Lighten(colour, factor);
}
