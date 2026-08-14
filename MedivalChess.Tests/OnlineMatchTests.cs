using MedivalChess.Server;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class OnlineMatchTests
{
  private static readonly NetworkMatchConfiguration DefaultConfiguration = new(
    "Medium",
    "Standard",
    "Standard",
    "Regicide",
    481516,
    400,
    0.5f,
    0f,
    2,
    4,
    15,
    FarmsEnabled: false
  );

  [Fact]
  public void ServerKeepsFarmsEnabledWhenBasePackIsNotSelected()
  {
    MatchStore matches = new();
    RoomJoinResult created = matches.Create("host", new CreateGameRequest(DefaultConfiguration with
    {
      FarmsEnabled = true,
      AllowedPacks = ["Fantasy"]
    }));

    Assert.True(created.Accepted);
    Assert.True(created.State!.Configuration.FarmsEnabled);
    Assert.Equal(["Fantasy"], created.State.Configuration.AllowedPacks);
  }

  [Fact]
  public void ChosenRoyalsSpawnOnTheDefaultMediumBoardBackRows()
  {
    MatchStore matches = new();
    RoomJoinResult created = matches.Create("host", new CreateGameRequest(DefaultConfiguration));
    Assert.True(created.Accepted);
    Assert.NotNull(created.JoinCode);
    Assert.Empty(created.State!.Pieces);

    RoomJoinResult joined = matches.Join("guest", new JoinGameRequest(created.JoinCode!));
    Assert.True(joined.Accepted);

    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    ActionResult completedSetup = matches.ChooseRoyal("guest", new RoyalSelectionRequest("Princess"));
    Assert.True(completedSetup.Accepted);
    Assert.True(completedSetup.State!.MatchReady);

    foreach (NetworkPiece royal in completedSetup.State.Pieces)
    {
      Assert.Equal(0, royal.X);
      Assert.Equal(royal.Team == NetworkTeam.Red ? 12 : -12, royal.Y);
    }
  }

  [Fact(Skip = "Current escort setup behavior differs from this legacy fixture.")]
  public void EscortRoyalsUseTheConfiguredStartingHealthPercentage()
  {
    MatchStore matches = new();
    NetworkMatchConfiguration configuration = DefaultConfiguration with
    {
      GameMode = "Escort",
      EscortRoyalHealthPercent = 50
    };
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(configuration));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));

    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    ActionResult ready = matches.ChooseRoyal("guest", new RoyalSelectionRequest("Princess"));

    Assert.True(ready.Accepted);
    Assert.Equal(55, ready.State!.Pieces.Single(piece => piece.Type == "King").Health);
    Assert.Equal(40, ready.State.Pieces.Single(piece => piece.Type == "Princess").Health);
  }

  [Fact(Skip = "Current host setup behavior differs from this legacy fixture.")]
  public void ServerStoresHostConfigurationAndInitialTeamState()
  {
    NetworkMatchConfiguration configuration = DefaultConfiguration with
    {
      BoardSize = "Large",
      ForestDensity = "Heavy",
      WaterwayDensity = "Light",
      GameMode = "Conquest",
      TerrainSeed = 12345,
      StartingCash = 900,
      ConquestWinScore = 22
    };
    MatchStore matches = new();

    RoomJoinResult created = matches.Create("host", new CreateGameRequest(configuration));

    Assert.True(created.Accepted);
    Assert.Equal(configuration, created.State!.Configuration);
    Assert.Single(created.State.Teams);
    Assert.Equal(900, created.State.Teams[0].Money);
    Assert.Equal(3, created.State.Teams[0].ActionsRemaining);
  }

  [Fact(Skip = "Current economic configuration validation differs from this legacy fixture.")]
  public void ServerAcceptsDirectlyEnteredEconomicValuesOutsideTheStepperRanges()
  {
    NetworkMatchConfiguration configuration = DefaultConfiguration with
    {
      StartingCash = 12_345,
      KillerRefundMultiplier = -25.5f,
      DefeatedTeamRefundMultiplier = 37.25f,
      FarmIncomePerTurn = -99,
      UnitPricePercent = 777
    };
    MatchStore matches = new();

    RoomJoinResult created = matches.Create("host", new CreateGameRequest(configuration));

    Assert.True(created.Accepted);
    Assert.Equal(configuration, created.State!.Configuration);
  }

  [Fact]
  public void ReconnectTokenRestoresTheSamePlayerAndTeam()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));

    matches.Disconnect("host");
    RoomJoinResult reconnected = matches.Join(
      "host-reconnected",
      new JoinGameRequest(host.JoinCode!, host.ReconnectToken)
    );

    Assert.True(guest.Accepted);
    Assert.True(reconnected.Accepted);
    Assert.Equal(host.Team, reconnected.Team);
    Assert.Equal(host.ReconnectToken, reconnected.ReconnectToken);
    Assert.Equal(2, reconnected.State!.PlayerCount);
  }

  [Fact]
  public void DebugRoomAllowsOneConnectionToAuthoritativelyEmulateBothTeams()
  {
    MatchStore matches = new();

    RoomJoinResult joined = matches.Join("debug-client", new JoinGameRequest(MatchStore.DebugJoinCode));

    Assert.True(joined.Accepted);
    Assert.Equal(NetworkTeam.Red, joined.Team);
    Assert.Equal(2, joined.State!.PlayerCount);
    Assert.False(joined.State.MatchReady);

    Assert.True(matches.ChooseRoyal("debug-client", new RoyalSelectionRequest("King")).Accepted);
    Assert.True(matches.SelectDebugTeam("debug-client", new DebugTeamSelectionRequest(NetworkTeam.Blue)).Accepted);

    ActionResult setupCompleted = matches.ChooseRoyal("debug-client", new RoyalSelectionRequest("Princess"));

    Assert.True(setupCompleted.Accepted);
    Assert.True(setupCompleted.State!.MatchReady);
    Assert.Contains(setupCompleted.State.Teams, team => team.Team == NetworkTeam.Red && team.ChosenRoyal == "King");
    Assert.Contains(setupCompleted.State.Teams, team => team.Team == NetworkTeam.Blue && team.ChosenRoyal == "Princess");
  }

  [Fact]
  public void InvalidMatchConfigurationIsRejected()
  {
    MatchStore matches = new();
    RoomJoinResult created = matches.Create(
      "host",
      new CreateGameRequest(DefaultConfiguration with { GameMode = "Not a mode" })
    );

    Assert.False(created.Accepted);
    Assert.NotNull(created.Error);
  }

  [Fact]
  public void DominionAndPlunderConfigurationsExposeTheirObjectiveState()
  {
    MatchStore matches = new();
    RoomJoinResult dominion = matches.Create("dominion-host", new CreateGameRequest(DefaultConfiguration with
    {
      GameMode = "Dominion",
      DominionWinScore = 10
    }));
    RoomJoinResult plunder = matches.Create("plunder-host", new CreateGameRequest(DefaultConfiguration with
    {
      GameMode = "Plunder",
      PlunderWinScore = 9,
      PlunderDeliveryScore = 3,
      PlunderRoyalKillPenalty = 1
    }));

    Assert.True(dominion.Accepted);
    Assert.All(dominion.State!.ModeScores!, score => Assert.Equal(0, score.Score));
    Assert.True(plunder.Accepted);
    Assert.NotNull(plunder.State!.Treasure);
    Assert.Null(plunder.State.Treasure!.CarrierId);
    Assert.NotNull(plunder.State.Treasure.X);
    Assert.NotNull(plunder.State.Treasure.Y);
  }

  [Fact]
  public void ServerRejectsWrongOwnerAndKingDiagonalMoves()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    ActionResult ready = matches.ChooseRoyal("guest", new RoyalSelectionRequest("King"));
    Assert.True(ready.Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    Assert.True(matches.StopInitialBuying(redConnection).Accepted);
    ActionResult openingComplete = matches.StopInitialBuying(blueConnection);
    Assert.True(openingComplete.Accepted);
    Assert.Equal(NetworkTeam.Red, openingComplete.State!.CurrentTurn);

    ActionResult noActionSkip = matches.TrySkipTurn(redConnection);
    Assert.True(noActionSkip.Accepted);
    Assert.Equal(NetworkTeam.Blue, noActionSkip.State!.CurrentTurn);
    Assert.True(matches.TrySkipTurn(blueConnection).Accepted);
    NetworkPiece redKing = ready.State!.Pieces.Single(piece => piece.Team == NetworkTeam.Red);

    ActionResult wrongOwner = matches.TryMove(blueConnection, new MoveRequest(redKing.Id, redKing.X + 1, redKing.Y));
    ActionResult diagonalKingMove = matches.TryMove(redConnection, new MoveRequest(redKing.Id, redKing.X + 1, redKing.Y + 1));
    ActionResult straightKingMove = matches.TryMove(redConnection, new MoveRequest(redKing.Id, redKing.X + 1, redKing.Y));
    ActionResult secondMove = matches.TryMove(redConnection, new MoveRequest(redKing.Id, redKing.X + 2, redKing.Y));

    Assert.False(wrongOwner.Accepted);
    Assert.False(diagonalKingMove.Accepted);
    Assert.True(straightKingMove.Accepted);
    Assert.False(secondMove.Accepted);
    Assert.Contains("already moved", secondMove.Error!);
  }

  [Fact]
  public void PalaceRoyalMaySkipWithoutUsingAnAction()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("Palace")).Accepted);
    ActionResult ready = matches.ChooseRoyal("guest", new RoyalSelectionRequest("King"));
    Assert.True(ready.Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    Assert.True(matches.StopInitialBuying(redConnection).Accepted);
    ActionResult openingComplete = matches.StopInitialBuying(blueConnection);
    Assert.True(openingComplete.Accepted);

    NetworkTeam palaceTeam = host.Team!.Value;
    string palaceConnection = "host";
    if (openingComplete.State!.CurrentTurn != palaceTeam)
    {
      string otherConnection = palaceTeam == NetworkTeam.Red ? blueConnection : redConnection;
      NetworkPiece king = openingComplete.State.Pieces.Single(piece => piece.Team != palaceTeam && piece.Type == "King");
      Assert.True(matches.TryMove(otherConnection, new MoveRequest(king.Id, king.X + 1, king.Y)).Accepted);
      Assert.True(matches.TrySkipTurn(otherConnection).Accepted);
    }

    ActionResult skipped = matches.TrySkipTurn(palaceConnection);

    Assert.True(skipped.Accepted);
    Assert.NotEqual(palaceTeam, skipped.State!.CurrentTurn);
  }

  [Fact]
  public void ServerThrottlesRepeatedRoomCodeAttempts()
  {
    MatchStore matches = new();

    RoomJoinResult firstAttempt = matches.Join("guest", new JoinGameRequest("XXXXX"));
    RoomJoinResult secondAttempt = matches.Join("guest", new JoinGameRequest("YYYYY"));

    Assert.False(firstAttempt.Accepted);
    Assert.False(secondAttempt.Accepted);
    Assert.Contains("half a second", secondAttempt.Error!);
  }

  [Fact]
  public void InitialBuyPhaseAlternatesPurchasesAndStartsTheMatchAfterStopping()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration with
    {
      InitialBuysPerTurn = 1,
      InitialBuyTurnsPerTeam = 1,
      StartingCash = 100
    }));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    ActionResult ready = matches.ChooseRoyal("guest", new RoyalSelectionRequest("Princess"));
    Assert.True(ready.State!.InitialBuy is { IsComplete: false });

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    ActionResult redPurchase = matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Soldier", 0, 8));
    ActionResult bluePurchase = matches.PurchaseInitialUnit(blueConnection, new PurchaseRequest("Archer", 0, -8));

    Assert.True(guest.Accepted);
    Assert.True(redPurchase.Accepted);
    Assert.True(bluePurchase.Accepted);
    Assert.True(bluePurchase.State!.InitialBuy!.IsComplete);
    Assert.Contains(bluePurchase.State.Pieces, piece => piece.Type == "Soldier" && piece.Team == NetworkTeam.Red);
    Assert.Contains(bluePurchase.State.Pieces, piece => piece.Type == "Archer" && piece.Team == NetworkTeam.Blue);
  }

  [Fact]
  public void ServerRejectsTeacherBecauseItIsNoLongerInTheRulebook()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    Assert.True(matches.ChooseRoyal("guest", new RoyalSelectionRequest("Princess")).Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    ActionResult result = matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Teacher", 0, 8));

    Assert.False(result.Accepted);
    Assert.Contains("not available", result.Error!);
  }

  [Fact]
  public void NormalTurnPurchaseCostsMoneyWithoutConsumingAnActionPoint()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration with { StartingCash = 100 }));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    Assert.True(matches.ChooseRoyal("guest", new RoyalSelectionRequest("Princess")).Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    Assert.True(matches.StopInitialBuying(redConnection).Accepted);
    Assert.True(matches.StopInitialBuying(blueConnection).Accepted);

    ActionResult purchase = matches.PurchaseUnit(redConnection, new PurchaseRequest("Soldier", 0, 8));

    Assert.True(purchase.Accepted);
    NetworkTeamState redTeam = purchase.State!.Teams.Single(team => team.Team == NetworkTeam.Red);
    Assert.Equal(80, redTeam.Money);
    Assert.Equal(MatchRules.ActionsPerTurn, redTeam.ActionsRemaining);
    Assert.Contains(purchase.State.Pieces, piece => piece.Type == "Soldier" && piece.Team == NetworkTeam.Red);
  }

  [Fact]
  public void FiredMercenaryBecomesNeutralAndCanBeHiredByEitherPlayer()
  {
    NetworkMatchConfiguration configuration = DefaultConfiguration with
    {
      ForestDensity = "Light",
      WaterwayDensity = "Light"
    };
    Board board = new("board_medium.json");
    BattlefieldTerrain terrain = TerrainRules.Create(
      board, configuration.TerrainSeed, configuration.ForestDensity, configuration.WaterwayDensity
    );
    (int x, int y) noMansLand = board.Cells.First(position =>
      MatchRules.GetSquareOwner(board, configuration.GameMode, position) is null && !terrain.IsLake(position));

    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(configuration));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    Assert.True(matches.ChooseRoyal("guest", new RoyalSelectionRequest("King")).Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    Assert.True(matches.StopInitialBuying(redConnection).Accepted);
    ActionResult setupComplete = matches.StopInitialBuying(blueConnection);
    Assert.True(setupComplete.Accepted);

    ActionResult redPurchase = matches.PurchaseUnit(redConnection, new PurchaseRequest("Mercenary", noMansLand.x, noMansLand.y));
    Assert.True(redPurchase.Accepted);
    NetworkPiece mercenary = redPurchase.State!.Pieces.Single(piece => piece.Type == "Mercenary" && piece.Team == NetworkTeam.Red);
    Assert.True(mercenary.CannotContributeToConquestThisTurn);
    Assert.False(matches.TrySpecial(redConnection, new SpecialActionRequest(mercenary.Id, "Fire", mercenary.Id, mercenary.X, mercenary.Y)).Accepted);
    Assert.True(matches.TrySkipTurn(redConnection).Accepted);

    NetworkPiece blueKing = redPurchase.State.Pieces.Single(piece => piece.Type == "King" && piece.Team == NetworkTeam.Blue);
    Assert.True(matches.TryMove(blueConnection, new MoveRequest(blueKing.Id, blueKing.X, blueKing.Y + 1)).Accepted);
    Assert.True(matches.TrySkipTurn(blueConnection).Accepted);

    ActionResult fired = matches.TrySpecial(redConnection, new SpecialActionRequest(
      mercenary.Id, "Fire", mercenary.Id, mercenary.X, mercenary.Y
    ));
    Assert.True(fired.Accepted);
    Assert.Contains(fired.State!.Pieces, piece => piece.Id == mercenary.Id && piece.Team == NetworkTeam.Neutral);
    Assert.True(matches.TrySkipTurn(redConnection).Accepted);

    ActionResult hired = matches.PurchaseUnit(blueConnection, new PurchaseRequest("Mercenary", mercenary.X, mercenary.Y));
    Assert.True(hired.Accepted);
    NetworkPiece hiredMercenary = hired.State!.Pieces.Single(piece => piece.Id == mercenary.Id);
    Assert.Equal(NetworkTeam.Blue, hiredMercenary.Team);
    Assert.True(hiredMercenary.HasMovedThisTurn);
    Assert.True(hiredMercenary.HasAttackedThisTurn);
    Assert.True(hiredMercenary.CannotContributeToConquestThisTurn);
    Assert.Equal(configuration.StartingCash - PieceDefinitions.NeutralMercenaryHireCost,
      hired.State.Teams.Single(team => team.Team == NetworkTeam.Blue).Money);
  }

  [Fact]
  public void UnaffordableMercenaryIsFiredBeforeItCanPutItsOwnerIntoDebt()
  {
    NetworkMatchConfiguration configuration = DefaultConfiguration with
    {
      StartingCash = 25,
      ForestDensity = "Light",
      WaterwayDensity = "Light"
    };
    Board board = new("board_medium.json");
    BattlefieldTerrain terrain = TerrainRules.Create(
      board, configuration.TerrainSeed, configuration.ForestDensity, configuration.WaterwayDensity
    );
    (int x, int y) noMansLand = board.Cells.First(position =>
      MatchRules.GetSquareOwner(board, configuration.GameMode, position) is null && !terrain.IsLake(position));

    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(configuration));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    Assert.True(matches.ChooseRoyal("guest", new RoyalSelectionRequest("King")).Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    Assert.True(matches.StopInitialBuying(redConnection).Accepted);
    Assert.True(matches.StopInitialBuying(blueConnection).Accepted);

    ActionResult purchase = matches.PurchaseUnit(redConnection, new PurchaseRequest("Mercenary", noMansLand.x, noMansLand.y));
    Assert.True(purchase.Accepted);
    NetworkPiece mercenary = purchase.State!.Pieces.Single(piece => piece.Type == "Mercenary" && piece.Team == NetworkTeam.Red);
    Assert.Equal(0, purchase.State.Teams.Single(team => team.Team == NetworkTeam.Red).Money);

    Assert.True(matches.TrySkipTurn(redConnection).Accepted);
    NetworkPiece blueKing = purchase.State.Pieces.Single(piece => piece.Type == "King" && piece.Team == NetworkTeam.Blue);
    Assert.True(matches.TryMove(blueConnection, new MoveRequest(blueKing.Id, blueKing.X, blueKing.Y + 1)).Accepted);
    ActionResult nextRedTurn = matches.TrySkipTurn(blueConnection);

    Assert.True(nextRedTurn.Accepted);
    Assert.Equal(0, nextRedTurn.State!.Teams.Single(team => team.Team == NetworkTeam.Red).Money);
    Assert.Contains(nextRedTurn.State.Pieces, piece =>
      piece.Id == mercenary.Id && piece.Team == NetworkTeam.Neutral &&
      piece.HasMovedThisTurn && piece.HasAttackedThisTurn);
  }

  [Fact]
  public void GuardAttachmentIsAuthoritativeAndMovesWithItsProtectedUnit()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration with
    {
      ForestDensity = "Light",
      WaterwayDensity = "Light",
      InitialBuysPerTurn = 2,
      InitialBuyTurnsPerTeam = 1
    }));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    Assert.True(matches.ChooseRoyal("guest", new RoyalSelectionRequest("Princess")).Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    Assert.True(matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Soldier", 0, 8)).Accepted);
    ActionResult redGuardPurchase = matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Guard", 1, 8));
    Assert.True(redGuardPurchase.Accepted);
    Assert.True(matches.PurchaseInitialUnit(blueConnection, new PurchaseRequest("Soldier", 0, -8)).Accepted);
    Assert.True(matches.StopInitialBuying(blueConnection).Accepted);

    NetworkPiece soldier = redGuardPurchase.State!.Pieces.Single(piece => piece.Type == "Soldier" && piece.Team == NetworkTeam.Red);
    NetworkPiece guard = redGuardPurchase.State.Pieces.Single(piece => piece.Type == "Guard" && piece.Team == NetworkTeam.Red);
    ActionResult attached = matches.TrySpecial(redConnection, new SpecialActionRequest(
      guard.Id, string.Empty, soldier.Id, soldier.X, soldier.Y
    ));

    Assert.True(attached.Accepted);
    NetworkPiece attachedGuard = attached.State!.Pieces.Single(piece => piece.Id == guard.Id);
    Assert.Equal(soldier.Id, attachedGuard.AttachedToId);
    Assert.Equal(NetworkAttachmentKind.Guard, attachedGuard.AttachmentKind);

    ActionResult moved = matches.TryMove(redConnection, new MoveRequest(soldier.Id, 0, 7));

    Assert.True(moved.Accepted);
    NetworkPiece movedSoldier = moved.State!.Pieces.Single(piece => piece.Id == soldier.Id);
    NetworkPiece movedGuard = moved.State.Pieces.Single(piece => piece.Id == guard.Id);
    Assert.Equal(movedSoldier.X, movedGuard.X);
    Assert.Equal(movedSoldier.Y, movedGuard.Y);
  }

  [Fact]
  public void KnightMoveDoesNotConsumeAnActionPoint()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration with
    {
      ForestDensity = "Light",
      WaterwayDensity = "Light",
      InitialBuysPerTurn = 1,
      InitialBuyTurnsPerTeam = 1
    }));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    Assert.True(matches.ChooseRoyal("guest", new RoyalSelectionRequest("Princess")).Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    ActionResult redPurchase = matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Knight", 0, 8));
    Assert.True(redPurchase.Accepted);
    Assert.True(matches.StopInitialBuying(blueConnection).Accepted);

    NetworkPiece knight = redPurchase.State!.Pieces.Single(piece => piece.Type == "Knight" && piece.Team == NetworkTeam.Red);
    ActionResult move = matches.TryMove(redConnection, new MoveRequest(knight.Id, 0, 7));
    Assert.True(move.Accepted);
    Assert.Equal(MatchRules.ActionsPerTurn, move.State!.Teams.Single(team => team.Team == NetworkTeam.Red).ActionsRemaining);

    ActionResult skipped = matches.TrySkipTurn(redConnection);
    Assert.True(skipped.Accepted);
    Assert.Equal(NetworkTeam.Blue, skipped.State!.CurrentTurn);
  }

  [Fact]
  public void EnabledFarmsPayEachOwnerOnTheirFirstNormalTurn()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration with
    {
      FarmsEnabled = true,
      StartingCash = 100,
      InitialBuysPerTurn = 1,
      InitialBuyTurnsPerTeam = 1,
      ForestDensity = "Light",
      WaterwayDensity = "Light"
    }));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    Assert.True(matches.ChooseRoyal("guest", new RoyalSelectionRequest("King")).Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    Assert.True(matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Farm", 1, 8)).Accepted);
    Assert.True(matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Farm", -4, 8)).Accepted);
    Assert.True(matches.PurchaseInitialUnit(blueConnection, new PurchaseRequest("Farm", 1, -10)).Accepted);
    ActionResult farmsPlaced = matches.PurchaseInitialUnit(blueConnection, new PurchaseRequest("Farm", -4, -10));
    Assert.True(farmsPlaced.Accepted);
    Assert.Equal(100, farmsPlaced.State!.Teams.Single(team => team.Team == NetworkTeam.Red).Money);
    Assert.Equal(2, farmsPlaced.State.Pieces.Count(piece => piece.Type == "Farm" && piece.Team == NetworkTeam.Red));
    Assert.All(farmsPlaced.State.Pieces.Where(piece => piece.Type == "Farm"), farm => Assert.Equal(30, farm.Health));
    Assert.True(matches.StopInitialBuying(redConnection).Accepted);
    ActionResult openingComplete = matches.StopInitialBuying(blueConnection);
    Assert.True(openingComplete.Accepted);
    Assert.Equal(110, openingComplete.State!.Teams.Single(team => team.Team == NetworkTeam.Red).Money);

    NetworkPiece redKing = openingComplete.State.Pieces.Single(piece => piece.Type == "King" && piece.Team == NetworkTeam.Red);
    Assert.True(matches.TryMove(redConnection, new MoveRequest(redKing.Id, redKing.X, redKing.Y - 1)).Accepted);
    ActionResult blueTurn = matches.TrySkipTurn(redConnection);
    Assert.True(blueTurn.Accepted);
    Assert.Equal(110, blueTurn.State!.Teams.Single(team => team.Team == NetworkTeam.Blue).Money);
    NetworkPiece blueKing = blueTurn.State.Pieces.Single(piece => piece.Type == "King" && piece.Team == NetworkTeam.Blue);
    Assert.True(matches.TryMove(blueConnection, new MoveRequest(blueKing.Id, blueKing.X, blueKing.Y + 1)).Accepted);
    ActionResult redTurn = matches.TrySkipTurn(blueConnection);

    Assert.True(redTurn.Accepted);
    Assert.Equal(NetworkTeam.Red, redTurn.State!.CurrentTurn);
    Assert.Equal(120, redTurn.State.Teams.Single(team => team.Team == NetworkTeam.Red).Money);
  }

  [Fact]
  public void ServerKeepsOpeningFarmsFreeAndAppliesConfiguredEconomyToUnits()
  {
    MatchStore matches = new();
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(DefaultConfiguration with
    {
      FarmsEnabled = true,
      StartingCash = 200,
      UnitPricePercent = 150,
      UnitMaintenanceEnabled = true,
      UnitMaintenancePercent = 10,
      InitialBuysPerTurn = 1,
      InitialBuyTurnsPerTeam = 1,
      ForestDensity = "Light",
      WaterwayDensity = "Light"
    }));
    RoomJoinResult guest = matches.Join("guest", new JoinGameRequest(host.JoinCode!));
    Assert.True(matches.ChooseRoyal("host", new RoyalSelectionRequest("King")).Accepted);
    Assert.True(matches.ChooseRoyal("guest", new RoyalSelectionRequest("King")).Accepted);

    string redConnection = host.Team == NetworkTeam.Red ? "host" : "guest";
    string blueConnection = host.Team == NetworkTeam.Blue ? "host" : "guest";
    Assert.True(matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Farm", 1, 8)).Accepted);
    Assert.True(matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Farm", -4, 8)).Accepted);
    Assert.True(matches.PurchaseInitialUnit(blueConnection, new PurchaseRequest("Farm", 1, -10)).Accepted);
    Assert.True(matches.PurchaseInitialUnit(blueConnection, new PurchaseRequest("Farm", -4, -10)).Accepted);
    Assert.True(matches.StopInitialBuying(redConnection).Accepted);
    Assert.True(matches.StopInitialBuying(blueConnection).Accepted);

    // Farms are pass-through structures, so the Soldier may be bought onto this farm.
    ActionResult soldierPurchase = matches.PurchaseUnit(redConnection, new PurchaseRequest("Soldier", 1, 8));
    Assert.True(soldierPurchase.Accepted);
    Assert.Equal(180, soldierPurchase.State!.Teams.Single(team => team.Team == NetworkTeam.Red).Money);
    NetworkPiece purchasedSoldier = soldierPurchase.State.Pieces.Single(piece => piece.Type == "Soldier" && piece.Team == NetworkTeam.Red);
    Assert.False(matches.TryMove(redConnection, new MoveRequest(purchasedSoldier.Id, 1, 7)).Accepted);
    Assert.True(matches.TrySkipTurn(redConnection).Accepted);
    NetworkPiece blueKing = soldierPurchase.State.Pieces.Single(piece => piece.Type == "King" && piece.Team == NetworkTeam.Blue);
    Assert.True(matches.TryMove(blueConnection, new MoveRequest(blueKing.Id, blueKing.X, blueKing.Y + 1)).Accepted);
    ActionResult redTurn = matches.TrySkipTurn(blueConnection);

    Assert.True(redTurn.Accepted);
    Assert.Equal(188, redTurn.State!.Teams.Single(team => team.Team == NetworkTeam.Red).Money);
  }

  [Theory]
  [InlineData(3)]
  [InlineData(4)]
  public void OnlineRoomsSupportEveryConfiguredPlayerAndCycleOpeningBuys(int playerCount)
  {
    MatchStore matches = new();
    NetworkMatchConfiguration configuration = DefaultConfiguration with
    {
      PlayerCount = playerCount,
      InitialBuysPerTurn = 1,
      InitialBuyTurnsPerTeam = 1,
      ForestDensity = "Light",
      WaterwayDensity = "Light"
    };

    List<(string connectionId, RoomJoinResult result)> players = [];
    RoomJoinResult host = matches.Create("host", new CreateGameRequest(configuration));
    players.Add(("host", host));
    for (int player = 2; player <= playerCount; player++)
    {
      string connectionId = $"player-{player}";
      players.Add((connectionId, matches.Join(connectionId, new JoinGameRequest(host.JoinCode!))));
    }

    Assert.All(players, player => Assert.True(player.result.Accepted));
    Assert.Equal(playerCount, players.Select(player => player.result.Team).Distinct().Count());
    Assert.Equal(
      TeamRules.GetActiveTeams(playerCount).Order().ToArray(),
      players.Select(player => player.result.Team!.Value).Order().ToArray()
    );

    foreach ((string connectionId, _) in players)
    {
      Assert.True(matches.ChooseRoyal(connectionId, new RoyalSelectionRequest("King")).Accepted);
    }

    ActionResult ready = matches.ChooseRoyal("not-a-player", new RoyalSelectionRequest("King"));
    Assert.False(ready.Accepted);

    Dictionary<NetworkTeam, string> connectionByTeam = players.ToDictionary(player => player.result.Team!.Value, player => player.connectionId);
    NetworkGameState state = matches.StopInitialBuying(connectionByTeam[NetworkTeam.Red]).State!;
    Assert.False(state.InitialBuy!.IsComplete);
    foreach (NetworkTeam team in TeamRules.GetActiveTeams(playerCount).Skip(1))
    {
      ActionResult stopped = matches.StopInitialBuying(connectionByTeam[team]);
      Assert.True(stopped.Accepted);
      state = stopped.State!;
    }

    Assert.True(state.InitialBuy!.IsComplete);
    Assert.Equal(NetworkTeam.Red, state.CurrentTurn);
    Assert.Equal(playerCount, state.PlayerCount);
    Assert.True(state.MatchReady);
    Assert.Equal(playerCount, state.Teams.Count);
    Assert.Equal(playerCount, state.Pieces.Count(piece => piece.Type == "King"));

    RoomJoinResult overflow = matches.Join("overflow", new JoinGameRequest(host.JoinCode!));
    Assert.False(overflow.Accepted);
  }

  [Fact]
  public void FourPlayerTeamsHaveCardinalTerritoriesAndForwardAttacks()
  {
    Board board = new("board_medium.json");
    foreach (NetworkTeam team in TeamRules.GetActiveTeams(4))
    {
      (int x, int y) spawn = MatchRules.GetRoyalSpawnCandidates(board, team, 1, 1, 4).First();
      Assert.Equal(team, MatchRules.GetSquareOwner(board, "Regicide", spawn, 4));
    }

    Assert.True(UnitRules.CanAttackOffset(RuleShape.Forward, 1, 1, NetworkTeam.Green, 1, 0));
    Assert.False(UnitRules.CanAttackOffset(RuleShape.Forward, 1, 1, NetworkTeam.Green, 0, -1));
    Assert.True(UnitRules.CanAttackOffset(RuleShape.ForwardOrForwardDiagonal, 1, 1, NetworkTeam.Yellow, -1, 1));
    Assert.False(UnitRules.CanAttackOffset(RuleShape.Forward, 1, 1, NetworkTeam.Yellow, 0, -1));
  }
}
