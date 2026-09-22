using MedivalChess.CPU;
using MedivalChess.Server;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class UnitStressTests
{
  private static readonly PieceDefinition[] InScopeUnits = PieceDefinitions.All
    .Where(definition => definition.Pack is not (Pack.Legacy or Pack.Chess))
    .OrderBy(definition => definition.Type.ToString(), StringComparer.Ordinal)
    .ToArray();

  [Fact]
  public void EveryInScopeUnitHasConsistentAuthoritativeSharedRules()
  {
    Assert.NotEmpty(InScopeUnits);
    Assert.Equal(
      InScopeUnits.Length,
      InScopeUnits.Select(definition => definition.Type).Distinct().Count());
    Assert.Equal(
      InScopeUnits.Length,
      InScopeUnits.Select(definition => definition.SourceUnitId).Distinct(StringComparer.Ordinal).Count());

    foreach (PieceDefinition definition in InScopeUnits)
    {
      Assert.True(UnitRules.TryGet(definition.Type.ToString(), out UnitRule? byType),
        $"Missing UnitRule for type {definition.Type} ({definition.SourceUnitId}).");
      Assert.True(UnitRules.TryGet(definition.SourceUnitId, out UnitRule? bySource),
        $"Missing UnitRule for source {definition.SourceUnitId}.");
      Assert.Same(byType, bySource);

      Assert.Equal(definition.Attack, byType.Attack);
      Assert.Equal(definition.Health, byType.Health);
      Assert.Equal(definition.Size.x, byType.Width);
      Assert.Equal(definition.Size.y, byType.Height);
      Assert.Equal(definition.Movement.Minimum, byType.MinimumMoveRange);
      Assert.Equal(definition.Movement.Maximum, byType.MoveRange);
      Assert.Equal(definition.AttackRange.Minimum, byType.MinimumAttackRange);
      Assert.Equal(definition.AttackRange.Maximum, byType.AttackRange);
      Assert.Equal(definition.Cost, byType.Cost);

      Assert.True(byType.Width > 0 && byType.Height > 0, definition.SourceUnitId);
      Assert.True(byType.Health > 0, definition.SourceUnitId);
      Assert.True(byType.Attack >= 0, definition.SourceUnitId);
      Assert.True(byType.Cost >= 0, definition.SourceUnitId);
      Assert.InRange(byType.MinimumMoveRange, 0, byType.MoveRange);
      Assert.InRange(byType.MinimumAttackRange, 0, byType.AttackRange);
    }
  }

  [Fact]
  public void EveryInScopeUnitCanGenerateAndApplyCpuActionsWithoutThrowing()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    CpuActionGenerator generator = new();

    foreach (PieceDefinition definition in InScopeUnits)
    {
      UnitRule rule = UnitRules.GetRequired(definition.Type.ToString());
      NetworkPiece subject = new(
        $"red-{definition.Type}",
        definition.Type.ToString(),
        NetworkTeam.Red,
        0,
        5,
        rule.Health);
      NetworkPiece enemy = new(
        $"blue-target-{definition.Type}",
        nameof(PieceType.King),
        NetworkTeam.Blue,
        7,
        -7,
        UnitRules.GetRequired(nameof(PieceType.King)).Health);
      CpuGameState state = CreateCpuState(configuration, [subject, enemy]);

      IReadOnlyDictionary<(int x, int y), List<(int x, int y)>> movement =
        CpuGameRules.GetLegalMovementPaths(state, subject);
      foreach (KeyValuePair<(int x, int y), List<(int x, int y)>> destination in movement.Take(40))
      {
        Assert.True(BoardRules.FootprintFitsBoard(
          state.Board,
          destination.Key.x,
          destination.Key.y,
          rule.Width,
          rule.Height),
          $"{definition.SourceUnitId} generated off-board move {destination.Key}.");
        MoveAction move = new(
          NetworkTeam.Red,
          subject.Id,
          destination.Key.x,
          destination.Key.y);
        Assert.True(move.IsLegal(state),
          $"{definition.SourceUnitId}: generated movement path but MoveAction rejected {move.Describe()}.");
        CpuGameState moved = move.Apply(state);
        AssertStateInvariants(moved, definition.SourceUnitId);
      }

      IReadOnlyList<ICpuGameAction> actions =
        generator.GenerateSearchActions(state, NetworkTeam.Red, purchasePlacementLimit: 3);
      Assert.All(actions, action =>
        Assert.True(action.IsLegal(state),
          $"{definition.SourceUnitId}: generator returned illegal {action.Describe()}."));

      foreach (ICpuGameAction action in actions.Take(30))
      {
        CpuGameState result = action.Apply(state);
        AssertStateInvariants(result, $"{definition.SourceUnitId}: {action.Describe()}");
      }
    }
  }

  [Fact]
  public void RandomMixedUnitRostersGenerateOnlySafeCpuActionsAcrossManySeeds()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    CpuActionGenerator generator = new();
    (int x, int y)[] redPositions =
    [
      (-8, 7), (-4, 7), (0, 7), (4, 7), (8, 7),
      (-8, 3), (-4, 3), (0, 3)
    ];
    (int x, int y)[] bluePositions =
    [
      (-8, -9), (-4, -9), (0, -9), (4, -9), (8, -9),
      (-8, -5), (-4, -5), (0, -5)
    ];

    for (int seed = 0; seed < 24; seed++)
    {
      Random random = new(seed * 7919 + 17);
      List<NetworkPiece> pieces = [];
      for (int index = 0; index < redPositions.Length; index++)
      {
        PieceDefinition definition = InScopeUnits[random.Next(InScopeUnits.Length)];
        UnitRule rule = UnitRules.GetRequired(definition.Type.ToString());
        (int x, int y) position = redPositions[index];
        pieces.Add(new NetworkPiece(
          $"r-{seed}-{index}",
          definition.Type.ToString(),
          NetworkTeam.Red,
          position.x,
          position.y,
          rule.Health));
      }
      for (int index = 0; index < bluePositions.Length; index++)
      {
        PieceDefinition definition = InScopeUnits[random.Next(InScopeUnits.Length)];
        UnitRule rule = UnitRules.GetRequired(definition.Type.ToString());
        (int x, int y) position = bluePositions[index];
        pieces.Add(new NetworkPiece(
          $"b-{seed}-{index}",
          definition.Type.ToString(),
          NetworkTeam.Blue,
          position.x,
          position.y,
          rule.Health));
      }

      CpuGameState state = CreateCpuState(configuration, pieces);
      AssertStateInvariants(state, $"seed {seed} initial");

      IReadOnlyList<ICpuGameAction> actions =
        generator.GenerateSearchActions(state, NetworkTeam.Red, purchasePlacementLimit: 8);
      Assert.NotEmpty(actions);
      Assert.All(actions, action =>
        Assert.True(action.IsLegal(state),
          $"seed {seed}: generator returned illegal {action.Describe()}."));

      foreach (ICpuGameAction action in actions.Take(50))
      {
        CpuGameState result = action.Apply(state);
        AssertStateInvariants(result, $"seed {seed}: {action.Describe()}");
      }
    }
  }

  [Fact]
  public void EverySelectableInScopeRoyalCompletesAuthoritativeServerSetup()
  {
    foreach (PieceDefinition royal in PieceDefinitions.Royals
      .Where(definition =>
        definition.Pack is not (Pack.Legacy or Pack.Chess) &&
        definition.IsPurchasable)
      .OrderBy(definition => definition.Type.ToString(), StringComparer.Ordinal))
    {
      NetworkMatchConfiguration configuration = CreateConfiguration();
      MatchStore matches = new();
      string hostConnection = $"host-{royal.Type}";
      string guestConnection = $"guest-{royal.Type}";
      RoomJoinResult host = matches.Create(
        hostConnection,
        new CreateGameRequest(configuration));
      Assert.True(host.Accepted, $"{royal.Type}: {host.Error}");
      RoomJoinResult guest = matches.Join(
        guestConnection,
        new JoinGameRequest(host.JoinCode!));
      Assert.True(guest.Accepted, $"{royal.Type}: {guest.Error}");

      ActionResult guestRoyal = matches.ChooseRoyal(
        guestConnection,
        new RoyalSelectionRequest(nameof(PieceType.King)));
      Assert.True(guestRoyal.Accepted, $"{royal.Type}: guest setup failed: {guestRoyal.Error}");

      ActionResult hostRoyal = matches.ChooseRoyal(
        hostConnection,
        new RoyalSelectionRequest(royal.Type.ToString()));
      Assert.True(hostRoyal.Accepted, $"{royal.Type}: royal setup failed: {hostRoyal.Error}");

      if (royal.Type == PieceType.Sheriff)
      {
        Assert.False(hostRoyal.State!.MatchReady);
        NetworkPiece sheriff = hostRoyal.State.Pieces.Single(piece =>
          piece.Team == host.Team &&
          piece.Type == nameof(PieceType.Sheriff));
        Assert.True(AdvancedAbilityRules.IsAwaitingSheriffPrison(sheriff.AbilityState));

        (int x, int y) prisonPosition = FindSheriffPrisonPlacement(
          hostRoyal.State, host.Team!.Value);
        ActionResult prisonPlaced = matches.ChooseRoyal(
          hostConnection,
          new RoyalSelectionRequest(
            nameof(PieceType.Sheriff),
            prisonPosition.x,
            prisonPosition.y));
        Assert.True(prisonPlaced.Accepted, prisonPlaced.Error);

        NetworkPiece linkedSheriff = prisonPlaced.State!.Pieces.Single(piece =>
          piece.Id == sheriff.Id);
        NetworkPiece prison = prisonPlaced.State.Pieces.Single(piece =>
          piece.Type == nameof(PieceType.Prison) &&
          piece.Team == host.Team &&
          AdvancedAbilityRules.IsSheriffPrison(piece.AbilityState));
        Assert.Equal(prison.Id, linkedSheriff.AbilityState?.LinkedPieceId);
        Assert.Equal(linkedSheriff.Id, prison.AbilityState?.LinkedPieceId);
        Assert.True(prisonPlaced.State.MatchReady);
      }
      else
      {
        Assert.True(hostRoyal.State!.MatchReady,
          $"{royal.Type} did not complete setup.");
      }
    }
  }

  [Fact]
  public void SheriffPrisonSetupRejectsIllegalPlacementWithoutLosingPendingState()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    MatchStore matches = new();
    RoomJoinResult host = matches.Create(
      "host-sheriff-illegal",
      new CreateGameRequest(configuration));
    RoomJoinResult guest = matches.Join(
      "guest-sheriff-illegal",
      new JoinGameRequest(host.JoinCode!));
    Assert.True(guest.Accepted);
    Assert.True(matches.ChooseRoyal(
      "guest-sheriff-illegal",
      new RoyalSelectionRequest(nameof(PieceType.King))).Accepted);

    ActionResult first = matches.ChooseRoyal(
      "host-sheriff-illegal",
      new RoyalSelectionRequest(nameof(PieceType.Sheriff)));
    Assert.True(first.Accepted);

    ActionResult illegal = matches.ChooseRoyal(
      "host-sheriff-illegal",
      new RoyalSelectionRequest(nameof(PieceType.Sheriff), 999, 999));
    Assert.False(illegal.Accepted);

    NetworkPiece stillPending = illegal.State!.Pieces.Single(piece =>
      piece.Team == host.Team &&
      piece.Type == nameof(PieceType.Sheriff));
    Assert.True(AdvancedAbilityRules.IsAwaitingSheriffPrison(stillPending.AbilityState));
    Assert.False(illegal.State.MatchReady);

    (int x, int y) legal = FindSheriffPrisonPlacement(
      illegal.State, host.Team!.Value);
    ActionResult completed = matches.ChooseRoyal(
      "host-sheriff-illegal",
      new RoyalSelectionRequest(nameof(PieceType.Sheriff), legal.x, legal.y));
    Assert.True(completed.Accepted, completed.Error);
    Assert.True(completed.State!.MatchReady);
  }

  [Fact]
  public void PrisonerTurnLocksClearAtNextOwnerTurnButPrisonLinksPersist()
  {
    UnitAbilityState prison = AdvancedAbilityRules.RecordPrisoner(
      AdvancedAbilityRules.MarkSheriffPrison(
        new UnitAbilityState(), "sheriff"),
      "prisoner");
    UnitAbilityState released = new()
    {
      CannotMoveThisTurn = true,
      CannotActThisTurn = true
    };

    UnitAbilityState nextTurn = AdvancedAbilityRules.StartOwnerTurn(
      released, 3, 4, 20);

    Assert.False(nextTurn.CannotMoveThisTurn);
    Assert.False(nextTurn.CannotActThisTurn);
    Assert.True(AdvancedAbilityRules.IsSheriffPrison(prison));
    Assert.Equal("sheriff", prison.LinkedPieceId);
    Assert.Equal(["prisoner"], prison.PrisonerIds);
  }

  private static CpuGameState CreateCpuState(
    NetworkMatchConfiguration configuration,
    IEnumerable<NetworkPiece> pieces) =>
    new(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 600, MatchRules.ActionsPerTurn, nameof(PieceType.King)),
        new CpuTeamState(NetworkTeam.Blue, 600, MatchRules.ActionsPerTurn, nameof(PieceType.King))
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );

  private static NetworkMatchConfiguration CreateConfiguration() =>
    new(
      "Medium",
      "Light",
      "Light",
      "Regicide",
      112358,
      600,
      0.5f,
      0f,
      2,
      2,
      15,
      FarmsEnabled: false,
      PlayerCount: 2,
      TerrainSource: "None",
      PackSelectionMode: PackDraftRules.ManualMode
    );

  private static (int x, int y) FindSheriffPrisonPlacement(
    NetworkGameState state,
    NetworkTeam team)
  {
    UnitRule prison = UnitRules.GetRequired(nameof(PieceType.Prison));
    Board board = BoardRules.GetBoard(state.Configuration);
    foreach ((int x, int y) position in board.Cells.OrderBy(p => p.y).ThenBy(p => p.x))
    {
      if (!BoardRules.CanPlaceForTeam(
            state.Configuration,
            team,
            position.x,
            position.y,
            prison.Width,
            prison.Height))
      {
        continue;
      }

      bool overlaps = state.Pieces.Any(piece =>
        piece.AttachedToId is null &&
        UnitRules.TryGet(piece.Type, out UnitRule? rule) &&
        UnitRules.FootprintsOverlap(
          piece.X,
          piece.Y,
          rule.Width,
          rule.Height,
          position.x,
          position.y,
          prison.Width,
          prison.Height));
      if (!overlaps)
      {
        return position;
      }
    }

    throw new InvalidOperationException("No legal Sheriff Prison placement was found.");
  }

  private static void AssertStateInvariants(CpuGameState state, string context)
  {
    Assert.Equal(
      state.Pieces.Count,
      state.Pieces.Select(piece => piece.Id).Distinct(StringComparer.Ordinal).Count());

    foreach (NetworkPiece piece in state.Pieces)
    {
      Assert.True(piece.Health > 0, $"{context}: living {piece.Id} has {piece.Health} health.");
      Assert.True(UnitRules.TryGet(piece.Type, out UnitRule? rule),
        $"{context}: unknown unit type {piece.Type}.");
      if (piece.AttachedToId is null)
      {
        Assert.True(BoardRules.FootprintFitsBoard(
          state.Board,
          piece.X,
          piece.Y,
          rule.Width,
          rule.Height),
          $"{context}: {piece.Id} footprint is off board at ({piece.X}, {piece.Y}).");
      }
      else
      {
        Assert.Contains(state.Pieces, host => host.Id == piece.AttachedToId);
      }
    }
  }
}
