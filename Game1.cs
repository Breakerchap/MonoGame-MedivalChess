using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using System;
using System.Collections.Generic;

namespace MedivalChess;

internal sealed class Game1 : Game
{
  private enum Screen
  {
    Title,
    Settings,
    Setup,
    Playing,
    GameOver
  }

  private enum BindingAction
  {
    MoveUp,
    MoveDown,
    MoveLeft,
    MoveRight,
    ZoomIn,
    ZoomOut,
    Buy
  }

  private enum SetupStage
  {
    Economy,
    RoyalSelection
  }

  private readonly GraphicsDeviceManager _graphics;
  private SpriteBatch _spriteBatch;
  private Texture2D _pixel;
  private SpriteFont _pieceLabelFont;
  private UiRenderer _ui;
  private Board _board;
  private readonly PieceSetup pieceSetup = new();
  private List<Team> _teams = [];
  private Piece selectedPiece;
  private Piece _cavalierAwaitingAttack;
  private readonly HashSet<(int x, int y)> _roads = [];
  private readonly Dictionary<(int x, int y), int> _barricades = [];
  private const int noMansLandHalfHeight = 3;
  private const float territoryTintAmount = 0.2f;
  private const int purchasePanelWidth = 380;
  private const int purchasePanelHeight = 470;
  private Vector2 _cameraPosition = Vector2.Zero;
  private float _zoom = 1f;
  private MouseState _previousMouseState;
  private KeyboardState _previousKeyboardState;
  private bool _isPurchaseMode;
  private int _selectedPurchaseIndex;
  private Screen _screen = Screen.Title;
  private TeamName _setupTeam = TeamName.Red;
  private int _selectedRoyalIndex;
  private SetupStage _setupStage = SetupStage.Economy;
  private int _startingCash = Globals.StartingCash;
  private float _killerRefundMultiplier = Globals.KillerDeathRefundMultiplier;
  private float _defeatedTeamRefundMultiplier = Globals.DefeatedTeamDeathRefundMultiplier;
  private TeamName? _winningTeam;
  private BindingAction? _bindingToChange;
  private bool _rotateBoard;
  private Keys _moveUpKey = Keys.W;
  private Keys _moveDownKey = Keys.S;
  private Keys _moveLeftKey = Keys.A;
  private Keys _moveRightKey = Keys.D;
  private Keys _zoomInKey = Keys.E;
  private Keys _zoomOutKey = Keys.Q;
  private Keys _buyKey = Keys.B;

  internal Game1()
  {
    _graphics = new GraphicsDeviceManager(this);
    Content.RootDirectory = "Content";
    IsMouseVisible = true;

    _graphics.PreferredBackBufferWidth = 2560;
    _graphics.PreferredBackBufferHeight = 1440;

    Window.AllowUserResizing = true;
  }

  protected override void Initialize()
  {
    _board = new Board();

    pieceSetup.AddPieces();
    _teams = pieceSetup.CreateTeams();

    base.Initialize();
  }

  protected override void LoadContent()
  {
    _spriteBatch = new SpriteBatch(GraphicsDevice);

    _pixel = new Texture2D(GraphicsDevice, 1, 1);
    _pixel.SetData(new[] { Color.White });
    _pieceLabelFont = Content.Load<SpriteFont>("PieceLabel");
    _ui = new UiRenderer(_spriteBatch, _pixel, _pieceLabelFont);
  }

  protected override void Update(GameTime gameTime)
  {
    float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

    KeyboardState keyboard = Keyboard.GetState();
    MouseState mouse = Mouse.GetState();

    bool wasLeftClick =
      mouse.LeftButton == ButtonState.Pressed &&
      _previousMouseState.LeftButton == ButtonState.Released;

    if (_screen != Screen.Playing)
    {
      UpdateMenu(keyboard, mouse, wasLeftClick);
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      base.Update(gameTime);
      return;
    }

    float cameraSpeed = 500f;
    float zoomSpeed = 1f;

    // Move camera
    if (keyboard.IsKeyDown(_moveLeftKey))
      _cameraPosition.X -= cameraSpeed * deltaTime / _zoom;

    if (keyboard.IsKeyDown(_moveRightKey))
      _cameraPosition.X += cameraSpeed * deltaTime / _zoom;

    if (keyboard.IsKeyDown(_moveUpKey))
      _cameraPosition.Y -= cameraSpeed * deltaTime / _zoom;

    if (keyboard.IsKeyDown(_moveDownKey))
      _cameraPosition.Y += cameraSpeed * deltaTime / _zoom;

    Matrix cameraTransform = CreateCameraTransform();

    Vector2 mouseScreen = mouse.Position.ToVector2();

    // Find which world position is currently under the mouse
    Vector2 mouseWorldBefore = Vector2.Transform(
      mouseScreen,
      Matrix.Invert(cameraTransform)
    );

    // Change zoom
    if (keyboard.IsKeyDown(_zoomInKey))
      _zoom += zoomSpeed * deltaTime * _zoom;

    if (keyboard.IsKeyDown(_zoomOutKey))
      _zoom -= zoomSpeed * deltaTime * _zoom;

    _zoom = MathHelper.Clamp(_zoom, 0.2f, 5f);

    cameraTransform = CreateCameraTransform();

    // Find where the mouse points after zooming
    Vector2 mouseWorldAfter = Vector2.Transform(
      mouseScreen,
      Matrix.Invert(cameraTransform)
    );

    // Move camera so the same world point stays under the mouse
    _cameraPosition += mouseWorldBefore - mouseWorldAfter;

    bool wasRightClick =
      mouse.RightButton == ButtonState.Pressed &&
      _previousMouseState.RightButton == ButtonState.Released;

    bool wasPurchaseModeToggle =
      keyboard.IsKeyDown(_buyKey) &&
      !_previousKeyboardState.IsKeyDown(_buyKey);
    bool wasPreviousPurchasePressed =
      keyboard.IsKeyDown(Keys.Up) &&
      !_previousKeyboardState.IsKeyDown(Keys.Up);
    bool wasNextPurchasePressed =
      keyboard.IsKeyDown(Keys.Down) &&
      !_previousKeyboardState.IsKeyDown(Keys.Down);

    if (wasPurchaseModeToggle)
    {
      _isPurchaseMode = !_isPurchaseMode;
      selectedPiece = null;
    }

    if (_isPurchaseMode && wasPreviousPurchasePressed)
    {
      _selectedPurchaseIndex =
        (_selectedPurchaseIndex - 1 + PieceDefinitions.Purchasable.Length) % PieceDefinitions.Purchasable.Length;
    }

    if (_isPurchaseMode && wasNextPurchasePressed)
    {
      _selectedPurchaseIndex =
        (_selectedPurchaseIndex + 1) % PieceDefinitions.Purchasable.Length;
    }

    bool clickedPurchasePanel =
      wasLeftClick && HandlePurchasePanelClick(mouse.Position);

    if (!clickedPurchasePanel && (wasLeftClick || wasRightClick))
    {
      const int cellSize = 64;
      int boardX = (int)MathF.Floor(mouseWorldBefore.X / cellSize) + _board.MinX;
      int boardY = (int)MathF.Floor(mouseWorldBefore.Y / cellSize) + _board.MinY;
      var targetPosition = (x: boardX, y: boardY);
      Piece pieceAtTarget = pieceSetup.GetPieceAt(targetPosition);

      if (_isPurchaseMode)
      {
        if (wasLeftClick)
        {
          TryPurchaseAndPlace(targetPosition);
        }
      }
      else if (selectedPiece == null)
      {
        if (pieceAtTarget?.Team == Team.CurrentTurn && pieceAtTarget.AttachedTo == null)
        {
          SelectPiece(pieceAtTarget);
        }
      }
      else if (pieceAtTarget == selectedPiece && targetPosition == selectedPiece.Position)
      {
        if (_cavalierAwaitingAttack == selectedPiece)
        {
          CompleteAction();
          _cavalierAwaitingAttack = null;
        }

        selectedPiece = null;
      }
      else if (wasLeftClick && _cavalierAwaitingAttack == selectedPiece)
      {
        CompleteAction();
        _cavalierAwaitingAttack = null;
        selectedPiece = null;
      }
      else if (
        wasLeftClick &&
        pieceAtTarget != null &&
        pieceAtTarget != selectedPiece &&
        pieceAtTarget.Team == Team.CurrentTurn &&
        pieceAtTarget.AttachedTo == null
      )
      {
        SelectPiece(pieceAtTarget);
      }
      else
      {
        bool usedSpecialAbility = wasRightClick &&
          TryUseSpecialAbility(selectedPiece, targetPosition, pieceAtTarget, keyboard);

        if (usedSpecialAbility)
        {
          selectedPiece = null;
        }
        else if (wasLeftClick && _cavalierAwaitingAttack == selectedPiece)
        {
          CompleteAction();
          _cavalierAwaitingAttack = null;
          selectedPiece = null;
        }
        else if (wasLeftClick)
        {
          int arrayX = targetPosition.x - _board.MinX;
          int arrayY = targetPosition.y - _board.MinY;

          bool isBoardCell =
            arrayX >= 0 &&
            arrayX < _board.BoardArray.GetLength(1) &&
            arrayY >= 0 &&
            arrayY < _board.BoardArray.GetLength(0) &&
            _board.BoardArray[arrayY, arrayX] == 1;

          bool isValidMove =
            isBoardCell &&
            Actions.IsValidMovementDestination(selectedPiece, targetPosition) &&
            CanMovePiece(selectedPiece, targetPosition);

          if (isValidMove)
          {
            var previousPosition = selectedPiece.Position;
            MovePieceWithCompanions(selectedPiece, targetPosition);

            if (selectedPiece.Definition.Type == PieceType.Elephant)
            {
              DamageElephantCrossedUnits(selectedPiece, previousPosition);
            }

            Console.WriteLine(
              $"Moved piece to ({boardX}, {boardY})."
            );

            if (selectedPiece.Definition.Type == PieceType.Cavalier)
            {
              _cavalierAwaitingAttack = selectedPiece;
            }
            else
            {
              CompleteAction();
            }
          }

          if (_cavalierAwaitingAttack != selectedPiece)
          {
            selectedPiece = null;
          }
        }
        else if (wasRightClick)
        {
          int arrayX = targetPosition.x - _board.MinX;
          int arrayY = targetPosition.y - _board.MinY;

          bool isBoardCell =
            arrayX >= 0 &&
            arrayX < _board.BoardArray.GetLength(1) &&
            arrayY >= 0 &&
            arrayY < _board.BoardArray.GetLength(0) &&
            _board.BoardArray[arrayY, arrayX] == 1;

          bool isValidAttack =
            isBoardCell &&
            Actions.CanAttackSquare(selectedPiece, targetPosition) &&
            HasClearDirectAttackPath(selectedPiece, targetPosition) &&
            ((selectedPiece.Definition.Attack > 0 &&
              ((pieceAtTarget != null && pieceAtTarget.Team != selectedPiece.Team) ||
               _barricades.ContainsKey(targetPosition))) ||
             CanUseAreaAttack(selectedPiece, targetPosition));

          if (isValidAttack)
          {
            if (selectedPiece.Definition.Type == PieceType.Catapult)
            {
              PerformAreaAttack(selectedPiece, targetPosition);
            }
            else if (selectedPiece.Definition.Type == PieceType.Ballista)
            {
              PerformPiercingAttack(selectedPiece, targetPosition);
            }
            else if (_barricades.ContainsKey(targetPosition))
            {
              DamageBarricade(selectedPiece, targetPosition);
            }
            else
            {
              ResolveDamage(selectedPiece, pieceAtTarget);
            }

            Console.WriteLine(
              $"Attacked at ({boardX}, {boardY})."
            );

            if (_screen == Screen.Playing)
            {
              CompleteAction();
            }

            _cavalierAwaitingAttack = null;
          }

          if (_cavalierAwaitingAttack == selectedPiece)
          {
            CompleteAction();
            _cavalierAwaitingAttack = null;
          }

          selectedPiece = null;
        }
      }
    }

    _previousMouseState = mouse;
    _previousKeyboardState = keyboard;

    base.Update(gameTime);
  }

