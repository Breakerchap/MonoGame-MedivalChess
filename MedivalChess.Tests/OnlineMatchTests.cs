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
    15
  );

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

  [Fact]
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
    Assert.True(matches.StopInitialBuying(blueConnection).Accepted);
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
  public void NormalTurnPurchaseCostsMoneyAndOneAction()
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
    Assert.Equal(2, redTeam.ActionsRemaining);
    Assert.Contains(purchase.State.Pieces, piece => piece.Type == "Soldier" && piece.Team == NetworkTeam.Red);
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
  public void CavalierMayEndItsOnlineMoveActivationWithoutAttacking()
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
    ActionResult redPurchase = matches.PurchaseInitialUnit(redConnection, new PurchaseRequest("Cavalier", 0, 8));
    Assert.True(redPurchase.Accepted);
    Assert.True(matches.StopInitialBuying(blueConnection).Accepted);

    NetworkPiece cavalier = redPurchase.State!.Pieces.Single(piece => piece.Type == "Cavalier" && piece.Team == NetworkTeam.Red);
    ActionResult move = matches.TryMove(redConnection, new MoveRequest(cavalier.Id, 0, 7));
    Assert.True(move.Accepted);
    Assert.Equal(3, move.State!.Teams.Single(team => team.Team == NetworkTeam.Red).ActionsRemaining);

    ActionResult complete = matches.TryCompleteCavalierActivation(
      redConnection,
      new CompleteCavalierActivationRequest(cavalier.Id)
    );

    Assert.True(complete.Accepted);
    Assert.Equal(2, complete.State!.Teams.Single(team => team.Team == NetworkTeam.Red).ActionsRemaining);
  }
}
