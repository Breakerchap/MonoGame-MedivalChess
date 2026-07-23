using Microsoft.Xna.Framework;

namespace MedivalChess;

internal static class UiTheme
{
  internal const int SpaceXs = 8;
  internal const int SpaceSm = 12;
  internal const int SpaceMd = 16;
  internal const int SpaceLg = 24;
  internal const int SpaceXl = 32;
  internal const int PanelBorderThickness = 2;
  internal const int ButtonHeight = 44;

  internal static readonly Color MenuBackground = new(18, 25, 35);
  internal static readonly Color BoardBackground = new(52, 68, 78);
  internal static readonly Color Panel = new(23, 30, 42, 242);
  internal static readonly Color PanelRaised = new(36, 46, 62, 250);
  internal static readonly Color PanelBorder = new(104, 126, 146);
  internal static readonly Color PanelBorderSubtle = new(67, 82, 101);
  internal static readonly Color Shadow = new(7, 10, 15, 150);

  internal static readonly Color Gold = new(218, 180, 91);
  internal static readonly Color GoldBright = new(246, 214, 123);
  internal static readonly Color TextPrimary = new(243, 238, 225);
  internal static readonly Color TextMuted = new(183, 196, 210);
  internal static readonly Color TextDim = new(132, 149, 166);

  internal static readonly Color NeutralButton = new(58, 73, 91);
  internal static readonly Color PrimaryButton = new(58, 113, 79);
  internal static readonly Color DangerButton = new(133, 67, 67);
  internal static readonly Color AccentButton = new(145, 110, 45);

  internal static readonly Color TeamRed = new(184, 74, 68);
  internal static readonly Color TeamBlue = new(71, 112, 178);
  internal static readonly Color Health = new(105, 185, 112);
  internal static readonly Color Attack = new(221, 116, 108);
  internal static readonly Color Move = new(230, 202, 103);

  internal static readonly Color DarkBoardCell = new(116, 83, 59);
  internal static readonly Color LightBoardCell = new(197, 174, 136);
  internal static readonly Color RedTerritory = new(160, 76, 71);
  internal static readonly Color BlueTerritory = new(75, 109, 145);
  internal static readonly Color NoMansLand = new(112, 112, 104);
  internal static readonly Color MoveOverlay = new(232, 202, 86, 138);
  internal static readonly Color AttackOverlay = new(211, 79, 72, 162);
  internal static readonly Color SelectionOutline = new(246, 214, 123);
}