  private void TryPurchaseAndPlace((int x, int y) targetPosition)
  {
    PieceDefinition definition = PieceDefinitions.Purchasable[_selectedPurchaseIndex];
    Team buyingTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);
    Piece targetPiece = pieceSetup.GetPieceAt(targetPosition);

    if (definition.Type == PieceType.Mercenary &&
        targetPiece?.Definition.Type == PieceType.Mercenary &&
        targetPiece.Team != Team.CurrentTurn)
    {
      long buyoutCost = targetPiece.NextMercenaryBid;
      if (buyoutCost > int.MaxValue || buyingTeam.Money < buyoutCost)
      {
        Console.WriteLine("You cannot afford to outbid that Mercenary.");
        return;
      }

      Team previousOwner = _teams.Find(team => team.TeamName == targetPiece.Team);
      buyingTeam.Money -= (int)buyoutCost;
      previousOwner.Money += (int)buyoutCost;
      targetPiece.Team = Team.CurrentTurn;
      targetPiece.LastBid = (int)buyoutCost;
      _isPurchaseMode = false;

      Console.WriteLine($"{Team.CurrentTurn} bought the Mercenary for {buyoutCost} gold.");
      CompleteAction();
      return;
    }

    bool canPlace =
      CanPlacePiece(definition, targetPosition, Team.CurrentTurn) &&
      buyingTeam.Money >= definition.Cost;

    if (!canPlace)
    {
      Console.WriteLine("Pieces must be placed on an empty square on your side of the board.");
      return;
    }

    Piece boughtPiece = Team.BuyPiece(definition, buyingTeam, targetPosition);
    pieceSetup.AddPiece(boughtPiece);
    _isPurchaseMode = false;

    Console.WriteLine(
      $"Bought and placed {definition.Type} at ({targetPosition.x}, {targetPosition.y})."
    );

