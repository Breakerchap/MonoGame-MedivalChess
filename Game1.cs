using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MedivalChess;

internal sealed class Game1 : Game
{
  private enum Screen
  {
    Title,
    OnlineLobby,
    OnlineJoin,
    OnlineWaiting,
    OnlineRoyalSelection,
    Settings,
    Setup,
    Playing,
    Pause,
    Encyclopedia,
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

  private enum OnlineInputField
  {
    ServerUrl,
    JoinCode
  }

  private enum SetupStage
  {
    Mode,
    Battlefield,
    Economy,
    RoyalSelection
  }

  private enum GameMode
  {
    Regicide,
    Conquest,
    Escort
  }

  private enum BoardSize
  {
    Small,
    Medium,
    Large
  }

  private enum TerrainDensity
  {
    Light,
    Standard,
    Heavy
  }

  private enum EngineerAbility
  {
    Road,
    Barrier,
    Mine,
    Demolish
  }

  private sealed class MovementAnimation
  {
    internal const float SecondsPerStep = 0.11f;

    internal Piece Piece { get; init; }
    internal List<(int x, int y)> Path { get; init; }
    internal float ElapsedSeconds { get; set; }
    internal float Duration => Path.Count * SecondsPerStep;
  }

  private readonly GraphicsDeviceManager _graphics;
  private SpriteBatch _spriteBatch;
  private Texture2D _pixel;
  private SpriteFont _pieceLabelFont;
  private UiRenderer _ui;
  private Board _board;
  private BattlefieldTerrain _terrain;
  private readonly PieceSetup pieceSetup = new();
  private List<Team> _teams = [];
  private Piece selectedPiece;
  private Piece _cavalierAwaitingAttack;
  private MovementAnimation _movementAnimation;
  private readonly HashSet<(int x, int y)> _roads = [];
  private readonly Dictionary<(int x, int y), int> _barricades = [];
  private readonly Dictionary<(int x, int y), TeamName> _mines = [];
  private readonly HashSet<(int x, int y)> _restoredLakeTiles = [];
  private readonly HashSet<TileEdge> _riverBridges = [];
  private const int noMansLandHalfHeight = 3;
  private const float territoryTintAmount = 0.2f;
  private const int purchasePanelWidth = 380;
  private const int purchasePanelHeight = 470;
  private int _terrainSeed;
  private Vector2 _cameraPosition = Vector2.Zero;
  private float _zoom = 1f;
  private MouseState _previousMouseState;
  private KeyboardState _previousKeyboardState;
  private bool _isPurchaseMode;
  private int _selectedPurchaseIndex;
  private int _selectedTeacherDefinitionIndex;
  private EngineerAbility _selectedEngineerAbility;
  private Screen _screen = Screen.Title;
  private TeamName _setupTeam = TeamName.Red;
  private int _selectedRoyalIndex;
  private SetupStage _setupStage = SetupStage.Mode;
  private BoardSize _selectedBoardSize = BoardSize.Medium;
  private TerrainDensity _forestDensity = TerrainDensity.Standard;
  private TerrainDensity _waterwayDensity = TerrainDensity.Standard;
  private int _startingCash = Globals.StartingCash;
  private float _killerRefundMultiplier = Globals.KillerDeathRefundMultiplier;
  private float _defeatedTeamRefundMultiplier = Globals.DefeatedTeamDeathRefundMultiplier;
  private int _initialBuysPerTurn = 2;
  private int _initialBuyTurnsPerTeam = 4;
  private InitialBuyPhase _initialBuyPhase;
  private TeamName? _winningTeam;
  private GameMode _gameMode = GameMode.Regicide;
  private int _conquestWinScore = 15;
  // Negative pressure moves toward Orange; positive pressure moves toward Purple.
  private int _conquestScore;
  private BindingAction? _bindingToChange;
  private Screen _settingsReturnScreen = Screen.Title;
  private int _encyclopediaIndex;
  private bool _rotateBoard;
  private Keys _moveUpKey = Keys.W;
  private Keys _moveDownKey = Keys.S;
  private Keys _moveLeftKey = Keys.A;
  private Keys _moveRightKey = Keys.D;
  private Keys _zoomInKey = Keys.E;
  private Keys _zoomOutKey = Keys.Q;
  private Keys _buyKey = Keys.B;
  private OnlineMatchClient _onlineClient;
  private string _onlineStatus = "OFFLINE";
  private string _onlineServerUrl = "http://localhost:5057";
  private string _onlineJoinCode = string.Empty;
  private OnlineInputField _onlineInputFocus = OnlineInputField.ServerUrl;
  private bool _onlineWaitingForOpponent;
  private bool _onlineRoyalChoicePending;
  private bool _onlineHostingSetup;
  private NetworkMatchConfiguration _onlineMatchConfiguration;
  private DateTimeOffset _nextOnlineJoinAttemptAt;
  private string _onlineError = string.Empty;

  internal Game1()
  {
    _graphics = new GraphicsDeviceManager(this);
    Content.RootDirectory = "Content";
    IsMouseVisible = true;
    Window.Title = "Crown & Siege";

    _graphics.PreferredBackBufferWidth = 2560;
    _graphics.PreferredBackBufferHeight = 1440;

    Window.AllowUserResizing = true;
  }

  protected override void Initialize()
  {
    _board = new Board();
    _terrainSeed = Random.Shared.Next();
    _terrain = BattlefieldTerrain.CreateRandom(_board, _terrainSeed);

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
    bool wasEscapePressed =
      keyboard.IsKeyDown(Keys.Escape) &&
      !_previousKeyboardState.IsKeyDown(Keys.Escape);
    _onlineClient?.DrainStates(ApplyOnlineState, error => _onlineError = error);

    if (_screen == Screen.Playing && wasEscapePressed)
    {
      _screen = Screen.Pause;
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      base.Update(gameTime);
      return;
    }

    if (_screen != Screen.Playing)
    {
      UpdateMenu(keyboard, mouse, wasLeftClick, wasEscapePressed);
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      base.Update(gameTime);
      return;
    }

    if (_movementAnimation != null)
    {
      UpdateMovementAnimation(deltaTime);
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      base.Update(gameTime);
      return;
    }

    float cameraSpeed = 500f;
    float zoomSpeed = 1f;

    Vector2 cameraInput = Vector2.Zero;
    if (keyboard.IsKeyDown(_moveLeftKey))
      cameraInput.X -= 1f;

    if (keyboard.IsKeyDown(_moveRightKey))
      cameraInput.X += 1f;

    if (keyboard.IsKeyDown(_moveUpKey))
      cameraInput.Y -= 1f;

    if (keyboard.IsKeyDown(_moveDownKey))
      cameraInput.Y += 1f;

    if (cameraInput != Vector2.Zero)
    {
      Vector2 worldCameraInput = Vector2.Transform(
        cameraInput,
        Matrix.Invert(GetBoardRotationTransform())
      );
      _cameraPosition += worldCameraInput * cameraSpeed * deltaTime / _zoom;
    }

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

    if (wasPurchaseModeToggle && _initialBuyPhase == null && _onlineClient == null)
    {
      _isPurchaseMode = !_isPurchaseMode;
      selectedPiece = null;
    }

    if (_isPurchaseMode && wasPreviousPurchasePressed)
    {
      CyclePurchaseSelection(-1);
    }

    if (_isPurchaseMode && wasNextPurchasePressed)
    {
      CyclePurchaseSelection(1);
    }

    bool clickedPurchasePanel =
      wasLeftClick && HandlePurchasePanelClick(mouse.Position);
    bool clickedInitialBuyStop =
      wasLeftClick && HandleInitialBuyStopClick(mouse.Position);
    bool clickedTeacherPanel =
      wasLeftClick && HandleTeacherChoiceClick(mouse.Position);
    bool clickedEngineerPanel =
      wasLeftClick && HandleEngineerAbilityClick(mouse.Position);
    bool clickedOxCarryPanel =
      wasLeftClick && HandleOxCarryPanelClick(mouse.Position);

    if (!clickedPurchasePanel && !clickedInitialBuyStop && !clickedTeacherPanel && !clickedEngineerPanel && !clickedOxCarryPanel && (wasLeftClick || wasRightClick))
    {
      const int cellSize = 64;
      int boardX = (int)MathF.Floor(mouseWorldBefore.X / cellSize) + _board.MinX;
      int boardY = (int)MathF.Floor(mouseWorldBefore.Y / cellSize) + _board.MinY;
      var targetPosition = (x: boardX, y: boardY);
      Piece pieceAtTarget = pieceSetup.GetPieceAt(targetPosition);

      if (_isPurchaseMode && _onlineClient == null)
      {
        if (wasLeftClick)
        {
          TryPurchaseAndPlace(targetPosition);
        }
      }
      else if (selectedPiece == null)
      {
        if (pieceAtTarget?.Team == Team.CurrentTurn && pieceAtTarget.AttachedTo == null && IsOnlineLocalTurn())
        {
          SelectPiece(pieceAtTarget);
        }
      }
      else if (pieceAtTarget == selectedPiece && selectedPiece.Occupies(targetPosition))
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
        pieceAtTarget.AttachedTo == null &&
        !TryGetMovementPathAt(selectedPiece, targetPosition, out _)
      )
      {
        SelectPiece(pieceAtTarget);
      }
      else
      {
        bool usedSpecialAbility = _onlineClient == null && wasRightClick &&
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

          if (isBoardCell && TryGetMovementPathAt(selectedPiece, targetPosition, out List<(int x, int y)> path))
          {
            if (selectedPiece.AttachedTo != null &&
                selectedPiece.AttachmentKind is AttachmentKind.Carried or AttachmentKind.Towed)
            {
              pieceSetup.Detach(selectedPiece);
            }

            if (_onlineClient != null)
            {
              _ = SendOnlineMoveAsync(selectedPiece, targetPosition);
              selectedPiece = null;
            }
            else
            {
              BeginMovementAnimation(selectedPiece, path);
            }
          }

          if (_movementAnimation == null && _cavalierAwaitingAttack != selectedPiece)
          {
            selectedPiece = null;
          }
        }
        else if (wasRightClick && _onlineClient == null)
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
            !selectedPiece.HasAttackedThisTurn &&
            Actions.CanAttackSquare(selectedPiece, targetPosition) &&
            HasClearAttackPath(selectedPiece, targetPosition) &&
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

            selectedPiece.HasAttackedThisTurn = true;

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

    if (targetPiece?.Definition.Type == PieceType.Mercenary &&
        targetPiece.Team != Team.CurrentTurn)
    {
      if (_initialBuyPhase != null)
      {
        Console.WriteLine("Mercenaries can only be bought off during the normal action phase.");
        return;
      }

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

      Console.WriteLine($"{Team.CurrentTurn} bought the Mercenary for {buyoutCost} gold.");
      CompletePurchase();
      return;
    }

    if (_initialBuyPhase != null && definition.Type == PieceType.Mercenary)
    {
      Console.WriteLine("Mercenaries cannot be bought during the initial buy phase.");
      return;
    }

    bool canPlace =
      (definition.Type == PieceType.Mercenary
        ? CanPlaceMercenary(targetPosition)
        : CanPlacePiece(definition, targetPosition, Team.CurrentTurn)) &&
      buyingTeam.Money >= definition.Cost;

    if (!canPlace)
    {
      Console.WriteLine(definition.Type == PieceType.Mercenary
        ? "Mercenaries must be placed on an empty edge square in No-Man's-Land."
        : "Pieces must be placed on an empty square on your side of the board.");
      return;
    }

    Piece boughtPiece = Team.BuyPiece(definition, buyingTeam, targetPosition);
    pieceSetup.AddPiece(boughtPiece);

    Console.WriteLine(
      $"Bought and placed {definition.Type} at ({targetPosition.x}, {targetPosition.y})."
    );

    CompletePurchase();
  }

  private void StartInitialBuyPhase()
  {
    _initialBuyPhase = new InitialBuyPhase(_initialBuysPerTurn, _initialBuyTurnsPerTeam);
    if (PieceDefinitions.Purchasable[_selectedPurchaseIndex].Type == PieceType.Mercenary)
    {
      CyclePurchaseSelection(1);
    }
    Team.ResetTurn();
    Team.SetCurrentTurn(_initialBuyPhase.CurrentTeam);
    _isPurchaseMode = true;
    selectedPiece = null;
    _cavalierAwaitingAttack = null;
    _screen = Screen.Playing;
    Console.WriteLine("Initial buy phase started.");
  }

  private void CyclePurchaseSelection(int direction)
  {
    for (int attempts = 0; attempts < PieceDefinitions.Purchasable.Length; attempts++)
    {
      _selectedPurchaseIndex =
        (_selectedPurchaseIndex + direction + PieceDefinitions.Purchasable.Length) % PieceDefinitions.Purchasable.Length;
      if (_initialBuyPhase == null ||
          PieceDefinitions.Purchasable[_selectedPurchaseIndex].Type != PieceType.Mercenary)
      {
        return;
      }
    }
  }

  private void CompletePurchase()
  {
    if (_initialBuyPhase == null)
    {
      _isPurchaseMode = false;
      CompleteAction();
      return;
    }

    _initialBuyPhase.RecordPurchase();
    UpdateInitialBuyPhaseState();
  }

  private void UpdateInitialBuyPhaseState()
  {
    if (_initialBuyPhase.IsComplete)
    {
      _initialBuyPhase = null;
      _isPurchaseMode = false;
      selectedPiece = null;
      Team.ResetTurn();
      foreach (Team team in _teams)
      {
        team.ActionPoints = Team.ActionsPerTurn;
      }
      ResetPieceTurnActions(TeamName.Red);
      ResetPieceTurnActions(TeamName.Blue);

      Console.WriteLine("Initial buy phase complete. The match has started.");
      return;
    }

    Team.SetCurrentTurn(_initialBuyPhase.CurrentTeam);
    _isPurchaseMode = true;
    selectedPiece = null;
  }

  private bool HandleInitialBuyStopClick(Point mousePosition)
  {
    if (_initialBuyPhase == null || !GetInitialBuyStopButtonBounds().Contains(mousePosition))
    {
      return false;
    }

    _initialBuyPhase.StopCurrentBuyer();
    UpdateInitialBuyPhaseState();
    return true;
  }

  private void CompleteAction()
  {
    Team currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);

