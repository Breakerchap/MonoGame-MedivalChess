using Microsoft.Xna.Framework;
using Xunit;

namespace MedivalChess.Tests;

public class UiLayoutTests
{
  [Fact]
  public void CenteredPanel_StaysWithinTheAvailableViewport()
  {
    Rectangle viewport = new(0, 0, 800, 600);

    Rectangle panel = UiLayout.Centered(viewport, 900, 700, 24);

    Assert.Equal(new Rectangle(24, 24, 752, 552), panel);
  }

  [Fact]
  public void HorizontalSlots_RespectTheGapAndFillTheRow()
  {
    Rectangle row = new(20, 40, 300, 44);

    Rectangle left = UiLayout.HorizontalSlot(row, 2, 0, 12);
    Rectangle right = UiLayout.HorizontalSlot(row, 2, 1, 12);

    Assert.Equal(144, left.Width);
    Assert.Equal(12, right.X - left.Right);
    Assert.Equal(row.Right, right.Right);
  }
}