    CompleteAction();
  }

  private void CompleteAction()
  {
    Team currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);

    if (currentTeam.SpendAction())
    {
      Team.AdvanceTurn();
      if (Team.CurrentTurn == TeamName.Red)
      {
        foreach (Piece palace in pieceSetup.Pieces)
        {
          if (palace.Definition.Type == PieceType.Palace)
          {
            Team palaceTeam = _teams.Find(team => team.TeamName == palace.Team);
            palaceTeam.Money += 10;
          }
        }
      }
    }
  }

  private static float AdjustRefundMultiplier(float multiplier, float adjustment)
  {
    return Math.Clamp(MathF.Round(multiplier + adjustment, 1), -10f, 10f);
  }

  private bool IsBoardCell(int arrayX, int arrayY)
  {
    return
      arrayX >= 0 &&
      arrayX < _board.BoardArray.GetLength(1) &&
      arrayY >= 0 &&
      arrayY < _board.BoardArray.GetLength(0) &&
      _board.BoardArray[arrayY, arrayX] == 1;
  }

  private bool CanPlacePiece(
    PieceDefinition definition,
    (int x, int y) position,
    TeamName? requiredOwner = null,
    Piece ignoredPiece = null
  )
  {
    for (int y = 0; y < definition.Size.y; y++)
    {
      for (int x = 0; x < definition.Size.x; x++)
      {
        int arrayX = position.x - _board.MinX + x;
        int arrayY = position.y - _board.MinY + y;

        if (!IsBoardCell(arrayX, arrayY))
        {
          return false;
        }

        if (_barricades.ContainsKey((position.x + x, position.y + y)))
        {
          return false;
        }

        if (requiredOwner.HasValue && GetSquareOwner(arrayY) != requiredOwner.Value)
        {
          return false;
        }
      }
    }

    return pieceSetup.IsFootprintClear(definition, position, ignoredPiece);
  }

  private HashSet<(int x, int y)> GetValidMovementHighlightSquares(Piece piece)
  {
    HashSet<(int x, int y)> highlightedSquares = [];

    foreach ((int x, int y) destination in Actions.ValidMovementDestinations(piece))
    {
      if (!CanMovePiece(piece, destination))
      {
        continue;
      }

      for (int footprintY = 0; footprintY < piece.Definition.Size.y; footprintY++)
      {
        for (int footprintX = 0; footprintX < piece.Definition.Size.x; footprintX++)
        {
          highlightedSquares.Add((destination.x + footprintX, destination.y + footprintY));
        }
      }
    }

    return highlightedSquares;
  }

  private HashSet<(int x, int y)> GetValidAttackHighlightSquares(Piece piece)
  {
    HashSet<(int x, int y)> highlightedSquares = [];

    for (int y = 0; y < _board.BoardArray.GetLength(0); y++)
    {
      for (int x = 0; x < _board.BoardArray.GetLength(1); x++)
      {
        if (!IsBoardCell(x, y))
        {
          continue;
        }

        var targetPosition = (x: x + _board.MinX, y: y + _board.MinY);
        Piece pieceAtTarget = pieceSetup.GetPieceAt(targetPosition);
        if (pieceAtTarget?.Team == piece.Team)
        {
          continue;
        }

        if (Actions.CanAttackSquare(piece, targetPosition) && HasClearDirectAttackPath(piece, targetPosition))
        {
          highlightedSquares.Add(targetPosition);
        }
      }
    }

    return highlightedSquares;
  }

  private void SelectPiece(Piece piece)
  {
    if (piece.AttachedTo != null)
    {
      return;
    }

    if (piece.Definition.Type == PieceType.Spy)
    {
      piece.MarkedTarget = null;
    }

    selectedPiece = piece;
    Console.WriteLine($"Selected {selectedPiece.Team} {selectedPiece.Definition.Type}.");
  }

  private static bool AreAdjacent(Piece first, Piece second)
  {
    foreach ((int x, int y) firstSquare in first.OccupiedSquares())
    {
      foreach ((int x, int y) secondSquare in second.OccupiedSquares())
      {
        if (Math.Abs(firstSquare.x - secondSquare.x) + Math.Abs(firstSquare.y - secondSquare.y) == 1)
        {
          return true;
        }
      }
    }

    return false;
  }

  private bool HasAdjacentPieceOfType(Piece piece, PieceType type, TeamName team)
  {
    foreach (Piece candidate in pieceSetup.Pieces)
    {
      if (candidate != piece && candidate.Team == team && candidate.Definition.Type == type && AreAdjacent(piece, candidate))
      {
        return true;
      }
    }

    return false;
  }

  private int GetAttackDamage(Piece attacker, Piece target)
  {
    int damage = attacker.Definition.Attack;

    if (HasAdjacentPieceOfType(attacker, PieceType.Baron, attacker.Team))
    {
      damage += 5;
    }

    foreach (Piece spy in pieceSetup.Pieces)
    {
      if (spy.Definition.Type == PieceType.Spy && spy.MarkedTarget == target)
      {
        damage += 10;
        break;
      }
    }

    return damage;
  }

  private void ResolveDamage(Piece attacker, Piece target, int? damageOverride = null)
  {
    Piece guard = pieceSetup.GetAttachedPiece(target, AttachmentKind.Guard);
    Piece damagedPiece = guard ?? target;
    int damage = damageOverride ?? GetAttackDamage(attacker, target);

    if (HasAdjacentPieceOfType(damagedPiece, PieceType.King, damagedPiece.Team))
    {
      damage = Math.Max(5, damage - 5);
    }

    damagedPiece.CurrentHealth -= damage;
    Console.WriteLine($"{attacker.Definition.Type} dealt {damage} damage to {damagedPiece.Definition.Type}.");

    if (damagedPiece.CurrentHealth > 0)
    {
      return;
    }

    if (damagedPiece.Team == TeamName.Neutral)
    {
      pieceSetup.RemovePiece(damagedPiece);
      return;
    }

    Team attackingTeam = _teams.Find(team => team.TeamName == attacker.Team);
    Team defeatedTeam = _teams.Find(team => team.TeamName == damagedPiece.Team);
    if (Actions.HandlePieceDeath(
      damagedPiece,
      attackingTeam,
      defeatedTeam,
      _killerRefundMultiplier,
      _defeatedTeamRefundMultiplier
    ))
    {
      pieceSetup.RemovePiece(damagedPiece);
      if (damagedPiece.Definition.Category == PieceCategory.Royal)
      {
        _winningTeam = attacker.Team;
        _screen = Screen.GameOver;
      }
    }
  }

  private bool TryUseSpecialAbility(
    Piece actor,
    (int x, int y) targetPosition,
    Piece targetPiece,
    KeyboardState keyboard
  )
  {
    if (actor.Definition.Type == PieceType.Spy &&
        targetPiece != null &&
        targetPiece.Team != actor.Team &&
        Actions.CanAttackSquare(actor, targetPosition))
    {
      actor.MarkedTarget = targetPiece;
      Console.WriteLine($"Spy marked {targetPiece.Definition.Type}.");
      CompleteAction();
      return true;
    }

    if (actor.Definition.Type == PieceType.Teacher &&
        targetPiece != null &&
        targetPiece.Team == actor.Team &&
        targetPiece.Definition.Category != PieceCategory.Royal &&
        Actions.CanAttackSquare(actor, targetPosition))
    {
      PieceDefinition replacementDefinition = PieceDefinitions.Purchasable[_selectedPurchaseIndex];
      Team team = _teams.Find(candidate => candidate.TeamName == actor.Team);
      int conversionCost = Math.Max(0, replacementDefinition.Cost - targetPiece.Definition.Cost);
      if (replacementDefinition.Category != PieceCategory.Royal &&
          replacementDefinition.Type != targetPiece.Definition.Type &&
          team.Money >= conversionCost &&
          CanPlacePiece(replacementDefinition, targetPiece.Position, null, targetPiece))
      {
        float healthPercent = targetPiece.CurrentHealth / (float)Math.Max(1, targetPiece.Definition.Health);
        Piece replacement = new(replacementDefinition, targetPiece.Position, targetPiece.Team)
        {
          CurrentHealth = Math.Max(1, (int)MathF.Ceiling(replacementDefinition.Health * healthPercent))
        };
        team.Money -= conversionCost;
        pieceSetup.ReplacePiece(targetPiece, replacement);
        Console.WriteLine($"Teacher changed {targetPiece.Definition.Type} into {replacementDefinition.Type}.");
        CompleteAction();
        return true;
      }
    }

    if (actor.Definition.Type == PieceType.Engineer &&
        targetPiece == null &&
        Actions.CanAttackSquare(actor, targetPosition) &&
        IsBoardCell(targetPosition.x - _board.MinX, targetPosition.y - _board.MinY) &&
        !_roads.Contains(targetPosition) &&
        !_barricades.ContainsKey(targetPosition))
    {
      if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
      {
        _barricades[targetPosition] = 20;
      }
      else
      {
        _roads.Add(targetPosition);
      }

      CompleteAction();
      return true;
    }

    if (actor.Definition.Type == PieceType.Guard &&
        targetPiece != null &&
        targetPiece.Team == actor.Team &&
        targetPiece.Definition.Category != PieceCategory.Royal &&
        actor.AttachedTo == null &&
        pieceSetup.GetAttachedPiece(targetPiece, AttachmentKind.Guard) == null &&
        Actions.CanAttackSquare(actor, targetPosition))
    {
      pieceSetup.Attach(actor, targetPiece, AttachmentKind.Guard);
      CompleteAction();
      return true;
    }

    if (actor.Definition.Type == PieceType.Ox &&
        targetPiece != null &&
        targetPiece.Team == actor.Team &&
        targetPiece != actor &&
        targetPiece.AttachedTo == null &&
        (targetPiece.Definition.Size == (1, 1) || targetPiece.Definition.Category == PieceCategory.Mechanical) &&
        Actions.CanAttackSquare(actor, targetPosition))
    {
      AttachmentKind kind = targetPiece.Definition.Category == PieceCategory.Mechanical
        ? AttachmentKind.Towed
        : AttachmentKind.Carried;
      pieceSetup.Attach(targetPiece, actor, kind);
      CompleteAction();
      return true;
    }

    return false;
  }

  private bool CanMovePiece(Piece piece, (int x, int y) destination)
  {
    if (!CanPlacePiece(piece.Definition, destination, null, piece))
    {
      return false;
    }

    if (!HasClearMovementPath(piece, destination, piece.Definition.Type == PieceType.Elephant))
    {
      return false;
    }

    Piece towedPiece = pieceSetup.GetAttachedPiece(piece, AttachmentKind.Towed);
    if (towedPiece != null)
    {
      var towedDestination = (
        x: towedPiece.Position.x + destination.x - piece.Position.x,
        y: towedPiece.Position.y + destination.y - piece.Position.y
      );

      if (!CanPlacePiece(towedPiece.Definition, towedDestination, null, towedPiece) ||
          FootprintsOverlap(piece.Definition, destination, towedPiece.Definition, towedDestination) ||
          !HasClearMovementPath(towedPiece, towedDestination, false))
      {
        return false;
      }
    }

    return true;
  }

  private bool HasClearMovementPath(Piece piece, (int x, int y) destination, bool mayCrossEnemyOneByOne)
  {
    int deltaX = destination.x - piece.Position.x;
    int deltaY = destination.y - piece.Position.y;
    int steps = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));

    for (int step = 1; step < steps; step++)
    {
      var intermediatePosition = (
        x: piece.Position.x + (int)MathF.Round(deltaX * step / (float)steps),
        y: piece.Position.y + (int)MathF.Round(deltaY * step / (float)steps)
      );

      foreach ((int x, int y) occupiedSquare in OccupiedSquares(piece.Definition, intermediatePosition))
      {
        if (_barricades.ContainsKey(occupiedSquare))
        {
          return false;
        }

        Piece crossedPiece = pieceSetup.GetPieceAt(occupiedSquare);
        if (crossedPiece == null || crossedPiece == piece)
        {
          continue;
        }

        if (!mayCrossEnemyOneByOne ||
            crossedPiece.Team == piece.Team ||
            crossedPiece.Definition.Size != (1, 1))
        {
          return false;
        }
      }
    }

    return true;
  }

  private static IEnumerable<(int x, int y)> OccupiedSquares(
    PieceDefinition definition,
    (int x, int y) position
  )
  {
    for (int y = 0; y < definition.Size.y; y++)
    {
      for (int x = 0; x < definition.Size.x; x++)
      {
        yield return (position.x + x, position.y + y);
      }
    }
  }

  private static bool FootprintsOverlap(
    PieceDefinition firstDefinition,
    (int x, int y) firstPosition,
    PieceDefinition secondDefinition,
    (int x, int y) secondPosition
  )
  {
    return
      firstPosition.x < secondPosition.x + secondDefinition.Size.x &&
      firstPosition.x + firstDefinition.Size.x > secondPosition.x &&
      firstPosition.y < secondPosition.y + secondDefinition.Size.y &&
      firstPosition.y + firstDefinition.Size.y > secondPosition.y;
  }

  private void MovePieceWithCompanions(Piece piece, (int x, int y) destination)
  {
    var displacement = (
      x: destination.x - piece.Position.x,
      y: destination.y - piece.Position.y
    );
    List<Piece> companions = [];
    if (piece.Definition.Type == PieceType.Emissary)
    {
      foreach (Piece candidate in pieceSetup.Pieces)
      {
        if (companions.Count == 2)
        {
          break;
        }

        if (candidate.Team == piece.Team && candidate != piece && candidate.AttachedTo == null &&
            candidate.Definition.Size == (1, 1) && AreAdjacent(piece, candidate))
        {
          companions.Add(candidate);
        }
      }
    }

    pieceSetup.MovePiece(piece, destination);

    foreach (Piece companion in companions)
    {
      var companionDestination = (
        x: companion.Position.x + displacement.x,
        y: companion.Position.y + displacement.y
      );
      if (CanPlacePiece(companion.Definition, companionDestination, null, companion))
      {
        pieceSetup.MovePiece(companion, companionDestination);
      }
    }
  }

  private void DamageElephantCrossedUnits(Piece elephant, (int x, int y) previousPosition)
  {
    int deltaX = elephant.Position.x - previousPosition.x;
    int deltaY = elephant.Position.y - previousPosition.y;
    int steps = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
    HashSet<Piece> damagedPieces = [];

    for (int step = 1; step < steps; step++)
    {
      var intermediatePosition = (
        x: previousPosition.x + (int)MathF.Round(deltaX * step / (float)steps),
        y: previousPosition.y + (int)MathF.Round(deltaY * step / (float)steps)
      );

      for (int y = 0; y < elephant.Definition.Size.y; y++)
      {
        for (int x = 0; x < elephant.Definition.Size.x; x++)
        {
          Piece crossedPiece = pieceSetup.GetPieceAt((
            intermediatePosition.x + x,
            intermediatePosition.y + y
          ));
          if (crossedPiece != null && crossedPiece.Team != elephant.Team &&
              crossedPiece.Definition.Size == (1, 1) && damagedPieces.Add(crossedPiece))
          {
            ResolveDamage(elephant, crossedPiece, 10);
          }
        }
      }
    }
  }

  private bool CanUseAreaAttack(Piece piece, (int x, int y) targetPosition)
  {
    if (piece.Definition.Type != PieceType.Catapult || !Actions.CanAttackSquare(piece, targetPosition))
    {
      return false;
    }

    for (int y = 0; y < 2; y++)
    {
      for (int x = 0; x < 2; x++)
      {
        if (!IsBoardCell(targetPosition.x - _board.MinX + x, targetPosition.y - _board.MinY + y))
        {
          return false;
        }
      }
    }

    return true;
  }

  private void PerformAreaAttack(Piece attacker, (int x, int y) targetPosition)
  {
    HashSet<Piece> targets = [];
    for (int y = 0; y < 2; y++)
    {
      for (int x = 0; x < 2; x++)
      {
        var areaPosition = (x: targetPosition.x + x, y: targetPosition.y + y);
        if (_barricades.ContainsKey(areaPosition))
        {
          DamageBarricade(attacker, areaPosition);
        }

        Piece target = pieceSetup.GetPieceAt(areaPosition);
        if (target != null)
        {
          targets.Add(target);
        }
      }
    }

    foreach (Piece target in targets)
    {
      ResolveDamage(attacker, target);
    }
  }

  private void PerformPiercingAttack(Piece attacker, (int x, int y) targetPosition)
  {
    foreach ((int x, int y) origin in attacker.OccupiedSquares())
    {
      int deltaX = targetPosition.x - origin.x;
      int deltaY = targetPosition.y - origin.y;
      if ((deltaX == 0 && deltaY == 0) || (deltaX != 0 && deltaY != 0))
      {
        continue;
      }

      int stepX = Math.Sign(deltaX);
      int stepY = Math.Sign(deltaY);
      HashSet<Piece> targets = [];
      for (int distance = 1; distance <= attacker.Definition.AttackShape.range; distance++)
      {
        var position = (x: origin.x + stepX * distance, y: origin.y + stepY * distance);
        if (!IsBoardCell(position.x - _board.MinX, position.y - _board.MinY))
        {
          break;
        }

        if (_barricades.ContainsKey(position))
        {
          DamageBarricade(attacker, position);
          break;
        }

        Piece target = pieceSetup.GetPieceAt(position);
        if (target != null && target.Team != attacker.Team)
        {
          targets.Add(target);
        }
      }

      foreach (Piece target in targets)
      {
        ResolveDamage(attacker, target);
      }

      return;
    }
  }

  private void DamageBarricade(Piece attacker, (int x, int y) position)
  {
    int damage = attacker.Definition.Attack;
    if (HasAdjacentPieceOfType(attacker, PieceType.Baron, attacker.Team))
    {
      damage += 5;
    }

    _barricades[position] -= damage;
    if (_barricades[position] <= 0)
    {
      _barricades.Remove(position);
      Console.WriteLine("Barricade destroyed.");
    }
  }

  private bool HasClearDirectAttackPath(Piece attacker, (int x, int y) targetPosition)
  {
    if (attacker.Definition.AttackShape.shape != Shape.Straight)
    {
      return true;
    }

    foreach ((int x, int y) origin in attacker.OccupiedSquares())
    {
      int deltaX = targetPosition.x - origin.x;
      int deltaY = targetPosition.y - origin.y;
      if ((deltaX == 0 && deltaY == 0) || (deltaX != 0 && deltaY != 0))
      {
        continue;
      }

      int distance = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
      int stepX = Math.Sign(deltaX);
      int stepY = Math.Sign(deltaY);
      bool isClear = true;
      for (int step = 1; step < distance; step++)
      {
        var position = (x: origin.x + stepX * step, y: origin.y + stepY * step);
        if (_barricades.ContainsKey(position))
        {
          isClear = false;
          break;
        }

        Piece blockingPiece = pieceSetup.GetPieceAt(position);
        if (blockingPiece != null &&
            !(attacker.Definition.Type == PieceType.Princess && blockingPiece.Team == attacker.Team))
        {
          isClear = false;
          break;
        }
      }

      if (isClear)
      {
        return true;
      }
    }

    return false;
  }

  private TeamName? GetSquareOwner(int arrayY)
  {
    int centreRow = _board.BoardArray.GetLength(0) / 2;

    if (arrayY < centreRow - noMansLandHalfHeight)
    {
      return TeamName.Blue;
    }

    if (arrayY > centreRow + noMansLandHalfHeight)
    {
      return TeamName.Red;
    }

    return null;
  }

  private Rectangle GetPurchasePanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    return UiLayout.AnchorTopRight(viewport, purchasePanelWidth, purchasePanelHeight, UiTheme.SpaceLg);
  }

  private Rectangle GetPreviousPurchaseButtonBounds()
  {
    Rectangle panel = GetPurchasePanelBounds();
    return new Rectangle(panel.X + UiTheme.SpaceLg, panel.Bottom - 68, 58, UiTheme.ButtonHeight);
  }

  private Rectangle GetNextPurchaseButtonBounds()
  {
    Rectangle panel = GetPurchasePanelBounds();
    return new Rectangle(panel.Right - UiTheme.SpaceLg - 58, panel.Bottom - 68, 58, UiTheme.ButtonHeight);
  }

  private Rectangle GetPurchaseButtonBounds()
  {
    Rectangle panel = GetPurchasePanelBounds();
    return new Rectangle(panel.X + 98, panel.Bottom - 68, panel.Width - 196, UiTheme.ButtonHeight);
  }

  private bool HandlePurchasePanelClick(Point mousePosition)
  {
    if (!GetPurchasePanelBounds().Contains(mousePosition))
    {
      return false;
    }

    if (GetPreviousPurchaseButtonBounds().Contains(mousePosition))
    {
      _selectedPurchaseIndex =
        (_selectedPurchaseIndex - 1 + PieceDefinitions.Purchasable.Length) % PieceDefinitions.Purchasable.Length;
    }
    else if (GetNextPurchaseButtonBounds().Contains(mousePosition))
    {
      _selectedPurchaseIndex =
        (_selectedPurchaseIndex + 1) % PieceDefinitions.Purchasable.Length;
    }
    else if (GetPurchaseButtonBounds().Contains(mousePosition))
    {
      _isPurchaseMode = !_isPurchaseMode;
      selectedPiece = null;
    }

    return true;
  }

  private void DrawPanel(Rectangle bounds, Color fill, Color border)
  {
    _ui.Panel(bounds, fill, border);
  }

  private void DrawProgressBar(Rectangle bounds, float progress, Color fill)
  {
    _ui.ProgressBar(bounds, progress, fill);
  }

  private void DrawWorldRectangle(Rectangle bounds, Color colour, float layerDepth)
  {
    _spriteBatch.Draw(
      _pixel,
      bounds,
      null,
      colour,
      0f,
      Vector2.Zero,
      SpriteEffects.None,
      layerDepth
    );
  }

  private void DrawWorldOutline(Rectangle bounds, Color colour, float layerDepth)
  {
    const int thickness = 3;
    DrawWorldRectangle(new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), colour, layerDepth);
    DrawWorldRectangle(new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), colour, layerDepth);
    DrawWorldRectangle(new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), colour, layerDepth);
    DrawWorldRectangle(new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), colour, layerDepth);
  }

  private Rectangle GetPieceWorldBounds(Piece piece, int cellSize)
  {
    int pieceX = piece.Position.x - _board.MinX;
    int pieceY = piece.Position.y - _board.MinY;
    return new Rectangle(
      pieceX * cellSize,
      pieceY * cellSize,
      piece.Definition.Size.x * cellSize,
      piece.Definition.Size.y * cellSize
    );
  }

  private void DrawPurchasePanel()
  {
    Rectangle panel = GetPurchasePanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    Rectangle previousButton = GetPreviousPurchaseButtonBounds();
    Rectangle nextButton = GetNextPurchaseButtonBounds();
    Rectangle purchaseButton = GetPurchaseButtonBounds();
    PieceDefinition definition = PieceDefinitions.Purchasable[_selectedPurchaseIndex];
    Color teamColour = UiTheme.GetTeamColour(Team.CurrentTurn);

    DrawPanel(panel, UiTheme.Panel, _isPurchaseMode ? UiTheme.Gold : UiTheme.PanelBorder);
    _ui.Text("PURCHASE PIECE", new Vector2(content.X, content.Y), UiTheme.Gold);
    _ui.Divider(content, content.Y + 30);

    Rectangle previewBounds = new(content.X, content.Y + 46, 88, 88);
    string label = UiText.BuildPieceLabel(definition);
    _ui.PiecePreview(previewBounds, teamColour, label);
    float detailX = previewBounds.Right + UiTheme.SpaceMd;
    _ui.Text(definition.Type.ToString().ToUpperInvariant(), new Vector2(detailX, previewBounds.Y + 4), UiTheme.TextPrimary);
    _ui.Text(definition.Category.ToString(), new Vector2(detailX, previewBounds.Y + 31), UiTheme.TextMuted, 0.82f);
    _ui.Text($"{definition.Cost} GOLD", new Vector2(detailX, previewBounds.Y + 56), UiTheme.Gold, 0.84f);

    Rectangle statGrid = new(content.X, previewBounds.Bottom + UiTheme.SpaceLg, content.Width, 150);
    Rectangle leftColumn = UiLayout.HorizontalSlot(statGrid, 2, 0, UiTheme.SpaceSm);
    Rectangle rightColumn = UiLayout.HorizontalSlot(statGrid, 2, 1, UiTheme.SpaceSm);
    int statHeight = 44;
    _ui.StatBlock(new Rectangle(leftColumn.X, statGrid.Y, leftColumn.Width, statHeight), "HEALTH", definition.Health.ToString(), UiTheme.Health);
    _ui.StatBlock(new Rectangle(rightColumn.X, statGrid.Y, rightColumn.Width, statHeight), "ATTACK", definition.Attack.ToString(), UiTheme.Attack);
    _ui.StatBlock(new Rectangle(leftColumn.X, statGrid.Y + 52, leftColumn.Width, statHeight), "MOVE", UiText.FormatAction(definition.Movement), UiTheme.Move);
    _ui.StatBlock(new Rectangle(rightColumn.X, statGrid.Y + 52, rightColumn.Width, statHeight), "RANGE", UiText.FormatAction(definition.AttackShape), UiTheme.TextPrimary);
    _ui.StatBlock(new Rectangle(leftColumn.X, statGrid.Y + 104, leftColumn.Width, statHeight), "SIZE", $"{definition.Size.x} x {definition.Size.y}", UiTheme.TextPrimary);
    _ui.StatBlock(new Rectangle(rightColumn.X, statGrid.Y + 104, rightColumn.Width, statHeight), "TEAM", UiText.GetTeamDisplayName(Team.CurrentTurn), teamColour);

    string purchaseHint = definition.Type == PieceType.Mercenary
      ? "Buy on your side, or outbid a rival Mercenary."
      : "Buy, then select a square on your side.";
    _ui.Text(purchaseHint, new Vector2(content.X, previousButton.Y - 48), UiTheme.TextMuted, 0.76f);
    DrawMenuButton(previousButton, "<", UiButtonTone.Neutral);
    DrawMenuButton(nextButton, ">", UiButtonTone.Neutral);
    DrawMenuButton(
      purchaseButton,
      _isPurchaseMode ? "CANCEL" : "BUY",
      _isPurchaseMode ? UiButtonTone.Danger : UiButtonTone.Primary,
      _isPurchaseMode
    );
  }

  private void DrawCenteredString(string text, Rectangle bounds, Color colour)
  {
    _ui.CenterText(text, bounds, colour);
  }

  private Matrix CreateCameraTransform()
  {
    Vector2 screenCentre = new(
      GraphicsDevice.Viewport.Width / 2f,
      GraphicsDevice.Viewport.Height / 2f
    );
    Matrix rotation = _rotateBoard
      ? Matrix.CreateRotationZ(MathHelper.PiOver2)
      : Matrix.Identity;

    return
      Matrix.CreateTranslation(-_cameraPosition.X, -_cameraPosition.Y, 0)
      * rotation
      * Matrix.CreateScale(_zoom)
      * Matrix.CreateTranslation(screenCentre.X, screenCentre.Y, 0);
  }

  private Rectangle GetTitleButtonBounds(int index)
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    int buttonWidth = Math.Min(360, Math.Max(1, viewport.Width - UiTheme.SpaceXl * 2));
    int menuTop = viewport.Center.Y - 8;
    return new Rectangle(
      viewport.Center.X - buttonWidth / 2,
      menuTop + index * (UiTheme.ButtonHeight + UiTheme.SpaceMd),
      buttonWidth,
      UiTheme.ButtonHeight
    );
  }

  private Rectangle GetSettingsPanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    return UiLayout.Centered(viewport, 660, 620, UiTheme.SpaceLg);
  }

  private Rectangle GetSettingsBindingBounds(int index)
  {
    Rectangle panel = GetSettingsPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    int actionCount = Enum.GetValues<BindingAction>().Length;
    int rowsTop = content.Y + 72;
    int rowsBottom = GetSettingsRotationButtonBounds().Y - UiTheme.SpaceMd;
    int rowHeight = Math.Clamp(
      (rowsBottom - rowsTop - UiTheme.SpaceXs * (actionCount - 1)) / actionCount,
      30,
      44
    );
    return new Rectangle(
      content.X,
      rowsTop + index * (rowHeight + UiTheme.SpaceXs),
      content.Width,
      rowHeight
    );
  }

  private Rectangle GetSettingsRotationButtonBounds()
  {
    Rectangle panel = GetSettingsPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(
      content.X,
      content.Bottom - UiTheme.ButtonHeight * 2 - UiTheme.SpaceSm,
      content.Width,
      UiTheme.ButtonHeight
    );
  }

  private Rectangle GetSettingsBackButtonBounds()
  {
    Rectangle panel = GetSettingsPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Bottom - UiTheme.ButtonHeight, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetSetupPanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    return UiLayout.Centered(viewport, 640, 500, UiTheme.SpaceLg);
  }

  private Rectangle GetSetupPreviousButtonBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, panel.Bottom - 68, 68, UiTheme.ButtonHeight);
  }

  private Rectangle GetSetupNextButtonBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.Right - 68, panel.Bottom - 68, 68, UiTheme.ButtonHeight);
  }

  private Rectangle GetSetupConfirmButtonBounds()
  {
    Rectangle previous = GetSetupPreviousButtonBounds();
    Rectangle next = GetSetupNextButtonBounds();
    return new Rectangle(
      previous.Right + UiTheme.SpaceSm,
      previous.Y,
      Math.Max(1, next.X - previous.Right - UiTheme.SpaceSm * 2),
      UiTheme.ButtonHeight
    );
  }

  private Rectangle GetEconomyRowBounds(int index)
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 92 + index * 60, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetEconomyDecreaseButtonBounds(int index)
  {
    Rectangle row = GetEconomyRowBounds(index);
    return new Rectangle(row.Right - 204, row.Y, 44, row.Height);
  }

  private Rectangle GetEconomyValueBounds(int index)
  {
    Rectangle row = GetEconomyRowBounds(index);
    return new Rectangle(row.Right - 152, row.Y, 100, row.Height);
  }

  private Rectangle GetEconomyIncreaseButtonBounds(int index)
  {
    Rectangle row = GetEconomyRowBounds(index);
    return new Rectangle(row.Right - 44, row.Y, 44, row.Height);
  }

  private void UpdateMenu(KeyboardState keyboard, MouseState mouse, bool wasLeftClick)
  {
    if (_screen == Screen.Settings && _bindingToChange.HasValue)
    {
      foreach (Keys key in keyboard.GetPressedKeys())
      {
        if (!_previousKeyboardState.IsKeyDown(key))
        {
          SetBinding(_bindingToChange.Value, key);
          _bindingToChange = null;
          return;
        }
      }
    }

    if (!wasLeftClick)
    {
      return;
    }

    Point mousePosition = mouse.Position;

    switch (_screen)
    {
      case Screen.Title:
        if (GetTitleButtonBounds(0).Contains(mousePosition))
        {
          _screen = Screen.Setup;
          _setupTeam = TeamName.Red;
          _selectedRoyalIndex = 0;
          _setupStage = SetupStage.Economy;
          _startingCash = Globals.StartingCash;
          _killerRefundMultiplier = Globals.KillerDeathRefundMultiplier;
          _defeatedTeamRefundMultiplier = Globals.DefeatedTeamDeathRefundMultiplier;
          _isPurchaseMode = false;
          Team.ResetTurn();
        }
        else if (GetTitleButtonBounds(1).Contains(mousePosition))
        {
          _screen = Screen.Settings;
        }
        else if (GetTitleButtonBounds(2).Contains(mousePosition))
        {
          Exit();
        }
        break;

      case Screen.Settings:
        for (int index = 0; index < Enum.GetValues<BindingAction>().Length; index++)
        {
          if (GetSettingsBindingBounds(index).Contains(mousePosition))
          {
            _bindingToChange = (BindingAction)index;
            return;
          }
        }

        if (GetSettingsRotationButtonBounds().Contains(mousePosition))
        {
          _rotateBoard = !_rotateBoard;
        }
        else if (GetSettingsBackButtonBounds().Contains(mousePosition))
        {
          _screen = Screen.Title;
        }
        break;

      case Screen.Setup:
        if (_setupStage == SetupStage.Economy)
        {
          if (GetEconomyDecreaseButtonBounds(0).Contains(mousePosition))
          {
            _startingCash = Math.Max(0, _startingCash - 100);
          }
          else if (GetEconomyIncreaseButtonBounds(0).Contains(mousePosition))
          {
            _startingCash = Math.Min(5000, _startingCash + 100);
          }
          else if (GetEconomyDecreaseButtonBounds(1).Contains(mousePosition))
          {
            _killerRefundMultiplier = AdjustRefundMultiplier(_killerRefundMultiplier, -0.1f);
          }
          else if (GetEconomyIncreaseButtonBounds(1).Contains(mousePosition))
          {
            _killerRefundMultiplier = AdjustRefundMultiplier(_killerRefundMultiplier, 0.1f);
          }
          else if (GetEconomyDecreaseButtonBounds(2).Contains(mousePosition))
          {
            _defeatedTeamRefundMultiplier = AdjustRefundMultiplier(_defeatedTeamRefundMultiplier, -0.1f);
          }
          else if (GetEconomyIncreaseButtonBounds(2).Contains(mousePosition))
          {
            _defeatedTeamRefundMultiplier = AdjustRefundMultiplier(_defeatedTeamRefundMultiplier, 0.1f);
          }
          else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
          {
            foreach (Team team in _teams)
            {
              team.Money = _startingCash;
              team.ActionPoints = Team.ActionsPerTurn;
            }

            _setupStage = SetupStage.RoyalSelection;
          }
        }
        else if (GetSetupPreviousButtonBounds().Contains(mousePosition))
        {
          _selectedRoyalIndex =
            (_selectedRoyalIndex - 1 + PieceDefinitions.Royals.Length) % PieceDefinitions.Royals.Length;
        }
        else if (GetSetupNextButtonBounds().Contains(mousePosition))
        {
          _selectedRoyalIndex =
            (_selectedRoyalIndex + 1) % PieceDefinitions.Royals.Length;
        }
        else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
        {
          PieceDefinition royal = PieceDefinitions.Royals[_selectedRoyalIndex];
          Team setupTeam = _teams.Find(team => team.TeamName == _setupTeam);
          setupTeam.ChooseRoyal(royal.Type);
          pieceSetup.AddPiece(new Piece(royal, FindRoyalSpawn(_setupTeam, royal), _setupTeam));

          if (_setupTeam == TeamName.Red)
          {
            _setupTeam = TeamName.Blue;
            _selectedRoyalIndex = 0;
          }
          else
          {
            Team.ResetTurn();
            _screen = Screen.Playing;
          }
        }
        break;

      case Screen.GameOver:
        if (GetTitleButtonBounds(2).Contains(mousePosition))
        {
          Exit();
        }
        break;
    }
  }

  private (int x, int y) FindRoyalSpawn(TeamName teamName, PieceDefinition definition)
  {
    int boardHeight = _board.BoardArray.GetLength(0);
    int firstArrayY = teamName == TeamName.Red
      ? boardHeight - definition.Size.y
      : 0;
    int rowStep = teamName == TeamName.Red ? -1 : 1;
    int centreX = _board.BoardArray.GetLength(1) / 2;

    for (int rowOffset = 0; rowOffset < boardHeight; rowOffset++)
    {
      int arrayY = firstArrayY + rowOffset * rowStep;
      if (arrayY < 0 || arrayY + definition.Size.y > boardHeight)
      {
        continue;
      }

      for (int offset = 0; offset < _board.BoardArray.GetLength(1); offset++)
      {
        int[] candidateXs = offset == 0
          ? [centreX]
          : [centreX - offset, centreX + offset];

        foreach (int arrayX in candidateXs)
        {
          if (arrayX >= 0 && arrayX + definition.Size.x <= _board.BoardArray.GetLength(1))
          {
            var position = (x: arrayX + _board.MinX, y: arrayY + _board.MinY);
            if (CanPlacePiece(definition, position, teamName))
            {
              return position;
            }
          }
        }
      }
    }

    throw new InvalidOperationException("Could not find an empty royal spawn square.");
  }

  private Keys GetBinding(BindingAction action)
  {
    return action switch
    {
      BindingAction.MoveUp => _moveUpKey,
      BindingAction.MoveDown => _moveDownKey,
      BindingAction.MoveLeft => _moveLeftKey,
      BindingAction.MoveRight => _moveRightKey,
      BindingAction.ZoomIn => _zoomInKey,
      BindingAction.ZoomOut => _zoomOutKey,
      BindingAction.Buy => _buyKey,
      _ => Keys.None
    };
  }

  private void SetBinding(BindingAction action, Keys key)
  {
    switch (action)
    {
      case BindingAction.MoveUp: _moveUpKey = key; break;
      case BindingAction.MoveDown: _moveDownKey = key; break;
      case BindingAction.MoveLeft: _moveLeftKey = key; break;
      case BindingAction.MoveRight: _moveRightKey = key; break;
      case BindingAction.ZoomIn: _zoomInKey = key; break;
      case BindingAction.ZoomOut: _zoomOutKey = key; break;
      case BindingAction.Buy: _buyKey = key; break;
    }
  }

  private static string GetBindingLabel(BindingAction action)
  {
    return action switch
    {
      BindingAction.MoveUp => "Move camera up",
      BindingAction.MoveDown => "Move camera down",
      BindingAction.MoveLeft => "Move camera left",
      BindingAction.MoveRight => "Move camera right",
      BindingAction.ZoomIn => "Zoom in",
      BindingAction.ZoomOut => "Zoom out",
      BindingAction.Buy => "Open purchase panel",
      _ => action.ToString()
    };
  }

  private void DrawMenuButton(
    Rectangle bounds,
    string label,
    UiButtonTone tone,
    bool selected = false
  )
  {
    _ui.Button(bounds, label, tone, selected);
  }

  private void DrawTitleScreen()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    Rectangle firstButton = GetTitleButtonBounds(0);
    Rectangle titleBounds = new(viewport.X, Math.Max(UiTheme.SpaceXl, firstButton.Y - 154), viewport.Width, 48);
    Rectangle subtitleBounds = new(viewport.X, titleBounds.Bottom + UiTheme.SpaceSm, viewport.Width, 24);

    _ui.CenterText("MEDIEVAL CHESS", titleBounds, UiTheme.GoldBright, 1.55f);
    _ui.CenterText("A MEDIEVAL STRATEGY GAME", subtitleBounds, UiTheme.TextMuted, 0.72f);
    _ui.Divider(new Rectangle(viewport.Center.X - 150, subtitleBounds.Bottom + UiTheme.SpaceMd, 300, 1), subtitleBounds.Bottom + UiTheme.SpaceMd, UiTheme.PanelBorder);

    DrawMenuButton(GetTitleButtonBounds(0), "START GAME", UiButtonTone.Primary);
    DrawMenuButton(GetTitleButtonBounds(1), "SETTINGS", UiButtonTone.Neutral);
    DrawMenuButton(GetTitleButtonBounds(2), "QUIT GAME", UiButtonTone.Danger);
  }

  private void DrawSettingsScreen()
  {
    Rectangle panel = GetSettingsPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.PanelBorder);
    _ui.Text("SETTINGS", new Vector2(content.X, content.Y), UiTheme.Gold);
    _ui.Text("Select a control to assign a new key.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.76f);
    _ui.Divider(content, content.Y + 56);

    BindingAction[] actions = Enum.GetValues<BindingAction>();
    for (int index = 0; index < actions.Length; index++)
    {
      BindingAction action = actions[index];
      Rectangle bounds = GetSettingsBindingBounds(index);
      bool isWaitingForKey = _bindingToChange == action;
      Rectangle keyBounds = new(bounds.Right - 116, bounds.Y + 5, 100, Math.Max(1, bounds.Height - 10));
      DrawMenuButton(bounds, string.Empty, isWaitingForKey ? UiButtonTone.Accent : UiButtonTone.Neutral, isWaitingForKey);
      _ui.Text(GetBindingLabel(action), new Vector2(bounds.X + UiTheme.SpaceMd, bounds.Center.Y - 10), UiTheme.TextPrimary, 0.82f);
      DrawPanel(
        keyBounds,
        UiTheme.Panel,
        isWaitingForKey ? UiTheme.Gold : UiTheme.PanelBorderSubtle
      );
      _ui.CenterText(
        isWaitingForKey ? "PRESS KEY" : GetBinding(action).ToString(),
        keyBounds,
        isWaitingForKey ? UiTheme.GoldBright : UiTheme.TextPrimary,
        0.72f
      );
    }

    DrawMenuButton(
      GetSettingsRotationButtonBounds(),
      _rotateBoard ? "BOARD ROTATION: 90 DEG" : "BOARD ROTATION: 0 DEG",
      _rotateBoard ? UiButtonTone.Accent : UiButtonTone.Neutral,
      _rotateBoard
    );
    DrawMenuButton(GetSettingsBackButtonBounds(), "BACK", UiButtonTone.Primary);
  }

  private void DrawSetupScreen()
  {
    Rectangle panel = GetSetupPanelBounds();

    if (_setupStage == SetupStage.Economy)
    {
      DrawEconomySetup(panel);
      return;
    }

    PieceDefinition royal = PieceDefinitions.Royals[_selectedRoyalIndex];
    Color teamColour = UiTheme.GetTeamColour(_setupTeam);
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);

    DrawPanel(panel, UiTheme.Panel, teamColour);
    _ui.Text($"{UiText.GetTeamDisplayName(_setupTeam)} CHOOSE YOUR ROYAL", new Vector2(content.X, content.Y), teamColour);
    _ui.Text("Your royal is placed on the back row.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.76f);
    _ui.Divider(content, content.Y + 56);

    Rectangle preview = new(content.X, content.Y + 76, 112, 112);
    string label = UiText.BuildPieceLabel(royal);
    _ui.PiecePreview(preview, teamColour, label);

    Rectangle details = new(preview.Right + UiTheme.SpaceLg, preview.Y, content.Right - preview.Right - UiTheme.SpaceLg, preview.Height);
    _ui.Text(royal.Type.ToString().ToUpperInvariant(), new Vector2(details.X, details.Y), UiTheme.TextPrimary);
    _ui.LabelValueRow(new Rectangle(details.X, details.Y + 30, details.Width, 26), "HEALTH", royal.Health.ToString(), UiTheme.Health);
    _ui.LabelValueRow(new Rectangle(details.X, details.Y + 58, details.Width, 26), "ATTACK", royal.Attack.ToString(), UiTheme.Attack);
    _ui.LabelValueRow(new Rectangle(details.X, details.Y + 86, details.Width, 26), "SIZE", $"{royal.Size.x} x {royal.Size.y}", UiTheme.TextPrimary);

    Rectangle actionGrid = new(content.X, preview.Bottom + UiTheme.SpaceLg, content.Width, 54);
    Rectangle moveStat = UiLayout.HorizontalSlot(actionGrid, 2, 0, UiTheme.SpaceSm);
    Rectangle rangeStat = UiLayout.HorizontalSlot(actionGrid, 2, 1, UiTheme.SpaceSm);
    _ui.StatBlock(moveStat, "MOVE", UiText.FormatAction(royal.Movement), UiTheme.Move);
    _ui.StatBlock(rangeStat, "ATTACK RANGE", UiText.FormatAction(royal.AttackShape), UiTheme.Attack);

    DrawMenuButton(GetSetupPreviousButtonBounds(), "<", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupNextButtonBounds(), ">", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONFIRM", UiButtonTone.Primary);
  }

  private void DrawEconomySetup(Rectangle panel)
  {
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("MATCH ECONOMY", new Vector2(content.X, content.Y), UiTheme.Gold);
    _ui.Text("Set starting resources and unit-death refunds.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.76f);
    _ui.Divider(content, content.Y + 56);

    string[] labels =
    [
      "Starting cash",
      "Killer refund",
      "Defeated team refund"
    ];
    string[] values =
    [
      _startingCash.ToString(),
      $"{_killerRefundMultiplier:0.0}x",
      $"{_defeatedTeamRefundMultiplier:0.0}x"
    ];

    for (int index = 0; index < labels.Length; index++)
    {
      Rectangle row = GetEconomyRowBounds(index);
      Rectangle valueBounds = GetEconomyValueBounds(index);
      DrawPanel(row, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
      _ui.Text(labels[index].ToUpperInvariant(), new Vector2(row.X + UiTheme.SpaceMd, row.Center.Y - 10), UiTheme.TextPrimary, 0.8f);
      DrawMenuButton(GetEconomyDecreaseButtonBounds(index), "-", UiButtonTone.Neutral);
      DrawPanel(valueBounds, UiTheme.Panel, UiTheme.Gold);
      _ui.CenterText(values[index], valueBounds, UiTheme.GoldBright);
      DrawMenuButton(GetEconomyIncreaseButtonBounds(index), "+", UiButtonTone.Neutral);
    }

    _ui.Text("Refunds use the defeated unit's cost.", new Vector2(content.X, GetSetupConfirmButtonBounds().Y - 48), UiTheme.TextMuted, 0.76f);
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONTINUE", UiButtonTone.Primary);
  }

  private void DrawGameOverScreen()
  {
    TeamName winner = _winningTeam ?? TeamName.Red;
    string message = $"{UiText.GetTeamDisplayName(winner)} WINS";
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    Color winnerColour = UiTheme.GetTeamColour(winner);
    _ui.CenterText(message, new Rectangle(viewport.X, viewport.Center.Y - 110, viewport.Width, 42), winnerColour, 1.3f);
    _ui.CenterText("The opposing royal has fallen.", new Rectangle(viewport.X, viewport.Center.Y - 54, viewport.Width, 24), UiTheme.TextPrimary, 0.85f);
    DrawMenuButton(GetTitleButtonBounds(2), "QUIT GAME", UiButtonTone.Danger);
  }

  private void DrawMenuScreen()
  {
    switch (_screen)
    {
      case Screen.Title: DrawTitleScreen(); break;
      case Screen.Settings: DrawSettingsScreen(); break;
      case Screen.Setup: DrawSetupScreen(); break;
      case Screen.GameOver: DrawGameOverScreen(); break;
    }
  }

  private Rectangle GetStatusPanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    int width = Math.Min(360, Math.Max(1, viewport.Width - UiTheme.SpaceLg * 2));
    int height = Math.Min(194, Math.Max(1, viewport.Height - UiTheme.SpaceLg * 2));
    return new Rectangle(UiTheme.SpaceLg, UiTheme.SpaceLg, width, height);
  }

  private Rectangle GetSelectedPiecePanelBounds()
  {
    Rectangle status = GetStatusPanelBounds();
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    int desiredHeight = selectedPiece == null ? 124 : 354;
    int height = Math.Min(desiredHeight, Math.Max(1, viewport.Bottom - status.Bottom - UiTheme.SpaceLg * 2));
    return new Rectangle(status.X, status.Bottom + UiTheme.SpaceMd, status.Width, height);
  }

  private void DrawStatusPanel()
  {
    Rectangle panel = GetStatusPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceMd);
    Team currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);
    Color turnColour = UiTheme.GetTeamColour(Team.CurrentTurn);

    DrawPanel(panel, UiTheme.Panel, turnColour);
    _ui.Text($"{UiText.GetTeamDisplayName(Team.CurrentTurn)} TURN", new Vector2(content.X, content.Y), turnColour);
    _ui.Divider(content, content.Y + 30);
    _ui.Text("ACTIVATION", new Vector2(content.X, content.Y + 43), UiTheme.TextMuted, 0.74f);

    for (int index = 0; index < Team.ActionsPerTurn; index++)
    {
      Rectangle actionPoint = new(content.X + index * 34, content.Y + 66, 26, 12);
      _spriteBatch.Draw(
        _pixel,
        actionPoint,
        index < currentTeam.ActionPoints ? turnColour : UiTheme.PanelBorderSubtle
      );
    }

    _ui.Text(
      currentTeam.ActionPoints > 0 ? "READY" : "ACTIVATED",
      new Vector2(content.X + 116, content.Y + 61),
      UiTheme.TextPrimary,
      0.76f
    );

    int moneyY = content.Y + 94;
    foreach (Team team in _teams)
    {
      Color teamColour = UiTheme.GetTeamColour(team.TeamName);
      Rectangle moneyRow = new(content.X, moneyY, content.Width, 30);
      DrawPanel(moneyRow, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
      _ui.LabelValueRow(moneyRow, $"{UiText.GetTeamDisplayName(team.TeamName)} GOLD", team.Money.ToString(), teamColour);
      moneyY += 36;
    }
  }

  private void DrawSelectedPiecePanel()
  {
    Rectangle panel = GetSelectedPiecePanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceMd);
    DrawPanel(panel, UiTheme.Panel, selectedPiece == null ? UiTheme.PanelBorder : UiTheme.SelectionOutline);

    if (selectedPiece == null)
    {
      _ui.Text("SELECT A PIECE", new Vector2(content.X, content.Y), UiTheme.TextPrimary);
      _ui.Divider(content, content.Y + 30);
      _ui.Text("Gold squares: move", new Vector2(content.X, content.Y + 44), UiTheme.Move, 0.8f);
      _ui.Text("Red squares: attack", new Vector2(content.X, content.Y + 68), UiTheme.Attack, 0.8f);
      return;
    }

    Color teamColour = UiTheme.GetTeamColour(selectedPiece.Team);
    _ui.Text("SELECTED PIECE", new Vector2(content.X, content.Y), UiTheme.Gold);
    _ui.Divider(content, content.Y + 30);

    Rectangle preview = new(content.X, content.Y + 46, 72, 72);
    string label = UiText.BuildPieceLabel(selectedPiece.Definition);
    _ui.PiecePreview(preview, teamColour, label);

    Rectangle details = new(preview.Right + UiTheme.SpaceMd, preview.Y, content.Right - preview.Right - UiTheme.SpaceMd, preview.Height);
    _ui.Text(selectedPiece.Definition.Type.ToString().ToUpperInvariant(), new Vector2(details.X, details.Y), UiTheme.TextPrimary);
    _ui.Text(UiText.GetTeamDisplayName(selectedPiece.Team), new Vector2(details.X, details.Y + 26), teamColour, 0.82f);
    _ui.LabelValueRow(
      new Rectangle(details.X, details.Y + 47, details.Width, 22),
      "HEALTH",
      $"{selectedPiece.CurrentHealth}/{selectedPiece.Definition.Health}",
      UiTheme.Health
    );
    DrawProgressBar(
      new Rectangle(details.X, details.Bottom - 10, details.Width, 10),
      selectedPiece.CurrentHealth / (float)Math.Max(1, selectedPiece.Definition.Health),
      UiTheme.Health
    );

    Rectangle actionGrid = new(content.X, preview.Bottom + UiTheme.SpaceMd, content.Width, 48);
    _ui.StatBlock(
      UiLayout.HorizontalSlot(actionGrid, 2, 0, UiTheme.SpaceSm),
      "MOVE",
      UiText.FormatAction(selectedPiece.Definition.Movement),
      UiTheme.Move
    );
    _ui.StatBlock(
      UiLayout.HorizontalSlot(actionGrid, 2, 1, UiTheme.SpaceSm),
      "ATTACK",
      selectedPiece.Definition.Attack.ToString(),
      UiTheme.Attack
    );
    Rectangle rangeRow = new(content.X, actionGrid.Bottom + UiTheme.SpaceSm, content.Width, 44);
    _ui.StatBlock(rangeRow, "ATTACK RANGE", UiText.FormatAction(selectedPiece.Definition.AttackShape), UiTheme.TextPrimary);
    _ui.Text("LEFT-CLICK gold to move", new Vector2(content.X, rangeRow.Bottom + UiTheme.SpaceMd), UiTheme.Move, 0.78f);
    _ui.Text(GetSelectedPieceControlHint(selectedPiece), new Vector2(content.X, rangeRow.Bottom + UiTheme.SpaceMd + 23), UiTheme.Attack, 0.72f);
    _ui.Text(GetSelectedPieceAbilityHint(selectedPiece), new Vector2(content.X, rangeRow.Bottom + UiTheme.SpaceMd + 44), UiTheme.TextMuted, 0.66f);
  }

  private static string GetSelectedPieceControlHint(Piece piece)
  {
    return piece.Definition.Type is PieceType.Spy or PieceType.Teacher or PieceType.Engineer or PieceType.Guard or PieceType.Ox
      ? "RIGHT-CLICK to use special"
      : "RIGHT-CLICK red to attack";
  }

  private static string GetSelectedPieceAbilityHint(Piece piece)
  {
    return piece.Definition.Type switch
    {
      PieceType.Cavalier => "SPECIAL: move, then attack before ending activation",
      PieceType.Spy => "SPECIAL: mark an enemy for +10 damage",
      PieceType.Teacher => "SPECIAL: change adjacent friendly unit",
      PieceType.Ox => "SPECIAL: carry 1x1 or tow Mechanical",
      PieceType.Engineer => "SPECIAL: empty square is Road; Shift is Barricade",
      PieceType.Ballista => "SPECIAL: attack pierces a straight line",
      PieceType.Elephant => "SPECIAL: cross enemy 1x1 units for 10 damage",
      PieceType.Guard => "SPECIAL: attach to protect a friendly unit",
      PieceType.Mercenary => "SPECIAL: rivals can outbid this unit for double",
      PieceType.King => "AURA: adjacent friendlies take 5 less damage",
      PieceType.Palace => "AURA: gains 10 gold at the start of each round",
      PieceType.Baron => "AURA: adjacent friendlies deal +5 damage",
      PieceType.Emissary => "SPECIAL: moves up to two adjacent 1x1 allies",
      _ => string.Empty
    };
  }

  protected override void Draw(GameTime gameTime)
  {
    GraphicsDevice.Clear(_screen == Screen.Playing ? UiTheme.BoardBackground : UiTheme.MenuBackground);

    if (_screen != Screen.Playing)
    {
      _spriteBatch.Begin();
      DrawMenuScreen();
      _spriteBatch.End();
      base.Draw(gameTime);
      return;
    }

    Matrix cameraTransform = CreateCameraTransform();

    _spriteBatch.Begin(SpriteSortMode.FrontToBack, transformMatrix: cameraTransform);

    /* Build Board */
    var BoardArray = _board.BoardArray;
    int cellSize = 64;
    HashSet<(int x, int y)> validMovementSquares = selectedPiece == null
      ? []
      : GetValidMovementHighlightSquares(selectedPiece);
    HashSet<(int x, int y)> validAttackSquares = selectedPiece == null
      ? []
      : GetValidAttackHighlightSquares(selectedPiece);

    for (int y = 0; y < BoardArray.GetLength(0); y++)
    {
      for (int x = 0; x < BoardArray.GetLength(1); x++)
      {
        if (BoardArray[y, x] == 1)
        {
          var boardPosition = (x: x + _board.MinX, y: y + _board.MinY);
          bool isValidMove = validMovementSquares.Contains(boardPosition);
          bool isValidAttack = validAttackSquares.Contains(boardPosition);

          Color baseCellColour =
            (x + y) % 2 == 0
            ? UiTheme.DarkBoardCell
            : UiTheme.LightBoardCell;
          TeamName? squareOwner = GetSquareOwner(y);
          Color territoryColour =
            squareOwner == TeamName.Red
            ? UiTheme.TeamOrange
            : squareOwner == TeamName.Blue
              ? UiTheme.TeamPurple
              : UiTheme.NoMansLand;

          Rectangle cellBounds = new(x * cellSize, y * cellSize, cellSize, cellSize);
          DrawWorldRectangle(
            cellBounds,
            Color.Lerp(baseCellColour, territoryColour, territoryTintAmount),
            0.1f
          );

          if (_roads.Contains(boardPosition))
          {
            DrawWorldRectangle(
              new Rectangle(cellBounds.X, cellBounds.Center.Y - 5, cellBounds.Width, 10),
              UiTheme.Road,
              0.101f
            );
          }

          if (_barricades.ContainsKey(boardPosition))
          {
            DrawWorldRectangle(
              new Rectangle(cellBounds.X + 8, cellBounds.Y + 16, cellBounds.Width - 16, cellBounds.Height - 32),
              UiTheme.Barricade,
              0.11f
            );
          }

          if (isValidMove)
          {
            DrawWorldRectangle(cellBounds, UiTheme.MoveOverlay, 0.102f);
          }

          if (isValidAttack)
          {
            DrawWorldOutline(cellBounds, UiTheme.AttackOutline, 0.103f);
          }

        }
      }
    }

    if (selectedPiece != null)
    {
      DrawWorldOutline(GetPieceWorldBounds(selectedPiece, cellSize), UiTheme.SelectionOutline, 0.106f);
    }

    /* Draw Pieces */

    foreach (Piece piece in pieceSetup.Pieces)
    {
      Rectangle pieceBounds = GetPieceWorldBounds(piece, cellSize);
      Color colour = UiTheme.GetTeamColour(piece.Team);

      _spriteBatch.Draw(
          _pixel,
          new Rectangle(
              pieceBounds.X + 5,
              pieceBounds.Y + 5,
              pieceBounds.Width - 10,
              pieceBounds.Height - 10
          ),
          null,
          colour,
          0f,
          Vector2.Zero,
          SpriteEffects.None,
          0.11f
      );

      DrawWorldOutline(pieceBounds, Color.Lerp(colour, UiTheme.Shadow, 0.45f), 0.111f);

      int pieceHealthBarWidth = pieceBounds.Width - 16;
      float healthRatio = piece.CurrentHealth / (float)Math.Max(1, piece.Definition.Health);
      DrawWorldRectangle(
        new Rectangle(pieceBounds.X + 8, pieceBounds.Bottom - 12, pieceHealthBarWidth, 5),
        UiTheme.Shadow,
        0.121f
      );
      DrawWorldRectangle(
        new Rectangle(
          pieceBounds.X + 8,
          pieceBounds.Bottom - 12,
          (int)(pieceHealthBarWidth * MathHelper.Clamp(healthRatio, 0f, 1f)),
          5
        ),
        UiTheme.Health,
        0.122f
      );

      string label = UiText.BuildPieceLabel(piece.Definition);
      Vector2 labelSize = _pieceLabelFont.MeasureString(label);

      _spriteBatch.DrawString(
        _pieceLabelFont,
        label,
        new Vector2(
          pieceBounds.Center.X - labelSize.X / 2f,
          pieceBounds.Center.Y - labelSize.Y / 2f
        ),
        UiTheme.TextPrimary,
        0f,
        Vector2.Zero,
        1f,
        SpriteEffects.None,
        0.12f
      );

      string healthText = $"HP {piece.CurrentHealth}";
      const float healthScale = 0.6f;
      Vector2 healthSize = _pieceLabelFont.MeasureString(healthText) * healthScale;
      _spriteBatch.DrawString(
        _pieceLabelFont,
        healthText,
        new Vector2(
          pieceBounds.Center.X - healthSize.X / 2f,
          pieceBounds.Y + 6
        ),
        UiTheme.TextPrimary,
        0f,
        Vector2.Zero,
        healthScale,
        SpriteEffects.None,
        0.13f
      );
    }

    _spriteBatch.End();

    _spriteBatch.Begin();

    DrawStatusPanel();
    DrawSelectedPiecePanel();
    DrawPurchasePanel();

    _spriteBatch.End();

    base.Draw(gameTime);
  }
}
