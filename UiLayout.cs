using Microsoft.Xna.Framework;
using System;

namespace MedivalChess;

internal static class UiLayout
{
  internal static Rectangle Viewport(int width, int height) => new(0, 0, width, height);

  internal static Rectangle Centered(Rectangle available, int desiredWidth, int desiredHeight, int margin)
  {
    int width = Math.Min(desiredWidth, Math.Max(1, available.Width - margin * 2));
    int height = Math.Min(desiredHeight, Math.Max(1, available.Height - margin * 2));
    return new Rectangle(
      available.Center.X - width / 2,
      available.Center.Y - height / 2,
      width,
      height
    );
  }

  internal static Rectangle AnchorTopRight(Rectangle available, int desiredWidth, int desiredHeight, int margin)
  {
    int width = Math.Min(desiredWidth, Math.Max(1, available.Width - margin * 2));
    int height = Math.Min(desiredHeight, Math.Max(1, available.Height - margin * 2));
    return new Rectangle(available.Right - margin - width, available.Y + margin, width, height);
  }

  internal static Rectangle Inset(Rectangle bounds, int amount)
  {
    int width = Math.Max(1, bounds.Width - amount * 2);
    int height = Math.Max(1, bounds.Height - amount * 2);
    return new Rectangle(bounds.X + amount, bounds.Y + amount, width, height);
  }

  internal static Rectangle HorizontalSlot(Rectangle bounds, int count, int index, int gap)
  {
    int totalGaps = gap * Math.Max(0, count - 1);
    int slotWidth = Math.Max(1, (bounds.Width - totalGaps) / count);
    int x = bounds.X + index * (slotWidth + gap);
    int width = index == count - 1 ? Math.Max(1, bounds.Right - x) : slotWidth;
    return new Rectangle(x, bounds.Y, width, bounds.Height);
  }
}
