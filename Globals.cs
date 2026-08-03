namespace MedivalChess.Shared;

/// <summary>Default economy settings for a new pre-game setup.</summary>
public static class Globals
{
  public const int StartingCash = 200;
  public const float KillerDeathRefundMultiplier = 0.0f;
  public const float DefeatedTeamDeathRefundMultiplier = 0.0f;
  public const int InitialBuysPerTurn = 2;
  public const int InitialBuyTurnsPerTeam = 3;
  public const bool FarmsEnabled = true;
  public const int FarmIncomePerTurn = 5;
  public const bool UnitMaintenanceEnabled = false;
  public const int UnitMaintenancePercent = 10;
  public const int UnitPricePercent = 100;
  public const bool InterestEnabled = false;
  public const int InterestPercent = 0;
  public const int DefaultEscortRoyalHealthPercent = 50;
  public const int DefaultDominionWinScore = 10;
  public const int DefaultPlunderWinScore = 9;
  public const int DefaultPlunderDeliveryScore = 3;
  public const int DefaultPlunderRoyalKillPenalty = 1;
}