    if (currentTeam.SpendAction())
    {
      if (ApplyConquestPressure(Team.CurrentTurn))
      {
        return;
      }

      Team.AdvanceTurn();
      ResetPieceTurnActions(Team.CurrentTurn);
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

  private void ResetPieceTurnActions(TeamName teamName)
  {
    foreach (Piece piece in pieceSetup.Pieces)
    {
      if (piece.Team == teamName)
      {
        piece.HasMovedThisTurn = false;
        piece.HasAttackedThisTurn = false;
      }
    }
  }

  private bool ApplyConquestPressure(TeamName teamThatFinishedTurn)
  {
    if (_gameMode != GameMode.Conquest || teamThatFinishedTurn != TeamName.Blue)
    {
      return false;
    }

    int orangePieces = GetConquestOccupyingPieceCount(TeamName.Red);
    int purplePieces = GetConquestOccupyingPieceCount(TeamName.Blue);
    int netPressure = purplePieces - orangePieces;
    if (netPressure == 0)
    {
      return false;
    }

    _conquestScore += netPressure;
    _conquestScore = Math.Clamp(_conquestScore, -_conquestWinScore, _conquestWinScore);

    if (Math.Abs(_conquestScore) < _conquestWinScore)
    {
      return false;
    }

    _winningTeam = _conquestScore < 0 ? TeamName.Red : TeamName.Blue;
    _screen = Screen.GameOver;
    selectedPiece = null;
    return true;
  }

  private int GetConquestOccupyingPieceCount(TeamName team)
  {
    return pieceSetup.Pieces.Count(piece =>
      piece.Team == team && piece.AttachmentKind == AttachmentKind.None &&
      piece.OccupiedSquares().Any(IsConquestSquare));
  }

  private bool IsOnlineLocalTurn()
  {
    if (_onlineClient == null)
    {
      return true;
    }

    return _onlineClient.Team is NetworkTeam team &&
      (team == NetworkTeam.Red ? Team.CurrentTurn == TeamName.Red : Team.CurrentTurn == TeamName.Blue);
  }

  private async System.Threading.Tasks.Task HostOnlineMatchAsync(NetworkMatchConfiguration configuration)
  {
    if (_onlineClient != null)
    {
      Console.WriteLine("Already connected to an online room.");
      return;
    }

    if (!TryGetOnlineServerUrl(out string serverUrl))
    {
      _onlineError = "Enter a valid http:// or https:// server URL.";
      _screen = Screen.OnlineLobby;
      return;
    }

    try
    {
      _onlineError = string.Empty;
      _onlineClient = new OnlineMatchClient(serverUrl);
      _onlineWaitingForOpponent = true;
      _onlineStatus = "CREATING PRIVATE ROOM...";
      _screen = Screen.OnlineWaiting;
      RoomJoinResult result = await _onlineClient.HostAsync(new CreateGameRequest(configuration));
      if (!result.Accepted)
      {
        Console.WriteLine($"Could not host room: {result.Error}");
        await _onlineClient.DisposeAsync();
        _onlineClient = null;
        _onlineWaitingForOpponent = false;
        _onlineError = result.Error ?? "Could not create a room.";
        _screen = Screen.OnlineLobby;
        return;
      }

      _onlineStatus = $"ONLINE {result.Team}  ROOM: {result.JoinCode}";
      Console.WriteLine($"Online room created. Join code: {result.JoinCode}");
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Could not connect to match server: {exception.Message}");
      if (_onlineClient != null)
      {
        await _onlineClient.DisposeAsync();
      }
      _onlineClient = null;
      _onlineWaitingForOpponent = false;
      _onlineError = "Could not reach the match server.";
      _screen = Screen.OnlineLobby;
    }
  }

  private async System.Threading.Tasks.Task JoinOnlineMatchAsync(string requestedJoinCode = null)
  {
    if (_onlineClient != null)
    {
      Console.WriteLine("Already connected to an online room.");
      return;
    }

    string joinCode = requestedJoinCode ?? _onlineJoinCode;
    if (string.IsNullOrWhiteSpace(joinCode))
    {
      _onlineError = "Enter the five-character room code.";
      return;
    }

    if (DateTimeOffset.UtcNow < _nextOnlineJoinAttemptAt)
    {
      _onlineError = "Please wait half a second before trying another room code.";
      return;
    }

    _nextOnlineJoinAttemptAt = DateTimeOffset.UtcNow.AddMilliseconds(500);

    if (!TryGetOnlineServerUrl(out string serverUrl))
    {
      _onlineError = "Enter a valid http:// or https:// server URL.";
      return;
    }

    try
    {
      _onlineError = string.Empty;
      _onlineClient = new OnlineMatchClient(serverUrl);
      RoomJoinResult result = await _onlineClient.JoinAsync(joinCode);
      if (!result.Accepted)
      {
        Console.WriteLine($"Could not join room: {result.Error}");
        await _onlineClient.DisposeAsync();
        _onlineClient = null;
        _onlineError = result.Error ?? "Could not join that room.";
        _screen = Screen.OnlineJoin;
        return;
      }

      _onlineStatus = $"ONLINE {result.Team}  ROOM: {result.JoinCode}";
      Console.WriteLine($"Joined online room {result.JoinCode} as {result.Team}.");
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Could not connect to match server: {exception.Message}");
      if (_onlineClient != null)
      {
        await _onlineClient.DisposeAsync();
      }
      _onlineClient = null;
      _onlineError = "Could not reach the match server.";
      _screen = Screen.OnlineJoin;
    }
  }

  private async System.Threading.Tasks.Task SendOnlineMoveAsync(Piece piece, (int x, int y) destination)
  {
    try
    {
      ActionResult result = await _onlineClient.MoveAsync(piece.NetworkId, destination.x, destination.y);
      if (!result.Accepted)
      {
        Console.WriteLine($"Move rejected: {result.Error}");
      }
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Move could not be sent: {exception.Message}");
    }
  }

  private async System.Threading.Tasks.Task SendOnlineRoyalChoiceAsync()
  {
    if (_onlineClient == null || _onlineRoyalChoicePending)
    {
      return;
    }

    _onlineRoyalChoicePending = true;
    try
    {
      PieceDefinition royal = PieceDefinitions.Royals[_selectedRoyalIndex];
      ActionResult result = await _onlineClient.ChooseRoyalAsync(royal.Type.ToString());
      if (!result.Accepted)
      {
        _onlineRoyalChoicePending = false;
        _onlineError = result.Error ?? "Could not choose that royal.";
      }
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Royal choice could not be sent: {exception.Message}");
      _onlineRoyalChoicePending = false;
      _onlineError = "Could not send royal choice.";
    }
  }

  private void ApplyOnlineState(NetworkGameState state)
  {
    ApplyOnlineConfiguration(state.Configuration);
    ApplyOnlineTeamStates(state.Teams);
    ApplyOnlinePieces(state.Pieces);

    if (_onlineWaitingForOpponent && state.PlayerCount < 2)
    {
      return;
    }

    if (!state.MatchReady)
    {
      _onlineWaitingForOpponent = false;
      NetworkTeam? localTeam = _onlineClient?.Team;
      bool hasChosenRoyal = localTeam is NetworkTeam team && state.Teams.Any(teamState =>
        teamState.Team == team && !string.IsNullOrWhiteSpace(teamState.ChosenRoyal));
      _onlineRoyalChoicePending = hasChosenRoyal;
      _onlineStatus = hasChosenRoyal
        ? $"WAITING FOR OPPONENT'S ROYAL  ROOM: {state.JoinCode}"
        : $"ONLINE ROYAL SETUP  ROOM: {state.JoinCode}";
      _screen = Screen.OnlineRoyalSelection;
      return;
    }

    Team.SetCurrentTurn(state.CurrentTurn == NetworkTeam.Red ? TeamName.Red : TeamName.Blue);
    selectedPiece = null;
    _movementAnimation = null;
    _initialBuyPhase = null;
    _isPurchaseMode = false;
    _onlineStatus = $"ONLINE {state.CurrentTurn} TURN  ROOM: {state.JoinCode}";
    _onlineWaitingForOpponent = false;
    _onlineRoyalChoicePending = false;
    _screen = Screen.Playing;
  }

  private void ApplyOnlineConfiguration(NetworkMatchConfiguration configuration)
  {
    if (_onlineMatchConfiguration is not null && _onlineMatchConfiguration.Equals(configuration))
    {
      return;
    }

    if (!Enum.TryParse(configuration.BoardSize, out BoardSize boardSize) ||
        !Enum.TryParse(configuration.ForestDensity, out TerrainDensity forestDensity) ||
        !Enum.TryParse(configuration.WaterwayDensity, out TerrainDensity waterwayDensity) ||
        !Enum.TryParse(configuration.GameMode, out GameMode gameMode))
    {
      _onlineError = "The room sent unsupported match settings.";
      return;
    }

    _onlineMatchConfiguration = configuration;
    _selectedBoardSize = boardSize;
    _forestDensity = forestDensity;
    _waterwayDensity = waterwayDensity;
    _gameMode = gameMode;
    _startingCash = configuration.StartingCash;
    _killerRefundMultiplier = configuration.KillerRefundMultiplier;
    _defeatedTeamRefundMultiplier = configuration.DefeatedTeamRefundMultiplier;
    _initialBuysPerTurn = configuration.InitialBuysPerTurn;
    _initialBuyTurnsPerTeam = configuration.InitialBuyTurnsPerTeam;
    _conquestWinScore = configuration.ConquestWinScore;
    ConfigureBattlefield(boardSize, forestDensity, waterwayDensity, configuration.TerrainSeed);
  }

  private void ApplyOnlineTeamStates(IReadOnlyList<NetworkTeamState> teamStates)
  {
    foreach (NetworkTeamState state in teamStates)
    {
      TeamName teamName = state.Team == NetworkTeam.Red ? TeamName.Red : TeamName.Blue;
      Team team = _teams.Find(candidate => candidate.TeamName == teamName);
      if (team is null)
      {
        continue;
      }

      team.Money = state.Money;
      team.ActionPoints = state.ActionsRemaining;
      team.ClearRoyal();
      if (Enum.TryParse(state.ChosenRoyal, out PieceType royal))
      {
        team.ChooseRoyal(royal);
      }
    }
  }

  private void ApplyOnlinePieces(IReadOnlyList<NetworkPiece> pieces)
  {
    pieceSetup.ClearPieces();
    foreach (NetworkPiece networkPiece in pieces)
    {
      if (!Enum.TryParse(networkPiece.Type, out PieceType pieceType))
      {
        continue;
      }

      PieceDefinition definition = PieceDefinitions.Encyclopedia.FirstOrDefault(candidate => candidate.Type == pieceType);
      if (definition is null)
      {
        continue;
      }

      Piece piece = new(
        definition,
        (networkPiece.X, networkPiece.Y),
        networkPiece.Team == NetworkTeam.Red ? TeamName.Red : TeamName.Blue
      )
      {
        NetworkId = networkPiece.Id,
        CurrentHealth = networkPiece.Health,
        HasMovedThisTurn = networkPiece.HasMovedThisTurn,
        HasAttackedThisTurn = networkPiece.HasAttackedThisTurn
      };
      pieceSetup.AddPiece(piece);
    }
  }

  private bool TryGetOnlineServerUrl(out string serverUrl)
  {
    serverUrl = _onlineServerUrl.Trim();
    if (!serverUrl.Contains("://", StringComparison.Ordinal))
    {
      bool isLocalServer = serverUrl.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
        serverUrl.StartsWith("127.0.0.1", StringComparison.Ordinal);
      serverUrl = $"{(isLocalServer ? "http" : "https")}://{serverUrl}";
    }

    if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri uri) ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
      return false;
    }

    serverUrl = serverUrl.TrimEnd('/');
    return true;
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

        if (!IsTraversableTerrainSquare((position.x + x, position.y + y)))
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

  private bool CanPlaceMercenary((int x, int y) position)
  {
    int arrayX = position.x - _board.MinX;
    int arrayY = position.y - _board.MinY;
    if (!IsTraversableTerrainSquare(position) || GetSquareOwner(arrayY).HasValue)
    {
      return false;
    }

    (int x, int y)[] adjacentOffsets = [(0, -1), (1, 0), (0, 1), (-1, 0)];
    bool isBoardEdge = adjacentOffsets.Any(offset =>
      !IsBoardCell(arrayX + offset.x, arrayY + offset.y));
    return isBoardEdge && pieceSetup.IsFootprintClear(PieceDefinitions.Mercenary, position);
  }

  private bool IsTraversableTerrainSquare((int x, int y) position)
  {
    return
      IsBoardCell(position.x - _board.MinX, position.y - _board.MinY) &&
      (!_terrain.IsLake(position) || _restoredLakeTiles.Contains(position)) &&
      !_barricades.ContainsKey(position);
  }

  private Dictionary<(int x, int y), List<(int x, int y)>> GetMovementPaths(Piece piece)
  {
    return MovementPathfinder.FindPaths(
      piece,
      destination => CanLandPieceAt(piece, destination),
      (from, destination) => CanTravelThroughPosition(piece, from, destination),
      destination => GetMovementCost(piece, destination),
      (from, to) => CrossesRiver(piece, from, to)
    );
  }

  private bool TryGetMovementPathAt(
    Piece piece,
    (int x, int y) clickedSquare,
    out List<(int x, int y)> path
  )
  {
    if (piece.HasMovedThisTurn)
    {
      path = null;
      return false;
    }

    Dictionary<(int x, int y), List<(int x, int y)>> paths = GetMovementPaths(piece);
    if (paths.TryGetValue(clickedSquare, out path))
    {
      return true;
    }

    foreach (((int x, int y) destination, List<(int x, int y)> candidatePath) in paths)
    {
      if (FootprintContains(piece.Definition, destination, clickedSquare))
      {
        path = candidatePath;
        return true;
      }
    }

    path = null;
    return false;
  }

  private bool CanLandPieceAt(Piece piece, (int x, int y) destination)
  {
    if (piece.Definition.Type == PieceType.Elephant)
    {
      return IsFootprintOnBoard(piece.Definition, destination);
    }

    if (!CanPlacePiece(piece.Definition, destination, null, piece))
    {
      return false;
    }

    Piece towedPiece = pieceSetup.GetAttachedPiece(piece, AttachmentKind.Towed);
    if (towedPiece == null)
    {
      return true;
    }

    var towedDestination = (
      x: towedPiece.Position.x + destination.x - piece.Position.x,
      y: towedPiece.Position.y + destination.y - piece.Position.y
    );
    return
      CanPlacePiece(towedPiece.Definition, towedDestination, null, towedPiece) &&
      !FootprintsOverlap(piece.Definition, destination, towedPiece.Definition, towedDestination);
  }

  private bool CanTravelThroughPosition(
    Piece piece,
    (int x, int y) from,
    (int x, int y) destination
  )
  {
    if (piece.Definition.Type == PieceType.Elephant)
    {
      return IsFootprintOnBoard(piece.Definition, destination);
    }

    foreach ((int x, int y) position in PositionsBetween(from, destination))
    {
      foreach ((int x, int y) occupiedSquare in OccupiedSquares(piece.Definition, position))
      {
        if (!IsTraversableTerrainSquare(occupiedSquare))
        {
          return false;
        }

        Piece blockingPiece = pieceSetup.GetPieceAt(occupiedSquare);
        if (blockingPiece == null || blockingPiece == piece)
        {
          continue;
        }

        return false;
      }
    }

    return true;
  }

  private int GetMovementCost(Piece piece, (int x, int y) destination)
  {
    if (piece.Definition.Type == PieceType.Elephant)
    {
      return 1;
    }

    int cost = 0;
    foreach ((int x, int y) occupiedSquare in OccupiedSquares(piece.Definition, destination))
    {
      if (_terrain.IsForest(occupiedSquare) && !_roads.Contains(occupiedSquare))
      {
        cost = Math.Max(cost, 2);
      }
      else if (_roads.Contains(occupiedSquare) && !_terrain.IsForest(occupiedSquare))
      {
        // A road built along open ground costs no movement points.
        cost = Math.Max(cost, 0);
      }
      else
      {
        cost = Math.Max(cost, 1);
      }
    }

    return cost;
  }

  private bool CrossesRiver(Piece piece, (int x, int y) from, (int x, int y) to)
  {
    if (piece.Definition.Type == PieceType.Elephant)
    {
      return false;
    }

    foreach ((int x, int y) fromSquare in OccupiedSquares(piece.Definition, from))
    {
      var toSquare = (
        x: fromSquare.x + to.x - from.x,
        y: fromSquare.y + to.y - from.y
      );
      if (CrossesRiverBetweenSquares(fromSquare, toSquare))
      {
        return true;
      }
    }

    return false;
  }

  private bool CrossesRiverBetweenSquares((int x, int y) from, (int x, int y) to)
  {
    int deltaX = to.x - from.x;
    int deltaY = to.y - from.y;
    int steps = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
    var current = from;

    for (int step = 1; step <= steps; step++)
    {
      var next = (
        x: from.x + (int)MathF.Round(deltaX * step / (float)steps),
        y: from.y + (int)MathF.Round(deltaY * step / (float)steps)
      );

      if (next.x != current.x && next.y != current.y)
      {
        var horizontalThenVertical = (x: next.x, y: current.y);
        var verticalThenHorizontal = (x: current.x, y: next.y);
        if (HasUnbridgedRiverBetween(current, horizontalThenVertical) ||
            HasUnbridgedRiverBetween(horizontalThenVertical, next) ||
            HasUnbridgedRiverBetween(current, verticalThenHorizontal) ||
            HasUnbridgedRiverBetween(verticalThenHorizontal, next))
        {
          return true;
        }
      }
      else if (HasUnbridgedRiverBetween(current, next))
      {
        return true;
      }

      current = next;
    }

    return false;
  }

  private bool HasUnbridgedRiverBetween((int x, int y) first, (int x, int y) second)
  {
    TileEdge edge = TileEdge.Between(first, second);
    return _terrain.HasRiverBetween(first, second) && !_riverBridges.Contains(edge);
  }

  private bool HasRiverBridgeBetween((int x, int y) first, (int x, int y) second)
  {
    return _riverBridges.Contains(TileEdge.Between(first, second));
  }

