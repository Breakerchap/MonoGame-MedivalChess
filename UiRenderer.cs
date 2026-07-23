using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace MedivalChess;

internal enum UiButtonTone
{
  Neutral,
  Primary,
  Danger,
  Accent
}

internal sealed class UiRenderer
{
  private readonly SpriteBatch _spriteBatch;
  private readonly Texture2D _pixel;
  private readonly SpriteFont _font;

  internal UiRenderer(SpriteBatch spriteBatch, Texture2D pixel, SpriteFont font)
  {
    _spriteBatch = spriteBatch;
    _pixel = pixel;
    _font = font;
  }

  internal void Panel(Rectangle bounds, Color fill, Color border)
  {
    _spriteBatch.Draw(_pixel, bounds, fill);
    DrawBorder(bounds, border);
  }

  internal void Divider(Rectangle bounds, int y, Color? colour = null)
  {
    _spriteBatch.Draw(
      _pixel,
      new Rectangle(bounds.X, y, bounds.Width, 1),
      colour ?? UiTheme.PanelBorderSubtle
    );
  }

  internal void Button(Rectangle bounds, string label, UiButtonTone tone, bool selected = false)
  {
    MouseState mouse = Mouse.GetState();
    bool isHovered = bounds.Contains(mouse.Position);
    bool isPressed = isHovered && mouse.LeftButton == ButtonState.Pressed;
    Color fill = GetButtonColour(tone);

    if (isHovered)
    {
      fill = Color.Lerp(fill, Color.White, 0.12f);
    }

    if (isPressed)
    {
      fill = Color.Lerp(fill, Color.Black, 0.22f);
    }

    Rectangle drawBounds = isPressed
      ? new Rectangle(bounds.X, bounds.Y + 1, bounds.Width, Math.Max(1, bounds.Height - 1))
      : bounds;
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y + 3, bounds.Width, bounds.Height), UiTheme.Shadow);
    Panel(
      drawBounds,
      fill,
      selected ? UiTheme.GoldBright : Color.Lerp(fill, UiTheme.TextPrimary, 0.24f)
    );
    _spriteBatch.Draw(
      _pixel,
      new Rectangle(drawBounds.X + 2, drawBounds.Y + 2, Math.Max(1, drawBounds.Width - 4), 1),
      Color.Lerp(fill, UiTheme.TextPrimary, 0.16f)
    );
    CenterText(label, drawBounds, UiTheme.TextPrimary);
  }

  internal void ProgressBar(Rectangle bounds, float progress, Color fill)
  {
    progress = MathHelper.Clamp(progress, 0f, 1f);
    _spriteBatch.Draw(_pixel, bounds, UiTheme.Shadow);
    _spriteBatch.Draw(
      _pixel,
      new Rectangle(bounds.X + 2, bounds.Y + 2, (int)((bounds.Width - 4) * progress), Math.Max(1, bounds.Height - 4)),
      fill
    );
  }

  internal void Text(string text, Vector2 position, Color colour, float scale = 1f)
  {
    _spriteBatch.DrawString(_font, text, position, colour, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
  }

  internal void CenterText(string text, Rectangle bounds, Color colour, float scale = 1f)
  {
    Vector2 size = _font.MeasureString(text) * scale;
    Text(
      text,
      new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f),
      colour,
      scale
    );
  }

  internal void RightText(string text, Rectangle bounds, Color colour, float scale = 1f)
  {
    Vector2 size = _font.MeasureString(text) * scale;
    Text(text, new Vector2(bounds.Right - size.X, bounds.Center.Y - size.Y / 2f), colour, scale);
  }

  internal void LabelValueRow(Rectangle bounds, string label, string value, Color valueColour)
  {
    Text(label, new Vector2(bounds.X, bounds.Center.Y - 10), UiTheme.TextMuted, 0.78f);
    RightText(value, bounds, valueColour);
  }

  internal void StatBlock(Rectangle bounds, string label, string value, Color valueColour)
  {
    Panel(bounds, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    Text(label, new Vector2(bounds.X + UiTheme.SpaceSm, bounds.Y + UiTheme.SpaceXs), UiTheme.TextMuted, 0.72f);
    Text(value, new Vector2(bounds.X + UiTheme.SpaceSm, bounds.Y + 25), valueColour);
  }

  internal void PiecePreview(Rectangle bounds, Color teamColour, string label)
  {
    Panel(bounds, Color.Lerp(teamColour, UiTheme.PanelRaised, 0.25f), Color.Lerp(teamColour, UiTheme.TextPrimary, 0.3f));
    CenterText(label, bounds, UiTheme.TextPrimary);
  }

  private void DrawBorder(Rectangle bounds, Color colour)
  {
    int thickness = UiTheme.PanelBorderThickness;
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), colour);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), colour);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), colour);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), colour);
  }

  private static Color GetButtonColour(UiButtonTone tone)
  {
    return tone switch
    {
      UiButtonTone.Primary => UiTheme.PrimaryButton,
      UiButtonTone.Danger => UiTheme.DangerButton,
      UiButtonTone.Accent => UiTheme.AccentButton,
      _ => UiTheme.NeutralButton
    };
  }
}
