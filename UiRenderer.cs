using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

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
  internal float InputScale { get; set; } = 1f;

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

  internal void Button(Rectangle bounds, string label, UiButtonTone tone, bool selected = false, float textScale = 1f)
  {
    MouseState mouse = Mouse.GetState();
    Point mousePosition = new(
      (int)MathF.Floor(mouse.Position.X / InputScale),
      (int)MathF.Floor(mouse.Position.Y / InputScale)
    );
    bool isHovered = bounds.Contains(mousePosition);
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
    CenterTextFitted(label, drawBounds, UiTheme.TextPrimary, textScale);
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

  internal void TextFitted(
    string text,
    Vector2 position,
    int maximumWidth,
    Color colour,
    float preferredScale = 1f,
    float minimumScale = 0.5f
  )
  {
    Text(text, position, colour, GetFittedTextScale(text, maximumWidth, preferredScale, minimumScale));
  }

  internal void TextWrapped(string text, Rectangle bounds, Color colour, float scale = 1f)
  {
    if (string.IsNullOrWhiteSpace(text) || bounds.Width <= 0 || bounds.Height <= 0)
    {
      return;
    }

    float lineHeight = _font.LineSpacing * scale;
    float y = bounds.Y;
    foreach (string line in WrapText(text, bounds.Width, scale))
    {
      if (y + lineHeight > bounds.Bottom)
      {
        break;
      }

      Text(line, new Vector2(bounds.X, y), colour, scale);
      y += lineHeight;
    }
  }

  internal int WrappedTextHeight(string text, int maximumWidth, float scale = 1f)
  {
    if (string.IsNullOrWhiteSpace(text) || maximumWidth <= 0)
    {
      return 0;
    }

    int lineCount = 0;
    foreach (string _ in WrapText(text, maximumWidth, scale))
    {
      lineCount++;
    }

    return (int)Math.Ceiling(lineCount * _font.LineSpacing * scale);
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

  internal void CenterTextFitted(
    string text,
    Rectangle bounds,
    Color colour,
    float preferredScale = 1f,
    float minimumScale = 0.5f,
    int horizontalPadding = 8
  )
  {
    int maximumWidth = Math.Max(1, bounds.Width - horizontalPadding * 2);
    CenterText(text, bounds, colour, GetFittedTextScale(text, maximumWidth, preferredScale, minimumScale));
  }

  internal void CenterTextWrapped(string text, Rectangle bounds, Color colour, float scale = 1f)
  {
    if (string.IsNullOrWhiteSpace(text) || bounds.Width <= 0 || bounds.Height <= 0)
    {
      return;
    }

    List<string> lines = new();
    foreach (string line in WrapText(text, bounds.Width, scale))
    {
      lines.Add(line);
    }

    if (lines.Count == 0)
    {
      return;
    }

    float lineHeight = _font.LineSpacing * scale;
    int visibleLineCount = Math.Min(lines.Count, Math.Max(1, (int)Math.Floor(bounds.Height / lineHeight)));
    float y = bounds.Y + (bounds.Height - visibleLineCount * lineHeight) / 2f;
    for (int index = 0; index < visibleLineCount; index++)
    {
      CenterText(
        lines[index],
        new Rectangle(bounds.X, (int)Math.Floor(y), bounds.Width, (int)Math.Ceiling(lineHeight)),
        colour,
        scale
      );
      y += lineHeight;
    }
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

  internal void StatBlock(Rectangle bounds, string label, string value, Color valueColour, float fontScale = 1f)
  {
    Panel(bounds, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    int textWidth = Math.Max(1, bounds.Width - UiTheme.SpaceSm * 2);
    float preferredLabelScale = (bounds.Height >= 48 ? 0.68f : 0.58f) * fontScale;
    float preferredValueScale = (bounds.Height >= 48 ? 0.90f : 0.78f) * fontScale;
    int availableTextHeight = Math.Max(1, bounds.Height - UiTheme.SpaceXs);
    float preferredTextHeight = _font.LineSpacing * (preferredLabelScale + preferredValueScale);
    float verticalScale = Math.Min(1f, availableTextHeight / preferredTextHeight);
    float labelScale = preferredLabelScale * verticalScale;
    float valueScale = preferredValueScale * verticalScale;
    int labelY = bounds.Y + UiTheme.SpaceXs / 2;
    int valueY = labelY + (int)Math.Ceiling(_font.LineSpacing * labelScale);

    float minimumLabelScale = Math.Min(labelScale, 0.42f);
    float minimumValueScale = Math.Min(valueScale, 0.50f);
    TextFitted(label, new Vector2(bounds.X + UiTheme.SpaceSm, labelY), textWidth, UiTheme.TextMuted, labelScale, minimumLabelScale);
    TextFitted(value, new Vector2(bounds.X + UiTheme.SpaceSm, valueY), textWidth, valueColour, valueScale, minimumValueScale);
  }

  internal void PiecePreview(Rectangle bounds, Color teamColour, string label)
  {
    Panel(bounds, Color.Lerp(teamColour, UiTheme.PanelRaised, 0.25f), Color.Lerp(teamColour, UiTheme.TextPrimary, 0.3f));
    CenterText(label, bounds, UiTheme.TextPrimary);
  }

  private IEnumerable<string> WrapText(string text, int maximumWidth, float scale)
  {
    foreach (string paragraph in text.Split('\n'))
    {
      string currentLine = string.Empty;
      foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
      {
        string candidate = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
        if (_font.MeasureString(candidate).X * scale <= maximumWidth)
        {
          currentLine = candidate;
          continue;
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
          yield return currentLine;
          currentLine = string.Empty;
        }

        if (_font.MeasureString(word).X * scale <= maximumWidth)
        {
          currentLine = word;
          continue;
        }

        foreach (string fragment in SplitLongWord(word, maximumWidth, scale))
        {
          yield return fragment;
        }
      }

      if (!string.IsNullOrEmpty(currentLine))
      {
        yield return currentLine;
      }
    }
  }

  private float GetFittedTextScale(string text, int maximumWidth, float preferredScale, float minimumScale)
  {
    if (string.IsNullOrEmpty(text) || maximumWidth <= 0)
    {
      return preferredScale;
    }

    float textWidth = _font.MeasureString(text).X;
    if (textWidth <= 0f)
    {
      return preferredScale;
    }

    float fittedScale = maximumWidth / textWidth;
    return MathHelper.Clamp(Math.Min(preferredScale, fittedScale), minimumScale, preferredScale);
  }

  private IEnumerable<string> SplitLongWord(string word, int maximumWidth, float scale)
  {
    if (_font.MeasureString(word).X * scale <= maximumWidth)
    {
      yield return word;
      yield break;
    }

    string fragment = string.Empty;
    foreach (char character in word)
    {
      string candidate = fragment + character;
      if (!string.IsNullOrEmpty(fragment) && _font.MeasureString(candidate).X * scale > maximumWidth)
      {
        yield return fragment;
        fragment = character.ToString();
      }
      else
      {
        fragment = candidate;
      }
    }

    if (!string.IsNullOrEmpty(fragment))
    {
      yield return fragment;
    }
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