  private HashSet<(int x, int y)> GetValidMovementHighlightSquares(Piece piece)
  {
    HashSet<(int x, int y)> highlightedSquares = [];
    if (piece.HasMovedThisTurn)
    {
      return highlightedSquares;
    }

    foreach ((int x, int y) destination in GetMovementPaths(piece).Keys)
    {
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
    if (piece.HasAttackedThisTurn ||
        (piece.Definition.AttackShape.shape == Shape.MoveOnEnemy && piece.HasMovedThisTurn))
    {
      return highlightedSquares;
    }

    if (piece.Definition.Type == PieceType.Engineer)
    {
      foreach ((int x, int y) boardPosition in _board.Cells)
      {
        Piece targetPiece = pieceSetup.GetPieceAt(boardPosition);
        if (CanUseEngineerAbilityAt(piece, boardPosition, targetPiece))
        {
          highlightedSquares.Add(boardPosition);
        }
      }

      return highlightedSquares;
    }

    if (piece.Definition.AttackShape.shape == Shape.MoveOnEnemy)
    {
      Dictionary<(int x, int y), List<(int x, int y)>> movementPaths = GetMovementPaths(piece);
      foreach (Piece target in pieceSetup.Pieces)
      {
        if (target == piece || target.AttachedTo != null || target.Team == piece.Team)
        {
          continue;
        }

        bool canMoveOverTarget = movementPaths.Values.Any(path =>
          path.Any(step => FootprintsOverlap(
            piece.Definition,
            step,
            target.Definition,
            target.Position
          ))
        );
        if (canMoveOverTarget)
        {
          foreach ((int x, int y) occupiedSquare in target.OccupiedSquares())
          {
            highlightedSquares.Add(occupiedSquare);
          }
        }
      }

      return highlightedSquares;
    }

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

        if (Actions.CanAttackSquare(piece, targetPosition) && HasClearAttackPath(piece, targetPosition))
        {
          highlightedSquares.Add(targetPosition);
        }
      }
    }

    return highlightedSquares;
  }

  private void SelectPiece(Piece piece, bool allowAttachedPiece = false)
  {
    if (piece.AttachedTo != null && !allowAttachedPiece)
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

    if (IsPieceInForest(damagedPiece))
    {
      damage = Math.Max(1, damage - _terrain.ForestDamageReduction);
    }

    damagedPiece.CurrentHealth -= damage;
    Console.WriteLine($"{attacker.Definition.Type} dealt {damage} damage to {damagedPiece.Definition.Type}.");

    HandlePieceDestroyed(damagedPiece, attacker.Team);
  }

  private void ResolveMineDamage(Piece target, TeamName mineOwner)
  {
    target.CurrentHealth -= 40;
    Console.WriteLine($"Mine dealt 40 damage to {target.Definition.Type}.");
    HandlePieceDestroyed(target, mineOwner);
  }

  private void HandlePieceDestroyed(Piece damagedPiece, TeamName? attackingTeamName)
  {
    if (damagedPiece.CurrentHealth > 0)
    {
      return;
    }

    if (damagedPiece.Team == TeamName.Neutral)
    {
      pieceSetup.RemovePiece(damagedPiece);
      return;
    }

    if (attackingTeamName is TeamName attackerName && attackerName != damagedPiece.Team)
    {
      Team attackingTeam = _teams.Find(team => team.TeamName == attackerName);
      Team defeatedTeam = _teams.Find(team => team.TeamName == damagedPiece.Team);
      Actions.HandlePieceDeath(
        damagedPiece,
        attackingTeam,
        defeatedTeam,
        _killerRefundMultiplier,
        _defeatedTeamRefundMultiplier
      );
    }

    pieceSetup.RemovePiece(damagedPiece);
    if (damagedPiece.Definition.Category == PieceCategory.Royal && _gameMode == GameMode.Regicide)
    {
      _winningTeam = damagedPiece.Team == TeamName.Red ? TeamName.Blue : TeamName.Red;
      _screen = Screen.GameOver;
    }
    else if (damagedPiece.Definition.Category == PieceCategory.Royal && _gameMode == GameMode.Escort)
    {
      RespawnEscortRoyal(damagedPiece);
    }
  }

