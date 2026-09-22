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
      Assert.True(byType.Health >= 0, definition.SourceUnitId);
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

    for (int seed = 0; seed < 24; seed++)
    {
      Random random = new(seed * 7919 + 17);
      List<NetworkPiece> pieces = [];
      for (int index = 0; index < 8; index++)
      {
        PieceDefinition definition = InScopeUnits[random.Next(InScopeUnits.Length)];
        UnitRule rule = UnitRules.GetRequired(definition.Type.ToString());
        (int x, int y) position = FindStressPlacement(
          configuration, definition, pieces, NetworkTeam.Red);
        pieces.Add(new NetworkPiece(
          $"r-{seed}-{index}",
          definition.Type.ToString(),
          NetworkTeam.Red,
          position.x,
          position.y,
          rule.Health));
      }
      for (int index = 0; index < 8; index++)
      {
        PieceDefinition definition = InScopeUnits[random.Next(InScopeUnits.Length)];
        UnitRule rule = UnitRules.GetRequired(definition.Type.ToString());
        (int x, int y) position = FindStressPlacement(
          configuration, definition, pieces, NetworkTeam.Blue);
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
  public void SheriffSetupHasALegalPrisonPlacementAcrossGeneratedTerrainMatrix()
  {
    string[] boardSizes = ["Small", "Medium", "Large"];
    string[] densities = ["Light", "Standard", "Heavy"];

    foreach (string boardSize in boardSizes)
    foreach (string forestDensity in densities)
    foreach (string waterwayDensity in densities)
    foreach (int seed in new[] { 101, 2027 })
    {
      NetworkMatchConfiguration configuration = CreateConfiguration() with
      {
        BoardSize = boardSize,
        ForestDensity = forestDensity,
        WaterwayDensity = waterwayDensity,
        TerrainSeed = seed,
        TerrainSource = "Procedural"
      };
      MatchStore matches = new();
      string suffix = $"{boardSize}-{forestDensity}-{waterwayDensity}-{seed}";
      string hostConnection = $"host-matrix-{suffix}";
      string guestConnection = $"guest-matrix-{suffix}";
      RoomJoinResult host = matches.Create(
        hostConnection, new CreateGameRequest(configuration));
      Assert.True(host.Accepted, suffix);
      RoomJoinResult guest = matches.Join(
        guestConnection, new JoinGameRequest(host.JoinCode!));
      Assert.True(guest.Accepted, suffix);
      Assert.True(matches.ChooseRoyal(
        guestConnection,
        new RoyalSelectionRequest(nameof(PieceType.King))).Accepted, suffix);

      ActionResult sheriffPlaced = matches.ChooseRoyal(
        hostConnection,
        new RoyalSelectionRequest(nameof(PieceType.Sheriff)));
      Assert.True(sheriffPlaced.Accepted, $"{suffix}: {sheriffPlaced.Error}");
      Assert.False(sheriffPlaced.State!.MatchReady);

      (int x, int y) prisonPosition = FindSheriffPrisonPlacement(
        sheriffPlaced.State, host.Team!.Value);
      ActionResult prisonPlaced = matches.ChooseRoyal(
        hostConnection,
        new RoyalSelectionRequest(
          nameof(PieceType.Sheriff),
          prisonPosition.x,
          prisonPosition.y));
      Assert.True(prisonPlaced.Accepted,
        $"{suffix}: {prisonPlaced.Error} at {prisonPosition}");
      Assert.True(prisonPlaced.State!.MatchReady, suffix);
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

  [Theory]
  [InlineData(PieceType.Guard, NetworkAttachmentKind.Guard)]
  [InlineData(PieceType.Shieldsman, NetworkAttachmentKind.Shieldsman)]
  [InlineData(PieceType.Shadow, NetworkAttachmentKind.Shadow)]
  [InlineData(PieceType.Muse, NetworkAttachmentKind.Muse)]
  [InlineData(PieceType.Succubus, NetworkAttachmentKind.Succubus)]
  [InlineData(PieceType.Imp, NetworkAttachmentKind.Imp)]
  [InlineData(PieceType.Swordsman, NetworkAttachmentKind.Passenger)]
  [InlineData(PieceType.Swordsman, NetworkAttachmentKind.Prisoner)]
  [InlineData(PieceType.Swordsman, NetworkAttachmentKind.Carried)]
  public void CpuAttachedUnitsCannotMoveIndependently(
    PieceType type,
    NetworkAttachmentKind attachmentKind)
  {
    UnitRule rule = UnitRules.GetRequired(type.ToString());
    CpuGameState state = CreateCpuState(
      CreateConfiguration(),
      [
        new NetworkPiece("host", nameof(PieceType.King), NetworkTeam.Red, 0, 0, 190),
        new NetworkPiece(
          "attached", type.ToString(), NetworkTeam.Red, 0, 0, rule.Health,
          AttachedToId: "host",
          AttachmentKind: attachmentKind),
        new NetworkPiece("enemy", nameof(PieceType.King), NetworkTeam.Blue, 6, -6, 190)
      ]);

    Assert.Empty(CpuGameRules.GetLegalMovementPaths(
      state, state.Pieces.Single(piece => piece.Id == "attached")));
    Assert.DoesNotContain(
      new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red),
      action => action is MoveAction { PieceId: "attached" });
  }

  [Fact]
  public void CarriedOxCanMoveAndDetachesInCpuSimulation()
  {
    UnitRule oxRule = UnitRules.GetRequired(nameof(PieceType.Ox));
    CpuGameState state = CreateCpuState(
      CreateConfiguration(),
      [
        new NetworkPiece("host", nameof(PieceType.Swordsman), NetworkTeam.Red, 0, 0, 30),
        new NetworkPiece(
          "ox", nameof(PieceType.Ox), NetworkTeam.Red, 0, 0, oxRule.Health,
          AttachedToId: "host",
          AttachmentKind: NetworkAttachmentKind.Carried),
        new NetworkPiece("enemy", nameof(PieceType.King), NetworkTeam.Blue, 6, -6, 190)
      ]);

    KeyValuePair<(int x, int y), List<(int x, int y)>> destination =
      Assert.Single(CpuGameRules.GetLegalMovementPaths(
        state, state.Pieces.Single(piece => piece.Id == "ox")).Take(1));
    MoveAction move = new(
      NetworkTeam.Red, "ox", destination.Key.x, destination.Key.y);
    Assert.True(move.IsLegal(state));

    CpuGameState moved = move.Apply(state);
    NetworkPiece ox = moved.Pieces.Single(piece => piece.Id == "ox");
    Assert.Null(ox.AttachedToId);
    Assert.Equal(NetworkAttachmentKind.None, ox.AttachmentKind);
  }

  [Fact]
  public void PrisonerSuccubusCannotMoveAttackOrUseItsAttachedAbility()
  {
    CpuGameState state = CreateCpuState(
      CreateConfiguration(),
      [
        new NetworkPiece(
          "prison", nameof(PieceType.Prison), NetworkTeam.Blue, 4, 4, 65,
          AbilityState: AdvancedAbilityRules.RecordPrisoner(
            AdvancedAbilityRules.MarkSheriffPrison(
              new UnitAbilityState(), "sheriff"),
            "succubus")),
        new NetworkPiece(
          "succubus", nameof(PieceType.Succubus), NetworkTeam.Red, 4, 4, 30,
          AttachedToId: "prison",
          AttachmentKind: NetworkAttachmentKind.Prisoner),
        new NetworkPiece(
          "enemy", nameof(PieceType.Swordsman), NetworkTeam.Blue, 4, 3, 30)
      ]);

    Assert.False(new MoveAction(NetworkTeam.Red, "succubus", 5, 4).IsLegal(state));
    Assert.False(new AttackAction(
      NetworkTeam.Red, "succubus", "enemy", 4, 3).IsLegal(state));
    Assert.False(new UseAbilityAction(
      NetworkTeam.Red, "succubus", "Detach", null, 5, 4).IsLegal(state));

    IReadOnlyList<ICpuGameAction> actions =
      new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red);
    Assert.DoesNotContain(actions, action => action switch
    {
      MoveAction move => move.PieceId == "succubus",
      AttackAction attack => attack.AttackerId == "succubus",
      UseAbilityAction ability => ability.ActorId == "succubus",
      _ => false
    });
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

  private static (int x, int y) FindStressPlacement(
    NetworkMatchConfiguration configuration,
    PieceDefinition definition,
    IReadOnlyList<NetworkPiece> existing,
    NetworkTeam team)
  {
    Board board = BoardRules.GetBoard(configuration);
    UnitRule rule = UnitRules.GetRequired(definition.Type.ToString());
    IEnumerable<(int x, int y)> candidates = team == NetworkTeam.Red
      ? board.Cells.OrderByDescending(position => position.y).ThenBy(position => position.x)
      : board.Cells.OrderBy(position => position.y).ThenBy(position => position.x);

    foreach ((int x, int y) position in candidates)
    {
      if (!BoardRules.FootprintFitsBoard(
            board, position.x, position.y, rule.Width, rule.Height))
      {
        continue;
      }

      bool overlaps = existing.Any(piece =>
        UnitRules.TryGet(piece.Type, out UnitRule? existingRule) &&
        UnitRules.FootprintsOverlap(
          piece.X, piece.Y, existingRule.Width, existingRule.Height,
          position.x, position.y, rule.Width, rule.Height));
      if (!overlaps)
      {
        return position;
      }
    }

    throw new InvalidOperationException(
      $"Could not place stress unit {definition.SourceUnitId} for {team}.");
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
    BattlefieldTerrain terrain = TerrainRules.Create(
      board,
      state.Configuration.TerrainSeed,
      state.Configuration.ForestDensity,
      state.Configuration.WaterwayDensity,
      state.Configuration.PlayerCount,
      state.Configuration.TerrainSource,
      state.Configuration.BoardSize,
      state.Configuration.PresetId);

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

      bool blockedByLake = false;
      for (int offsetY = 0; offsetY < prison.Height && !blockedByLake; offsetY++)
      for (int offsetX = 0; offsetX < prison.Width; offsetX++)
      {
        if (terrain.IsLake((position.x + offsetX, position.y + offsetY)))
        {
          blockedByLake = true;
          break;
        }
      }
      if (blockedByLake)
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

    throw new InvalidOperationException(
      $"No legal Sheriff Prison placement was found for {state.Configuration.BoardSize}/" +
      $"{state.Configuration.ForestDensity}/{state.Configuration.WaterwayDensity}/" +
      $"{state.Configuration.TerrainSeed}.");
  }

  private static void AssertStateInvariants(CpuGameState state, string context)
  {
    Assert.Equal(
      state.Pieces.Count,
      state.Pieces.Select(piece => piece.Id).Distinct(StringComparer.Ordinal).Count());

    foreach (NetworkPiece piece in state.Pieces)
    {
      Assert.True(UnitRules.TryGet(piece.Type, out UnitRule? rule),
        $"{context}: unknown unit type {piece.Type}.");
      Assert.True(
        rule.Health == 0 ? piece.Health == 0 : piece.Health > 0,
        $"{context}: living {piece.Id} has invalid {piece.Health} health for base health {rule.Health}.");
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