  private void RespawnEscortRoyal(Piece defeatedRoyal)
  {
    if (defeatedRoyal.Definition.Type == PieceType.Palace)
    {
      return;
    }

    Piece respawnedRoyal = new(
      defeatedRoyal.Definition,
      FindRoyalSpawn(defeatedRoyal.Team, defeatedRoyal.Definition),
      defeatedRoyal.Team
    );
    pieceSetup.AddPiece(respawnedRoyal);
    Console.WriteLine($"{defeatedRoyal.Team}'s royal has respawned at the back line.");
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
      PieceDefinition replacementDefinition = PieceDefinitions.Purchasable[_selectedTeacherDefinitionIndex];
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

    if (actor.Definition.Type == PieceType.Engineer)
    {
      return TryUseEngineerAbility(actor, targetPosition, targetPiece);
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
        pieceSetup.GetAttachedPiece(actor, AttachmentKind.Carried) == null &&
        pieceSetup.GetAttachedPiece(actor, AttachmentKind.Towed) == null &&
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

  private bool TryUseEngineerAbility(
    Piece engineer,
    (int x, int y) targetPosition,
    Piece targetPiece
  )
  {
    if (engineer.HasAttackedThisTurn ||
        !Actions.CanAttackSquare(engineer, targetPosition) ||
        !IsBoardCell(targetPosition.x - _board.MinX, targetPosition.y - _board.MinY))
    {
      return false;
    }

    bool improvementChanged = _selectedEngineerAbility switch
    {
      EngineerAbility.Road => TryBuildRoad(engineer, targetPosition, targetPiece),
      EngineerAbility.Barrier => TryBuildBarrier(targetPosition, targetPiece),
      EngineerAbility.Mine => TryBuildMine(engineer, targetPosition, targetPiece),
      EngineerAbility.Demolish => TryDemolishImprovement(engineer, targetPosition),
      _ => false
    };
    if (!improvementChanged)
    {
      return false;
    }

    engineer.HasAttackedThisTurn = true;
    CompleteAction();
    return true;
  }

  private bool CanUseEngineerAbilityAt(
    Piece engineer,
    (int x, int y) targetPosition,
    Piece targetPiece
  )
  {
    if (engineer.HasAttackedThisTurn ||
        !Actions.CanAttackSquare(engineer, targetPosition) ||
        !IsBoardCell(targetPosition.x - _board.MinX, targetPosition.y - _board.MinY))
    {
      return false;
    }

    return _selectedEngineerAbility switch
    {
      EngineerAbility.Road => targetPiece is null && !IsEngineeringImprovementAt(targetPosition) &&
        (HasUnbridgedRiverBetween(engineer.Position, targetPosition) ||
         (_terrain.IsLake(targetPosition) && !_restoredLakeTiles.Contains(targetPosition)) ||
         IsTraversableTerrainSquare(targetPosition)),
      EngineerAbility.Barrier or EngineerAbility.Mine => targetPiece is null &&
        !IsEngineeringImprovementAt(targetPosition) && IsTraversableTerrainSquare(targetPosition),
      EngineerAbility.Demolish => IsEngineeringImprovementAt(targetPosition) ||
        HasRiverBridgeBetween(engineer.Position, targetPosition),
      _ => false
    };
  }

  private bool TryBuildRoad(Piece engineer, (int x, int y) targetPosition, Piece targetPiece)
  {
    if (targetPiece != null || IsEngineeringImprovementAt(targetPosition))
    {
      return false;
    }

    if (HasUnbridgedRiverBetween(engineer.Position, targetPosition))
    {
      _riverBridges.Add(TileEdge.Between(engineer.Position, targetPosition));
      Console.WriteLine("Engineer built a bridge across the river.");
      return true;
    }

    if (_terrain.IsLake(targetPosition) && !_restoredLakeTiles.Contains(targetPosition))
    {
      _restoredLakeTiles.Add(targetPosition);
      Console.WriteLine("Engineer built a bridge across the lake.");
      return true;
    }

    if (!IsTraversableTerrainSquare(targetPosition))
    {
      return false;
    }

    _roads.Add(targetPosition);
    Console.WriteLine(_terrain.IsForest(targetPosition)
      ? "Engineer built a road through the forest."
      : "Engineer built a road across open ground.");
    return true;
  }

  private bool TryBuildBarrier((int x, int y) targetPosition, Piece targetPiece)
  {
    if (targetPiece != null || IsEngineeringImprovementAt(targetPosition) ||
        !IsTraversableTerrainSquare(targetPosition))
    {
      return false;
    }

    _barricades[targetPosition] = 60;
    Console.WriteLine("Engineer built a 60 HP barrier.");
    return true;
  }

  private bool TryBuildMine(Piece engineer, (int x, int y) targetPosition, Piece targetPiece)
  {
    if (targetPiece != null || IsEngineeringImprovementAt(targetPosition) ||
        !IsTraversableTerrainSquare(targetPosition))
    {
      return false;
    }

    _mines[targetPosition] = engineer.Team;
    Console.WriteLine("Engineer placed a mine.");
    return true;
  }

  private bool TryDemolishImprovement(Piece engineer, (int x, int y) targetPosition)
  {
    bool removed = _roads.Remove(targetPosition) ||
      _barricades.Remove(targetPosition) ||
      _mines.Remove(targetPosition) ||
      _restoredLakeTiles.Remove(targetPosition) ||
      _riverBridges.Remove(TileEdge.Between(engineer.Position, targetPosition));
    if (removed)
    {
      Console.WriteLine("Engineer demolished an improvement.");
    }

    return removed;
  }

  private bool IsEngineeringImprovementAt((int x, int y) position)
  {
    return _roads.Contains(position) ||
      _barricades.ContainsKey(position) ||
      _mines.ContainsKey(position) ||
      _restoredLakeTiles.Contains(position);
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

  private bool IsFootprintOnBoard(PieceDefinition definition, (int x, int y) position)
  {
    return OccupiedSquares(definition, position).All(square =>
      IsBoardCell(square.x - _board.MinX, square.y - _board.MinY)
    );
  }

  private static bool FootprintContains(
    PieceDefinition definition,
    (int x, int y) position,
    (int x, int y) square
  )
  {
    return square.x >= position.x &&
      square.x < position.x + definition.Size.x &&
      square.y >= position.y &&
      square.y < position.y + definition.Size.y;
  }

  private static IEnumerable<(int x, int y)> PositionsBetween(
    (int x, int y) from,
    (int x, int y) destination
  )
  {
    int steps = Math.Max(Math.Abs(destination.x - from.x), Math.Abs(destination.y - from.y));
    for (int step = 1; step <= steps; step++)
    {
      yield return (
        from.x + (int)MathF.Round((destination.x - from.x) * step / (float)steps),
        from.y + (int)MathF.Round((destination.y - from.y) * step / (float)steps)
      );
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

  private void BeginMovementAnimation(Piece piece, List<(int x, int y)> path)
  {
    _movementAnimation = new MovementAnimation
    {
      Piece = piece,
      Path = path
    };
  }

  private void UpdateMovementAnimation(float deltaTime)
  {
    _movementAnimation.ElapsedSeconds += deltaTime;
    if (_movementAnimation.ElapsedSeconds < _movementAnimation.Duration)
    {
      return;
    }

    MovementAnimation completedAnimation = _movementAnimation;
    _movementAnimation = null;
    Piece movedPiece = completedAnimation.Piece;
    (int x, int y) destination = completedAnimation.Path[^1];

    if (movedPiece.Definition.AttackShape.shape == Shape.MoveOnEnemy &&
        AttackUnitsMovedOver(movedPiece, completedAnimation.Path))
    {
      movedPiece.HasAttackedThisTurn = true;
    }

    MovePieceWithCompanions(movedPiece, destination);
    TriggerMinesAlongMovement(movedPiece, completedAnimation.Path);

    if (_screen == Screen.GameOver || !pieceSetup.Pieces.Contains(movedPiece))
    {
      selectedPiece = null;
      return;
    }

    Console.WriteLine($"Moved {movedPiece.Definition.Type} to ({destination.x}, {destination.y}).");
    if (HasEscortVictory(movedPiece))
    {
      _winningTeam = movedPiece.Team;
      _screen = Screen.GameOver;
      selectedPiece = null;
      return;
    }

    if (movedPiece.Definition.Type == PieceType.Cavalier)
    {
      selectedPiece = movedPiece;
      _cavalierAwaitingAttack = movedPiece;
    }
    else
    {
      selectedPiece = null;
      CompleteAction();
    }
  }

  private bool HasEscortVictory(Piece piece)
  {
    if (_gameMode != GameMode.Escort || piece.Definition.Category != PieceCategory.Royal)
    {
      return false;
    }

    int enemyBackRow = piece.Team == TeamName.Red
      ? _board.MinY
      : _board.MinY + _board.BoardArray.GetLength(0) - 1;
    return piece.OccupiedSquares().Any(square => square.y == enemyBackRow);
  }

  private bool AttackUnitsMovedOver(Piece attacker, IReadOnlyList<(int x, int y)> path)
  {
    HashSet<Piece> damagedPieces = [];
    foreach (Piece crossedPiece in new List<Piece>(pieceSetup.Pieces))
    {
      if (crossedPiece == attacker ||
          crossedPiece.AttachedTo != null ||
          crossedPiece.Team == attacker.Team)
      {
        continue;
      }

      bool wasMovedOver = path.Any(step => FootprintsOverlap(
        attacker.Definition,
        step,
        crossedPiece.Definition,
        crossedPiece.Position
      ));
      if (wasMovedOver && damagedPieces.Add(crossedPiece))
      {
        ResolveDamage(attacker, crossedPiece);
      }
    }

    return damagedPieces.Count > 0;
  }

  private void TriggerMinesAlongMovement(Piece movingPiece, IReadOnlyList<(int x, int y)> path)
  {
    if (movingPiece.Definition.Type == PieceType.Engineer)
    {
      return;
    }

    List<((int x, int y) position, TeamName owner)> triggeredMines = [];
    foreach ((int x, int y) step in path)
    {
      foreach ((int x, int y) occupiedSquare in OccupiedSquares(movingPiece.Definition, step))
      {
        if (_mines.TryGetValue(occupiedSquare, out TeamName owner) && owner != movingPiece.Team)
        {
          triggeredMines.Add((occupiedSquare, owner));
        }
      }
    }

    foreach (((int x, int y) position, TeamName owner) mine in triggeredMines.Distinct())
    {
      _mines.Remove(mine.position);
      ExplodeMine(mine.position, mine.owner, movingPiece);
      if (_screen == Screen.GameOver)
      {
        return;
      }
    }
  }

  private void ExplodeMine((int x, int y) position, TeamName owner, Piece movingPiece)
  {
    HashSet<Piece> affectedPieces = pieceSetup.Pieces
      .Where(piece => piece.OccupiedSquares().Any(square =>
        Math.Abs(square.x - position.x) <= 1 && Math.Abs(square.y - position.y) <= 1))
      .ToHashSet();
    affectedPieces.Add(movingPiece);

    Console.WriteLine($"Mine exploded at ({position.x}, {position.y}).");
    foreach (Piece affectedPiece in affectedPieces.ToArray())
    {
      if (pieceSetup.Pieces.Contains(affectedPiece))
      {
        ResolveMineDamage(affectedPiece, owner);
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

        if (_terrain.IsForest(position))
        {
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

  private bool IsPieceInForest(Piece piece)
  {
    foreach ((int x, int y) occupiedSquare in piece.OccupiedSquares())
    {
      if (_terrain.IsForest(occupiedSquare))
      {
        return true;
      }
    }

    return false;
  }

  private bool HasClearAttackPath(Piece attacker, (int x, int y) targetPosition)
  {
    return HasClearDirectAttackPath(attacker, targetPosition) &&
      HasClearForestPathForRangedAttack(attacker, targetPosition) &&
      HasClearBarrierPath(attacker, targetPosition);
  }

  private bool HasClearBarrierPath(Piece attacker, (int x, int y) targetPosition)
  {
    foreach ((int x, int y) origin in attacker.OccupiedSquares())
    {
      if (!SquaresBetween(origin, targetPosition).Any(_barricades.ContainsKey))
      {
        return true;
      }
    }

    return false;
  }

  private bool HasClearForestPathForRangedAttack(Piece attacker, (int x, int y) targetPosition)
  {
    bool isRangedAttacker =
      attacker.Definition.Category == PieceCategory.Ranged ||
      attacker.Definition.Type is PieceType.Princess or PieceType.Cannon or PieceType.Ballista;
    if (!isRangedAttacker)
    {
      return true;
    }

    foreach ((int x, int y) origin in attacker.OccupiedSquares())
    {
      var offset = (x: targetPosition.x - origin.x, y: targetPosition.y - origin.y);
      if (!Actions.ValidActionSquares(attacker, false).Contains(offset))
      {
        continue;
      }

      bool pathIsClear = true;
      foreach ((int x, int y) square in SquaresBetween(origin, targetPosition))
      {
        if (_terrain.IsForest(square))
        {
          pathIsClear = false;
          break;
        }
      }

      if (pathIsClear)
      {
        return true;
      }
    }

    return false;
  }

  private static IEnumerable<(int x, int y)> SquaresBetween(
    (int x, int y) start,
    (int x, int y) end
  )
  {
    int x = start.x;
    int y = start.y;
    int deltaX = Math.Abs(end.x - start.x);
    int deltaY = Math.Abs(end.y - start.y);
    int stepX = Math.Sign(end.x - start.x);
    int stepY = Math.Sign(end.y - start.y);
    int error = deltaX - deltaY;

    while (x != end.x || y != end.y)
    {
      int doubledError = error * 2;
      if (doubledError > -deltaY)
      {
        error -= deltaY;
        x += stepX;
      }

      if (doubledError < deltaX)
      {
        error += deltaX;
        y += stepY;
      }

      if (x != end.x || y != end.y)
      {
        yield return (x, y);
      }
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
    int halfHeight = _gameMode == GameMode.Conquest
      ? noMansLandHalfHeight + 1
      : noMansLandHalfHeight;

    if (arrayY < centreRow - halfHeight)
    {
      return TeamName.Blue;
    }

    if (arrayY > centreRow + halfHeight)
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
      CyclePurchaseSelection(-1);
    }
    else if (GetNextPurchaseButtonBounds().Contains(mousePosition))
    {
      CyclePurchaseSelection(1);
    }
    else if (GetPurchaseButtonBounds().Contains(mousePosition))
    {
      if (_initialBuyPhase == null)
      {
        _isPurchaseMode = !_isPurchaseMode;
        selectedPiece = null;
      }
    }

    return true;
  }

  private bool HandleTeacherChoiceClick(Point mousePosition)
  {
    if (selectedPiece?.Definition.Type != PieceType.Teacher ||
        !GetSelectedPiecePanelBounds().Contains(mousePosition))
    {
      return false;
    }

    if (GetTeacherPreviousButtonBounds().Contains(mousePosition))
    {
      _selectedTeacherDefinitionIndex =
        (_selectedTeacherDefinitionIndex - 1 + PieceDefinitions.Purchasable.Length) % PieceDefinitions.Purchasable.Length;
    }
    else if (GetTeacherNextButtonBounds().Contains(mousePosition))
    {
      _selectedTeacherDefinitionIndex =
        (_selectedTeacherDefinitionIndex + 1) % PieceDefinitions.Purchasable.Length;
    }

    return true;
  }

  private bool HandleOxCarryPanelClick(Point mousePosition)
  {
    if (selectedPiece?.Definition.Type != PieceType.Ox ||
        !GetSelectedPiecePanelBounds().Contains(mousePosition))
    {
      return false;
    }

    Piece cargo = GetOxCargo(selectedPiece);
    if (cargo != null && GetOxCargoButtonBounds().Contains(mousePosition))
    {
      SelectPiece(cargo, true);
    }

    return true;
  }

  private bool HandleEngineerAbilityClick(Point mousePosition)
  {
    if (selectedPiece?.Definition.Type != PieceType.Engineer ||
        !GetSelectedPiecePanelBounds().Contains(mousePosition))
    {
      return false;
    }

    if (GetEngineerPreviousButtonBounds().Contains(mousePosition))
    {
      _selectedEngineerAbility = (EngineerAbility)(
        ((int)_selectedEngineerAbility - 1 + Enum.GetValues<EngineerAbility>().Length) %
        Enum.GetValues<EngineerAbility>().Length
      );
    }
    else if (GetEngineerNextButtonBounds().Contains(mousePosition))
    {
      _selectedEngineerAbility = (EngineerAbility)(
        ((int)_selectedEngineerAbility + 1) % Enum.GetValues<EngineerAbility>().Length
      );
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

  private void DrawWorldPieceText(Matrix cameraTransform, int cellSize)
  {
    float textRotation = _rotateBoard ? MathHelper.PiOver2 : 0f;
    foreach (Piece piece in pieceSetup.Pieces)
    {
      if (piece.AttachmentKind == AttachmentKind.Carried && piece.AttachedTo != null)
      {
        Rectangle hostBounds = GetPieceWorldBounds(piece.AttachedTo, cellSize);
        Rectangle cargoBadge = new(hostBounds.Right - 30, hostBounds.Y + 6, 24, 24);
        DrawRotatedWorldText(
          UiText.BuildPieceLabel(piece.Definition),
          new Vector2(cargoBadge.Center.X, cargoBadge.Center.Y),
          0.48f,
          Vector2.One * 0.5f,
          textRotation,
          cameraTransform
        );
        continue;
      }

      Rectangle pieceBounds = GetPieceWorldBounds(piece, cellSize);
      DrawRotatedWorldText(
        UiText.BuildPieceLabel(piece.Definition),
        new Vector2(pieceBounds.Center.X, pieceBounds.Center.Y),
        1f,
        Vector2.One * 0.5f,
        textRotation,
        cameraTransform
      );
      DrawRotatedWorldText(
        $"HP {piece.CurrentHealth}",
        new Vector2(pieceBounds.Center.X, pieceBounds.Y + 6),
        0.6f,
        new Vector2(0.5f, 0f),
        textRotation,
        cameraTransform
      );
    }
  }

  private void DrawRotatedWorldText(
    string text,
    Vector2 worldAnchor,
    float scale,
    Vector2 originRatio,
    float rotation,
    Matrix cameraTransform
  )
  {
    Vector2 textSize = _pieceLabelFont.MeasureString(text);
    Vector2 origin = textSize * originRatio;
    _spriteBatch.DrawString(
      _pieceLabelFont,
      text,
      Vector2.Transform(worldAnchor, cameraTransform),
      UiTheme.TextPrimary,
      rotation,
      origin,
      scale * _zoom,
      SpriteEffects.None,
      0f
    );
  }

  private Rectangle GetPieceWorldBounds(Piece piece, int cellSize)
  {
    Vector2 renderedPosition = GetRenderedPosition(piece);
    int pieceX = (int)MathF.Round((renderedPosition.X - _board.MinX) * cellSize);
    int pieceY = (int)MathF.Round((renderedPosition.Y - _board.MinY) * cellSize);
    return new Rectangle(
      pieceX,
      pieceY,
      piece.Definition.Size.x * cellSize,
      piece.Definition.Size.y * cellSize
    );
  }

  private Vector2 GetRenderedPosition(Piece piece)
  {
    if (_movementAnimation == null)
    {
      return new Vector2(piece.Position.x, piece.Position.y);
    }

    Piece animatedPiece = _movementAnimation.Piece;
    bool followsAnimatedPiece =
      piece == animatedPiece ||
      (piece.AttachedTo == animatedPiece &&
       piece.AttachmentKind is AttachmentKind.Carried or AttachmentKind.Towed);
    if (!followsAnimatedPiece)
    {
      return new Vector2(piece.Position.x, piece.Position.y);
    }

    float pathProgress = _movementAnimation.ElapsedSeconds / MovementAnimation.SecondsPerStep;
    int segmentIndex = Math.Min((int)pathProgress, _movementAnimation.Path.Count - 1);
    float segmentProgress = MathHelper.Clamp(pathProgress - segmentIndex, 0f, 1f);
    (int x, int y) segmentStart = segmentIndex == 0
      ? animatedPiece.Position
      : _movementAnimation.Path[segmentIndex - 1];
    (int x, int y) segmentEnd = _movementAnimation.Path[segmentIndex];
    Vector2 animatedPosition = Vector2.Lerp(
      new Vector2(segmentStart.x, segmentStart.y),
      new Vector2(segmentEnd.x, segmentEnd.y),
      segmentProgress
    );

    if (piece == animatedPiece)
    {
      return animatedPosition;
    }

    return new Vector2(piece.Position.x, piece.Position.y) +
      animatedPosition - new Vector2(animatedPiece.Position.x, animatedPiece.Position.y);
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
    _ui.Text(_initialBuyPhase == null ? "PURCHASE PIECE" : "INITIAL PURCHASE", new Vector2(content.X, content.Y), UiTheme.Gold);
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
      ? _initialBuyPhase != null
        ? "Mercenaries are unavailable during the initial buy phase."
        : "Place on a No-Man's-Land edge, or outbid a rival for +10 gold."
      : _initialBuyPhase != null
        ? $"{_initialBuyPhase.PurchasesThisTurn}/{_initialBuyPhase.PurchasesPerTurn} bought. Select a square on your side."
        : "Buy, then select a square. Click a rival Mercenary to buy it off.";
    _ui.Text(purchaseHint, new Vector2(content.X, previousButton.Y - 48), UiTheme.TextMuted, 0.76f);
    DrawMenuButton(previousButton, "<", UiButtonTone.Neutral);
    DrawMenuButton(nextButton, ">", UiButtonTone.Neutral);
    DrawMenuButton(
      purchaseButton,
      _initialBuyPhase != null ? "BUY MODE" : _isPurchaseMode ? "CANCEL" : "BUY",
      _initialBuyPhase != null ? UiButtonTone.Accent : _isPurchaseMode ? UiButtonTone.Danger : UiButtonTone.Primary,
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
    return
      Matrix.CreateTranslation(-_cameraPosition.X, -_cameraPosition.Y, 0)
      * GetBoardRotationTransform()
      * Matrix.CreateScale(_zoom)
      * Matrix.CreateTranslation(screenCentre.X, screenCentre.Y, 0);
  }

  private Matrix GetBoardRotationTransform()
  {
    return _rotateBoard ? Matrix.CreateRotationZ(MathHelper.PiOver2) : Matrix.Identity;
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

  private Rectangle GetOnlinePanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    return UiLayout.Centered(viewport, 560, 470, UiTheme.SpaceLg);
  }

  private Rectangle GetOnlineButtonBounds(int index)
  {
    Rectangle content = UiLayout.Inset(GetOnlinePanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 174 + index * (UiTheme.ButtonHeight + UiTheme.SpaceSm), content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetOnlineServerUrlBounds()
  {
    Rectangle content = UiLayout.Inset(GetOnlinePanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 76, content.Width, 56);
  }

  private Rectangle GetOnlineJoinCodeBounds()
  {
    Rectangle content = UiLayout.Inset(GetOnlinePanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 152, content.Width, 52);
  }

  private Rectangle GetOnlineJoinButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetOnlinePanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 226, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetOnlineBackButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetOnlinePanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 286, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetOnlineWaitingCancelButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetOnlinePanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Bottom - UiTheme.ButtonHeight, content.Width, UiTheme.ButtonHeight);
  }

  private void UpdateOnlineTextInput(Keys key, bool shiftHeld)
  {
    string value = _onlineInputFocus == OnlineInputField.ServerUrl
      ? _onlineServerUrl
      : _onlineJoinCode;
    int maximumLength = _onlineInputFocus == OnlineInputField.ServerUrl ? 160 : 5;
    if (key == Keys.Back)
    {
      if (value.Length > 0)
      {
        value = value[..^1];
      }
      SetOnlineInputValue(value);
      _onlineError = string.Empty;
      return;
    }

    if (value.Length < maximumLength && TryGetOnlineInputCharacter(key, shiftHeld, out char character))
    {
      value += _onlineInputFocus == OnlineInputField.JoinCode
        ? char.ToUpperInvariant(character)
        : character;
      SetOnlineInputValue(value);
      _onlineError = string.Empty;
    }
  }

  private void PasteOnlineInput()
  {
    if (!TryGetClipboardText(out string clipboardText))
    {
      _onlineError = "Could not read text from the clipboard.";
      return;
    }

    string value = _onlineInputFocus == OnlineInputField.ServerUrl
      ? clipboardText.Trim()
      : ExtractRoomCodeFromClipboard(clipboardText);
    int maximumLength = _onlineInputFocus == OnlineInputField.ServerUrl ? 160 : 5;
    SetOnlineInputValue(value[..Math.Min(value.Length, maximumLength)]);
    _onlineError = string.Empty;
  }

  private static string ExtractRoomCodeFromClipboard(string clipboardText)
  {
    string[] candidates = clipboardText
      .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
      .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
      .ToArray();
    string exactCode = candidates.FirstOrDefault(candidate => candidate.Length == 5);
    return (exactCode ?? new string(clipboardText.Where(char.IsLetterOrDigit).Take(5).ToArray())).ToUpperInvariant();
  }

  private static bool TryGetClipboardText(out string clipboardText)
  {
    clipboardText = string.Empty;
    if (!OperatingSystem.IsWindows())
    {
      return false;
    }

    try
    {
      if (!OpenClipboard(IntPtr.Zero))
      {
        return false;
      }

      try
      {
        IntPtr handle = GetClipboardData(13); // CF_UNICODETEXT
        if (handle == IntPtr.Zero)
        {
          return false;
        }

        IntPtr pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
          return false;
        }

        try
        {
          clipboardText = Marshal.PtrToStringUni(pointer) ?? string.Empty;
        }
        finally
        {
          GlobalUnlock(handle);
        }
      }
      finally
      {
        CloseClipboard();
      }

      return !string.IsNullOrWhiteSpace(clipboardText);
    }
    catch (Exception)
    {
      return false;
    }
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool OpenClipboard(IntPtr windowHandle);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool CloseClipboard();

  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr GetClipboardData(uint format);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern IntPtr GlobalLock(IntPtr handle);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool GlobalUnlock(IntPtr handle);

  private void SetOnlineInputValue(string value)
  {
    if (_onlineInputFocus == OnlineInputField.ServerUrl)
    {
      _onlineServerUrl = value;
    }
    else
    {
      _onlineJoinCode = value;
    }
  }

  private static bool TryGetOnlineInputCharacter(Keys key, bool shiftHeld, out char character)
  {
    string name = key.ToString();
    if (name.Length == 1 && char.IsLetterOrDigit(name[0]))
    {
      character = name[0];
      return true;
    }

    character = key switch
    {
      Keys.OemPeriod => '.',
      Keys.OemMinus => '-',
      Keys.OemSemicolon => shiftHeld ? ':' : ';',
      Keys.OemQuestion => shiftHeld ? '?' : '/',
      _ => default
    };
    return character != default;
  }

  private void DrawOnlineServerUrlField(Rectangle bounds)
  {
    bool isFocused = _onlineInputFocus == OnlineInputField.ServerUrl;
    DrawPanel(bounds, UiTheme.PanelRaised, isFocused ? UiTheme.Gold : UiTheme.PanelBorderSubtle);
    _ui.Text("MATCH SERVER URL", new Vector2(bounds.X + UiTheme.SpaceMd, bounds.Y + 5), UiTheme.TextMuted, 0.58f);
    _ui.Text(
      string.IsNullOrWhiteSpace(_onlineServerUrl) ? "https://your-server.onrender.com" : _onlineServerUrl,
      new Vector2(bounds.X + UiTheme.SpaceMd, bounds.Y + 28),
      string.IsNullOrWhiteSpace(_onlineServerUrl) ? UiTheme.TextDim : UiTheme.TextPrimary,
      0.63f
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

  private Rectangle GetPausePanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    return UiLayout.Centered(viewport, 460, 430, UiTheme.SpaceLg);
  }

  private Rectangle GetPauseButtonBounds(int index)
  {
    Rectangle panel = GetPausePanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    int top = content.Y + 98;
    return new Rectangle(
      content.X,
      top + index * (UiTheme.ButtonHeight + UiTheme.SpaceSm),
      content.Width,
      UiTheme.ButtonHeight
    );
  }

  private Rectangle GetEncyclopediaPanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    return UiLayout.Centered(viewport, 980, 700, UiTheme.SpaceLg);
  }

  private Rectangle GetEncyclopediaPreviousButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetEncyclopediaPanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 58, 62, 38);
  }

  private Rectangle GetEncyclopediaNextButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetEncyclopediaPanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.Right - 62, content.Y + 58, 62, 38);
  }

  private Rectangle GetEncyclopediaBackButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetEncyclopediaPanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.Right - 150, content.Bottom - UiTheme.ButtonHeight, 150, UiTheme.ButtonHeight);
  }

  private Rectangle GetSetupPanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    return UiLayout.Centered(viewport, 640, 620, UiTheme.SpaceLg);
  }

  private Rectangle GetSetupPreviousButtonBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, panel.Bottom - 68, 68, UiTheme.ButtonHeight);
  }

  private Rectangle GetSetupBackButtonBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.Right - 96, content.Y - 2, 96, 30);
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

  private Rectangle GetBattlefieldRowBounds(int index) => GetEconomyRowBounds(index);

  private Rectangle GetBattlefieldDecreaseButtonBounds(int index) => GetEconomyDecreaseButtonBounds(index);

  private Rectangle GetBattlefieldValueBounds(int index) => GetEconomyValueBounds(index);

  private Rectangle GetBattlefieldIncreaseButtonBounds(int index) => GetEconomyIncreaseButtonBounds(index);

  private static string GetBoardFileName(BoardSize boardSize)
  {
    return boardSize switch
    {
      BoardSize.Small => "board_small.json",
      BoardSize.Large => "board_large.json",
      _ => "board_medium.json"
    };
  }

  private void ApplyBattlefieldSetup()
  {
    ConfigureBattlefield(_selectedBoardSize, _forestDensity, _waterwayDensity, Random.Shared.Next());
  }

  private void ConfigureBattlefield(
    BoardSize boardSize,
    TerrainDensity forestDensity,
    TerrainDensity waterwayDensity,
    int terrainSeed
  )
  {
    _terrainSeed = terrainSeed;
    _board = new Board(GetBoardFileName(boardSize));
    _terrain = BattlefieldTerrain.CreateRandom(_board, terrainSeed, CreateTerrainGenerationSettings(forestDensity, waterwayDensity));
    _roads.Clear();
    _barricades.Clear();
    _mines.Clear();
    _restoredLakeTiles.Clear();
    _riverBridges.Clear();
  }

  private static TerrainGenerationSettings CreateTerrainGenerationSettings(
    TerrainDensity forestDensity,
    TerrainDensity waterwayDensity
  )
  {
    return new TerrainGenerationSettings
    {
      MinimumForestGroups = forestDensity switch
      {
        TerrainDensity.Light => 2,
        TerrainDensity.Heavy => 6,
        _ => 4
      },
      MaximumForestGroups = forestDensity switch
      {
        TerrainDensity.Light => 3,
        TerrainDensity.Heavy => 8,
        _ => 6
      },
      MinimumForestClusterSize = forestDensity == TerrainDensity.Light ? 2 : 3,
      MaximumForestClusterSize = forestDensity switch
      {
        TerrainDensity.Light => 4,
        TerrainDensity.Heavy => 8,
        _ => 6
      },
      LargeBoardCellCount = waterwayDensity switch
      {
        TerrainDensity.Light => int.MaxValue,
        TerrainDensity.Heavy => 0,
        _ => 300
      },
      AdditionalRiverChance = waterwayDensity switch
      {
        TerrainDensity.Light => 0,
        TerrainDensity.Heavy => 1,
        _ => 0.4
      }
    };
  }

  private void ReturnToTitle()
  {
    if (_onlineClient != null)
    {
      _ = _onlineClient.DisposeAsync().AsTask();
      _onlineClient = null;
    }

    _onlineWaitingForOpponent = false;
    _onlineRoyalChoicePending = false;
    _onlineHostingSetup = false;
    _onlineMatchConfiguration = null;
    _onlineError = string.Empty;
    _onlineStatus = "OFFLINE";
    pieceSetup.ClearPieces();
    _teams = pieceSetup.CreateTeams();
    ConfigureBattlefield(BoardSize.Medium, TerrainDensity.Standard, TerrainDensity.Standard, Random.Shared.Next());
    selectedPiece = null;
    _cavalierAwaitingAttack = null;
    _movementAnimation = null;
    _initialBuyPhase = null;
    _isPurchaseMode = false;
    _selectedPurchaseIndex = 0;
    _selectedTeacherDefinitionIndex = 0;
    _selectedEngineerAbility = EngineerAbility.Road;
    _winningTeam = null;
    _gameMode = GameMode.Regicide;
    _conquestWinScore = 15;
    _conquestScore = 0;
    _setupTeam = TeamName.Red;
    _selectedRoyalIndex = 0;
    _setupStage = SetupStage.Mode;
    _cameraPosition = Vector2.Zero;
    _zoom = 1f;
    Team.ResetTurn();
    _screen = Screen.Title;
  }

  private void BeginMatchSetup(bool onlineHost = false)
  {
    _screen = Screen.Setup;
    _setupTeam = TeamName.Red;
    _selectedRoyalIndex = 0;
    _setupStage = SetupStage.Mode;
    _gameMode = GameMode.Regicide;
    _conquestWinScore = 15;
    _conquestScore = 0;
    _selectedBoardSize = BoardSize.Medium;
    _forestDensity = TerrainDensity.Standard;
    _waterwayDensity = TerrainDensity.Standard;
    _startingCash = Globals.StartingCash;
    _killerRefundMultiplier = Globals.KillerDeathRefundMultiplier;
    _defeatedTeamRefundMultiplier = Globals.DefeatedTeamDeathRefundMultiplier;
    _initialBuysPerTurn = 2;
    _initialBuyTurnsPerTeam = 4;
    _initialBuyPhase = null;
    _isPurchaseMode = false;
    _selectedEngineerAbility = EngineerAbility.Road;
    _onlineHostingSetup = onlineHost;
    _onlineMatchConfiguration = null;
    Team.ResetTurn();
  }

  private void PrepareOnlineRoom()
  {
    pieceSetup.ClearPieces();
    _teams = pieceSetup.CreateTeams();
    ConfigureBattlefield(_selectedBoardSize, _forestDensity, _waterwayDensity, _terrainSeed);
    selectedPiece = null;
    _initialBuyPhase = null;
    _isPurchaseMode = false;
    _selectedRoyalIndex = 0;
    _onlineRoyalChoicePending = false;
    Team.ResetTurn();
  }

  private NetworkMatchConfiguration BuildOnlineMatchConfiguration()
  {
    return new NetworkMatchConfiguration(
      _selectedBoardSize.ToString(),
      _forestDensity.ToString(),
      _waterwayDensity.ToString(),
      _gameMode.ToString(),
      _terrainSeed,
      _startingCash,
      _killerRefundMultiplier,
      _defeatedTeamRefundMultiplier,
      _initialBuysPerTurn,
      _initialBuyTurnsPerTeam,
      _conquestWinScore
    );
  }

  private void UpdateMenu(
    KeyboardState keyboard,
    MouseState mouse,
    bool wasLeftClick,
    bool wasEscapePressed
  )
  {
    if (wasEscapePressed)
    {
      if (_bindingToChange.HasValue)
      {
        _bindingToChange = null;
        return;
      }

      switch (_screen)
      {
        case Screen.Pause:
          _screen = Screen.Playing;
          return;
        case Screen.Encyclopedia:
          _screen = Screen.Pause;
          return;
        case Screen.Settings when _settingsReturnScreen == Screen.Pause:
          _screen = Screen.Pause;
          return;
        case Screen.Settings:
          _screen = _settingsReturnScreen;
          return;
        case Screen.OnlineJoin:
          _screen = Screen.OnlineLobby;
          return;
        case Screen.OnlineLobby:
          _screen = Screen.Title;
          return;
        case Screen.OnlineWaiting:
          ReturnToTitle();
          return;
        case Screen.OnlineRoyalSelection:
          ReturnToTitle();
          return;
        case Screen.Setup:
          NavigateSetupBack();
          return;
      }
    }

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

    if (_screen is Screen.OnlineLobby or Screen.OnlineJoin)
    {
      bool controlHeld = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
      foreach (Keys key in keyboard.GetPressedKeys())
      {
        if (!_previousKeyboardState.IsKeyDown(key))
        {
          if (key == Keys.V && controlHeld)
          {
            PasteOnlineInput();
          }
          else if (key == Keys.Enter && _screen == Screen.OnlineJoin &&
              _onlineInputFocus == OnlineInputField.JoinCode && _onlineJoinCode.Length > 0)
          {
            _ = JoinOnlineMatchAsync(_onlineJoinCode);
          }
          else
          {
            UpdateOnlineTextInput(key, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
          }
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
          BeginMatchSetup();
        }
        else if (GetTitleButtonBounds(1).Contains(mousePosition))
        {
          _screen = Screen.OnlineLobby;
        }
        else if (GetTitleButtonBounds(2).Contains(mousePosition))
        {
          _settingsReturnScreen = Screen.Title;
          _screen = Screen.Settings;
        }
        else if (GetTitleButtonBounds(3).Contains(mousePosition))
        {
          Exit();
        }
        break;

      case Screen.OnlineLobby:
        if (GetOnlineServerUrlBounds().Contains(mousePosition))
        {
          _onlineInputFocus = OnlineInputField.ServerUrl;
        }
        else if (GetOnlineButtonBounds(0).Contains(mousePosition))
        {
          BeginMatchSetup(onlineHost: true);
        }
        else if (GetOnlineButtonBounds(1).Contains(mousePosition))
        {
          _onlineJoinCode = string.Empty;
          _onlineInputFocus = OnlineInputField.JoinCode;
          _screen = Screen.OnlineJoin;
        }
        else if (GetOnlineButtonBounds(2).Contains(mousePosition))
        {
          _screen = Screen.Title;
        }
        break;

      case Screen.OnlineJoin:
        if (GetOnlineServerUrlBounds().Contains(mousePosition))
        {
          _onlineInputFocus = OnlineInputField.ServerUrl;
        }
        else if (GetOnlineJoinCodeBounds().Contains(mousePosition))
        {
          _onlineInputFocus = OnlineInputField.JoinCode;
        }
        else if (GetOnlineJoinButtonBounds().Contains(mousePosition) && _onlineJoinCode.Length > 0)
        {
          _ = JoinOnlineMatchAsync(_onlineJoinCode);
        }
        else if (GetOnlineBackButtonBounds().Contains(mousePosition))
        {
          _screen = Screen.OnlineLobby;
        }
        break;

      case Screen.OnlineWaiting:
        if (GetOnlineWaitingCancelButtonBounds().Contains(mousePosition))
        {
          ReturnToTitle();
        }
        break;

      case Screen.OnlineRoyalSelection:
        if (GetSetupBackButtonBounds().Contains(mousePosition))
        {
          ReturnToTitle();
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
          _ = SendOnlineRoyalChoiceAsync();
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
          _screen = _settingsReturnScreen;
        }
        break;

      case Screen.Pause:
        if (GetPauseButtonBounds(0).Contains(mousePosition))
        {
          _screen = Screen.Playing;
        }
        else if (GetPauseButtonBounds(1).Contains(mousePosition))
        {
          _settingsReturnScreen = Screen.Pause;
          _screen = Screen.Settings;
        }
        else if (GetPauseButtonBounds(2).Contains(mousePosition))
        {
          _screen = Screen.Encyclopedia;
        }
        else if (GetPauseButtonBounds(3).Contains(mousePosition))
        {
          ReturnToTitle();
        }
        break;

      case Screen.Encyclopedia:
        if (GetEncyclopediaPreviousButtonBounds().Contains(mousePosition))
        {
          _encyclopediaIndex =
            (_encyclopediaIndex - 1 + PieceDefinitions.Encyclopedia.Length) % PieceDefinitions.Encyclopedia.Length;
        }
        else if (GetEncyclopediaNextButtonBounds().Contains(mousePosition))
        {
          _encyclopediaIndex =
            (_encyclopediaIndex + 1) % PieceDefinitions.Encyclopedia.Length;
        }
        else if (GetEncyclopediaBackButtonBounds().Contains(mousePosition))
        {
          _screen = Screen.Pause;
        }
        break;

      case Screen.Setup:
        if (GetSetupBackButtonBounds().Contains(mousePosition))
        {
          NavigateSetupBack();
        }
        else if (_setupStage == SetupStage.Mode)
        {
          if (GetSetupPreviousButtonBounds().Contains(mousePosition))
          {
            _gameMode = (GameMode)(((int)_gameMode - 1 + Enum.GetValues<GameMode>().Length) % Enum.GetValues<GameMode>().Length);
          }
          else if (GetSetupNextButtonBounds().Contains(mousePosition))
          {
            _gameMode = (GameMode)(((int)_gameMode + 1) % Enum.GetValues<GameMode>().Length);
          }
          else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
          {
            _setupStage = SetupStage.Battlefield;
          }
        }
        else if (_setupStage == SetupStage.Battlefield)
        {
          if (GetBattlefieldDecreaseButtonBounds(0).Contains(mousePosition))
          {
            _selectedBoardSize = (BoardSize)Math.Max((int)BoardSize.Small, (int)_selectedBoardSize - 1);
          }
          else if (GetBattlefieldIncreaseButtonBounds(0).Contains(mousePosition))
          {
            _selectedBoardSize = (BoardSize)Math.Min((int)BoardSize.Large, (int)_selectedBoardSize + 1);
          }
          else if (GetBattlefieldDecreaseButtonBounds(1).Contains(mousePosition))
          {
            _forestDensity = (TerrainDensity)Math.Max((int)TerrainDensity.Light, (int)_forestDensity - 1);
          }
          else if (GetBattlefieldIncreaseButtonBounds(1).Contains(mousePosition))
          {
            _forestDensity = (TerrainDensity)Math.Min((int)TerrainDensity.Heavy, (int)_forestDensity + 1);
          }
          else if (GetBattlefieldDecreaseButtonBounds(2).Contains(mousePosition))
          {
            _waterwayDensity = (TerrainDensity)Math.Max((int)TerrainDensity.Light, (int)_waterwayDensity - 1);
          }
          else if (GetBattlefieldIncreaseButtonBounds(2).Contains(mousePosition))
          {
            _waterwayDensity = (TerrainDensity)Math.Min((int)TerrainDensity.Heavy, (int)_waterwayDensity + 1);
          }
          else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
          {
            ApplyBattlefieldSetup();
            _setupStage = SetupStage.Economy;
          }
        }
        else if (_setupStage == SetupStage.Economy)
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
          else if (GetEconomyDecreaseButtonBounds(3).Contains(mousePosition))
          {
            _initialBuysPerTurn = Math.Max(1, _initialBuysPerTurn - 1);
          }
          else if (GetEconomyIncreaseButtonBounds(3).Contains(mousePosition))
          {
            _initialBuysPerTurn++;
          }
          else if (GetEconomyDecreaseButtonBounds(4).Contains(mousePosition))
          {
            _initialBuyTurnsPerTeam = Math.Max(1, _initialBuyTurnsPerTeam - 1);
          }
          else if (GetEconomyIncreaseButtonBounds(4).Contains(mousePosition))
          {
            _initialBuyTurnsPerTeam++;
          }
          else if (_gameMode == GameMode.Conquest && GetEconomyDecreaseButtonBounds(5).Contains(mousePosition))
          {
            _conquestWinScore = Math.Max(1, _conquestWinScore - 1);
          }
          else if (_gameMode == GameMode.Conquest && GetEconomyIncreaseButtonBounds(5).Contains(mousePosition))
          {
            _conquestWinScore++;
          }
          else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
          {
            foreach (Team team in _teams)
            {
              team.Money = _startingCash;
              team.ActionPoints = Team.ActionsPerTurn;
            }

            if (_onlineHostingSetup)
            {
              _onlineMatchConfiguration = BuildOnlineMatchConfiguration();
              PrepareOnlineRoom();
              _onlineHostingSetup = false;
              _ = HostOnlineMatchAsync(_onlineMatchConfiguration);
            }
            else
            {
              _setupStage = SetupStage.RoyalSelection;
            }
          }
        }
        else if (GetSetupPreviousButtonBounds().Contains(mousePosition))
        {
          _selectedRoyalIndex = GetNextSelectableRoyalIndex(_selectedRoyalIndex, -1);
        }
        else if (GetSetupNextButtonBounds().Contains(mousePosition))
        {
          _selectedRoyalIndex = GetNextSelectableRoyalIndex(_selectedRoyalIndex, 1);
        }
        else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
        {
          PieceDefinition royal = PieceDefinitions.Royals[_selectedRoyalIndex];
          if (_gameMode == GameMode.Escort && royal.Type == PieceType.Palace)
          {
            _selectedRoyalIndex = GetNextSelectableRoyalIndex(_selectedRoyalIndex, 1);
            return;
          }
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
            StartInitialBuyPhase();
          }
        }
        break;

      case Screen.GameOver:
        if (GetTitleButtonBounds(2).Contains(mousePosition))
        {
          ReturnToTitle();
        }
        else if (GetTitleButtonBounds(3).Contains(mousePosition))
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

  private int GetNextSelectableRoyalIndex(int currentIndex, int direction)
  {
    for (int offset = 1; offset <= PieceDefinitions.Royals.Length; offset++)
    {
      int index = (currentIndex + direction * offset + PieceDefinitions.Royals.Length) % PieceDefinitions.Royals.Length;
      if (_gameMode != GameMode.Escort || PieceDefinitions.Royals[index].Type != PieceType.Palace)
      {
        return index;
      }
    }

    return currentIndex;
  }

  private void NavigateSetupBack()
  {
    switch (_setupStage)
    {
      case SetupStage.Mode:
        ReturnToTitle();
        break;
      case SetupStage.Battlefield:
        _setupStage = SetupStage.Mode;
        break;
      case SetupStage.Economy:
        _setupStage = SetupStage.Battlefield;
        break;
      case SetupStage.RoyalSelection when _setupTeam == TeamName.Blue:
        Piece redRoyal = pieceSetup.Pieces.FirstOrDefault(piece =>
          piece.Team == TeamName.Red && piece.Definition.Category == PieceCategory.Royal);
        if (redRoyal != null)
        {
          pieceSetup.RemovePiece(redRoyal);
          _teams.Find(team => team.TeamName == TeamName.Red).ClearRoyal();
        }
        _setupTeam = TeamName.Red;
        _selectedRoyalIndex = 0;
        break;
      default:
        _setupStage = SetupStage.Economy;
        break;
    }
  }

  private bool IsConquestSquare((int x, int y) position)
  {
    int centreX = _board.MinX + _board.BoardArray.GetLength(1) / 2;
    int centreY = _board.MinY + _board.BoardArray.GetLength(0) / 2;
    return Math.Abs(position.x - centreX) <= 1 &&
      Math.Abs(position.y - centreY) <= 1 &&
      IsBoardCell(position.x - _board.MinX, position.y - _board.MinY);
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

    _ui.CenterText("CROWN & SIEGE", titleBounds, UiTheme.GoldBright, 1.55f);
    _ui.CenterText("A MEDIEVAL STRATEGY GAME", subtitleBounds, UiTheme.TextMuted, 0.72f);
    _ui.Divider(new Rectangle(viewport.Center.X - 150, subtitleBounds.Bottom + UiTheme.SpaceMd, 300, 1), subtitleBounds.Bottom + UiTheme.SpaceMd, UiTheme.PanelBorder);

    DrawMenuButton(GetTitleButtonBounds(0), "START GAME", UiButtonTone.Primary);
    DrawMenuButton(GetTitleButtonBounds(1), "ONLINE MULTIPLAYER", UiButtonTone.Accent);
    DrawMenuButton(GetTitleButtonBounds(2), "SETTINGS", UiButtonTone.Neutral);
    DrawMenuButton(GetTitleButtonBounds(3), "QUIT GAME", UiButtonTone.Danger);
  }

  private void DrawOnlineLobbyScreen()
  {
    Rectangle panel = GetOnlinePanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("ONLINE MULTIPLAYER", new Vector2(content.X, content.Y), UiTheme.Gold);
    _ui.Text("Enter the server link, then host a room or join one.", new Vector2(content.X, content.Y + 30), UiTheme.TextMuted, 0.7f);
    _ui.Text("Click a field, then press Ctrl+V to paste.", new Vector2(content.X, content.Y + 51), UiTheme.TextDim, 0.56f);
    DrawOnlineServerUrlField(GetOnlineServerUrlBounds());
    if (!string.IsNullOrWhiteSpace(_onlineError))
    {
      _ui.Text(_onlineError, new Vector2(content.X, content.Y + 142), UiTheme.Attack, 0.64f);
    }
    _ui.Divider(content, content.Y + 158);
    DrawMenuButton(GetOnlineButtonBounds(0), "HOST NEW ROOM", UiButtonTone.Primary);
    DrawMenuButton(GetOnlineButtonBounds(1), "JOIN ROOM", UiButtonTone.Accent);
    DrawMenuButton(GetOnlineButtonBounds(2), "BACK", UiButtonTone.Neutral);
  }

  private void DrawOnlineJoinScreen()
  {
    Rectangle panel = GetOnlinePanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    Rectangle codeBounds = GetOnlineJoinCodeBounds();
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("JOIN PRIVATE ROOM", new Vector2(content.X, content.Y), UiTheme.Gold);
    _ui.Text("Enter the same server link and room code as the host.", new Vector2(content.X, content.Y + 30), UiTheme.TextMuted, 0.7f);
    _ui.Text("Click a field, then press Ctrl+V to paste.", new Vector2(content.X, content.Y + 51), UiTheme.TextDim, 0.56f);
    DrawOnlineServerUrlField(GetOnlineServerUrlBounds());
    DrawPanel(codeBounds, UiTheme.PanelRaised, _onlineInputFocus == OnlineInputField.JoinCode ? UiTheme.Gold : UiTheme.PanelBorderSubtle);
    _ui.CenterText(string.IsNullOrEmpty(_onlineJoinCode) ? "ROOM CODE" : _onlineJoinCode, codeBounds, string.IsNullOrEmpty(_onlineJoinCode) ? UiTheme.TextDim : UiTheme.GoldBright, 1.1f);
    if (!string.IsNullOrWhiteSpace(_onlineError))
    {
      _ui.Text(_onlineError, new Vector2(content.X, codeBounds.Bottom + 4), UiTheme.Attack, 0.56f);
    }
    DrawMenuButton(GetOnlineJoinButtonBounds(), "JOIN", UiButtonTone.Primary);
    DrawMenuButton(GetOnlineBackButtonBounds(), "BACK", UiButtonTone.Neutral);
  }

  private void DrawOnlineWaitingScreen()
  {
    Rectangle panel = GetOnlinePanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    string roomCode = string.IsNullOrWhiteSpace(_onlineClient?.JoinCode) ? "-----" : _onlineClient.JoinCode;
    string team = _onlineClient?.Team?.ToString().ToUpperInvariant() ?? "";
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.CenterText("PRIVATE ROOM CREATED", new Rectangle(content.X, content.Y, content.Width, 30), UiTheme.GoldBright, 1.05f);
    _ui.CenterText("SHARE THIS ROOM CODE", new Rectangle(content.X, content.Y + 64, content.Width, 22), UiTheme.TextMuted, 0.76f);
    _ui.CenterText(roomCode, new Rectangle(content.X, content.Y + 94, content.Width, 52), UiTheme.GoldBright, 1.45f);
    _ui.CenterText($"YOU WILL PLAY {team}", new Rectangle(content.X, content.Y + 166, content.Width, 24), UiTheme.TextPrimary, 0.82f);
    _ui.CenterText("Waiting for another player to join...", new Rectangle(content.X, content.Y + 214, content.Width, 22), UiTheme.TextMuted, 0.74f);
    DrawMenuButton(GetOnlineWaitingCancelButtonBounds(), "CANCEL", UiButtonTone.Neutral);
  }

  private void DrawOnlineRoyalSelectionScreen()
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    PieceDefinition royal = PieceDefinitions.Royals[_selectedRoyalIndex];
    TeamName localTeam = _onlineClient?.Team == NetworkTeam.Blue ? TeamName.Blue : TeamName.Red;
    Color teamColour = UiTheme.GetTeamColour(localTeam);
    bool waitingForOpponent = _onlineRoyalChoicePending;

    DrawPanel(panel, UiTheme.Panel, teamColour);
    _ui.Text(waitingForOpponent ? "ROYAL CHOSEN" : "CHOOSE YOUR ROYAL", new Vector2(content.X, content.Y), teamColour);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    _ui.Text(
      waitingForOpponent ? "Waiting for the other player to choose theirs." : "You choose only your own royal. It is placed on your back row.",
      new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.72f);
    _ui.Divider(content, content.Y + 56);

    Rectangle preview = new(content.X, content.Y + 76, 112, 112);
    _ui.PiecePreview(preview, teamColour, UiText.BuildPieceLabel(royal));
    Rectangle details = new(preview.Right + UiTheme.SpaceLg, preview.Y, content.Right - preview.Right - UiTheme.SpaceLg, preview.Height);
    _ui.Text(royal.Type.ToString().ToUpperInvariant(), new Vector2(details.X, details.Y), UiTheme.TextPrimary);
    _ui.LabelValueRow(new Rectangle(details.X, details.Y + 30, details.Width, 26), "HEALTH", royal.Health.ToString(), UiTheme.Health);
    _ui.LabelValueRow(new Rectangle(details.X, details.Y + 58, details.Width, 26), "ATTACK", royal.Attack.ToString(), UiTheme.Attack);
    _ui.LabelValueRow(new Rectangle(details.X, details.Y + 86, details.Width, 26), "SIZE", $"{royal.Size.x} x {royal.Size.y}", UiTheme.TextPrimary);

    Rectangle actionGrid = new(content.X, preview.Bottom + UiTheme.SpaceLg, content.Width, 54);
    _ui.StatBlock(UiLayout.HorizontalSlot(actionGrid, 2, 0, UiTheme.SpaceSm), "MOVE", UiText.FormatAction(royal.Movement), UiTheme.Move);
    _ui.StatBlock(UiLayout.HorizontalSlot(actionGrid, 2, 1, UiTheme.SpaceSm), "ATTACK RANGE", UiText.FormatAction(royal.AttackShape), UiTheme.Attack);

    DrawMenuButton(GetSetupPreviousButtonBounds(), "<", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupNextButtonBounds(), ">", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupConfirmButtonBounds(), waitingForOpponent ? "WAITING..." : "CONFIRM ROYAL", waitingForOpponent ? UiButtonTone.Neutral : UiButtonTone.Primary, waitingForOpponent);
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
    DrawMenuButton(
      GetSettingsBackButtonBounds(),
      _settingsReturnScreen == Screen.Pause ? "BACK TO PAUSE" : "BACK",
      UiButtonTone.Primary
    );
  }

  private void DrawSetupScreen()
  {
    Rectangle panel = GetSetupPanelBounds();

    if (_setupStage == SetupStage.Mode)
    {
      DrawModeSetup(panel);
      return;
    }

    if (_setupStage == SetupStage.Battlefield)
    {
      DrawBattlefieldSetup(panel);
      return;
    }

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
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
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

  private void DrawModeSetup(Rectangle panel)
  {
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    (string title, string objective, string detail) = _gameMode switch
    {
      GameMode.Conquest => (
        "CONQUEST",
        "Hold the centre 3 x 3 objective to move the control bar toward your side.",
        $"Win at {_conquestWinScore} control. Royals may fall without ending the match."
      ),
      GameMode.Escort => (
        "ESCORT",
        "Get your royal onto the enemy back edge before the opposing royal does.",
        "A defeated royal respawns at its own back edge. Palace is unavailable."
      ),
      _ => (
        "REGICIDE",
        "Destroy the opposing royal to win the battle.",
        "The classic Crown & Siege match."
      )
    };

    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("CHOOSE GAME MODE", new Vector2(content.X, content.Y), UiTheme.Gold);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    _ui.Text(
      _onlineHostingSetup ? "Choose the rules other players will use in this private room." : "Select the victory condition before configuring the battlefield.",
      new Vector2(content.X, content.Y + 28),
      UiTheme.TextMuted,
      0.74f
    );
    _ui.Divider(content, content.Y + 56);

    Rectangle modeCard = new(content.X, content.Y + 86, content.Width, 216);
    DrawPanel(modeCard, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    _ui.CenterText(title, new Rectangle(modeCard.X, modeCard.Y + 26, modeCard.Width, 34), UiTheme.GoldBright, 1.08f);
    _ui.CenterText(objective, new Rectangle(modeCard.X + UiTheme.SpaceLg, modeCard.Y + 82, modeCard.Width - UiTheme.SpaceXl * 2, 42), UiTheme.TextPrimary, 0.72f);
    _ui.CenterText(detail, new Rectangle(modeCard.X + UiTheme.SpaceLg, modeCard.Y + 146, modeCard.Width - UiTheme.SpaceXl * 2, 40), UiTheme.TextMuted, 0.68f);
    _ui.CenterText($"{(int)_gameMode + 1}/{Enum.GetValues<GameMode>().Length}", new Rectangle(modeCard.X, modeCard.Bottom - 30, modeCard.Width, 18), UiTheme.TextDim, 0.64f);

    DrawMenuButton(GetSetupPreviousButtonBounds(), "<", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupNextButtonBounds(), ">", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONTINUE", UiButtonTone.Primary);
  }

  private void DrawBattlefieldSetup(Rectangle panel)
  {
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("BATTLEFIELD SETUP", new Vector2(content.X, content.Y), UiTheme.Gold);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    _ui.Text("Choose the battlefield before setting the match economy.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.76f);
    _ui.Divider(content, content.Y + 56);

    string[] labels = ["Board size", "Forest density", "Waterways"];
    string[] values =
    [
      _selectedBoardSize.ToString(),
      _forestDensity.ToString(),
      _waterwayDensity.ToString()
    ];

    for (int index = 0; index < labels.Length; index++)
    {
      Rectangle row = GetBattlefieldRowBounds(index);
      Rectangle valueBounds = GetBattlefieldValueBounds(index);
      DrawPanel(row, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
      _ui.Text(labels[index].ToUpperInvariant(), new Vector2(row.X + UiTheme.SpaceMd, row.Center.Y - 10), UiTheme.TextPrimary, 0.8f);
      DrawMenuButton(GetBattlefieldDecreaseButtonBounds(index), "-", UiButtonTone.Neutral);
      DrawPanel(valueBounds, UiTheme.Panel, UiTheme.Gold);
      _ui.CenterText(values[index].ToUpperInvariant(), valueBounds, UiTheme.GoldBright, 0.76f);
      DrawMenuButton(GetBattlefieldIncreaseButtonBounds(index), "+", UiButtonTone.Neutral);
    }

    _ui.Text("Light waterways use one river; heavy waterways use two.", new Vector2(content.X, GetSetupConfirmButtonBounds().Y - 58), UiTheme.TextMuted, 0.68f);
    _ui.Text("Forests are always denser away from the edge.", new Vector2(content.X, GetSetupConfirmButtonBounds().Y - 34), UiTheme.TextMuted, 0.68f);
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONTINUE", UiButtonTone.Primary);
  }

  private void DrawEconomySetup(Rectangle panel)
  {
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("MATCH ECONOMY", new Vector2(content.X, content.Y), UiTheme.Gold);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    _ui.Text("Set economy and the pre-game purchase phase.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.76f);
    _ui.Divider(content, content.Y + 56);

    List<string> labels = [
      "Starting cash", "Killer refund", "Defeated team refund", "Buys per buy turn", "Buy turns per team"
    ];
    List<string> values = [
      _startingCash.ToString(), $"{_killerRefundMultiplier:0.0}x", $"{_defeatedTeamRefundMultiplier:0.0}x",
      _initialBuysPerTurn.ToString(), _initialBuyTurnsPerTeam.ToString()
    ];
    if (_gameMode == GameMode.Conquest)
    {
      labels.Add("Conquest control to win");
      values.Add(_conquestWinScore.ToString());
    }

    for (int index = 0; index < labels.Count; index++)
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

    string economyHint = _gameMode == GameMode.Conquest
      ? "Conquest control resolves after each team's three actions."
      : "Buy turns alternate. A player may stop buying early.";
    _ui.Text(economyHint, new Vector2(content.X, GetSetupConfirmButtonBounds().Y - 48), UiTheme.TextMuted, 0.72f);
    DrawMenuButton(GetSetupConfirmButtonBounds(), _onlineHostingSetup ? "HOST ROOM" : "CONTINUE", UiButtonTone.Primary);
  }

  private void DrawPauseScreen()
  {
    Rectangle panel = GetPausePanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.CenterText("PAUSED", new Rectangle(content.X, content.Y, content.Width, 30), UiTheme.GoldBright, 1.15f);
    _ui.CenterText("The battlefield is waiting for you.", new Rectangle(content.X, content.Y + 38, content.Width, 22), UiTheme.TextMuted, 0.74f);
    _ui.Divider(content, content.Y + 74);

    DrawMenuButton(GetPauseButtonBounds(0), "RESUME", UiButtonTone.Primary);
    DrawMenuButton(GetPauseButtonBounds(1), "SETTINGS", UiButtonTone.Neutral);
    DrawMenuButton(GetPauseButtonBounds(2), "ENCYCLOPEDIA", UiButtonTone.Accent);
    DrawMenuButton(GetPauseButtonBounds(3), "EXIT TO TITLE", UiButtonTone.Danger);
    _ui.CenterText("ESC resumes", new Rectangle(content.X, content.Bottom - 24, content.Width, 18), UiTheme.TextDim, 0.66f);
  }

  private void DrawEncyclopediaScreen()
  {
    Rectangle panel = GetEncyclopediaPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    PieceDefinition definition = PieceDefinitions.Encyclopedia[_encyclopediaIndex];
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("FIELD ENCYCLOPEDIA", new Vector2(content.X, content.Y), UiTheme.Gold);
    _ui.Text("Unit stats and core battlefield rules", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.74f);
    _ui.Divider(content, content.Y + 50);

    Rectangle previous = GetEncyclopediaPreviousButtonBounds();
    Rectangle next = GetEncyclopediaNextButtonBounds();
    Rectangle selection = new(previous.Right + UiTheme.SpaceSm, previous.Y, next.X - previous.Right - UiTheme.SpaceSm * 2, previous.Height);
    DrawMenuButton(previous, "<", UiButtonTone.Neutral);
    DrawPanel(selection, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    _ui.CenterText(
      $"{_encyclopediaIndex + 1}/{PieceDefinitions.Encyclopedia.Length}  {GetPieceDisplayName(definition.Type)}",
      selection,
      UiTheme.TextPrimary,
      0.76f
    );
    DrawMenuButton(next, ">", UiButtonTone.Neutral);

    Rectangle backButton = GetEncyclopediaBackButtonBounds();
    Rectangle rules = new(content.X, backButton.Y - 154, content.Width, 132);
    Rectangle unitCard = new(content.X, selection.Bottom + UiTheme.SpaceLg, content.Width, Math.Max(120, rules.Y - selection.Bottom - UiTheme.SpaceLg * 2));
    DrawPanel(unitCard, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);

    int cardPadding = Math.Min(UiTheme.SpaceLg, Math.Max(UiTheme.SpaceSm, unitCard.Height / 8));
    int previewSize = Math.Clamp(unitCard.Height - cardPadding * 2, 58, 106);
    Rectangle preview = new(unitCard.X + cardPadding, unitCard.Y + cardPadding, previewSize, previewSize);
    _ui.PiecePreview(preview, UiTheme.Gold, UiText.BuildPieceLabel(definition));

    Rectangle details = new(
      preview.Right + cardPadding,
      unitCard.Y + cardPadding,
      Math.Max(1, unitCard.Right - preview.Right - cardPadding * 2),
      unitCard.Height - cardPadding * 2
    );
    _ui.Text(GetPieceDisplayName(definition.Type), new Vector2(details.X, details.Y), UiTheme.TextPrimary);
    _ui.Text(definition.Category.ToString().ToUpperInvariant(), new Vector2(details.X, details.Y + 26), UiTheme.Gold, 0.72f);

    int statsY = details.Y + 48;
    int statHeight = Math.Clamp((details.Bottom - statsY - UiTheme.SpaceSm - 20) / 2, 28, 42);
    Rectangle statRowOne = new(details.X, statsY, details.Width, statHeight);
    Rectangle statRowTwo = new(details.X, statsY + statHeight + UiTheme.SpaceSm, details.Width, statHeight);
    Rectangle statOne = UiLayout.HorizontalSlot(statRowOne, 3, 0, UiTheme.SpaceXs);
    Rectangle statTwo = UiLayout.HorizontalSlot(statRowOne, 3, 1, UiTheme.SpaceXs);
    Rectangle statThree = UiLayout.HorizontalSlot(statRowOne, 3, 2, UiTheme.SpaceXs);
    Rectangle statFour = UiLayout.HorizontalSlot(statRowTwo, 3, 0, UiTheme.SpaceXs);
    Rectangle statFive = UiLayout.HorizontalSlot(statRowTwo, 3, 1, UiTheme.SpaceXs);
    Rectangle statSix = UiLayout.HorizontalSlot(statRowTwo, 3, 2, UiTheme.SpaceXs);
    _ui.StatBlock(statOne, "HEALTH", definition.Health.ToString(), UiTheme.Health);
    _ui.StatBlock(statTwo, "ATTACK", definition.Attack.ToString(), UiTheme.Attack);
    _ui.StatBlock(statThree, "MOVE", UiText.FormatAction(definition.Movement), UiTheme.Move);
    _ui.StatBlock(statFour, "RANGE", UiText.FormatAction(definition.AttackShape), UiTheme.TextPrimary);
    _ui.StatBlock(statFive, "SIZE", $"{definition.Size.x} x {definition.Size.y}", UiTheme.TextPrimary);
    _ui.StatBlock(statSix, "COST", definition.Cost == 0 ? "START" : definition.Cost.ToString(), UiTheme.GoldBright);

    int abilityY = statRowTwo.Bottom + 5;
    if (abilityY + 16 <= details.Bottom)
    {
      _ui.Text(GetEncyclopediaAbilityText(definition.Type), new Vector2(details.X, abilityY), UiTheme.TextMuted, 0.64f);
    }

    DrawPanel(rules, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    _ui.Text("BASIC RULES", new Vector2(rules.X + UiTheme.SpaceMd, rules.Y + UiTheme.SpaceSm), UiTheme.Gold, 0.76f);
    string[] ruleLines =
    [
      "Three actions per turn: move, attack, or purchase a unit.",
      "Gold squares are valid moves; red outlines mark possible attacks.",
      "Forests slow units and block ranged attacks.",
      "Rivers use remaining movement; lakes cannot be crossed.",
      "Destroy the enemy royal to win. Buy units with team money."
    ];
    for (int index = 0; index < ruleLines.Length; index++)
    {
      _ui.Text(ruleLines[index], new Vector2(rules.X + UiTheme.SpaceMd, rules.Y + 34 + index * 18), UiTheme.TextMuted, 0.62f);
    }

    DrawMenuButton(backButton, "BACK TO PAUSE", UiButtonTone.Primary);
  }

  private static string GetPieceDisplayName(PieceType type)
  {
    return type switch
    {
      PieceType.FieldHospital => "FIELD HOSPITAL",
      PieceType.Crossbowman => "CROSSBOWMAN",
      PieceType.Spearman => "SPEAR-MAN",
      PieceType.Knight => "KNIGHT (SWORD)",
      _ => type.ToString().ToUpperInvariant()
    };
  }

  private static string GetEncyclopediaAbilityText(PieceType type)
  {
    return type switch
    {
      PieceType.Cavalier => "Can move and attack before spending its action.",
      PieceType.Spy => "Marks an enemy to increase damage dealt to it.",
      PieceType.Catapult => "Attacks a four-square area at range.",
      PieceType.Teacher => "Can convert an adjacent friendly unit.",
      PieceType.Ox => "Carries one friendly unit or tows one Mechanical unit.",
      PieceType.Engineer => "Builds roads or barricades on empty squares.",
      PieceType.Ballista => "Its attack pierces enemies in a straight line.",
      PieceType.Elephant => "Ignores terrain and tramples enemy units it moves over.",
      PieceType.Guard => "Attaches to a friendly unit and takes damage for it.",
      PieceType.Mercenary => "Place on a No-Man's-Land edge. An enemy can buy it for its last bid plus 10 gold.",
      PieceType.King => "Adjacent allies take less damage.",
      PieceType.Palace => "Generates gold at the start of each round.",
      PieceType.Baron => "Adjacent allies deal more damage.",
      PieceType.Emissary => "Moves up to two adjacent 1x1 allies with it.",
      _ => "Use its movement, attack range, and size to control the battlefield."
    };
  }

  private void DrawGameOverScreen()
  {
    TeamName winner = _winningTeam ?? TeamName.Red;
    string message = $"{UiText.GetTeamDisplayName(winner)} WINS";
    string reason = _gameMode switch
    {
      GameMode.Conquest => "Their control pushed the conquest bar to its side.",
      GameMode.Escort => "Their royal reached the opposing back edge.",
      _ => "The opposing royal has fallen."
    };
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    Color winnerColour = UiTheme.GetTeamColour(winner);
    _ui.CenterText(message, new Rectangle(viewport.X, viewport.Center.Y - 110, viewport.Width, 42), winnerColour, 1.3f);
    _ui.CenterText(reason, new Rectangle(viewport.X, viewport.Center.Y - 54, viewport.Width, 24), UiTheme.TextPrimary, 0.85f);
    DrawMenuButton(GetTitleButtonBounds(2), "RETURN TO TITLE", UiButtonTone.Primary);
    DrawMenuButton(GetTitleButtonBounds(3), "QUIT GAME", UiButtonTone.Danger);
  }

  private void DrawMenuScreen()
  {
    switch (_screen)
    {
      case Screen.Title: DrawTitleScreen(); break;
      case Screen.OnlineLobby: DrawOnlineLobbyScreen(); break;
      case Screen.OnlineJoin: DrawOnlineJoinScreen(); break;
      case Screen.OnlineWaiting: DrawOnlineWaitingScreen(); break;
      case Screen.OnlineRoyalSelection: DrawOnlineRoyalSelectionScreen(); break;
      case Screen.Settings: DrawSettingsScreen(); break;
      case Screen.Setup: DrawSetupScreen(); break;
      case Screen.Pause: DrawPauseScreen(); break;
      case Screen.Encyclopedia: DrawEncyclopediaScreen(); break;
      case Screen.GameOver: DrawGameOverScreen(); break;
    }
  }

  private bool IsInGameOverlayScreen()
  {
    return _screen is Screen.Pause or Screen.Encyclopedia ||
      (_screen == Screen.Settings && _settingsReturnScreen == Screen.Pause);
  }

  private Rectangle GetStatusPanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    int width = Math.Min(360, Math.Max(1, viewport.Width - UiTheme.SpaceLg * 2));
    int desiredHeight = _initialBuyPhase == null
      ? _gameMode == GameMode.Conquest ? 260 : (_onlineClient == null ? 194 : 226)
      : 260;
    int height = Math.Min(desiredHeight, Math.Max(1, viewport.Height - UiTheme.SpaceLg * 2));
    return new Rectangle(UiTheme.SpaceLg, UiTheme.SpaceLg, width, height);
  }

  private Rectangle GetInitialBuyStopButtonBounds()
  {
    Rectangle panel = GetStatusPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceMd);
    return new Rectangle(content.X, content.Bottom - UiTheme.ButtonHeight, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetSelectedPiecePanelBounds()
  {
    Rectangle status = GetStatusPanelBounds();
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    int desiredHeight = selectedPiece == null
      ? 124
      : selectedPiece.Definition.Type is PieceType.Teacher or PieceType.Engineer or PieceType.Ox ? 438 : 354;
    int height = Math.Min(desiredHeight, Math.Max(1, viewport.Bottom - status.Bottom - UiTheme.SpaceLg * 2));
    return new Rectangle(status.X, status.Bottom + UiTheme.SpaceMd, status.Width, height);
  }

  private Rectangle GetTeacherChoiceBounds()
  {
    Rectangle content = UiLayout.Inset(GetSelectedPiecePanelBounds(), UiTheme.SpaceMd);
    return new Rectangle(content.X, content.Y + 326, content.Width, 70);
  }

  private Rectangle GetTeacherPreviousButtonBounds()
  {
    Rectangle row = GetTeacherChoiceBounds();
    return new Rectangle(row.X, row.Y + 28, 42, 34);
  }

  private Rectangle GetTeacherNextButtonBounds()
  {
    Rectangle row = GetTeacherChoiceBounds();
    return new Rectangle(row.Right - 42, row.Y + 28, 42, 34);
  }

  private Rectangle GetTeacherChoiceValueBounds()
  {
    Rectangle row = GetTeacherChoiceBounds();
    return new Rectangle(row.X + 50, row.Y + 28, row.Width - 100, 34);
  }

  private Rectangle GetOxCargoButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetSelectedPiecePanelBounds(), UiTheme.SpaceMd);
    return new Rectangle(content.X, content.Y + 346, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetEngineerAbilityBounds()
  {
    Rectangle content = UiLayout.Inset(GetSelectedPiecePanelBounds(), UiTheme.SpaceMd);
    return new Rectangle(content.X, content.Y + 326, content.Width, 82);
  }

  private Rectangle GetEngineerPreviousButtonBounds()
  {
    Rectangle row = GetEngineerAbilityBounds();
    return new Rectangle(row.X, row.Y + 28, 42, 34);
  }

  private Rectangle GetEngineerNextButtonBounds()
  {
    Rectangle row = GetEngineerAbilityBounds();
    return new Rectangle(row.Right - 42, row.Y + 28, 42, 34);
  }

  private Rectangle GetEngineerAbilityValueBounds()
  {
    Rectangle row = GetEngineerAbilityBounds();
    return new Rectangle(row.X + 50, row.Y + 28, row.Width - 100, 34);
  }

  private void DrawStatusPanel()
  {
    Rectangle panel = GetStatusPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceMd);
    Team currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);
    Color turnColour = UiTheme.GetTeamColour(Team.CurrentTurn);

    DrawPanel(panel, UiTheme.Panel, turnColour);

    if (_initialBuyPhase != null)
    {
      int buyTurnNumber = _initialBuyPhase.GetBuyTurnsUsed(Team.CurrentTurn) + 1;
      _ui.Text("INITIAL BUY PHASE", new Vector2(content.X, content.Y), UiTheme.Gold);
      _ui.Divider(content, content.Y + 30);
      _ui.Text(
        $"{UiText.GetTeamDisplayName(Team.CurrentTurn)} BUY TURN {buyTurnNumber}/{_initialBuyPhase.BuyTurnsPerTeam}",
        new Vector2(content.X, content.Y + 43),
        turnColour,
        0.7f
      );
      _ui.Text(
        $"{_initialBuyPhase.PurchasesThisTurn}/{_initialBuyPhase.PurchasesPerTurn} UNITS THIS TURN",
        new Vector2(content.X, content.Y + 66),
        UiTheme.TextPrimary,
        0.72f
      );

      int initialMoneyY = content.Y + 100;
      foreach (Team team in _teams)
      {
        Color teamColour = UiTheme.GetTeamColour(team.TeamName);
        Rectangle moneyRow = new(content.X, initialMoneyY, content.Width, 30);
        DrawPanel(moneyRow, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
        _ui.LabelValueRow(moneyRow, $"{UiText.GetTeamDisplayName(team.TeamName)} GOLD", team.Money.ToString(), teamColour);
        initialMoneyY += 36;
      }

      DrawMenuButton(GetInitialBuyStopButtonBounds(), "STOP BUYING", UiButtonTone.Danger);
      return;
    }

    _ui.Text($"{UiText.GetTeamDisplayName(Team.CurrentTurn)} TURN", new Vector2(content.X, content.Y), turnColour);
    _ui.Divider(content, content.Y + 30);
    _ui.Text("ACTION POINTS", new Vector2(content.X, content.Y + 43), UiTheme.TextMuted, 0.74f);

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
      $"{currentTeam.ActionPoints}/{Team.ActionsPerTurn} REMAINING",
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

    if (_onlineClient != null)
    {
      string roomCode = string.IsNullOrWhiteSpace(_onlineClient.JoinCode) ? "CONNECTING" : _onlineClient.JoinCode;
      _ui.Text($"ONLINE  ROOM: {roomCode}", new Vector2(content.X, content.Bottom - 18), UiTheme.Gold, 0.68f);
    }

    if (_gameMode == GameMode.Conquest)
    {
      DrawConquestControlBar(new Rectangle(content.X, content.Y + 174, content.Width, 48));
    }
  }

  private void DrawConquestControlBar(Rectangle bounds)
  {
    _ui.Text("CONQUEST CONTROL", new Vector2(bounds.X, bounds.Y), UiTheme.Gold, 0.7f);
    Rectangle bar = new(bounds.X, bounds.Y + 22, bounds.Width, 14);
    DrawPanel(bar, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);

    int centre = bar.Center.X;
    int halfWidth = Math.Max(1, bar.Width / 2 - 3);
    float ratio = MathHelper.Clamp(Math.Abs(_conquestScore) / (float)Math.Max(1, _conquestWinScore), 0f, 1f);
    int fillWidth = (int)MathF.Round(halfWidth * ratio);
    if (_conquestScore < 0)
    {
      _spriteBatch.Draw(_pixel, new Rectangle(centre - fillWidth, bar.Y + 3, fillWidth, bar.Height - 6), UiTheme.TeamOrange);
    }
    else if (_conquestScore > 0)
    {
      _spriteBatch.Draw(_pixel, new Rectangle(centre, bar.Y + 3, fillWidth, bar.Height - 6), UiTheme.TeamPurple);
    }

    _spriteBatch.Draw(_pixel, new Rectangle(centre - 1, bar.Y + 1, 2, bar.Height - 2), UiTheme.GoldBright);
    _ui.Text($"ORANGE  {Math.Max(0, -_conquestScore)}/{_conquestWinScore}", new Vector2(bounds.X, bounds.Bottom - 10), UiTheme.TeamOrange, 0.54f);
    _ui.Text($"PURPLE  {Math.Max(0, _conquestScore)}/{_conquestWinScore}", new Vector2(bounds.Right - 104, bounds.Bottom - 10), UiTheme.TeamPurple, 0.54f);
  }

  private void DrawSelectedPiecePanel()
  {
    Rectangle panel = GetSelectedPiecePanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceMd);
    DrawPanel(panel, UiTheme.Panel, selectedPiece == null ? UiTheme.PanelBorder : UiTheme.SelectionOutline);

    if (selectedPiece == null)
    {
      _ui.Text(_initialBuyPhase == null ? "SELECT A PIECE" : "INITIAL PURCHASE", new Vector2(content.X, content.Y), UiTheme.TextPrimary);
      _ui.Divider(content, content.Y + 30);
      if (_initialBuyPhase != null)
      {
        _ui.Text("Choose a unit from the right panel.", new Vector2(content.X, content.Y + 44), UiTheme.Gold, 0.76f);
        _ui.Text("Use STOP BUYING when this team is done.", new Vector2(content.X, content.Y + 68), UiTheme.TextMuted, 0.68f);
        return;
      }

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
      selectedPiece.HasMovedThisTurn ? "USED" : UiText.FormatAction(selectedPiece.Definition.Movement),
      selectedPiece.HasMovedThisTurn ? UiTheme.TextDim : UiTheme.Move
    );
    _ui.StatBlock(
      UiLayout.HorizontalSlot(actionGrid, 2, 1, UiTheme.SpaceSm),
      selectedPiece.Definition.Type == PieceType.Engineer ? "ABILITY" : "ATTACK",
      selectedPiece.HasAttackedThisTurn
        ? "USED"
        : selectedPiece.Definition.Type == PieceType.Engineer
          ? _selectedEngineerAbility.ToString().ToUpperInvariant()
          : selectedPiece.Definition.Attack.ToString(),
      selectedPiece.HasAttackedThisTurn ? UiTheme.TextDim : UiTheme.Attack
    );
    Rectangle rangeRow = new(content.X, actionGrid.Bottom + UiTheme.SpaceSm, content.Width, 44);
    _ui.StatBlock(
      rangeRow,
      selectedPiece.Definition.Type == PieceType.Engineer ? "BUILD RANGE" : "ATTACK RANGE",
      UiText.FormatAction(selectedPiece.Definition.AttackShape),
      UiTheme.TextPrimary
    );
    _ui.Text(
      selectedPiece.HasMovedThisTurn ? "MOVE USED THIS TURN" : "LEFT-CLICK gold to move",
      new Vector2(content.X, rangeRow.Bottom + UiTheme.SpaceMd),
      selectedPiece.HasMovedThisTurn ? UiTheme.TextDim : UiTheme.Move,
      0.78f
    );
    _ui.Text(GetSelectedPieceControlHint(selectedPiece), new Vector2(content.X, rangeRow.Bottom + UiTheme.SpaceMd + 23), UiTheme.Attack, 0.72f);

    if (selectedPiece.Definition.Type == PieceType.Teacher)
    {
      DrawTeacherChoiceControls();
      return;
    }

    if (selectedPiece.Definition.Type == PieceType.Engineer)
    {
      DrawEngineerAbilityControls();
      return;
    }

    if (selectedPiece.Definition.Type == PieceType.Ox)
    {
      DrawOxCarryControls();
      return;
    }

    _ui.Text(GetSelectedPieceAbilityHint(selectedPiece), new Vector2(content.X, rangeRow.Bottom + UiTheme.SpaceMd + 44), UiTheme.TextMuted, 0.66f);
  }

  private void DrawTeacherChoiceControls()
  {
    Rectangle row = GetTeacherChoiceBounds();
    Rectangle choiceValue = GetTeacherChoiceValueBounds();
    PieceDefinition choice = PieceDefinitions.Purchasable[_selectedTeacherDefinitionIndex];

    _ui.Text("CONVERT ADJACENT FRIENDLY INTO", new Vector2(row.X, row.Y), UiTheme.Gold, 0.68f);
    DrawMenuButton(GetTeacherPreviousButtonBounds(), "<", UiButtonTone.Neutral);
    DrawPanel(choiceValue, UiTheme.PanelRaised, UiTheme.Gold);
    _ui.CenterText($"{choice.Type.ToString().ToUpperInvariant()}  {choice.Cost} GOLD", choiceValue, UiTheme.TextPrimary, 0.7f);
    DrawMenuButton(GetTeacherNextButtonBounds(), ">", UiButtonTone.Neutral);
    _ui.Text("RIGHT-CLICK the target to convert it", new Vector2(row.X, row.Bottom + 6), UiTheme.TextMuted, 0.68f);
  }

  private void DrawOxCarryControls()
  {
    Piece cargo = GetOxCargo(selectedPiece);
    if (cargo == null)
    {
      _ui.Text("CARGO BAY EMPTY", new Vector2(GetOxCargoButtonBounds().X, GetOxCargoButtonBounds().Y - 26), UiTheme.TextMuted, 0.74f);
      _ui.Text("RIGHT-CLICK a friendly 1x1 to carry", new Vector2(GetOxCargoButtonBounds().X, GetOxCargoButtonBounds().Y + 4), UiTheme.TextMuted, 0.68f);
      return;
    }

    string cargoKind = cargo.AttachmentKind == AttachmentKind.Towed ? "TOWING" : "CARRYING";
    Rectangle button = GetOxCargoButtonBounds();
    _ui.Text($"{cargoKind}: {cargo.Definition.Type.ToString().ToUpperInvariant()}", new Vector2(button.X, button.Y - 26), UiTheme.Gold, 0.74f);
    DrawMenuButton(button, "SELECT CARGO", UiButtonTone.Accent);
    _ui.Text("Cargo may attack. Moving it dismounts it.", new Vector2(button.X, button.Bottom + 6), UiTheme.TextMuted, 0.64f);
  }

  private void DrawEngineerAbilityControls()
  {
    Rectangle row = GetEngineerAbilityBounds();
    Rectangle valueBounds = GetEngineerAbilityValueBounds();
    (string title, string detail) = _selectedEngineerAbility switch
    {
      EngineerAbility.Barrier => ("BARRIER", "60 HP wall; blocks movement and attacks."),
      EngineerAbility.Mine => ("MINE", "Enemy trigger: 3 x 3 blast for 40 damage."),
      EngineerAbility.Demolish => ("DEMOLISH", "Instantly removes any Engineer improvement."),
      _ => ("ROAD", "Forest costs 1; open road costs 0; bridges water.")
    };

    _ui.Text("ENGINEER ABILITY", new Vector2(row.X, row.Y), UiTheme.Gold, 0.68f);
    DrawMenuButton(GetEngineerPreviousButtonBounds(), "<", UiButtonTone.Neutral);
    DrawPanel(valueBounds, UiTheme.PanelRaised, UiTheme.Gold);
    _ui.CenterText(title, valueBounds, UiTheme.TextPrimary, 0.74f);
    DrawMenuButton(GetEngineerNextButtonBounds(), ">", UiButtonTone.Neutral);
    _ui.Text(detail, new Vector2(row.X, row.Bottom - 16), UiTheme.TextMuted, 0.58f);
  }

  private Piece GetOxCargo(Piece ox)
  {
    return pieceSetup.GetAttachedPiece(ox, AttachmentKind.Carried) ??
      pieceSetup.GetAttachedPiece(ox, AttachmentKind.Towed);
  }

  private static string GetSelectedPieceControlHint(Piece piece)
  {
    if (piece.Definition.Type == PieceType.Engineer)
    {
      return piece.HasAttackedThisTurn ? "ABILITY USED THIS TURN" : "RIGHT-CLICK to build or demolish";
    }

    if (piece.HasAttackedThisTurn)
    {
      return "ATTACK USED THIS TURN";
    }

    if (piece.Definition.AttackShape.shape == Shape.MoveOnEnemy)
    {
      return "MOVE over red squares to attack";
    }

    return piece.Definition.Type is PieceType.Spy or PieceType.Teacher or PieceType.Engineer or PieceType.Guard or PieceType.Ox
      ? "RIGHT-CLICK to use special"
      : "RIGHT-CLICK red to attack";
  }

  private static string GetSelectedPieceAbilityHint(Piece piece)
  {
    if (piece.AttachedTo?.Definition.Type == PieceType.Ox &&
        piece.AttachmentKind is AttachmentKind.Carried or AttachmentKind.Towed)
    {
      return "RIDING: attack normally; moving dismounts you";
    }

    return piece.Definition.Type switch
    {
      PieceType.Cavalier => "SPECIAL: move, then attack before ending activation",
      PieceType.Spy => "SPECIAL: mark an enemy for +10 damage",
      PieceType.Teacher => "SPECIAL: change adjacent friendly unit",
      PieceType.Ox => "SPECIAL: carry 1x1 or tow Mechanical",
      PieceType.Engineer => "SPECIAL: road, barrier, mine, or demolish",
      PieceType.Ballista => "SPECIAL: attack pierces a straight line",
      PieceType.Elephant => "SPECIAL: move over enemy 1x1 units to attack",
      PieceType.Guard => "SPECIAL: attach to protect a friendly unit",
      PieceType.Mercenary => "SPECIAL: rivals can buy this unit for its last bid +10",
      PieceType.King => "AURA: adjacent friendlies take 5 less damage",
      PieceType.Palace => "AURA: gains 10 gold at the start of each round",
      PieceType.Baron => "AURA: adjacent friendlies deal +5 damage",
      PieceType.Emissary => "SPECIAL: moves up to two adjacent 1x1 allies",
      _ => string.Empty
    };
  }

  protected override void Draw(GameTime gameTime)
  {
    bool drawsGameView = _screen == Screen.Playing || IsInGameOverlayScreen();
    GraphicsDevice.Clear(drawsGameView ? UiTheme.BoardBackground : UiTheme.MenuBackground);

    if (!drawsGameView)
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

          if (_gameMode == GameMode.Conquest && IsConquestSquare(boardPosition))
          {
            DrawWorldRectangle(cellBounds, new Color(218, 180, 91, 46), 0.101f);
            DrawWorldOutline(cellBounds, new Color(246, 214, 123, 170), 0.102f);
          }

          if (_terrain.IsLake(boardPosition))
          {
            DrawWorldRectangle(cellBounds, UiTheme.Lake, 0.101f);
            DrawWorldRectangle(
              new Rectangle(cellBounds.X + 11, cellBounds.Y + 14, cellBounds.Width - 28, 3),
              UiTheme.LakeHighlight,
              0.102f
            );
            DrawWorldRectangle(
              new Rectangle(cellBounds.X + 24, cellBounds.Y + 35, cellBounds.Width - 34, 3),
              UiTheme.LakeHighlight,
              0.102f
            );
          }
          else if (_terrain.IsForest(boardPosition))
          {
            DrawWorldRectangle(cellBounds, UiTheme.Forest, 0.101f);
            DrawWorldRectangle(
              new Rectangle(cellBounds.X + 12, cellBounds.Y + 10, 14, 24),
              UiTheme.ForestDark,
              0.102f
            );
            DrawWorldRectangle(
              new Rectangle(cellBounds.Right - 26, cellBounds.Bottom - 34, 14, 24),
              UiTheme.ForestDark,
              0.102f
            );
          }

          if (_restoredLakeTiles.Contains(boardPosition))
          {
            DrawWorldRectangle(
              new Rectangle(cellBounds.X + 5, cellBounds.Center.Y - 7, cellBounds.Width - 10, 14),
              UiTheme.Bridge,
              0.106f
            );
          }

          var rightPosition = (x: boardPosition.x + 1, y: boardPosition.y);
          var belowPosition = (x: boardPosition.x, y: boardPosition.y + 1);
          if (_terrain.HasRiverBetween(boardPosition, rightPosition))
          {
            DrawWorldRectangle(
              new Rectangle(cellBounds.Right - 3, cellBounds.Y, 6, cellBounds.Height),
              UiTheme.River,
              0.105f
            );
            if (HasRiverBridgeBetween(boardPosition, rightPosition))
            {
              DrawWorldRectangle(
                new Rectangle(cellBounds.Right - 7, cellBounds.Y + 6, 14, cellBounds.Height - 12),
                UiTheme.Bridge,
                0.106f
              );
            }
          }

          if (_terrain.HasRiverBetween(boardPosition, belowPosition))
          {
            DrawWorldRectangle(
              new Rectangle(cellBounds.X, cellBounds.Bottom - 3, cellBounds.Width, 6),
              UiTheme.River,
              0.105f
            );
            if (HasRiverBridgeBetween(boardPosition, belowPosition))
            {
              DrawWorldRectangle(
                new Rectangle(cellBounds.X + 6, cellBounds.Bottom - 7, cellBounds.Width - 12, 14),
                UiTheme.Bridge,
                0.106f
              );
            }
          }

          if (_roads.Contains(boardPosition))
          {
            bool roadIsInForest = _terrain.IsForest(boardPosition);
            DrawWorldRectangle(
              new Rectangle(
                roadIsInForest ? cellBounds.X + 6 : cellBounds.X,
                cellBounds.Center.Y - (roadIsInForest ? 8 : 5),
                roadIsInForest ? cellBounds.Width - 12 : cellBounds.Width,
                roadIsInForest ? 16 : 10
              ),
              roadIsInForest ? UiTheme.ForestRoad : UiTheme.Road,
              0.101f
            );
            if (roadIsInForest)
            {
              DrawWorldRectangle(
                new Rectangle(cellBounds.X + 8, cellBounds.Center.Y - 2, cellBounds.Width - 16, 4),
                UiTheme.RoadHighlight,
                0.102f
              );
            }
          }

          if (_barricades.ContainsKey(boardPosition))
          {
            DrawWorldRectangle(
              new Rectangle(cellBounds.X + 8, cellBounds.Y + 16, cellBounds.Width - 16, cellBounds.Height - 32),
              UiTheme.Barricade,
              0.11f
            );
            int barrierHealthWidth = (cellBounds.Width - 16) * _barricades[boardPosition] / 60;
            DrawWorldRectangle(
              new Rectangle(cellBounds.X + 8, cellBounds.Bottom - 13, barrierHealthWidth, 3),
              UiTheme.Health,
              0.111f
            );
          }

          if (_mines.TryGetValue(boardPosition, out TeamName mineOwner))
          {
            DrawWorldRectangle(
              new Rectangle(cellBounds.Center.X - 7, cellBounds.Center.Y - 7, 14, 14),
              UiTheme.GetTeamColour(mineOwner),
              0.111f
            );
            DrawWorldOutline(
              new Rectangle(cellBounds.Center.X - 9, cellBounds.Center.Y - 9, 18, 18),
              UiTheme.MineOutline,
              0.112f
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

      if (piece.AttachmentKind == AttachmentKind.Carried && piece.AttachedTo != null)
      {
        Rectangle hostBounds = GetPieceWorldBounds(piece.AttachedTo, cellSize);
        Rectangle cargoBadge = new(hostBounds.Right - 30, hostBounds.Y + 6, 24, 24);
        DrawWorldRectangle(cargoBadge, colour, 0.125f);
        DrawWorldOutline(cargoBadge, UiTheme.TextPrimary, 0.126f);
        continue;
      }

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

    }

    _spriteBatch.End();

    _spriteBatch.Begin();

    DrawWorldPieceText(cameraTransform, cellSize);

    if (_screen == Screen.Playing)
    {
      DrawStatusPanel();
      DrawSelectedPiecePanel();
      DrawPurchasePanel();
    }
    else
    {
      Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
      _spriteBatch.Draw(_pixel, viewport, new Color(5, 9, 14, 176));
      DrawMenuScreen();
    }

    _spriteBatch.End();

    base.Draw(gameTime);
  }
}
