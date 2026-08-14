using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MedivalChess.Campaign;
using MedivalChess.CPU;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace MedivalChess;

internal sealed partial class Game1 : Game
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
    GameOver,
    LevelEditor,
    CustomLevels,
    EditorDiscardConfirm
  }

  private enum BindingAction
  {
    MoveUp,
    MoveDown,
    MoveLeft,
    MoveRight,
    ZoomIn,
    ZoomOut,
    Buy,
    EndTurn
  }

  private enum EditorConfirmAction
  {
    Exit,
    New
  }

  /// <summary>A speculative CPU response that is usable only when the authoritative snapshot still matches.</summary>
  private sealed record CpuPreplannedTurn(NetworkTeam Team, ulong ExpectedStateHash, CpuTurnPlan Plan);

  private enum OnlineInputField
  {
    ServerUrl,
    JoinCode
  }

  private enum SetupStage
  {
    Mode,
    Packs,
    Battlefield,
    Economy,
    ModeSettings,
    RoyalSelection
  }

  private enum GameMode
  {
    Regicide,
    Conquest,
    Escort,
    Dominion,
    Plunder
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

  private enum TerrainSource
  {
    Preset,
    Procedural,
    None
  }

  private enum EngineerAbility
  {
    Road,
    Barrier,
    Mine,
    Demolish
  }

  private enum FpsCap
  {
    Thirty = 30,
    Sixty = 60,
    OneTwenty = 120,
    OneFifty = 150,
    OneEighty = 180,
    TwoForty = 240,
    Unlimited = 0
  }

  private enum PlanningPath
  {
    Any,
    Straight
  }

  private sealed record PlanningMark((int x, int y) Start, (int x, int y)? End, PlanningPath Path);

  private sealed class MovementAnimation
  {
    internal const float SecondsPerStep = 0.11f;

    internal Piece Piece { get; init; }
    internal List<(int x, int y)> Path { get; init; }
    internal (int x, int y) StartPosition { get; init; }
    internal bool IsAuthoritativeSnapshot { get; init; }
    internal float ElapsedSeconds { get; set; }
    internal float Duration => Path.Count * SecondsPerStep;
  }

  private readonly GraphicsDeviceManager _graphics;
  private SpriteBatch _spriteBatch;
  private Texture2D _pixel;
  private RenderTarget2D _staticBattlefield;
  private int _staticBattlefieldStamp;
  private bool _staticBattlefieldDirty = true;
  private SpriteFont _pieceLabelFont;
  private UiRenderer _ui;
  private Board _board;
  private BattlefieldTerrain _terrain;
  private readonly PieceSetup pieceSetup = new();
  private List<Team> _teams = [];
  private Piece selectedPiece;
  private Piece _cachedSelectedPiece;
  private Dictionary<(int x, int y), List<(int x, int y)>> _cachedSelectedMovementPaths = [];
  private HashSet<(int x, int y)> _cachedSelectedMovementSquares = [];
  private HashSet<(int x, int y)> _cachedSelectedAttackSquares = [];
  private readonly Dictionary<Piece, (bool CanMove, bool CanAttack)> _cachedUnitActions = [];
  private TeamName? _cachedActionTeam;
  private int _gameplayRenderCacheStamp;
  private bool _gameplayRenderCacheDirty = true;
  private Rectangle _visibleWorldBounds;
  private MovementAnimation _movementAnimation;
  // Roads are owned improvements: only their owner receives the movement benefit.
  // Neutral roads are reserved for campaign-authored map features and are usable by everyone.
  private readonly Dictionary<(int x, int y), TeamName> _roads = [];
  private readonly Dictionary<(int x, int y), int> _barricades = [];
  private readonly Dictionary<(int x, int y), TeamName> _mines = [];
  private readonly HashSet<(int x, int y)> _restoredLakeTiles = [];
  private readonly HashSet<TileEdge> _riverBridges = [];
  private const int noMansLandHalfHeight = MatchRules.DefaultNoMansLandHalfHeight;
  private const float territoryTintAmount = 0.2f;
  private const int purchasePanelWidth = 380;
  private const int purchasePanelHeight = 510;
  private const int purchaseUnitListHeaderHeight = 28;
  private const int purchaseUnitListPadding = 8;
  private const int purchaseUnitListGap = 4;
  private const int purchaseUnitListMinimumRowHeight = 18;
  private const int purchaseUnitListMaximumRowHeight = 32;
  private const int settingsControlHeight = 36;
  private int _terrainSeed;
  // Separate from terrain so lower CPU difficulties can vary their close-plan strategy between
  // matches even when a developer reuses a terrain seed.
  private int _cpuMatchVariationSeed = Random.Shared.Next();
  private Vector2 _cameraPosition = Vector2.Zero;
  private float _zoom = 1f;
  private MouseState _previousMouseState;
  private KeyboardState _previousKeyboardState;
  private bool _isPurchaseMode;
  private bool _isPurchaseUnitListExpanded;
  private int _selectedPurchaseIndex;
  private EngineerAbility _selectedEngineerAbility;
  private Screen _screen = Screen.Title;
  private TeamName _setupTeam = TeamName.Red;
  private int _selectedRoyalIndex;
  private PieceDefinition _royalAwaitingPlacement;
  private SetupStage _setupStage = SetupStage.Mode;
  private readonly HashSet<Pack> _allowedPacks = [Pack.Base];
  private BoardSize _selectedBoardSize = BoardSize.Medium;
  private TerrainDensity _forestDensity = TerrainDensity.Standard;
  private TerrainDensity _waterwayDensity = TerrainDensity.Standard;
  private TerrainSource _terrainSource = TerrainSource.Preset;
  private string _selectedTerrainPresetId;
  private string _selectedTerrainPresetName;
  private bool _terrainPresetBrowserOpen;
  private IReadOnlyList<BattlefieldTerrainPreset> _terrainPresetBrowserPresets = [];
  private Board _terrainPresetBrowserBoard;
  private int _terrainPresetBrowserPage;
  private int _startingCash = Globals.StartingCash;
  private float _killerRefundMultiplier = Globals.KillerDeathRefundMultiplier;
  private float _defeatedTeamRefundMultiplier = Globals.DefeatedTeamDeathRefundMultiplier;
  private int _initialBuysPerTurn = Globals.InitialBuysPerTurn;
  private int _initialBuyTurnsPerTeam = Globals.InitialBuyTurnsPerTeam;
  private bool _farmsEnabled = Globals.FarmsEnabled;
  private int _farmIncomePerTurn = Globals.FarmIncomePerTurn;
  private bool _unitMaintenanceEnabled = Globals.UnitMaintenanceEnabled;
  private int _unitMaintenancePercent = Globals.UnitMaintenancePercent;
  private int _unitPricePercent = Globals.UnitPricePercent;
  private bool _interestEnabled = Globals.InterestEnabled;
  private int _interestPercent = Globals.InterestPercent;
  private int _economyInputIndex = -1;
  private string _economyInputText = string.Empty;
  private int _timerInputIndex = -1;
  private string _timerInputText = string.Empty;
  private int _playerCount = 2;
  private InitialBuyPhase _initialBuyPhase;
  private TeamName? _winningTeam;
  private GameMode _gameMode = GameMode.Regicide;
  private int _conquestWinScore = MatchRules.DefaultConquestWinScore;
  private int _escortRoyalHealthPercent = Globals.DefaultEscortRoyalHealthPercent;
  private int _dominionWinScore = Globals.DefaultDominionWinScore;
  private int _plunderWinScore = Globals.DefaultPlunderWinScore;
  private int _plunderDeliveryScore = Globals.DefaultPlunderDeliveryScore;
  private int _plunderRoyalKillPenalty = Globals.DefaultPlunderRoyalKillPenalty;
  private bool _chessTimerEnabled;
  private int _chessTimerMinutes = 10;
  private int _chessTimerSeconds;
  private int _chessTimerIncrementSeconds;
  private readonly Dictionary<TeamName, double> _localClockSeconds = [];
  private NetworkClockState _onlineClock;
  // Negative pressure moves toward Orange; positive pressure moves toward Purple.
  private int _conquestScore;
  // Mirrors the simulation turn counter so speculative CPU plans can be matched to live state.
  private int _cpuTurnNumber;
  private readonly Dictionary<TeamName, int> _conquestScores = [];
  private readonly Dictionary<TeamName, int> _modeScores = [];
  private (int x, int y)? _treasurePosition;
  private string _treasureCarrierId;
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
  private Keys _endTurnKey = Keys.Space;
  private bool _zoomTowardsMouse;
  private FpsCap _fpsCap = FpsCap.Sixty;
  private float _uiScale = 1f;
  private int _resolutionIndex = 1;
  private readonly List<PlanningMark> _planningMarks = [];
  private (int x, int y)? _planningStart;
  private PlanningPath _planningPath;
  private bool _planningGestureActive;
  private OnlineMatchClient _onlineClient;
  private readonly Dictionary<TeamName, CpuProfile> _cpuProfiles = [];
  private readonly Queue<ICpuGameAction> _cpuActionQueue = [];
  private readonly List<CpuMoveRecord> _cpuRecentMoves = [];
  private System.Threading.Tasks.Task<CpuTurnPlan> _cpuPlanningTask;
  private System.Threading.CancellationTokenSource _cpuPlanningCancellation;
  private NetworkTeam? _cpuPlanningTeam;
  private System.Threading.Tasks.Task<CpuPreplannedTurn> _cpuPreplanningTask;
  private System.Threading.CancellationTokenSource _cpuPreplanningCancellation;
  private CpuDecisionReport _lastCpuDecisionReport;
  private float _cpuActionDelaySeconds;
  private string _onlineStatus = "OFFLINE";
  private string _onlineServerUrl = "https://crown-and-siege-server.onrender.com";
  private string _onlineJoinCode = string.Empty;
  private OnlineInputField _onlineInputFocus = OnlineInputField.ServerUrl;
  private bool _onlineIsHost;
  private bool _onlineRoyalChoicePending;
  private bool _debugTeamSwitchPending;
  private bool _onlineHostingSetup;
  private bool _cpuOpponentSetup;
  private CpuDifficultyLevel _selectedCpuDifficulty = CpuDifficultyLevel.Medium;
  private CpuPersonality _selectedCpuPersonality = CpuPersonality.Balanced;
  private NetworkMatchConfiguration _onlineMatchConfiguration;
  private DateTimeOffset _nextOnlineJoinAttemptAt;
  private string _onlineError = string.Empty;
  private LevelEditorScreen _levelEditor;
  private IReadOnlyList<CustomLevelSummary> _customLevels = [];
  private bool _campaignTestPlay;
  private EditorConfirmAction _editorConfirmAction;
  private CampaignLevelDefinition _campaignTestDefinition;
  private CampaignTerritoryMap _campaignTerritories;
  private int _campaignCompletedRounds;

  internal Game1()
  {
    _graphics = new GraphicsDeviceManager(this);
    Content.RootDirectory = "Content";
    IsMouseVisible = true;
    Window.Title = "Crown & Siege";

    _graphics.PreferredBackBufferWidth = 1920;
    _graphics.PreferredBackBufferHeight = 1080;
    _graphics.SynchronizeWithVerticalRetrace = false;
    ApplyFpsCap();

    Window.AllowUserResizing = true;
  }

  protected override void Initialize()
  {
    _board = new Board();
    _terrainSeed = Random.Shared.Next();
    _terrain = TerrainRules.Create(_board, _terrainSeed, _forestDensity.ToString(), _waterwayDensity.ToString(), _playerCount, _terrainSource.ToString(), _selectedBoardSize.ToString());

    pieceSetup.AddPieces();
    ConfigureTeamsForPlayerCount();

    base.Initialize();
  }

  protected override void LoadContent()
  {
    _spriteBatch = new SpriteBatch(GraphicsDevice);

    _pixel = new Texture2D(GraphicsDevice, 1, 1);
    _pixel.SetData(new[] { Color.White });
    _pieceLabelFont = Content.Load<SpriteFont>("PieceLabel");
    _ui = new UiRenderer(_spriteBatch, _pixel, _pieceLabelFont);
    UiLayout.Scale = _uiScale;
    _ui.InputScale = _uiScale;
    _levelEditor = new LevelEditorScreen(_ui, _spriteBatch, _pixel);
  }

  protected override void Update(GameTime gameTime)
  {
    float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

    KeyboardState keyboard = Keyboard.GetState();
    MouseState mouse = Mouse.GetState();

    // Do not let background windows consume clicks or hotkeys. Keeping the
    // previous input state current also prevents a held key/click from firing
    // when the game regains focus.
    if (!IsActive)
    {
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      _onlineClient?.DrainStates(ApplyOnlineState, error => _onlineError = error);
      base.Update(gameTime);
      return;
    }

    bool wasLeftClick =
      mouse.LeftButton == ButtonState.Pressed &&
      _previousMouseState.LeftButton == ButtonState.Released;
    bool wasRightClick =
      mouse.RightButton == ButtonState.Pressed &&
      _previousMouseState.RightButton == ButtonState.Released;
    bool wasRightRelease =
      mouse.RightButton == ButtonState.Released &&
      _previousMouseState.RightButton == ButtonState.Pressed;
    bool wasEscapePressed =
      keyboard.IsKeyDown(Keys.Escape) &&
      !_previousKeyboardState.IsKeyDown(Keys.Escape);
    _onlineClient?.DrainStates(ApplyOnlineState, error => _onlineError = error);
    if (_screen == Screen.Playing && _onlineClient is null)
    {
      UpdateLocalChessClock(deltaTime);
    }

    if (_screen == Screen.LevelEditor)
    {
      _levelEditor.Update(
        keyboard,
        _previousKeyboardState,
        mouse,
        ToUiPoint(mouse.Position),
        wasLeftClick,
        mouse.LeftButton == ButtonState.Pressed,
        wasRightClick,
        wasEscapePressed,
        GetUiViewport()
      );
      HandleLevelEditorRequests();
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      base.Update(gameTime);
      return;
    }

    if (_screen == Screen.CustomLevels)
    {
      UpdateCustomLevels(mouse, wasLeftClick, wasEscapePressed);
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      base.Update(gameTime);
      return;
    }

    if (_screen == Screen.EditorDiscardConfirm)
    {
      UpdateEditorDiscardConfirmation(mouse, wasLeftClick, wasEscapePressed);
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      base.Update(gameTime);
      return;
    }

    if (_screen == Screen.Playing && wasEscapePressed)
    {
      if (_campaignTestPlay)
      {
        ReturnToEditorFromTestPlay();
        _previousMouseState = mouse;
        _previousKeyboardState = keyboard;
        base.Update(gameTime);
        return;
      }
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

    // CPU planning runs on a background worker, but this Update method still owns the camera.
    // Process it before the CPU-turn early return so the player can look around while Hard/Best
    // searches use their full time budgets.
    Vector2 mouseWorldBefore = UpdateCamera(keyboard, mouse, deltaTime);
    bool planningInput = UpdatePlanningGesture(keyboard, mouse, mouseWorldBefore, wasRightClick, wasRightRelease);

    if (_movementAnimation != null)
    {
      UpdateMovementAnimation(deltaTime);
      RefreshGameplayRenderCache();
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      base.Update(gameTime);
      return;
    }

    if (IsCpuTurn())
    {
      if (!planningInput && (wasLeftClick || wasRightClick))
      {
        InspectPieceAt(ToUiPoint(mouse.Position), mouseWorldBefore);
      }

      UpdateCpuTurn(deltaTime);
      RefreshGameplayRenderCache();
      _previousMouseState = mouse;
      _previousKeyboardState = keyboard;
      base.Update(gameTime);
      return;
    }

    UpdateCpuPreplanning();

    bool wasPurchaseModeToggle =
      keyboard.IsKeyDown(_buyKey) &&
      !_previousKeyboardState.IsKeyDown(_buyKey);
    bool wasPreviousPurchasePressed =
      keyboard.IsKeyDown(Keys.Up) &&
      !_previousKeyboardState.IsKeyDown(Keys.Up);
    bool wasNextPurchasePressed =
      keyboard.IsKeyDown(Keys.Down) &&
      !_previousKeyboardState.IsKeyDown(Keys.Down);
    bool wasSkipTurnPressed =
      keyboard.IsKeyDown(_endTurnKey) &&
      !_previousKeyboardState.IsKeyDown(_endTurnKey);

    if (wasSkipTurnPressed)
    {
      TrySkipCurrentTurn();
    }

    if (wasPurchaseModeToggle && _initialBuyPhase == null && _royalAwaitingPlacement is null)
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
      _royalAwaitingPlacement is null && wasLeftClick && HandlePurchasePanelClick(ToUiPoint(mouse.Position));
    bool clickedInitialBuyStop =
      wasLeftClick && HandleInitialBuyStopClick(ToUiPoint(mouse.Position));
    bool clickedSkipTurn =
      wasLeftClick && HandleSkipTurnClick(ToUiPoint(mouse.Position));
    bool clickedDebugTeamSwitch =
      wasLeftClick && HandleDebugTeamSwitchClick(ToUiPoint(mouse.Position));
    bool clickedEngineerPanel =
      wasLeftClick && HandleEngineerAbilityClick(ToUiPoint(mouse.Position));
    bool clickedOxCarryPanel =
      wasLeftClick && HandleOxCarryPanelClick(ToUiPoint(mouse.Position));
    bool clickedMercenaryPanel =
      wasLeftClick && HandleMercenaryPanelClick(ToUiPoint(mouse.Position));

    if (!planningInput && !clickedPurchasePanel && !clickedInitialBuyStop && !clickedSkipTurn && !clickedDebugTeamSwitch && !clickedEngineerPanel && !clickedOxCarryPanel && !clickedMercenaryPanel && (wasLeftClick || wasRightClick))
    {
      const int cellSize = 64;
      int boardX = (int)MathF.Floor(mouseWorldBefore.X / cellSize) + _board.MinX;
      int boardY = (int)MathF.Floor(mouseWorldBefore.Y / cellSize) + _board.MinY;
      var targetPosition = (x: boardX, y: boardY);
      Piece pieceAtTarget = pieceSetup.GetPieceAt(targetPosition);
      Piece friendlyPieceAtTarget = GetUnattachedPieceAt(targetPosition, Team.CurrentTurn);
      Piece inspectablePieceAtTarget = GetUnattachedPieceAt(targetPosition);

      if (_royalAwaitingPlacement is not null)
      {
        if (wasLeftClick)
        {
          TryPlaceSelectedRoyal(targetPosition);
        }
      }
      else if (_isPurchaseMode)
      {
        if (wasLeftClick)
        {
          TryPurchaseAndPlace(targetPosition);
        }
      }
      else if (selectedPiece == null)
      {
        if (inspectablePieceAtTarget is not null)
        {
          SelectPiece(inspectablePieceAtTarget);
        }
      }
      else if (selectedPiece.Team != Team.CurrentTurn || !IsOnlineLocalTurn())
      {
        if (inspectablePieceAtTarget is not null && inspectablePieceAtTarget != selectedPiece)
        {
          SelectPiece(inspectablePieceAtTarget);
        }
        else
        {
          selectedPiece = null;
        }
      }
      else if (selectedPiece.Occupies(targetPosition))
      {
        selectedPiece = null;
      }
      else if (
        wasLeftClick &&
        friendlyPieceAtTarget != null &&
        friendlyPieceAtTarget != selectedPiece &&
        !TryGetMovementPathAt(selectedPiece, targetPosition, out _)
      )
      {
        SelectPiece(friendlyPieceAtTarget);
      }
      else
      {
        Piece hostilePieceAtTarget = GetUnattachedHostilePieceAt(targetPosition, selectedPiece.Team);
        bool usedSpecialAbility = wasRightClick &&
          (_onlineClient is null
            ? TryUseSpecialAbility(selectedPiece, targetPosition, hostilePieceAtTarget ?? pieceAtTarget, keyboard)
            : TrySendOnlineSpecialAbility(selectedPiece, targetPosition, hostilePieceAtTarget ?? pieceAtTarget));

        if (usedSpecialAbility)
        {
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
            if (_onlineClient != null)
            {
              _ = SendOnlineMoveAsync(selectedPiece, targetPosition);
              selectedPiece = null;
            }
            else
            {
              if (selectedPiece.AttachedTo != null &&
                  selectedPiece.AttachmentKind == AttachmentKind.Carried)
              {
                pieceSetup.Detach(selectedPiece);
              }
              BeginMovementAnimation(selectedPiece, path);
            }
          }

          if (_movementAnimation == null)
          {
            selectedPiece = null;
          }
        }
        else if (wasRightClick)
        {
          if (_onlineClient != null)
          {
            bool canSendOnlineAttack =
              (hostilePieceAtTarget is not null ||
               _barricades.ContainsKey(targetPosition)) &&
              !selectedPiece.HasAttackedThisTurn &&
              selectedPiece.Definition.Attack > 0 &&
              Actions.CanAttackSquare(selectedPiece, targetPosition) &&
              HasClearAttackPath(selectedPiece, targetPosition);
            if (canSendOnlineAttack)
            {
              if (hostilePieceAtTarget is not null)
              {
                _ = SendOnlineAttackAsync(selectedPiece, hostilePieceAtTarget);
              }
              else
              {
                _ = SendOnlineImprovementAttackAsync(selectedPiece, targetPosition);
              }
            }
            selectedPiece = null;
          }
          else
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
              selectedPiece.Definition.Attack > 0 &&
              (hostilePieceAtTarget is not null ||
               _barricades.ContainsKey(targetPosition));

            if (isValidAttack)
            {
              if (selectedPiece.Definition.Type == PieceType.Ballista)
              {
                PerformPiercingAttack(selectedPiece, targetPosition);
              }
              else if (_barricades.ContainsKey(targetPosition))
              {
                DamageBarricade(selectedPiece, targetPosition);
              }
              else
              {
                PerformSharedUnitAttack(selectedPiece, hostilePieceAtTarget);
              }

              selectedPiece.HasAttackedThisTurn = true;
              selectedPiece.CavalierFollowUpMoveAvailable = AbilityRules.GrantsCavalierFollowUpMove(
                selectedPiece.Definition.Type.ToString(), selectedPiece.HasMovedThisTurn);

              Console.WriteLine(
                $"Attacked at ({boardX}, {boardY})."
              );

              if (_screen == Screen.Playing)
              {
                CompleteAction();
              }

            }

            selectedPiece = null;
          }
        }
      }
    }

    _previousMouseState = mouse;
    _previousKeyboardState = keyboard;

    RefreshGameplayRenderCache();

    base.Update(gameTime);
  }

  private Vector2 UpdateCamera(KeyboardState keyboard, MouseState mouse, float deltaTime)
  {
    const float cameraSpeed = 500f;
    const float zoomSpeed = 1f;
    Vector2 cameraInput = Vector2.Zero;
    if (keyboard.IsKeyDown(_moveLeftKey)) cameraInput.X -= 1f;
    if (keyboard.IsKeyDown(_moveRightKey)) cameraInput.X += 1f;
    if (keyboard.IsKeyDown(_moveUpKey)) cameraInput.Y -= 1f;
    if (keyboard.IsKeyDown(_moveDownKey)) cameraInput.Y += 1f;

    if (cameraInput != Vector2.Zero)
    {
      Vector2 worldCameraInput = Vector2.Transform(cameraInput, Matrix.Invert(GetBoardRotationTransform()));
      _cameraPosition += worldCameraInput * cameraSpeed * deltaTime / _zoom;
    }

    Vector2 mouseScreen = mouse.Position.ToVector2();
    Vector2 mouseWorldBefore = Vector2.Transform(mouseScreen, Matrix.Invert(CreateCameraTransform()));
    if (keyboard.IsKeyDown(_zoomInKey)) _zoom += zoomSpeed * deltaTime * _zoom;
    if (keyboard.IsKeyDown(_zoomOutKey)) _zoom -= zoomSpeed * deltaTime * _zoom;
    _zoom = MathHelper.Clamp(_zoom, 0.2f, 5f);

    if (_zoomTowardsMouse)
    {
      Vector2 mouseWorldAfter = Vector2.Transform(mouseScreen, Matrix.Invert(CreateCameraTransform()));
      _cameraPosition += mouseWorldBefore - mouseWorldAfter;
    }
    return mouseWorldBefore;
  }

  private void ApplyFpsCap()
  {
    int framesPerSecond = (int)_fpsCap;
    IsFixedTimeStep = framesPerSecond > 0;
    if (framesPerSecond > 0)
    {
      TargetElapsedTime = TimeSpan.FromSeconds(1d / framesPerSecond);
    }
  }

  private void CycleFpsCap()
  {
    FpsCap[] caps = Enum.GetValues<FpsCap>();
    int index = Array.IndexOf(caps, _fpsCap);
    _fpsCap = caps[(Math.Max(0, index) + 1) % caps.Length];
    ApplyFpsCap();
  }

  private void AdjustUiScale(int direction)
  {
    const float minimum = 0.5f;
    const float maximum = 2.5f;
    const float step = 0.1f;
    _uiScale = Math.Clamp(MathF.Round(_uiScale + direction * step, 1), minimum, maximum);
    UiLayout.Scale = _uiScale;
    _ui.InputScale = _uiScale;
  }

  private Point ToUiPoint(Point screenPoint) => new(
    (int)MathF.Floor(screenPoint.X / _uiScale),
    (int)MathF.Floor(screenPoint.Y / _uiScale)
  );

  private Rectangle GetUiViewport() => UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

  private void CycleResolution()
  {
    (int width, int height)[] resolutions = [(1280, 720), (1920, 1080), (2560, 1440), (3840, 2160)];
    _resolutionIndex = (_resolutionIndex + 1) % resolutions.Length;
    (int width, int height) resolution = resolutions[_resolutionIndex];
    _graphics.PreferredBackBufferWidth = resolution.width;
    _graphics.PreferredBackBufferHeight = resolution.height;
    _graphics.ApplyChanges();
  }

  private string GetFpsCapLabel() => _fpsCap == FpsCap.Unlimited ? "UNLIMITED" : $"{(int)_fpsCap} FPS";

  private string GetResolutionLabel()
  {
    return _resolutionIndex switch
    {
      0 => "720P",
      1 => "FHD (1080P)",
      2 => "QHD (1440P)",
      _ => "4K (2160P)"
    };
  }

  private void StartLocalChessClock()
  {
    _localClockSeconds.Clear();
    if (!_chessTimerEnabled) return;
    double startingSeconds = _chessTimerMinutes * 60d + _chessTimerSeconds;
    foreach (TeamName team in Team.ActiveTeams)
    {
      _localClockSeconds[team] = startingSeconds;
    }
  }

  private void UpdateLocalChessClock(float deltaTime)
  {
    if (!_chessTimerEnabled || _winningTeam is not null || !_localClockSeconds.TryGetValue(Team.CurrentTurn, out double remaining))
    {
      return;
    }

    remaining = Math.Max(0d, remaining - deltaTime);
    _localClockSeconds[Team.CurrentTurn] = remaining;
    if (remaining > 0d) return;
    _winningTeam = Team.ActiveTeams.First(team => team != Team.CurrentTurn);
    selectedPiece = null;
    _screen = Screen.GameOver;
  }

  private void ApplyOnlineClockState(NetworkClockState clock) => _onlineClock = clock;

  private double GetClockSeconds(TeamName team)
  {
    if (_onlineClient is null)
    {
      return _localClockSeconds.GetValueOrDefault(team);
    }
    if (_onlineClock is null) return 0d;
    NetworkClockTeamState clockTeam = _onlineClock.Teams.FirstOrDefault(entry => entry.Team == team.ToNetworkTeam());
    if (clockTeam is null) return 0d;
    long remaining = clockTeam.RemainingMilliseconds;
    if (_onlineClock.ActiveTeam == clockTeam.Team)
    {
      remaining -= Math.Max(0L, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _onlineClock.UpdatedAtUnixMilliseconds);
    }
    return Math.Max(0d, remaining / 1000d);
  }

  private string FormatClock(TeamName team)
  {
    int totalSeconds = Math.Max(0, (int)Math.Ceiling(GetClockSeconds(team)));
    return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
  }

  private bool UpdatePlanningGesture(
    KeyboardState keyboard,
    MouseState mouse,
    Vector2 mouseWorld,
    bool wasRightClick,
    bool wasRightRelease
  )
  {
    bool altHeld = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
    bool shiftHeld = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
    if (wasRightClick && altHeld && shiftHeld)
    {
      ClearPlanningMarks();
      return true;
    }
    if (wasRightClick && (altHeld || shiftHeld) && TryGetBoardPosition(mouseWorld, out (int x, int y) plannedStart))
    {
      _planningStart = plannedStart;
      _planningPath = shiftHeld ? PlanningPath.Straight : PlanningPath.Any;
      _planningGestureActive = true;
      return true;
    }

    if (!_planningGestureActive)
    {
      return false;
    }

    if (wasRightRelease)
    {
      if (_planningStart is (int x, int y) start && TryGetBoardPosition(mouseWorld, out (int x, int y) end))
      {
        (int x, int y)? arrowEnd = start == end ? null : end;
        PlanningMark mark = new(start, arrowEnd, _planningPath);
        int existing = _planningMarks.FindIndex(candidate => candidate == mark);
        if (existing >= 0) _planningMarks.RemoveAt(existing);
        else _planningMarks.Add(mark);
      }
      _planningStart = null;
      _planningGestureActive = false;
    }

    return true;
  }

  private void ClearPlanningMarks()
  {
    _planningMarks.Clear();
    _planningStart = null;
    _planningGestureActive = false;
  }

  private bool TryGetBoardPosition(Vector2 worldPosition, out (int x, int y) position)
  {
    position = (
      (int)MathF.Floor(worldPosition.X / 64f) + _board.MinX,
      (int)MathF.Floor(worldPosition.Y / 64f) + _board.MinY
    );
    return IsBoardCell(position.x - _board.MinX, position.y - _board.MinY);
  }

  private Vector2 GetBoardSquareCenter((int x, int y) position, int cellSize) => new(
    (position.x - _board.MinX) * cellSize + cellSize * 0.5f,
    (position.y - _board.MinY) * cellSize + cellSize * 0.5f
  );

  private void TryPurchaseAndPlace((int x, int y) targetPosition)
  {
    TryPurchaseAndPlace(GetPurchasablePieces()[_selectedPurchaseIndex], targetPosition);
  }

  private void TryPurchaseAndPlace(PieceDefinition definition, (int x, int y) targetPosition)
  {
    if (!IsCampaignPurchaseAllowed(Team.CurrentTurn, definition.Identifier))
    {
      Console.WriteLine($"{definition.Type} is not available for {Team.CurrentTurn} in this campaign level.");
      return;
    }
    if (_initialBuyPhase?.IsFarmPlacementPhase == true && definition.Type != PieceType.Farm)
    {
      _onlineError = "Place your two farms before buying units.";
      return;
    }
    if (_onlineClient != null)
    {
      if (!IsOnlineLocalTurn())
      {
        _onlineError = "It is not your initial buy turn.";
        return;
      }

      if (_initialBuyPhase != null && definition.Type == PieceType.Mercenary)
      {
        Console.WriteLine("Mercenaries are unavailable during the initial buy phase.");
        return;
      }

      if (_initialBuyPhase != null)
      {
        _ = SendOnlineInitialPurchaseAsync(definition, targetPosition);
      }
      else
      {
        _ = SendOnlinePurchaseAsync(definition, targetPosition);
      }
      return;
    }

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

      if (targetPiece.Team != TeamName.Neutral)
      {
        Console.WriteLine("Only neutral Mercenaries can be hired.");
        return;
      }

      long buyoutCost = PieceDefinitions.NeutralMercenaryHireCost;
      if (buyoutCost > int.MaxValue || buyingTeam.Money < buyoutCost)
      {
        Console.WriteLine("You cannot afford to hire that Mercenary.");
        return;
      }

      buyingTeam.Money = ClampCurrency((long)buyingTeam.Money - buyoutCost);
      targetPiece.Team = Team.CurrentTurn;
      targetPiece.LastBid = (int)buyoutCost;
      targetPiece.HasMovedThisTurn = true;
      targetPiece.HasAttackedThisTurn = true;
      targetPiece.CannotContributeToConquestThisTurn = true;

      Console.WriteLine($"{Team.CurrentTurn} hired the neutral Mercenary for {buyoutCost} gold.");
      CompletePurchase();
      return;
    }

    if (_initialBuyPhase != null && definition.Type == PieceType.Mercenary)
    {
      Console.WriteLine("Mercenaries cannot be bought during the initial buy phase.");
      return;
    }

    bool isOpeningFarmPlacement = _initialBuyPhase?.IsFarmPlacementPhase == true && definition.Type == PieceType.Farm;
    bool canPlace =
      (definition.Type == PieceType.Mercenary
        ? CanPlaceMercenary(targetPosition)
        : CanPlacePiece(definition, targetPosition, Team.CurrentTurn)) &&
      (isOpeningFarmPlacement || buyingTeam.Money >= GetUnitPrice(definition));

    if (!canPlace)
    {
      Console.WriteLine(definition.Type == PieceType.Mercenary
        ? "Mercenaries must be placed on an empty square in No-Man's-Land."
        : "Pieces must be placed on an empty square on your side of the board.");
      return;
    }

    int price = isOpeningFarmPlacement ? 0 : GetUnitPrice(definition);
    buyingTeam.Money = ClampCurrency((long)buyingTeam.Money - price);
    Piece boughtPiece = new(definition, targetPosition, buyingTeam.TeamName)
    {
      LastBid = price,
      HasMovedThisTurn = _initialBuyPhase is null,
      HasAttackedThisTurn = _initialBuyPhase is null,
      CannotContributeToConquestThisTurn = _initialBuyPhase is null
    };
    pieceSetup.AddPiece(boughtPiece);

    Console.WriteLine(
      $"Bought and placed {definition.Type} at ({targetPosition.x}, {targetPosition.y})."
    );

    CompletePurchase();
  }

  private void StartInitialBuyPhase()
  {
    _cpuTurnNumber = 0;
    _cpuRecentMoves.Clear();
    InitializeModeObjectives();
    _initialBuyPhase = new InitialBuyPhase(_initialBuysPerTurn, _initialBuyTurnsPerTeam, Team.ActiveTeams, _farmsEnabled);
    EnsureInitialBuySelection();
    if (GetPurchasablePieces()[_selectedPurchaseIndex].Type == PieceType.Mercenary)
    {
      CyclePurchaseSelection(1);
    }
    Team.ResetTurn();
    StartLocalChessClock();
    Team.SetCurrentTurn(_initialBuyPhase.CurrentTeam);
    _isPurchaseMode = true;
    selectedPiece = null;
    _screen = Screen.Playing;
    Console.WriteLine("Initial buy phase started.");
  }

  private void CyclePurchaseSelection(int direction)
  {
    IReadOnlyList<PieceDefinition> purchasablePieces = GetPurchasablePieces();
    if (_initialBuyPhase?.IsFarmPlacementPhase == true)
    {
      EnsureInitialBuySelection();
      return;
    }
    for (int attempts = 0; attempts < purchasablePieces.Count; attempts++)
    {
      _selectedPurchaseIndex =
        (_selectedPurchaseIndex + direction + purchasablePieces.Count) % purchasablePieces.Count;
      if (_initialBuyPhase == null ||
          purchasablePieces[_selectedPurchaseIndex].Type != PieceType.Mercenary)
      {
        return;
      }
    }
  }

  private IReadOnlyList<PieceDefinition> GetPurchasablePieces()
  {
    IEnumerable<PieceDefinition> definitions;
    if (_campaignTestPlay && _campaignTestDefinition is not null)
    {
      definitions = CampaignUnitResolver.GetPurchasableIdentifiers(_campaignTestDefinition)
        .Select(identifier => CampaignUnitResolver.TryResolve(_campaignTestDefinition, identifier, null, out PieceDefinition definition)
          ? definition
          : null)
        .Where(definition => definition is not null)
        .Cast<PieceDefinition>()
        .Where(definition => IsCampaignPurchaseAllowed(Team.CurrentTurn, definition.Identifier));
    }
    else
    {
      definitions = PieceDefinitions.Purchasable.Where(definition => _allowedPacks.Contains(definition.Pack));
    }

    return _farmsEnabled
      ? definitions.ToArray()
      : definitions.Where(definition => definition.Type != PieceType.Farm).ToArray();
  }

  private void EnsurePurchaseSelectionIsValid()
  {
    int purchaseCount = GetPurchasablePieces().Count;
    _selectedPurchaseIndex = purchaseCount == 0 ? 0 : _selectedPurchaseIndex % purchaseCount;
  }

  private bool TrySelectPurchaseIndex(int index)
  {
    IReadOnlyList<PieceDefinition> purchasablePieces = GetPurchasablePieces();
    if (index < 0 || index >= purchasablePieces.Count)
    {
      return false;
    }

    PieceDefinition definition = purchasablePieces[index];
    if ((_initialBuyPhase?.IsFarmPlacementPhase == true && definition.Type != PieceType.Farm) ||
        (_initialBuyPhase != null && definition.Type == PieceType.Mercenary))
    {
      return false;
    }

    _selectedPurchaseIndex = index;
    return true;
  }

  private void EnsureInitialBuySelection()
  {
    if (_initialBuyPhase?.IsFarmPlacementPhase != true)
    {
      return;
    }

    int farmIndex = GetPurchasablePieces().ToList().FindIndex(piece => piece.Type == PieceType.Farm);
    if (farmIndex >= 0) _selectedPurchaseIndex = farmIndex;
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
        team.ActionPoints = team.ActionLimit;
      }
      foreach (TeamName team in Team.ActiveTeams)
      {
        ResetPieceTurnActions(team);
      }
      ApplyTurnEconomy(Team.CurrentTurn);

      Console.WriteLine("Initial buy phase complete. The match has started.");
      return;
    }

    Team.SetCurrentTurn(_initialBuyPhase.CurrentTeam);
    _isPurchaseMode = true;
    EnsureInitialBuySelection();
    selectedPiece = null;
  }

  private bool HandleInitialBuyStopClick(Point mousePosition)
  {
    if (_initialBuyPhase == null || !_initialBuyPhase.CanStopCurrentBuyer || !GetInitialBuyStopButtonBounds().Contains(mousePosition))
    {
      return false;
    }

    if (_onlineClient != null)
    {
      if (!IsOnlineLocalTurn())
      {
        _onlineError = "It is not your initial buy turn.";
        return false;
      }

      _ = SendOnlineStopInitialBuyingAsync();
    }
    else
    {
      _initialBuyPhase.StopCurrentBuyer();
      UpdateInitialBuyPhaseState();
    }
    return true;
  }

  private bool HandleSkipTurnClick(Point mousePosition)
  {
    if (_initialBuyPhase != null || !GetSkipTurnButtonBounds().Contains(mousePosition))
    {
      return false;
    }

    TrySkipCurrentTurn();
    return true;
  }

  private void TrySkipCurrentTurn()
  {
    if (_screen != Screen.Playing || _initialBuyPhase != null || _royalAwaitingPlacement is not null || !IsOnlineLocalTurn())
    {
      return;
    }

    Team currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);
    if (!CanSkipCurrentTurn(currentTeam))
    {
      return;
    }

    if (_onlineClient is null)
    {
      if (Globals.ActionLimitsEnabled)
      {
        currentTeam.ActionPoints = 1;
      }
      CompleteAction(endTurn: true);
    }
    else
    {
      _ = SendOnlineSkipTurnAsync();
    }
  }

  private static bool CanSkipCurrentTurn(Team team) =>
    !Globals.ActionLimitsEnabled || team.ActionPoints < team.ActionLimit || team.ChosenRoyal == PieceType.Palace;

  private bool HandleDebugTeamSwitchClick(Point mousePosition)
  {
    if (!IsDebugOnlineMatch || !GetDebugTeamSwitchButtonBounds().Contains(mousePosition))
    {
      return false;
    }

    _ = SwitchDebugTeamAsync();
    return true;
  }

  private void CompleteAction(bool endTurn = false)
  {
    Team currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);

    if (ApplyCampaignRuntimeObjectives())
    {
      return;
    }

    if (endTurn || currentTeam.SpendAction())
    {
      if (ApplyEndOfTurnObjectives(Team.CurrentTurn))
      {
        return;
      }

      bool completedRound = Team.CurrentTurn == Team.ActiveTeams[^1];
      if (_onlineClient is null && _chessTimerEnabled)
      {
        _localClockSeconds[Team.CurrentTurn] = _localClockSeconds.GetValueOrDefault(Team.CurrentTurn) + _chessTimerIncrementSeconds;
      }
      Team.AdvanceTurn();
      if (completedRound) _campaignCompletedRounds++;
      _cpuTurnNumber++;
      _cpuActionQueue.Clear();
      ApplyTurnEconomy(Team.CurrentTurn);
      ResetPieceTurnActions(Team.CurrentTurn);
      ApplyCampaignRuntimeObjectives();
    }
  }

  private bool ApplyCampaignRuntimeObjectives()
  {
    if (!_campaignTestPlay || _campaignTestDefinition is null || _screen != Screen.Playing)
    {
      return false;
    }
    NetworkTeam? winner = CampaignRuntimeObjectives.FindWinner(
      _campaignTestDefinition,
      pieceSetup.Pieces,
      _teams,
      _campaignCompletedRounds
    );
    if (!winner.HasValue && _campaignTestDefinition.Scenario.TurnLimit is int turnLimit && _campaignCompletedRounds >= turnLimit)
    {
      winner = _campaignTestDefinition.Teams
        .FirstOrDefault(team => team.Controller == CampaignTeamController.Cpu)?.Team ??
        _campaignTestDefinition.Scenario.FirstTeam;
    }
    if (!winner.HasValue) return false;
    _winningTeam = winner.Value.ToTeamName();
    _screen = Screen.GameOver;
    return true;
  }

  private bool IsCpuTurn()
  {
    return _screen == Screen.Playing && _onlineClient is null && _winningTeam is null &&
      _cpuProfiles.ContainsKey(Team.CurrentTurn);
  }

  private void UpdateCpuTurn(float deltaTime)
  {
    if (_cpuActionDelaySeconds > 0f)
    {
      _cpuActionDelaySeconds = Math.Max(0f, _cpuActionDelaySeconds - deltaTime);
      return;
    }

    if (_cpuActionQueue.Count == 0)
    {
      TeamName team = Team.CurrentTurn;
      if (TryConsumeCpuPreplan(team, out CpuTurnPlan preplannedPlan))
      {
        QueueCpuPlan(team, preplannedPlan);
      }
      else
      {
        if (_cpuPlanningTask is null)
        {
          // Search operates only on this immutable snapshot. Running it away from Update keeps
          // movement, rendering, and input responsive even on a busy opening board.
          CpuGameState snapshot = CreateCpuGameState();
          CpuProfile profile = _cpuProfiles[team];
          NetworkTeam cpuTeam = team.ToNetworkTeam();
          _cpuPlanningTeam = cpuTeam;
          _cpuPlanningCancellation = new System.Threading.CancellationTokenSource();
          System.Threading.CancellationToken cancellationToken = _cpuPlanningCancellation.Token;
          _cpuPlanningTask = StartCpuWorker(
            () => new CpuPlayer().ChooseTurn(snapshot, cpuTeam, profile, cancellationToken),
            cancellationToken
          );
          return;
        }

        if (!_cpuPlanningTask.IsCompleted)
        {
          return;
        }

        System.Threading.Tasks.Task<CpuTurnPlan> completedTask = _cpuPlanningTask;
        _cpuPlanningTask = null;
        _cpuPlanningCancellation?.Dispose();
        _cpuPlanningCancellation = null;
        NetworkTeam? plannedTeam = _cpuPlanningTeam;
        _cpuPlanningTeam = null;
        if (completedTask.IsCanceled || completedTask.IsFaulted || plannedTeam != team.ToNetworkTeam())
        {
          if (completedTask.Exception is not null)
          {
            Console.WriteLine($"CPU planning failed: {completedTask.Exception.GetBaseException().Message}");
          }
          return;
        }

        QueueCpuPlan(team, completedTask.Result);
      }

      if (_cpuActionQueue.Count == 0)
      {
        return;
      }
    }

    ICpuGameAction nextAction = _cpuActionQueue.Dequeue();
    CpuGameState currentState = CreateCpuGameState();
    if (!nextAction.IsLegal(currentState) || !ExecuteCpuAction(nextAction))
    {
      // The visible match is always authoritative. Re-plan instead of applying a stale action.
      _cpuActionQueue.Clear();
      return;
    }

    RecordCpuMove(currentState, nextAction);

    _cpuActionDelaySeconds = 0.18f;
  }

  private void QueueCpuPlan(TeamName team, CpuTurnPlan plan)
  {
    _lastCpuDecisionReport = plan.Report;
    foreach (ICpuGameAction action in plan.Actions)
    {
      _cpuActionQueue.Enqueue(action);
    }

    string actions = plan.Actions.Count == 0
      ? "no legal action"
      : string.Join(" -> ", plan.Actions.Select(action => action.Describe()));
    Console.WriteLine($"CPU {team}: {actions} | score {plan.EstimatedScore:0.0}");
    Console.WriteLine(CpuDebugFormatter.FormatDecision(plan.Report, maximumChoices: 1));
  }

  private void RecordCpuMove(CpuGameState stateBeforeAction, ICpuGameAction action)
  {
    if (action is not MoveAction move)
    {
      return;
    }

    NetworkPiece piece = stateBeforeAction.Pieces.FirstOrDefault(candidate => candidate.Id == move.PieceId);
    if (piece is null)
    {
      return;
    }

    _cpuRecentMoves.Add(new CpuMoveRecord(
      move.Team,
      move.PieceId,
      piece.X,
      piece.Y,
      move.DestinationX,
      move.DestinationY,
      stateBeforeAction.TurnNumber
    ));
    if (_cpuRecentMoves.Count > CpuMoveRecord.MaximumEntries)
    {
      _cpuRecentMoves.RemoveRange(0, _cpuRecentMoves.Count - CpuMoveRecord.MaximumEntries);
    }
  }

  private void CancelCpuPlanning()
  {
    CancelCpuWorker(ref _cpuPlanningTask, ref _cpuPlanningCancellation);
    _cpuPlanningTeam = null;
    CancelCpuPreplanning();
  }

  private void UpdateCpuPreplanning()
  {
    // Opening farms are selected by the fast deterministic path below; predicting human farm
    // placement cannot be reused reliably and needlessly competes with rendering.
    if (_onlineClient is not null || _initialBuyPhase is not null || _cpuPreplanningTask is not null || _cpuProfiles.Count == 0)
    {
      return;
    }

    CpuGameState snapshot = CreateCpuGameState();
    NetworkTeam predictedOpponent = snapshot.CurrentTurn;
    NetworkTeam predictedCpu = TeamRules.GetNextTeam(predictedOpponent, snapshot.Configuration.PlayerCount);
    if (!_cpuProfiles.TryGetValue(predictedCpu.ToTeamName(), out CpuProfile cpuProfile))
    {
      return;
    }

    // Use a deliberately light opponent model. Its result is only a speculative cache entry;
    // the exact state hash is checked before a real CPU action is ever queued.
    CpuProfile opponentProfile = CpuProfile.Easy(snapshot.Configuration.TerrainSeed + (int)predictedOpponent);
    _cpuPreplanningCancellation = new System.Threading.CancellationTokenSource();
    System.Threading.CancellationToken cancellationToken = _cpuPreplanningCancellation.Token;
    _cpuPreplanningTask = StartCpuWorker(
      () => BuildCpuPreplan(snapshot, predictedOpponent, predictedCpu, opponentProfile, cpuProfile, cancellationToken),
      cancellationToken
    );
  }

  private bool TryConsumeCpuPreplan(TeamName team, out CpuTurnPlan plan)
  {
    plan = null;
    if (_cpuPreplanningTask is null)
    {
      return false;
    }

    if (!_cpuPreplanningTask.IsCompleted)
    {
      // Do not compete with an obsolete prediction during the real CPU turn.
      CancelCpuPreplanning();
      return false;
    }

    System.Threading.Tasks.Task<CpuPreplannedTurn> completedTask = _cpuPreplanningTask;
    _cpuPreplanningTask = null;
    _cpuPreplanningCancellation?.Dispose();
    _cpuPreplanningCancellation = null;
    if (completedTask.IsCanceled || completedTask.IsFaulted)
    {
      return false;
    }

    CpuPreplannedTurn prepared = completedTask.Result;
    if (prepared is null || prepared.Team != team.ToNetworkTeam())
    {
      return false;
    }

    ulong currentHash = new GameStateHasher().ComputeSearchHash(CreateCpuGameState());
    if (prepared.ExpectedStateHash != currentHash)
    {
      return false;
    }

    plan = prepared.Plan;
    return true;
  }

  private void CancelCpuPreplanning()
  {
    CancelCpuWorker(ref _cpuPreplanningTask, ref _cpuPreplanningCancellation);
  }

  /// <summary>
  /// Releases cancelled CPU worker resources only after a running search has observed its token.
  /// This keeps speculative plans from retaining cancellation sources across many human turns.
  /// </summary>
  private static void CancelCpuWorker<T>(
    ref System.Threading.Tasks.Task<T> task,
    ref System.Threading.CancellationTokenSource cancellation
  )
  {
    System.Threading.Tasks.Task<T> pendingTask = task;
    System.Threading.CancellationTokenSource cancellationSource = cancellation;
    task = null;
    cancellation = null;
    if (cancellationSource is null)
    {
      return;
    }

    cancellationSource.Cancel();
    if (pendingTask is null || pendingTask.IsCompleted)
    {
      cancellationSource.Dispose();
      return;
    }

    _ = pendingTask.ContinueWith(
      _ => cancellationSource.Dispose(),
      System.Threading.CancellationToken.None,
      System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,
      System.Threading.Tasks.TaskScheduler.Default
    );
  }

  private static System.Threading.Tasks.Task<T> StartCpuWorker<T>(
    Func<T> work,
    System.Threading.CancellationToken cancellationToken
  ) => System.Threading.Tasks.Task.Factory.StartNew(
    work,
    cancellationToken,
    System.Threading.Tasks.TaskCreationOptions.LongRunning | System.Threading.Tasks.TaskCreationOptions.DenyChildAttach,
    System.Threading.Tasks.TaskScheduler.Default
  );

  private static CpuPreplannedTurn BuildCpuPreplan(
    CpuGameState snapshot,
    NetworkTeam opponent,
    NetworkTeam expectedCpu,
    CpuProfile opponentProfile,
    CpuProfile cpuProfile,
    System.Threading.CancellationToken cancellationToken
  )
  {
    CpuPlayer player = new();
    CpuGameState predicted = ApplyPlannedTurn(snapshot, opponent, player.ChooseTurn(snapshot, opponent, opponentProfile, cancellationToken));
    if (cancellationToken.IsCancellationRequested || predicted.IsFinished || predicted.CurrentTurn != expectedCpu)
    {
      return null;
    }

    CpuTurnPlan response = player.ChooseTurn(predicted, expectedCpu, cpuProfile, cancellationToken);
    return cancellationToken.IsCancellationRequested ? null : new CpuPreplannedTurn(
      expectedCpu,
      new GameStateHasher().ComputeSearchHash(predicted),
      response
    );
  }

  private static CpuGameState ApplyPlannedTurn(CpuGameState state, NetworkTeam team, CpuTurnPlan plan)
  {
    foreach (ICpuGameAction action in plan.Actions)
    {
      if (state.IsFinished || state.CurrentTurn != team || !action.IsLegal(state))
      {
        break;
      }
      state = action.Apply(state);
    }

    EndTurnAction endTurn = new(team);
    return !state.IsFinished && state.CurrentTurn == team && endTurn.IsLegal(state)
      ? endTurn.Apply(state)
      : state;
  }

  private CpuGameState CreateCpuGameState()
  {
    NetworkMatchConfiguration configuration = BuildOnlineMatchConfiguration();
    NetworkInitialBuyState initialBuy = _initialBuyPhase is null ? null : new NetworkInitialBuyState(
      _initialBuyPhase.CurrentTeam.ToNetworkTeam(),
      _initialBuyPhase.PurchasesThisTurn,
      _initialBuyPhase.PurchasesPerTurn,
      _initialBuyPhase.GetBuyTurnsUsed(TeamName.Red),
      _initialBuyPhase.GetBuyTurnsUsed(TeamName.Blue),
      _initialBuyPhase.BuyTurnsPerTeam,
      _initialBuyPhase.HasStopped(TeamName.Red),
      _initialBuyPhase.HasStopped(TeamName.Blue),
      _initialBuyPhase.IsComplete,
      Team.ActiveTeams.Select(team => new NetworkInitialBuyTeamState(
        team.ToNetworkTeam(),
        _initialBuyPhase.GetBuyTurnsUsed(team),
        _initialBuyPhase.HasStopped(team),
        _initialBuyPhase.GetFarmsPlaced(team)
      )).ToArray(),
      _initialBuyPhase.IsFarmPlacementPhase
    );
    List<NetworkImprovement> improvements = [];
    improvements.AddRange(_roads.Select(entry => new NetworkImprovement("Road", entry.Key.x, entry.Key.y, Owner: entry.Value.ToNetworkTeam())));
    improvements.AddRange(_barricades.Select(entry => new NetworkImprovement("Barrier", entry.Key.x, entry.Key.y, entry.Value)));
    improvements.AddRange(_mines.Select(entry => new NetworkImprovement("Mine", entry.Key.x, entry.Key.y, Owner: entry.Value.ToNetworkTeam())));

    return new CpuGameState(
      configuration,
      pieceSetup.Pieces.Select(piece => new NetworkPiece(
        piece.NetworkId,
        piece.Definition.Type.ToString(),
        piece.Team.ToNetworkTeam(),
        piece.Position.x,
        piece.Position.y,
        piece.CurrentHealth,
        piece.HasMovedThisTurn,
        piece.HasAttackedThisTurn,
        piece.AttachedTo?.NetworkId,
        (NetworkAttachmentKind)piece.AttachmentKind,
        piece.MarkedTarget?.NetworkId,
        piece.LastBid,
        piece.EngineerBuildsThisTurn,
        piece.CannotContributeToConquestThisTurn,
        piece.CavalierFollowUpMoveAvailable,
        piece.AttacksThisTurn,
        piece.HasRevived,
        piece.TurnsInCurrentForm,
        piece.IsRoyalProxy,
        piece.PossessedUnitId,
        piece.Facing.x,
        piece.Facing.y,
        piece.PendingDamage
      )),
      _teams.Select(team => new CpuTeamState(
        team.TeamName.ToNetworkTeam(), team.Money, team.ActionPoints, team.ChosenRoyal?.ToString(), team.ActionLimit
      )),
      Team.CurrentTurn.ToNetworkTeam(),
      turnNumber: _cpuTurnNumber,
      terrain: _terrain,
      winner: _winningTeam?.ToNetworkTeam(),
      initialBuy: initialBuy,
      conquestScore: _conquestScore,
      conquestScores: _conquestScores.Select(entry => KeyValuePair.Create(entry.Key.ToNetworkTeam(), entry.Value)),
      modeScores: _modeScores.Select(entry => KeyValuePair.Create(entry.Key.ToNetworkTeam(), entry.Value)),
      treasurePosition: _treasurePosition,
      treasureCarrierId: _treasureCarrierId,
      roads: _roads.Select(entry => KeyValuePair.Create(entry.Key, entry.Value.ToNetworkTeam())),
      barricades: _barricades,
      mines: _mines.Select(entry => KeyValuePair.Create(entry.Key, entry.Value.ToNetworkTeam())),
      riverBridges: _riverBridges,
      scenario: CreateCampaignCpuScenario(configuration),
      recentMoves: _cpuRecentMoves,
      board: _campaignTestPlay ? _board : null
    );
  }

  private CpuScenarioDefinition CreateCampaignCpuScenario(NetworkMatchConfiguration configuration)
  {
    CpuScenarioDefinition match = CpuScenarioDefinition.ForMatch(configuration);
    if (!_campaignTestPlay || _campaignTestDefinition is null)
    {
      return match;
    }

    return new CpuScenarioDefinition
    {
      Id = "campaign-" + match.Id,
      VictoryGoals = match.VictoryGoals,
      DefeatConditions = match.DefeatConditions,
      SecondaryGoals = match.SecondaryGoals,
      Weights = match.Weights,
      TurnLimit = match.TurnLimit,
      WinnerOnTurnLimit = match.WinnerOnTurnLimit,
      ScriptedReinforcements = match.ScriptedReinforcements,
      Restrictions = new CpuScenarioRestrictions
      {
        AdditionalActionRule = (state, action) => IsCampaignCpuActionAllowed(state, action)
      }
    };
  }

  private bool IsCampaignCpuActionAllowed(CpuGameState state, ICpuGameAction action)
  {
    if (action is PurchaseAction purchase && !IsCampaignPurchaseAllowed(purchase.Team.ToTeamName(), ParsePieceType(purchase.UnitType)))
    {
      return false;
    }
    if (action is UseAbilityAction ability)
    {
      NetworkPiece actor = state.Pieces.FirstOrDefault(piece => piece.Id == ability.ActorId);
      if (actor is null || !IsCampaignAbilityAllowed(ability.Team.ToTeamName(), ParsePieceType(actor.Type))) return false;
    }
    return true;
  }

  private static PieceType ParsePieceType(string type) =>
    Enum.TryParse(type, ignoreCase: false, out PieceType parsed) ? parsed : PieceType.Peasant;

  private bool IsCampaignPurchaseAllowed(TeamName teamName, PieceType type) =>
    IsCampaignPurchaseAllowed(teamName, type.ToString());

  private bool IsCampaignPurchaseAllowed(TeamName teamName, string unitType)
  {
    if (!_campaignTestPlay || _campaignTestDefinition is null) return true;
    CampaignRestrictionsDefinition restrictions = _campaignTestDefinition.Restrictions;
    CampaignTeamDefinition team = _campaignTestDefinition.Teams.FirstOrDefault(candidate => candidate.Team == teamName.ToNetworkTeam());
    bool teamAllowsUnit = team?.PurchaseListMode switch
    {
      CampaignPurchaseListMode.All => CampaignUnitResolver.GetPurchasableIdentifiers(_campaignTestDefinition).Contains(unitType),
      CampaignPurchaseListMode.Custom => team.AvailableUnitTypes.Contains(unitType),
      _ => false
    };
    return restrictions.PurchasesEnabled && team is not null && team.PurchasesEnabled && teamAllowsUnit &&
      (restrictions.AllowedUnitTypes.Count == 0 || restrictions.AllowedUnitTypes.Contains(unitType)) &&
      !restrictions.DisabledUnitTypes.Contains(unitType);
  }

  private bool IsCampaignAbilityAllowed(TeamName teamName, PieceType type)
  {
    if (!_campaignTestPlay || _campaignTestDefinition is null) return true;
    CampaignRestrictionsDefinition restrictions = _campaignTestDefinition.Restrictions;
    CampaignTeamDefinition team = _campaignTestDefinition.Teams.FirstOrDefault(candidate => candidate.Team == teamName.ToNetworkTeam());
    string unitType = type.ToString();
    return restrictions.AbilitiesEnabled && team is not null &&
      !restrictions.DisabledAbilityUnitTypes.Contains(unitType) &&
      !team.DisabledAbilityUnitTypes.Contains(unitType);
  }

  private bool ExecuteCpuAction(ICpuGameAction action)
  {
    switch (action)
    {
      case MoveAction move:
        {
          Piece piece = pieceSetup.Pieces.FirstOrDefault(candidate => candidate.NetworkId == move.PieceId);
          if (piece is null || !TryGetMovementPathAt(piece, (move.DestinationX, move.DestinationY), out List<(int x, int y)> path))
          {
            return false;
          }
          if (piece.AttachedTo is not null && piece.AttachmentKind == AttachmentKind.Carried)
          {
            pieceSetup.Detach(piece);
          }
          BeginMovementAnimation(piece, path);
          return true;
        }
      case AttackAction attack:
        return ExecuteCpuAttack(attack);
      case PurchaseAction purchase:
        {
          PieceDefinition definition = GetPurchasablePieces().FirstOrDefault(piece => piece.Type.ToString() == purchase.UnitType);
          if (definition is null)
          {
            return false;
          }

          Team buyingTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);
          int moneyBefore = buyingTeam.Money;
          int pieceCountBefore = pieceSetup.Pieces.Count;
          Piece targetBefore = pieceSetup.GetPieceAt((purchase.X, purchase.Y));
          TeamName? targetTeamBefore = targetBefore?.Team;
          int actionPointsBefore = buyingTeam.ActionPoints;
          int purchasesBefore = _initialBuyPhase?.PurchasesThisTurn ?? -1;
          TryPurchaseAndPlace(definition, (purchase.X, purchase.Y));
          return buyingTeam.Money != moneyBefore ||
            pieceSetup.Pieces.Count != pieceCountBefore ||
            targetBefore?.Team != targetTeamBefore ||
            buyingTeam.ActionPoints != actionPointsBefore ||
            (_initialBuyPhase?.PurchasesThisTurn ?? -1) != purchasesBefore;
        }
      case UseAbilityAction ability:
        {
          Piece actor = pieceSetup.Pieces.FirstOrDefault(candidate => candidate.NetworkId == ability.ActorId);
          if (actor is null)
          {
            return false;
          }
          _selectedEngineerAbility = ability.Ability switch
          {
            "Barrier" => EngineerAbility.Barrier,
            "Mine" => EngineerAbility.Mine,
            "Demolish" => EngineerAbility.Demolish,
            _ => EngineerAbility.Road
          };
          Piece target = ability.TargetPieceId is null
            ? null
            : pieceSetup.Pieces.FirstOrDefault(candidate => candidate.NetworkId == ability.TargetPieceId);
          return TryUseSpecialAbility(actor, (ability.TargetX, ability.TargetY), target, Keyboard.GetState());
        }
      case EndTurnAction:
        {
          TeamName before = Team.CurrentTurn;
          TrySkipCurrentTurn();
          return Team.CurrentTurn != before;
        }
      case StopInitialBuyingAction:
        {
          if (_initialBuyPhase is null || !_initialBuyPhase.CanStopCurrentBuyer)
          {
            return false;
          }
          _initialBuyPhase.StopCurrentBuyer();
          UpdateInitialBuyPhaseState();
          return true;
        }
      default:
        return false;
    }
  }

  private bool ExecuteCpuAttack(AttackAction action)
  {
    Piece attacker = pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == action.AttackerId);
    Piece target = action.TargetPieceId is null
      ? null
      : pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == action.TargetPieceId);
    var targetPosition = (action.TargetX, action.TargetY);
    bool isValidAttack = attacker is not null && !attacker.HasAttackedThisTurn && attacker.Definition.Attack > 0 &&
      Actions.CanAttackSquare(attacker, targetPosition) && HasClearAttackPath(attacker, targetPosition) &&
      ((target is not null && target.Team != attacker.Team) || (target is null && _barricades.ContainsKey(targetPosition)));
    if (!isValidAttack)
    {
      return false;
    }

    if (attacker.Definition.Type == PieceType.Ballista)
    {
      PerformPiercingAttack(attacker, targetPosition);
    }
    else if (_barricades.ContainsKey(targetPosition))
    {
      DamageBarricade(attacker, targetPosition);
    }
    else
    {
      PerformSharedUnitAttack(attacker, target);
    }

    attacker.HasAttackedThisTurn = true;
    attacker.CavalierFollowUpMoveAvailable = AbilityRules.GrantsCavalierFollowUpMove(
      attacker.Definition.Type.ToString(), attacker.HasMovedThisTurn);
    if (_screen == Screen.Playing)
    {
      CompleteAction();
    }
    return true;
  }

  private void ResetPieceTurnActions(TeamName teamName)
  {
    ApplySharedStartOfTurnEffects(teamName);
    foreach (Piece piece in pieceSetup.Pieces.OrderBy(piece => piece.Definition.Type == PieceType.Farm ? 0 : 1).ToArray())
    {
      if (piece.Team == teamName)
      {
        piece.HasMovedThisTurn = false;
        piece.HasAttackedThisTurn = false;
        piece.CavalierFollowUpMoveAvailable = false;
        piece.EngineerBuildsThisTurn = 0;
        piece.CannotContributeToConquestThisTurn = false;
      }
    }
  }

  private void ApplyTurnEconomy(TeamName teamName)
  {
    Team team = _teams.Find(candidate => candidate.TeamName == teamName);
    if (_interestEnabled && _interestPercent != 0)
    {
      int interest = EconomyRules.GetInterest(team.Money, _interestPercent);
      team.Money = ClampCurrency((long)team.Money + interest);
      Console.WriteLine($"{UiText.GetTeamDisplayName(teamName)} received {interest} gold in interest.");
    }

    int farmCount = pieceSetup.Pieces.Count(piece =>
      piece.Team == teamName && piece.AttachedTo is null && piece.Definition.Type == PieceType.Farm);
    long income = farmCount * (long)_farmIncomePerTurn;
    if (income != 0)
    {
      team.Money = ClampCurrency((long)team.Money + income);
      Console.WriteLine($"{UiText.GetTeamDisplayName(teamName)} collected {income} gold from farms.");
    }

    ApplySharedAbilityUpkeep(teamName, team);
    if (_screen == Screen.GameOver)
    {
      return;
    }

    if (!_unitMaintenanceEnabled || _unitMaintenancePercent <= 0)
    {
      return;
    }

    long upkeep = GetTeamMaintenance(teamName);
    if (upkeep > 0)
    {
      team.Money = ClampCurrency((long)team.Money - upkeep);
      Console.WriteLine($"{UiText.GetTeamDisplayName(teamName)} paid {upkeep} gold in unit upkeep.");
    }
  }

  private static int ClampCurrency(long amount) => (int)Math.Clamp(amount, int.MinValue, int.MaxValue);

  private int GetTeamMaintenance(TeamName teamName)
  {
    if (!_unitMaintenanceEnabled || _unitMaintenancePercent <= 0)
    {
      return 0;
    }

    long upkeep = pieceSetup.Pieces
      .Where(piece => piece.Team == teamName && piece.AttachedTo is null)
      .Sum(piece => (long)GetUnitMaintenance(piece.Definition));
    return ClampCurrency(upkeep);
  }

  private int GetUnitPrice(PieceDefinition definition) =>
    definition.Type == PieceType.Farm
      ? definition.Cost
      : EconomyRules.GetUnitPrice(definition.Cost, _unitPricePercent);

  private int GetUnitMaintenance(PieceDefinition definition) =>
    definition.Type == PieceType.Farm
      ? 0
      : EconomyRules.GetUnitMaintenance(definition.Cost, _unitMaintenancePercent);

  private bool ApplyConquestPressure(TeamName teamThatFinishedTurn)
  {
    if (_gameMode != GameMode.Conquest)
    {
      return false;
    }

    if (_playerCount > 2)
    {
      int score = Math.Clamp(
        _conquestScores.GetValueOrDefault(teamThatFinishedTurn) + GetConquestOccupyingPieceCount(teamThatFinishedTurn),
        0,
        _conquestWinScore
      );
      _conquestScores[teamThatFinishedTurn] = score;
      if (score < _conquestWinScore) return false;
      _winningTeam = teamThatFinishedTurn;
      _screen = Screen.GameOver;
      selectedPiece = null;
      return true;
    }

    int pressure = GetConquestOccupyingPieceCount(teamThatFinishedTurn);
    if (pressure == 0)
    {
      return false;
    }

    _conquestScore += teamThatFinishedTurn == TeamName.Red ? -pressure : pressure;
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
      piece.Team == team && piece.AttachmentKind == AttachmentKind.None && !piece.CannotContributeToConquestThisTurn &&
      piece.OccupiedSquares().Any(IsConquestSquare));
  }

  private bool ApplyEndOfTurnObjectives(TeamName teamThatFinishedTurn)
  {
    if (_gameMode == GameMode.Conquest)
    {
      return ApplyConquestPressure(teamThatFinishedTurn);
    }

    if (_gameMode != GameMode.Dominion)
    {
      return false;
    }

    int score = Math.Clamp(
      _modeScores.GetValueOrDefault(teamThatFinishedTurn) + GetDominionControlledPointCount(teamThatFinishedTurn),
      0,
      _dominionWinScore
    );
    _modeScores[teamThatFinishedTurn] = score;
    if (score < _dominionWinScore)
    {
      return false;
    }

    _winningTeam = teamThatFinishedTurn;
    _screen = Screen.GameOver;
    selectedPiece = null;
    return true;
  }

  private int GetDominionControlledPointCount(TeamName team)
  {
    int controlledPoints = 0;
    foreach ((int x, int y) point in MatchRules.GetDominionControlPoints(_board))
    {
      bool friendlyTouching = pieceSetup.Pieces.Any(piece =>
        piece.Team == team && piece.AttachedTo is null && piece.Occupies(point));
      bool enemyTouching = pieceSetup.Pieces.Any(piece =>
        piece.Team is not TeamName.Neutral && piece.Team != team && piece.AttachedTo is null && piece.Occupies(point));
      if (friendlyTouching && !enemyTouching)
      {
        controlledPoints++;
      }
    }

    return controlledPoints;
  }

  private void InitializeModeObjectives()
  {
    _modeScores.Clear();
    foreach (TeamName team in Team.ActiveTeams)
    {
      _modeScores[team] = 0;
    }

    _treasureCarrierId = null;
    _treasurePosition = _gameMode == GameMode.Plunder
      ? MatchRules.GetTreasureSpawn(_board)
      : null;
  }

  private bool IsOnlineLocalTurn()
  {
    if (_onlineClient == null)
    {
      return true;
    }

    return _onlineClient.Team is NetworkTeam team && Team.CurrentTurn == team.ToTeamName();
  }

  private bool IsDebugOnlineMatch => _onlineClient?.IsDebugRoom == true;

  private async System.Threading.Tasks.Task SwitchDebugTeamAsync()
  {
    OnlineMatchClient debugClient = _onlineClient;
    if (debugClient == null || !debugClient.IsDebugRoom || _debugTeamSwitchPending)
    {
      return;
    }

    _debugTeamSwitchPending = true;
    selectedPiece = null;
    NetworkTeam nextTeam = debugClient.Team == NetworkTeam.Blue ? NetworkTeam.Red : NetworkTeam.Blue;
    try
    {
      ActionResult result = await debugClient.SelectDebugTeamAsync(nextTeam);
      if (!result.Accepted)
      {
        _onlineError = result.Error ?? "Could not switch the debug player.";
      }
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Debug side switch could not be sent: {exception.Message}");
      _onlineError = "Could not switch the debug player.";
    }
    finally
    {
      _debugTeamSwitchPending = false;
    }
  }

  private string GetDebugTeamSwitchLabel()
  {
    NetworkTeam nextTeam = _onlineClient?.Team == NetworkTeam.Blue ? NetworkTeam.Red : NetworkTeam.Blue;
    string nextTeamName = nextTeam == NetworkTeam.Red ? "ORANGE" : "PURPLE";
    return _debugTeamSwitchPending ? "DEBUG: SWITCHING..." : $"DEBUG: SWITCH TO {nextTeamName}";
  }

  private async System.Threading.Tasks.Task HostOnlineMatchAsync(NetworkMatchConfiguration configuration)
  {
    if (_onlineClient != null)
    {
      Console.WriteLine("Already connected to an online room.");
      return;
    }

    ClearPlanningMarks();

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
      _onlineIsHost = true;
      _onlineStatus = "CREATING PRIVATE ROOM...";
      _screen = Screen.OnlineWaiting;
      RoomJoinResult result = await _onlineClient.HostAsync(new CreateGameRequest(configuration));
      if (!result.Accepted)
      {
        Console.WriteLine($"Could not host room: {result.Error}");
        await _onlineClient.DisposeAsync();
        _onlineClient = null;
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

    ClearPlanningMarks();

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
      _onlineIsHost = false;
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

  private async System.Threading.Tasks.Task SendOnlineAttackAsync(Piece attacker, Piece target)
  {
    try
    {
      ActionResult result = await _onlineClient.AttackAsync(attacker.NetworkId, target.NetworkId);
      if (!result.Accepted)
      {
        _onlineError = result.Error ?? "That attack was rejected.";
      }
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Attack could not be sent: {exception.Message}");
      _onlineError = "Could not send the attack.";
    }
  }

  private async System.Threading.Tasks.Task SendOnlineImprovementAttackAsync(Piece attacker, (int x, int y) targetPosition)
  {
    try
    {
      ActionResult result = await _onlineClient.AttackAsync(attacker.NetworkId, null, targetPosition.x, targetPosition.y);
      if (!result.Accepted) _onlineError = result.Error ?? "That structure attack was rejected.";
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Structure attack could not be sent: {exception.Message}");
      _onlineError = "Could not send the structure attack.";
    }
  }

  private bool TrySendOnlineSpecialAbility(Piece actor, (int x, int y) targetPosition, Piece target)
  {
    bool engineerDemolition = actor.Definition.Type == PieceType.Engineer &&
      _selectedEngineerAbility == EngineerAbility.Demolish;
    if (actor.HasAttackedThisTurn && !engineerDemolition)
    {
      return false;
    }

    bool plunderTreasureTarget = target is null && CanPickUpTreasure(actor, targetPosition);
    bool isSpecialTarget = plunderTreasureTarget || actor.Definition.Type switch
    {
      PieceType.Spy => target is not null && target.Team != actor.Team,
      PieceType.Engineer => true,
      PieceType.Guard or PieceType.Ox => target is not null && target.Team == actor.Team,
      PieceType.Mercenary => targetPosition == actor.Position,
      _ => false
    };
    if (!isSpecialTarget)
    {
      return false;
    }

    string ability = plunderTreasureTarget
      ? "PickUpTreasure"
      : actor.Definition.Type == PieceType.Engineer
      ? _selectedEngineerAbility.ToString()
      : actor.Definition.Type == PieceType.Mercenary
        ? "Fire"
        : string.Empty;
    _ = SendOnlineSpecialAsync(actor, ability, target?.NetworkId, actor.Definition.Type == PieceType.Mercenary ? actor.Position : targetPosition);
    return true;
  }

  private async System.Threading.Tasks.Task SendOnlineSpecialAsync(
    Piece actor,
    string ability,
    string targetId,
    (int x, int y) targetPosition
  )
  {
    try
    {
      ActionResult result = await _onlineClient.SpecialAsync(
        actor.NetworkId, ability, targetId, targetPosition.x, targetPosition.y
      );
      if (!result.Accepted) _onlineError = result.Error ?? "That special action was rejected.";
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Special action could not be sent: {exception.Message}");
      _onlineError = "Could not send the special action.";
    }
  }

  private async System.Threading.Tasks.Task SendOnlineRoyalChoiceAsync((int x, int y) position)
  {
    if (_onlineClient == null || _onlineRoyalChoicePending)
    {
      return;
    }

    _onlineRoyalChoicePending = true;
    try
    {
      PieceDefinition royal = GetAllowedRoyals()[_selectedRoyalIndex];
      ActionResult result = await _onlineClient.ChooseRoyalAsync(royal.Type.ToString(), position.x, position.y);
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

  private async System.Threading.Tasks.Task SendOnlineInitialPurchaseAsync(
    PieceDefinition definition,
    (int x, int y) position
  )
  {
    try
    {
      ActionResult result = await _onlineClient.PurchaseInitialUnitAsync(definition.Type.ToString(), position.x, position.y);
      if (!result.Accepted)
      {
        _onlineError = result.Error ?? "That purchase was rejected.";
      }
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Initial purchase could not be sent: {exception.Message}");
      _onlineError = "Could not send the initial purchase.";
    }
  }

  private async System.Threading.Tasks.Task SendOnlinePurchaseAsync(
    PieceDefinition definition,
    (int x, int y) position
  )
  {
    try
    {
      ActionResult result = await _onlineClient.PurchaseUnitAsync(definition.Type.ToString(), position.x, position.y);
      if (!result.Accepted)
      {
        _onlineError = result.Error ?? "That purchase was rejected.";
      }
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Purchase could not be sent: {exception.Message}");
      _onlineError = "Could not send the purchase.";
    }
  }

  private async System.Threading.Tasks.Task SendOnlineStopInitialBuyingAsync()
  {
    try
    {
      ActionResult result = await _onlineClient.StopInitialBuyingAsync();
      if (!result.Accepted)
      {
        _onlineError = result.Error ?? "Could not stop buying.";
      }
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Stop buying could not be sent: {exception.Message}");
      _onlineError = "Could not stop buying.";
    }
  }

  private async System.Threading.Tasks.Task SendOnlineSkipTurnAsync()
  {
    try
    {
      ActionResult result = await _onlineClient.SkipTurnAsync();
      if (!result.Accepted) _onlineError = result.Error ?? "Could not end the turn.";
    }
    catch (Exception exception)
    {
      Console.WriteLine($"Skip turn could not be sent: {exception.Message}");
      _onlineError = "Could not end the turn.";
    }
  }

  private void ApplyOnlineState(NetworkGameState state)
  {
    Dictionary<string, (int x, int y)> previousPositions = pieceSetup.Pieces
      .Where(piece => !string.IsNullOrWhiteSpace(piece.NetworkId))
      .ToDictionary(piece => piece.NetworkId, piece => piece.Position);
    ApplyOnlineConfiguration(state.Configuration);
    ApplyOnlineClockState(state.Clock);
    ApplyOnlineTeamStates(state.Teams);
    ApplyOnlineImprovements(state.Improvements);
    ApplyOnlinePieces(state.Pieces);
    _conquestScore = state.ConquestScore;
    _conquestScores.Clear();
    foreach (NetworkConquestTeamState score in state.ConquestScores ?? [])
    {
      _conquestScores[score.Team.ToTeamName()] = score.Score;
    }
    _modeScores.Clear();
    foreach (NetworkModeTeamState score in state.ModeScores ?? [])
    {
      _modeScores[score.Team.ToTeamName()] = score.Score;
    }
    _treasurePosition = state.Treasure is { X: int treasureX, Y: int treasureY }
      ? (treasureX, treasureY)
      : null;
    _treasureCarrierId = state.Treasure?.CarrierId;
    if (state.Winner is NetworkTeam winner)
    {
      _winningTeam = winner.ToTeamName();
      _screen = Screen.GameOver;
      selectedPiece = null;
      return;
    }

    if (state.PlayerCount < state.Configuration.PlayerCount)
    {
      _onlineStatus = $"WAITING FOR {state.Configuration.PlayerCount - state.PlayerCount} MORE PLAYER(S)  ROOM: {state.JoinCode}";
      _screen = Screen.OnlineWaiting;
      return;
    }

    if (!state.MatchReady)
    {
      NetworkTeam? localTeam = _onlineClient?.Team;
      bool hasChosenRoyal = localTeam is NetworkTeam team && state.Teams.Any(teamState =>
        teamState.Team == team && !string.IsNullOrWhiteSpace(teamState.ChosenRoyal));
      _onlineRoyalChoicePending = hasChosenRoyal;
      _setupTeam = localTeam?.ToTeamName() ?? TeamName.Red;
      _onlineStatus = hasChosenRoyal
        ? $"WAITING FOR OPPONENT'S ROYAL  ROOM: {state.JoinCode}"
        : $"ONLINE ROYAL SETUP  ROOM: {state.JoinCode}";
      _screen = Screen.OnlineRoyalSelection;
      return;
    }

    if (state.InitialBuy is { IsComplete: false } initialBuy)
    {
      _initialBuyPhase = new InitialBuyPhase(
        initialBuy.PurchasesPerTurn,
        initialBuy.BuyTurnsPerTeam,
        initialBuy.CurrentTeam.ToTeamName(),
        initialBuy.PurchasesThisTurn,
        GetInitialBuyTeamStates(initialBuy),
        initialBuy.IsComplete,
        initialBuy.IsFarmPlacementPhase
      );
      Team.SetCurrentTurn(_initialBuyPhase.CurrentTeam);
      selectedPiece = null;
      _isPurchaseMode = true;
      EnsureInitialBuySelection();
      _onlineStatus = $"ONLINE INITIAL PURCHASE  ROOM: {state.JoinCode}";
      _onlineRoyalChoicePending = false;
      _screen = Screen.Playing;
      _movementAnimation = null;
      return;
    }

    Team.SetCurrentTurn(state.CurrentTurn.ToTeamName());
    selectedPiece = null;
    _initialBuyPhase = null;
    _isPurchaseMode = false;
    _onlineStatus = $"ONLINE {state.CurrentTurn} TURN  ROOM: {state.JoinCode}";
    _onlineRoyalChoicePending = false;
    _screen = Screen.Playing;
    BeginOnlineMovementAnimation(state.Pieces, previousPositions);
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
        !Enum.TryParse(configuration.TerrainSource, out TerrainSource terrainSource) ||
        !Enum.TryParse(configuration.GameMode, out GameMode gameMode))
    {
      _onlineError = "The room sent unsupported match settings.";
      return;
    }

    _onlineMatchConfiguration = configuration;
    _selectedBoardSize = boardSize;
    _forestDensity = forestDensity;
    _waterwayDensity = waterwayDensity;
    _terrainSource = terrainSource;
    _selectedTerrainPresetId = configuration.PresetId;
    _selectedTerrainPresetName = null;
    _allowedPacks.Clear();
    _allowedPacks.UnionWith(PackRules.GetAllowedPacks(configuration.AllowedPacks));
    _gameMode = gameMode;
    _startingCash = configuration.StartingCash;
    _killerRefundMultiplier = configuration.KillerRefundMultiplier;
    _defeatedTeamRefundMultiplier = configuration.DefeatedTeamRefundMultiplier;
    _initialBuysPerTurn = configuration.InitialBuysPerTurn;
    _initialBuyTurnsPerTeam = configuration.InitialBuyTurnsPerTeam;
    _conquestWinScore = configuration.ConquestWinScore;
    _farmsEnabled = configuration.FarmsEnabled;
    _farmIncomePerTurn = configuration.FarmIncomePerTurn;
    _unitMaintenanceEnabled = configuration.UnitMaintenanceEnabled;
    _unitMaintenancePercent = configuration.UnitMaintenancePercent;
    _unitPricePercent = configuration.UnitPricePercent;
    _interestEnabled = configuration.InterestEnabled;
    _interestPercent = configuration.InterestPercent;
    _escortRoyalHealthPercent = configuration.EscortRoyalHealthPercent;
    _dominionWinScore = configuration.DominionWinScore;
    _plunderWinScore = configuration.PlunderWinScore;
    _plunderDeliveryScore = configuration.PlunderDeliveryScore;
    _plunderRoyalKillPenalty = configuration.PlunderRoyalKillPenalty;
    _chessTimerEnabled = configuration.ChessTimerEnabled;
    _chessTimerMinutes = configuration.ChessTimerMinutes;
    _chessTimerSeconds = configuration.ChessTimerSeconds;
    _chessTimerIncrementSeconds = configuration.ChessTimerIncrementSeconds;
    _playerCount = configuration.PlayerCount;
    ConfigureTeamsForPlayerCount();
    EnsurePurchaseSelectionIsValid();
    ConfigureBattlefield(boardSize, forestDensity, waterwayDensity, configuration.TerrainSeed);
  }

  private void ApplyOnlineTeamStates(IReadOnlyList<NetworkTeamState> teamStates)
  {
    foreach (NetworkTeamState state in teamStates)
    {
      TeamName teamName = state.Team.ToTeamName();
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
    Dictionary<string, Piece> piecesByNetworkId = [];
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
        networkPiece.Team.ToTeamName()
      )
      {
        NetworkId = networkPiece.Id,
        CurrentHealth = networkPiece.Health,
        HasMovedThisTurn = networkPiece.HasMovedThisTurn,
        HasAttackedThisTurn = networkPiece.HasAttackedThisTurn,
        CavalierFollowUpMoveAvailable = networkPiece.CavalierFollowUpMoveAvailable,
        LastBid = networkPiece.LastBid,
        EngineerBuildsThisTurn = networkPiece.EngineerBuildsThisTurn,
        CannotContributeToConquestThisTurn = networkPiece.CannotContributeToConquestThisTurn,
        AttacksThisTurn = networkPiece.AttacksThisTurn,
        HasRevived = networkPiece.HasRevived,
        TurnsInCurrentForm = networkPiece.TurnsInCurrentForm,
        IsRoyalProxy = networkPiece.IsRoyalProxy,
        PossessedUnitId = networkPiece.PossessedUnitId,
        Facing = AbilityStateRules.GetFacing(
          networkPiece.Team,
          networkPiece.FacingX,
          networkPiece.FacingY
        ),
        PendingDamage = networkPiece.PendingDamage ?? Array.Empty<NetworkPendingDamage>()
      };
      pieceSetup.AddPiece(piece);
      piecesByNetworkId[networkPiece.Id] = piece;
    }

    foreach (NetworkPiece networkPiece in pieces)
    {
      if (!piecesByNetworkId.TryGetValue(networkPiece.Id, out Piece piece)) continue;
      if (networkPiece.AttachedToId is not null && piecesByNetworkId.TryGetValue(networkPiece.AttachedToId, out Piece host))
      {
        piece.AttachedTo = host;
        piece.AttachmentKind = networkPiece.AttachmentKind switch
        {
          NetworkAttachmentKind.Guard => AttachmentKind.Guard,
          NetworkAttachmentKind.Carried => AttachmentKind.Carried,
          _ => AttachmentKind.None
        };
      }

      if (networkPiece.MarkedTargetId is not null && piecesByNetworkId.TryGetValue(networkPiece.MarkedTargetId, out Piece markedTarget))
      {
        piece.MarkedTarget = markedTarget;
      }
    }
    pieceSetup.RefreshOccupancy();
  }

  private void ApplyOnlineImprovements(IReadOnlyList<NetworkImprovement> improvements)
  {
    _roads.Clear();
    _barricades.Clear();
    _mines.Clear();
    foreach (NetworkImprovement improvement in improvements ?? [])
    {
      if (improvement.Type == "Road") _roads[(improvement.X, improvement.Y)] = (improvement.Owner ?? NetworkTeam.Neutral).ToTeamName();
      else if (improvement.Type == "Barrier") _barricades[(improvement.X, improvement.Y)] = improvement.Health;
      else if (improvement.Type == "Mine" && improvement.Owner is NetworkTeam owner)
      {
        _mines[(improvement.X, improvement.Y)] = owner.ToTeamName();
      }
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
    float adjusted = MathF.Round(multiplier + adjustment, 1);
    return float.IsFinite(adjusted) ? adjusted : multiplier;
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

        if (requiredOwner.HasValue && GetSquareOwner((position.x + x, position.y + y)) != requiredOwner.Value)
        {
          return false;
        }
      }
    }

    return pieceSetup.IsFootprintClear(definition, position, ignoredPiece);
  }

  private bool CanPlaceMercenary((int x, int y) position)
  {
    if (!IsTraversableTerrainSquare(position) || GetSquareOwner(position).HasValue)
    {
      return false;
    }

    return pieceSetup.IsFootprintClear(PieceDefinitions.Mercenary, position);
  }

  private bool IsInTeamTerritory((int x, int y) position, TeamName team)
  {
    int arrayX = position.x - _board.MinX;
    int arrayY = position.y - _board.MinY;
    return IsBoardCell(arrayX, arrayY) && GetSquareOwner(position) == team;
  }

  private bool TryGetPurchasePlacementPreview(
    out PieceDefinition definition,
    out (int x, int y) targetPosition,
    out bool canPurchaseAtTarget
  )
  {
    definition = GetPurchasablePieces()[_selectedPurchaseIndex];
    targetPosition = default;
    canPurchaseAtTarget = false;

    if (!_isPurchaseMode || !IsOnlineLocalTurn())
    {
      return false;
    }

    MouseState mouse = Mouse.GetState();
    if (IsPointerOverPurchaseMenu(ToUiPoint(mouse.Position)))
    {
      return false;
    }

    Vector2 mouseWorld = Vector2.Transform(
      mouse.Position.ToVector2(),
      Matrix.Invert(CreateCameraTransform())
    );
    targetPosition = (
      (int)MathF.Floor(mouseWorld.X / 64f) + _board.MinX,
      (int)MathF.Floor(mouseWorld.Y / 64f) + _board.MinY
    );

    if (!IsBoardCell(targetPosition.x - _board.MinX, targetPosition.y - _board.MinY))
    {
      return false;
    }

    Team buyingTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);
    Piece targetPiece = pieceSetup.GetPieceAt(targetPosition);
    bool isNeutralMercenaryHire =
      _initialBuyPhase == null &&
      targetPiece?.Definition.Type == PieceType.Mercenary &&
      targetPiece.Team == TeamName.Neutral;
    bool isOpeningFarmPlacement = _initialBuyPhase?.IsFarmPlacementPhase == true && definition.Type == PieceType.Farm;
    bool hasEnoughGold = isOpeningFarmPlacement
      ? true
      : isNeutralMercenaryHire
      ? buyingTeam.Money >= PieceDefinitions.NeutralMercenaryHireCost
      : buyingTeam.Money >= GetUnitPrice(definition);
    bool isEligibleForPurchase =
      !(definition.Type == PieceType.Mercenary && _initialBuyPhase != null) &&
      (isNeutralMercenaryHire ||
       (definition.Type == PieceType.Mercenary
         ? CanPlaceMercenary(targetPosition)
         : CanPlacePiece(definition, targetPosition, Team.CurrentTurn)));

    canPurchaseAtTarget = isEligibleForPurchase && hasEnoughGold;
    return true;
  }

  private void DrawPurchasePlacementPreview(int cellSize)
  {
    if (!TryGetPurchasePlacementPreview(out PieceDefinition definition, out var targetPosition, out bool canPurchaseAtTarget))
    {
      return;
    }

    Rectangle footprint = new(
      (targetPosition.x - _board.MinX) * cellSize,
      (targetPosition.y - _board.MinY) * cellSize,
      definition.Size.x * cellSize,
      definition.Size.y * cellSize
    );
    Color outline = canPurchaseAtTarget
      ? Color.Lerp(UiTheme.GetTeamColour(Team.CurrentTurn), UiTheme.GoldBright, 0.4f)
      : UiTheme.Attack;
    Color fill = new(outline.R, outline.G, outline.B, canPurchaseAtTarget ? (byte)46 : (byte)30);
    Color border = new(outline.R, outline.G, outline.B, canPurchaseAtTarget ? (byte)190 : (byte)145);

    DrawWorldRectangle(footprint, fill, 0.134f);
    DrawWorldOutline(footprint, border, 0.135f);
  }

  private void DrawRoyalPlacementPreview(int cellSize)
  {
    if (_royalAwaitingPlacement is null)
    {
      return;
    }

    MouseState mouse = Mouse.GetState();
    if (GetStatusPanelBounds().Contains(ToUiPoint(mouse.Position)))
    {
      return;
    }

    Vector2 mouseWorld = Vector2.Transform(
      mouse.Position.ToVector2(),
      Matrix.Invert(CreateCameraTransform())
    );
    (int x, int y) targetPosition = (
      (int)MathF.Floor(mouseWorld.X / cellSize) + _board.MinX,
      (int)MathF.Floor(mouseWorld.Y / cellSize) + _board.MinY
    );
    if (!IsBoardCell(targetPosition.x - _board.MinX, targetPosition.y - _board.MinY))
    {
      return;
    }

    bool canPlace = CanPlacePiece(_royalAwaitingPlacement, targetPosition, _setupTeam);
    Rectangle footprint = new(
      (targetPosition.x - _board.MinX) * cellSize,
      (targetPosition.y - _board.MinY) * cellSize,
      _royalAwaitingPlacement.Size.x * cellSize,
      _royalAwaitingPlacement.Size.y * cellSize
    );
    Color outline = canPlace
      ? Color.Lerp(UiTheme.GetTeamColour(_setupTeam), UiTheme.GoldBright, 0.4f)
      : UiTheme.Attack;
    Color fill = new(outline.R, outline.G, outline.B, canPlace ? (byte)46 : (byte)30);
    Color border = new(outline.R, outline.G, outline.B, canPlace ? (byte)190 : (byte)145);

    DrawWorldRectangle(footprint, fill, 0.134f);
    DrawWorldOutline(footprint, border, 0.135f);
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
    UnitRule movementRule = GetEffectiveMovementRule(piece);
    bool hasPalaceSupport = GetSupportingPalace(piece) is not null;
    return MovementPathfinder.FindPaths(
      piece,
      destination => CanLandPieceAt(piece, destination, hasPalaceSupport),
      (from, destination) => CanTravelThroughPosition(piece, from, destination),
      destination => GetMovementCost(piece, destination),
      (from, to) => CrossesRiver(piece, from, to),
      movementRule,
      (from, destination) => GetMovementCost(piece, from, destination),
      destination => GetMovementRangeAt(piece, movementRule, destination),
      movementRule.MoveRange + (hasPalaceSupport ? 1 : 0)
    );
  }

  private Piece GetSupportingPalace(Piece piece) => piece.Definition.Type == PieceType.Palace
    ? null
    : pieceSetup.Pieces.FirstOrDefault(candidate => candidate.Team == piece.Team && candidate.AttachedTo is null &&
      candidate.Definition.Type == PieceType.Palace);

  private bool IsPalaceAssistedMovement(Piece piece, (int x, int y) from, (int x, int y) destination)
  {
    Piece palace = GetSupportingPalace(piece);
    return palace is not null && AbilityRules.MovesTowardPalace(
      GetEffectiveMovementRule(piece), from, destination,
      UnitRules.FromPieceDefinition(palace.Definition), palace.Position
    );
  }

  private int GetMovementRangeAt(Piece piece, UnitRule movementRule, (int x, int y) destination) =>
    movementRule.MoveRange + (IsPalaceAssistedMovement(piece, piece.Position, destination) ? 1 : 0);

  private UnitRule GetEffectiveMovementRule(Piece piece)
  {
    UnitRule rule = UnitRules.FromPieceDefinition(piece.Definition);
    Piece oxAttachment = pieceSetup.Pieces.FirstOrDefault(candidate =>
      candidate.AttachedTo == piece && candidate.Definition.Type == PieceType.Ox);
    if (oxAttachment is not null)
    {
      rule = rule with
      {
        MoveRange = rule.MoveRange + AbilityRules.GetAttachmentMovementBonus(oxAttachment.Definition.Type.ToString())
      };
    }
    if (IsTreasureCarrier(piece))
    {
      rule = rule with { MoveRange = Math.Max(1, rule.MoveRange - 1) };
    }

    return AbilityRules.CanUseCavalierFollowUpMove(
      piece.Definition.Type.ToString(), piece.CavalierFollowUpMoveAvailable)
      ? rule with { MoveRange = 2, MovePattern = RuleShape.Straight }
      : rule;
  }

  private static bool CanMoveThisTurn(Piece piece) => !piece.HasMovedThisTurn ||
    AbilityRules.CanUseCavalierFollowUpMove(piece.Definition.Type.ToString(), piece.CavalierFollowUpMoveAvailable);

  private bool TryGetMovementPathAt(
    Piece piece,
    (int x, int y) clickedSquare,
    out List<(int x, int y)> path
  )
  {
    if (!CanMoveThisTurn(piece))
    {
      path = null;
      return false;
    }

    Dictionary<(int x, int y), List<(int x, int y)>> paths = piece == _cachedSelectedPiece
      ? _cachedSelectedMovementPaths
      : GetMovementPaths(piece);
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

  private bool CanLandPieceAt(Piece piece, (int x, int y) destination, bool mayUsePalaceSupport)
  {
    if (piece.Definition.Type != PieceType.Elephant)
    {
      if (!IsFootprintOnBoard(piece.Definition, destination) ||
          OccupiedSquares(piece.Definition, destination).Any(_barricades.ContainsKey) ||
          (!mayUsePalaceSupport && OccupiedSquares(piece.Definition, destination).Any(_terrain.IsLake)))
      {
        return false;
      }
      return pieceSetup.IsFootprintClear(piece.Definition, destination, piece);
    }

    if (!IsFootprintOnBoard(piece.Definition, destination) ||
        OccupiedSquares(piece.Definition, destination).Any(_barricades.ContainsKey))
    {
      return false;
    }

    // Elephants trample enemies rather than stopping beside them.  They may finish on a
    // hostile footprint, but friendly units still block them like every other mover.
    return !pieceSetup.Pieces.Any(other =>
      other != piece &&
      other.AttachedTo != piece &&
      other.AttachedTo is null &&
      other.Definition.Type != PieceType.Farm &&
      other.Team == piece.Team &&
      FootprintsOverlap(piece.Definition, destination, other.Definition, other.Position));
  }

  private bool CanTravelThroughPosition(
    Piece piece,
    (int x, int y) from,
    (int x, int y) destination
  )
  {
    foreach ((int x, int y) position in PositionsBetween(from, destination))
    {
      foreach ((int x, int y) occupiedSquare in OccupiedSquares(piece.Definition, position))
      {
        bool ignoresTerrain = piece.Definition.Type == PieceType.Elephant ||
          IsPalaceAssistedMovement(piece, from, destination);
        bool terrainBlocks = !ignoresTerrain && !IsTraversableTerrainSquare(occupiedSquare);
        if (terrainBlocks || _barricades.ContainsKey(occupiedSquare) ||
            !IsBoardCell(occupiedSquare.x - _board.MinX, occupiedSquare.y - _board.MinY))
        {
          return false;
        }

        Piece blockingPiece = pieceSetup.GetPieceAt(occupiedSquare);
        if (blockingPiece == null || blockingPiece == piece)
        {
          continue;
        }

        if (blockingPiece.Definition.Type == PieceType.Farm)
        {
          continue;
        }

        if (piece.Definition.Type == PieceType.Elephant && blockingPiece.Team != piece.Team)
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
    return GetMovementCost(piece, piece.Position, destination);
  }

  private int GetMovementCost(Piece piece, (int x, int y) from, (int x, int y) destination)
  {
    if (piece.Definition.Type == PieceType.Elephant)
    {
      return 1;
    }
    int cost = 0;
    bool ignoresTerrain = IsPalaceAssistedMovement(piece, from, destination);
    foreach ((int x, int y) occupiedSquare in OccupiedSquares(piece.Definition, destination))
    {
      bool usesOwnedRoad = UsesRoad(piece.Team, occupiedSquare);
      if (_terrain.IsForest(occupiedSquare) && !usesOwnedRoad && !ignoresTerrain)
      {
        cost = Math.Max(cost, 2);
      }
      else if (usesOwnedRoad && !_terrain.IsForest(occupiedSquare))
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

  private bool UsesRoad(TeamName team, (int x, int y) position) =>
    _roads.TryGetValue(position, out TeamName owner) &&
    (owner == team || owner == TeamName.Neutral);

  private bool CrossesRiver(Piece piece, (int x, int y) from, (int x, int y) to)
  {
    if (piece.Definition.Type == PieceType.Elephant || IsPalaceAssistedMovement(piece, from, to))
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

  private HashSet<(int x, int y)> GetValidAttackHighlightSquares(
    Piece piece,
    Dictionary<(int x, int y), List<(int x, int y)>> movementPaths = null
  )
  {
    HashSet<(int x, int y)> highlightedSquares = [];
    bool engineerDemolition = piece.Definition.Type == PieceType.Engineer &&
      _selectedEngineerAbility == EngineerAbility.Demolish;
    if ((piece.HasAttackedThisTurn && !engineerDemolition) ||
        (piece.Definition.Type == PieceType.Elephant && piece.HasMovedThisTurn))
    {
      return highlightedSquares;
    }

    if (piece.Definition.Type == PieceType.Farm)
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

    if (piece.Definition.Type == PieceType.Elephant)
    {
      movementPaths ??= GetMovementPaths(piece);
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

    if (_treasurePosition is (int treasureX, int treasureY) treasurePosition && CanPickUpTreasure(piece, treasurePosition))
    {
      highlightedSquares.Add(treasurePosition);
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
        Piece pieceAtTarget = GetUnattachedPieceAt(targetPosition);
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

  private Piece GetUnattachedPieceAt((int x, int y) position, TeamName team) =>
    pieceSetup.GetUnattachedPieceAt(position, team);

  private Piece GetUnattachedPieceAt((int x, int y) position) =>
    pieceSetup.GetUnattachedPieceAt(position);

  private Piece GetUnattachedHostilePieceAt((int x, int y) position, TeamName team) =>
    pieceSetup.GetUnattachedHostilePieceAt(position, team);

  private bool HasAvailableAttack(Piece piece, IEnumerable<(int x, int y)> targets)
  {
    foreach ((int x, int y) targetPosition in targets)
    {
      if (piece.Definition.Type is PieceType.Elephant or PieceType.Engineer ||
          _barricades.ContainsKey(targetPosition) ||
          CanPickUpTreasure(piece, targetPosition) ||
          GetUnattachedHostilePieceAt(targetPosition, piece.Team) is not null)
      {
        return true;
      }
    }

    return false;
  }

  private void DrawAvailableUnitHighlights(int cellSize)
  {
    if (_screen != Screen.Playing || _initialBuyPhase is not null || _royalAwaitingPlacement is not null ||
        _movementAnimation is not null || !IsOnlineLocalTurn() || IsCpuTurn() ||
        _cachedActionTeam != Team.CurrentTurn)
    {
      return;
    }

    foreach ((Piece piece, (bool CanMove, bool CanAttack) actions) in _cachedUnitActions)
    {
      Rectangle bounds = GetPieceWorldBounds(piece, cellSize);
      if (!IsVisibleWorldBounds(bounds)) continue;
      bool canMove = actions.CanMove;
      bool canAttack = actions.CanAttack;

      if (canMove && piece != selectedPiece)
      {
        DrawWorldOutline(bounds, UiTheme.Move, 0.123f);
      }

      if (canAttack)
      {
        Rectangle attackBounds = bounds;
        attackBounds.Inflate(-5, -5);
        DrawWorldOutline(attackBounds, UiTheme.Attack, 0.124f);
      }
    }
  }

  // Highlight/path generation is deliberately kept out of Draw. The stamp catches
  // every state that can affect legal movement or attacks, including online snapshots.
  private void RefreshGameplayRenderCache()
  {
    int stamp = GetGameplayRenderCacheStamp();
    if (!_gameplayRenderCacheDirty && stamp == _gameplayRenderCacheStamp) return;

    _gameplayRenderCacheDirty = false;
    _gameplayRenderCacheStamp = stamp;
    _cachedSelectedPiece = null;
    _cachedSelectedMovementPaths = [];
    _cachedSelectedMovementSquares = [];
    _cachedSelectedAttackSquares = [];
    _cachedUnitActions.Clear();
    _cachedActionTeam = null;

    // CPU and remote turns never need local action affordances. This keeps rendering
    // work from contending with CPU planning and avoids showing unusable overlays.
    if (_screen != Screen.Playing || IsCpuTurn() || !IsOnlineLocalTurn() ||
        _initialBuyPhase is not null || _royalAwaitingPlacement is not null ||
        _movementAnimation is not null) return;

    foreach (Piece piece in pieceSetup.Pieces.Where(piece =>
      piece.Team == Team.CurrentTurn && piece.AttachedTo is null))
    {
      Dictionary<(int x, int y), List<(int x, int y)>> paths = CanMoveThisTurn(piece)
        ? GetMovementPaths(piece)
        : [];
      HashSet<(int x, int y)> attacks = GetValidAttackHighlightSquares(piece, paths);
      _cachedUnitActions[piece] = (paths.Count > 0, HasAvailableAttack(piece, attacks));

      if (piece == selectedPiece && CanActWithPiece(piece))
      {
        _cachedSelectedPiece = piece;
        _cachedSelectedMovementPaths = paths;
        _cachedSelectedMovementSquares = GetMovementHighlightSquares(piece, paths.Keys);
        _cachedSelectedAttackSquares = attacks;
      }
    }
    _cachedActionTeam = Team.CurrentTurn;
  }

  private int GetGameplayRenderCacheStamp()
  {
    HashCode hash = new();
    hash.Add((int)_screen); hash.Add((int)Team.CurrentTurn); hash.Add(selectedPiece);
    hash.Add((int)_selectedEngineerAbility); hash.Add(_initialBuyPhase is not null);
    hash.Add(_royalAwaitingPlacement is not null); hash.Add(_movementAnimation is not null);
    hash.Add(_board); hash.Add(_terrain); hash.Add((int)_gameMode); hash.Add(_playerCount);
    hash.Add(_treasurePosition); hash.Add(_treasureCarrierId);
    foreach (Piece piece in pieceSetup.Pieces)
    {
      hash.Add(piece); hash.Add(piece.Position); hash.Add((int)piece.Team);
      hash.Add(piece.AttachedTo); hash.Add((int)piece.AttachmentKind);
      hash.Add(piece.CurrentHealth); hash.Add(piece.HasMovedThisTurn); hash.Add(piece.HasAttackedThisTurn);
      hash.Add(piece.CavalierFollowUpMoveAvailable); hash.Add(piece.EngineerBuildsThisTurn);
      hash.Add(piece.MarkedTarget);
    }
    foreach (var road in _roads) hash.Add(road);
    foreach (var barricade in _barricades) hash.Add(barricade);
    foreach (var mine in _mines) hash.Add(mine);
    foreach (var bridge in _riverBridges) hash.Add(bridge);
    foreach (var lakeTile in _restoredLakeTiles) hash.Add(lakeTile);
    return hash.ToHashCode();
  }

  private static HashSet<(int x, int y)> GetMovementHighlightSquares(
    Piece piece, IEnumerable<(int x, int y)> destinations)
  {
    HashSet<(int x, int y)> squares = [];
    foreach ((int x, int y) destination in destinations)
    for (int y = 0; y < piece.Definition.Size.y; y++)
    for (int x = 0; x < piece.Definition.Size.x; x++) squares.Add((destination.x + x, destination.y + y));
    return squares;
  }

  private void SelectPiece(Piece piece, bool allowAttachedPiece = false)
  {
    if (piece.AttachedTo != null && !allowAttachedPiece)
    {
      return;
    }

    selectedPiece = piece;
    _gameplayRenderCacheDirty = true;
    Console.WriteLine($"Selected {selectedPiece.Team} {selectedPiece.Definition.Type}.");
  }

  private void InspectPieceAt(Point screenPosition, Vector2 worldPosition)
  {
    if (GetStatusPanelBounds().Contains(screenPosition) ||
        GetSelectedPiecePanelBounds().Contains(screenPosition) ||
        IsPointerOverPurchaseMenu(screenPosition) ||
        GetChessClockPanelBounds().Contains(screenPosition))
    {
      return;
    }

    const int cellSize = 64;
    (int x, int y) targetPosition = (
      (int)MathF.Floor(worldPosition.X / cellSize) + _board.MinX,
      (int)MathF.Floor(worldPosition.Y / cellSize) + _board.MinY
    );
    Piece piece = GetUnattachedPieceAt(targetPosition);
    if (piece is not null)
    {
      SelectPiece(piece);
    }
    else
    {
      selectedPiece = null;
    }
  }

  private bool CanActWithPiece(Piece piece) =>
    piece.Team == Team.CurrentTurn && IsOnlineLocalTurn() && !IsCpuTurn();

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

  private int GetAttackDamage(Piece attacker, Piece target) =>
    GetSharedLocalAttackDamage(attacker, target);

  private void ResolveDamage(Piece attacker, Piece target, int? damageOverride = null)
  {
    if (target is null || !CanSharedAttackDamage(attacker, target))
    {
      return;
    }

    Piece guard = pieceSetup.GetAttachedPiece(target, AttachmentKind.Guard);
    Piece damagedPiece = guard ?? target;
    Piece oxAttachment = pieceSetup.Pieces.FirstOrDefault(candidate =>
      candidate.AttachedTo == target && AbilityRules.SharesIncomingDamageWithHost(candidate.Definition.Type.ToString()));
    int unmitigatedDamage = damageOverride ?? GetAttackDamage(attacker, target);

    ApplyDamageToPiece(attacker, damagedPiece, unmitigatedDamage);
    if (oxAttachment is not null && oxAttachment != damagedPiece && pieceSetup.Pieces.Contains(oxAttachment))
    {
      ApplyDamageToPiece(attacker, oxAttachment, unmitigatedDamage);
    }

    foreach (Piece spy in pieceSetup.Pieces.Where(spy => spy.MarkedTarget == target))
    {
      spy.MarkedTarget = null;
    }
  }

  private void ApplyDamageToPiece(Piece attacker, Piece damagedPiece, int unmitigatedDamage)
  {
    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentPieceOfType(damagedPiece, PieceType.Baron, damagedPiece.Team),
      IsPieceInForest(damagedPiece),
      _terrain.ForestDamageReduction
    );
    damagedPiece.CurrentHealth -= damage;
    Console.WriteLine($"{attacker.Definition.Type} dealt {damage} damage to {damagedPiece.Definition.Type}.");
    HandlePieceDestroyed(damagedPiece, attacker.Team);
  }

  private void ResolveMineDamage(Piece target, TeamName mineOwner)
  {
    target.CurrentHealth -= AbilityRules.EngineerMineDamage;
    Console.WriteLine($"Mine dealt {AbilityRules.EngineerMineDamage} damage to {target.Definition.Type}.");
    HandlePieceDestroyed(target, mineOwner);
  }

  private void HandlePieceDestroyed(Piece damagedPiece, TeamName? attackingTeamName)
  {
    if (damagedPiece.CurrentHealth > 0)
    {
      return;
    }

    ApplySharedDeathExplosion(damagedPiece);
    DropTreasure(damagedPiece);

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
        _defeatedTeamRefundMultiplier,
        GetUnitPrice(damagedPiece.Definition)
      );
    }

    pieceSetup.RemovePiece(damagedPiece);
    if (damagedPiece.Definition.Category == PieceCategory.Royal && _gameMode == GameMode.Regicide)
    {
      if (attackingTeamName is TeamName winner && winner != damagedPiece.Team)
      {
        _winningTeam = winner;
        _screen = Screen.GameOver;
      }
    }
    else if (damagedPiece.Definition.Category == PieceCategory.Royal && _gameMode == GameMode.Escort)
    {
      RespawnEscortRoyal(damagedPiece);
    }
    else if (damagedPiece.Definition.Category == PieceCategory.Royal && _gameMode == GameMode.Plunder &&
      attackingTeamName is TeamName attacker && attacker != damagedPiece.Team)
    {
      ApplyPlunderRoyalKillPenalty(attacker);
    }
  }

  private void ApplyPlunderRoyalKillPenalty(TeamName attacker)
  {
    int score = Math.Max(0, _modeScores.GetValueOrDefault(attacker) - _plunderRoyalKillPenalty);
    _modeScores[attacker] = score;
    Console.WriteLine($"{UiText.GetTeamDisplayName(attacker)} lost {_plunderRoyalKillPenalty} Plunder point(s) for destroying a royal.");
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
    )
    { CurrentHealth = GetRoyalStartingHealth(defeatedRoyal.Definition) };
    pieceSetup.AddPiece(respawnedRoyal);
    Console.WriteLine($"{defeatedRoyal.Team}'s royal has respawned at the back line.");
  }

  private bool IsTreasureCarrier(Piece piece) =>
    _gameMode == GameMode.Plunder && !string.IsNullOrWhiteSpace(_treasureCarrierId) &&
    piece.NetworkId == _treasureCarrierId;

  private void DropTreasure(Piece piece)
  {
    if (!IsTreasureCarrier(piece))
    {
      return;
    }

    _treasureCarrierId = null;
    _treasurePosition = piece.Position;
    Console.WriteLine("The fallen carrier dropped the treasure.");
  }

  private bool TryUseSpecialAbility(
    Piece actor,
    (int x, int y) targetPosition,
    Piece targetPiece,
    KeyboardState keyboard
  )
  {
    if (!IsCampaignAbilityAllowed(actor.Team, actor.Definition.Type))
    {
      Console.WriteLine($"{actor.Definition.Type}'s ability is disabled for this campaign level.");
      return false;
    }
    bool engineerDemolition = actor.Definition.Type == PieceType.Engineer &&
      _selectedEngineerAbility == EngineerAbility.Demolish;
    if (actor.HasAttackedThisTurn && !engineerDemolition)
    {
      return false;
    }

    if (actor.Definition.Type == PieceType.Mercenary && targetPosition == actor.Position)
    {
      return TryFireMercenary(actor);
    }

    if (TryPickUpTreasure(actor, targetPosition, targetPiece))
    {
      return true;
    }

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

    if (actor.Definition.Type == PieceType.Engineer)
    {
      return TryUseEngineerAbility(actor, targetPosition, targetPiece);
    }

    if (actor.Definition.Type == PieceType.Guard &&
        targetPiece != null &&
        targetPiece.Team == actor.Team &&
        !IsTreasureCarrier(targetPiece) &&
        AbilityRules.CanGuardAttach(
          UnitRules.FromPieceDefinition(actor.Definition),
          UnitRules.FromPieceDefinition(targetPiece.Definition),
          actor.AttachedTo != null,
          pieceSetup.GetAttachedPiece(targetPiece, AttachmentKind.Guard) != null
        ) &&
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
        !IsTreasureCarrier(targetPiece) &&
        AbilityRules.CanOxAttach(
          UnitRules.FromPieceDefinition(actor.Definition),
          UnitRules.FromPieceDefinition(targetPiece.Definition),
          actor.AttachedTo != null,
          pieceSetup.Pieces.Any(candidate =>
            candidate.AttachedTo == targetPiece && candidate.Definition.Type == PieceType.Ox)
        ) &&
        Actions.CanAttackSquare(actor, targetPosition))
    {
      pieceSetup.Attach(targetPiece, actor, AttachmentKind.Carried);
      CompleteAction();
      return true;
    }

    return false;
  }

  private bool TryPickUpTreasure(Piece actor, (int x, int y) targetPosition, Piece targetPiece)
  {
    if (targetPiece is not null || !CanPickUpTreasure(actor, targetPosition))
    {
      return false;
    }

    _treasureCarrierId = actor.NetworkId;
    _treasurePosition = null;
    actor.HasAttackedThisTurn = true;
    Console.WriteLine($"{actor.Definition.Type} picked up the treasure.");
    CompleteAction();
    return true;
  }

  private bool CanPickUpTreasure(Piece actor, (int x, int y) position)
  {
    return _gameMode == GameMode.Plunder && _treasurePosition == position &&
      string.IsNullOrWhiteSpace(_treasureCarrierId) && actor.AttachedTo is null &&
      !actor.HasAttackedThisTurn && actor.Definition.Size == (1, 1) &&
      actor.Definition.Category != PieceCategory.Royal &&
      Math.Abs(actor.Position.x - position.x) + Math.Abs(actor.Position.y - position.y) == 1;
  }

  private bool TryFireMercenary(Piece mercenary)
  {
    if (mercenary.Team != Team.CurrentTurn)
    {
      return false;
    }

    mercenary.Team = TeamName.Neutral;
    mercenary.HasMovedThisTurn = true;
    mercenary.HasAttackedThisTurn = true;
    Console.WriteLine("Mercenary fired and left neutral in No-Man's-Land.");
    CompleteAction();
    return true;
  }

  private bool TryUseEngineerAbility(
    Piece engineer,
    (int x, int y) targetPosition,
    Piece targetPiece
  )
  {
    bool demolition = _selectedEngineerAbility == EngineerAbility.Demolish;
    if ((!demolition && engineer.EngineerBuildsThisTurn >= 2) ||
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
      EngineerAbility.Demolish => targetPiece is null && TryDemolishImprovement(engineer, targetPosition),
      _ => false
    };
    if (!improvementChanged)
    {
      return false;
    }

    if (!demolition)
    {
      engineer.EngineerBuildsThisTurn++;
      engineer.HasAttackedThisTurn = engineer.EngineerBuildsThisTurn >= 2;
    }
    CompleteAction();
    return true;
  }

  private bool CanUseEngineerAbilityAt(
    Piece engineer,
    (int x, int y) targetPosition,
    Piece targetPiece
  )
  {
    bool demolition = _selectedEngineerAbility == EngineerAbility.Demolish;
    if ((!demolition && engineer.EngineerBuildsThisTurn >= 2) ||
        !Actions.CanAttackSquare(engineer, targetPosition) ||
        !IsBoardCell(targetPosition.x - _board.MinX, targetPosition.y - _board.MinY))
    {
      return false;
    }

    return _selectedEngineerAbility switch
    {
      EngineerAbility.Road => targetPiece is null && !IsEngineeringImprovementAt(targetPosition) &&
        IsTraversableTerrainSquare(targetPosition),
      EngineerAbility.Barrier => targetPiece is null &&
        !IsEngineeringImprovementAt(targetPosition) && IsTraversableTerrainSquare(targetPosition),
      EngineerAbility.Mine => targetPiece is null &&
        !IsEngineeringImprovementAt(targetPosition) && IsTraversableTerrainSquare(targetPosition),
      EngineerAbility.Demolish => targetPiece is null && IsEngineeringImprovementAt(targetPosition),
      _ => false
    };
  }

  private bool TryBuildRoad(Piece engineer, (int x, int y) targetPosition, Piece targetPiece)
  {
    if (targetPiece != null || IsEngineeringImprovementAt(targetPosition))
    {
      return false;
    }

    if (!IsTraversableTerrainSquare(targetPosition))
    {
      return false;
    }

    _roads[targetPosition] = engineer.Team;
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

    _barricades[targetPosition] = AbilityRules.EngineerBarrierHealth;
    Console.WriteLine($"Engineer built a {AbilityRules.EngineerBarrierHealth} HP barrier.");
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
      _restoredLakeTiles.Remove(targetPosition);
    if (removed)
    {
      Console.WriteLine("Engineer demolished an improvement.");
    }

    return removed;
  }

  private bool IsEngineeringImprovementAt((int x, int y) position)
  {
    return _roads.ContainsKey(position) ||
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
        if (candidate.Team == piece.Team && candidate != piece && candidate.AttachedTo == null && !IsTreasureCarrier(candidate) &&
            AbilityRules.IsEmissaryCompanion(
              UnitRules.FromPieceDefinition(candidate.Definition), piece.Position, candidate.Position))
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
        companion.HasMovedThisTurn = true;
      }
    }
  }

  private void BeginMovementAnimation(Piece piece, List<(int x, int y)> path)
  {
    _movementAnimation = new MovementAnimation
    {
      Piece = piece,
      Path = path,
      StartPosition = piece.Position
    };
  }

  private void BeginOnlineMovementAnimation(
    IReadOnlyList<NetworkPiece> pieces,
    IReadOnlyDictionary<string, (int x, int y)> previousPositions
  )
  {
    foreach (NetworkPiece networkPiece in pieces)
    {
      if (!previousPositions.TryGetValue(networkPiece.Id, out (int x, int y) previous) ||
          previous == (networkPiece.X, networkPiece.Y))
      {
        continue;
      }

      Piece piece = pieceSetup.Pieces.FirstOrDefault(candidate => candidate.NetworkId == networkPiece.Id);
      if (piece is null)
      {
        continue;
      }

      _movementAnimation = new MovementAnimation
      {
        Piece = piece,
        StartPosition = previous,
        Path = BuildOnlineAnimationPath(piece, previous, (networkPiece.X, networkPiece.Y)),
        IsAuthoritativeSnapshot = true
      };
      return;
    }
  }

  private static List<(int x, int y)> BuildOnlineAnimationPath(
    Piece piece,
    (int x, int y) from,
    (int x, int y) to
  )
  {
    List<(int x, int y)> path = [];
    (int x, int y) current = from;
    bool canMoveDiagonally = piece.Definition.Movement.shape is
      Shape.Any or Shape.AbsoluteStraightOrDiagonal or Shape.ForwardOrForwardDiagonal;

    while (canMoveDiagonally && current.x != to.x && current.y != to.y)
    {
      current.x += Math.Sign(to.x - current.x);
      current.y += Math.Sign(to.y - current.y);
      path.Add(current);
    }

    while (current.x != to.x)
    {
      current.x += Math.Sign(to.x - current.x);
      path.Add(current);
    }
    while (current.y != to.y)
    {
      current.y += Math.Sign(to.y - current.y);
      path.Add(current);
    }
    return path;
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
    if (completedAnimation.IsAuthoritativeSnapshot)
    {
      return;
    }

    Piece movedPiece = completedAnimation.Piece;
    (int x, int y) destination = completedAnimation.Path[^1];
    bool usesCavalierFollowUpMove = movedPiece.CavalierFollowUpMoveAvailable;

    if (movedPiece.Definition.Type == PieceType.Elephant &&
        AttackUnitsMovedOver(movedPiece, completedAnimation.Path))
    {
      movedPiece.HasAttackedThisTurn = true;
    }

    MovePieceWithCompanions(movedPiece, destination);
    if (usesCavalierFollowUpMove)
    {
      movedPiece.CavalierFollowUpMoveAvailable = false;
    }
    TriggerMinesAlongMovement(movedPiece, completedAnimation.Path);

    if (_screen == Screen.GameOver || !pieceSetup.Pieces.Contains(movedPiece))
    {
      selectedPiece = null;
      return;
    }

    Console.WriteLine($"Moved {movedPiece.Definition.Type} to ({destination.x}, {destination.y}).");
    if (TryDeliverTreasure(movedPiece))
    {
      selectedPiece = null;
      if (_screen == Screen.GameOver)
      {
        return;
      }
    }
    if (HasEscortVictory(movedPiece))
    {
      _winningTeam = movedPiece.Team;
      _screen = Screen.GameOver;
      selectedPiece = null;
      return;
    }

    selectedPiece = null;
    CompleteAction();
  }

  private bool HasEscortVictory(Piece piece)
  {
    if (_gameMode != GameMode.Escort || piece.Definition.Category != PieceCategory.Royal)
    {
      return false;
    }

    return piece.OccupiedSquares().Any(square =>
      MatchRules.IsOnEnemyBackEdge(_board, piece.Team.ToNetworkTeam(), square));
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

      bool wasMovedOver = AbilityRules.PathOverlapsUnit(
        UnitRules.FromPieceDefinition(attacker.Definition),
        path,
        UnitRules.FromPieceDefinition(crossedPiece.Definition),
        crossedPiece.Position.x,
        crossedPiece.Position.y
      );
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

  private void PerformPiercingAttack(Piece attacker, (int x, int y) targetPosition)
  {
    UnitRule ballistaRule = UnitRules.FromPieceDefinition(attacker.Definition);
    foreach ((int x, int y) position in AbilityRules.GetPiercingRay(
      ballistaRule,
      attacker.Position.x,
      attacker.Position.y,
      targetPosition.x,
      targetPosition.y
    ))
    {
      if (!IsBoardCell(position.x - _board.MinX, position.y - _board.MinY))
      {
        break;
      }
      if (_barricades.ContainsKey(position))
      {
        DamageBarricade(attacker, position);
        break;
      }
      if (_terrain.IsForest(position)) break;
      Piece target = pieceSetup.GetPieceAt(position);
      if (target?.Definition.Type == PieceType.Farm)
      {
        continue;
      }
      if (target != null && target.Team != attacker.Team) ResolveDamage(attacker, target);
    }
  }

  private bool TryDeliverTreasure(Piece piece)
  {
    if (_gameMode != GameMode.Plunder || !IsTreasureCarrier(piece) ||
        !IsInTeamTerritory(piece.Position, piece.Team))
    {
      return false;
    }

    int score = Math.Clamp(
      _modeScores.GetValueOrDefault(piece.Team) + _plunderDeliveryScore,
      0,
      _plunderWinScore
    );
    _modeScores[piece.Team] = score;
    _treasureCarrierId = null;
    _treasurePosition = MatchRules.GetTreasureSpawn(_board);
    Console.WriteLine($"{UiText.GetTeamDisplayName(piece.Team)} delivered the treasure for {_plunderDeliveryScore} points.");

    if (score < _plunderWinScore)
    {
      return true;
    }

    _winningTeam = piece.Team;
    _screen = Screen.GameOver;
    return true;
  }

  private void PerformBombardAttack(Piece attacker, Piece target)
  {
    HashSet<Piece> affectedPieces = pieceSetup.Pieces
      .Where(piece => piece != attacker && piece.AttachedTo is null && piece.OccupiedSquares().Any(square =>
        target.OccupiedSquares().Any(targetSquare =>
          Math.Abs(square.x - targetSquare.x) <= 1 && Math.Abs(square.y - targetSquare.y) <= 1)))
      .ToHashSet();

    foreach (Piece affectedPiece in affectedPieces.ToArray())
    {
      if (pieceSetup.Pieces.Contains(affectedPiece))
      {
        ResolveDamage(attacker, affectedPiece, 10);
      }
    }
  }

  private void DamageBarricade(Piece attacker, (int x, int y) position)
  {
    int damage = AbilityRules.GetBaseAttack(
      UnitRules.FromPieceDefinition(attacker.Definition),
      attacker.CurrentHealth
    );
    if (HasAdjacentPieceOfType(attacker, PieceType.Baron, attacker.Team))
    {
      damage += CombatRules.BaronDamageBonus;
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
    UnitRule rule = UnitRules.FromPieceDefinition(attacker.Definition);
    if (attacker.Definition.Type == PieceType.Catapult) return true;
    return LineOfSightRules.HasClearAttackPath(
      rule,
      attacker.OccupiedSquares(),
      targetPosition,
      _terrain.IsForest,
      _barricades.ContainsKey,
      square =>
      {
        Piece blockingPiece = pieceSetup.GetPieceAt(square);
        return blockingPiece is not null && blockingPiece.Definition.Type != PieceType.Farm &&
          !(attacker.Definition.Type == PieceType.Princess && blockingPiece.Team == attacker.Team);
      }
    );
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
    if (attacker.Definition.AttackPattern != Shape.Line)
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

  private TeamName? GetSquareOwner((int x, int y) position)
  {
    NetworkTeam? owner = _campaignTestPlay && _campaignTerritories is not null
      ? _campaignTerritories.GetSquareOwner(_board, position, _playerCount)
      : MatchRules.GetSquareOwner(_board, _gameMode.ToString(), position, _playerCount);
    return owner?.ToTeamName();
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

  private Rectangle GetPurchaseUnitListToggleBounds()
  {
    Rectangle panel = GetPurchasePanelBounds();
    return new Rectangle(panel.X, panel.Bottom + UiTheme.SpaceSm, panel.Width, purchaseUnitListHeaderHeight);
  }

  private Rectangle GetPurchaseUnitListBounds(out int columnCount, out int rowHeight)
  {
    Rectangle toggle = GetPurchaseUnitListToggleBounds();
    int unitCount = GetPurchasablePieces().Count;
    if (unitCount == 0)
    {
      columnCount = 1;
      rowHeight = purchaseUnitListMinimumRowHeight;
      return toggle;
    }

    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    int availableHeight = Math.Max(1, viewport.Bottom - toggle.Y - UiTheme.SpaceLg);
    columnCount = 4;
    for (int columns = 2; columns <= 4; columns++)
    {
      int rowCount = (unitCount + columns - 1) / columns;
      int minimumHeight = purchaseUnitListHeaderHeight + purchaseUnitListPadding * 2 +
        rowCount * purchaseUnitListMinimumRowHeight + (rowCount - 1) * purchaseUnitListGap;
      if (minimumHeight <= availableHeight)
      {
        columnCount = columns;
        break;
      }
    }

    int rows = (unitCount + columnCount - 1) / columnCount;
    int availableRowHeight = (availableHeight - purchaseUnitListHeaderHeight - purchaseUnitListPadding * 2 -
      (rows - 1) * purchaseUnitListGap) / rows;
    rowHeight = Math.Clamp(availableRowHeight, purchaseUnitListMinimumRowHeight, purchaseUnitListMaximumRowHeight);
    int height = purchaseUnitListHeaderHeight + purchaseUnitListPadding * 2 +
      rows * rowHeight + (rows - 1) * purchaseUnitListGap;
    return new Rectangle(toggle.X, toggle.Y, toggle.Width, height);
  }

  private Rectangle GetPurchaseUnitListItemBounds(int index)
  {
    Rectangle list = GetPurchaseUnitListBounds(out int columnCount, out int rowHeight);
    int itemWidth = (list.Width - purchaseUnitListPadding * 2 - (columnCount - 1) * purchaseUnitListGap) / columnCount;
    int row = index / columnCount;
    int column = index % columnCount;
    return new Rectangle(
      list.X + purchaseUnitListPadding + column * (itemWidth + purchaseUnitListGap),
      list.Y + purchaseUnitListHeaderHeight + purchaseUnitListPadding + row * (rowHeight + purchaseUnitListGap),
      itemWidth,
      rowHeight
    );
  }

  private bool IsPointerOverPurchaseMenu(Point position)
  {
    if (GetPurchasePanelBounds().Contains(position) || GetPurchaseUnitListToggleBounds().Contains(position))
    {
      return true;
    }

    return _isPurchaseUnitListExpanded && GetPurchaseUnitListBounds(out _, out _).Contains(position);
  }

  private bool HandlePurchasePanelClick(Point mousePosition)
  {
    Rectangle panel = GetPurchasePanelBounds();
    Rectangle unitListToggle = GetPurchaseUnitListToggleBounds();
    bool clickedExpandedUnitList = _isPurchaseUnitListExpanded &&
      GetPurchaseUnitListBounds(out _, out _).Contains(mousePosition);
    if (!panel.Contains(mousePosition) && !unitListToggle.Contains(mousePosition) && !clickedExpandedUnitList)
    {
      return false;
    }

    if (unitListToggle.Contains(mousePosition))
    {
      _isPurchaseUnitListExpanded = !_isPurchaseUnitListExpanded;
    }
    else if (clickedExpandedUnitList)
    {
      IReadOnlyList<PieceDefinition> purchasablePieces = GetPurchasablePieces();
      for (int index = 0; index < purchasablePieces.Count; index++)
      {
        if (GetPurchaseUnitListItemBounds(index).Contains(mousePosition))
        {
          TrySelectPurchaseIndex(index);
          break;
        }
      }
    }
    else if (GetPreviousPurchaseButtonBounds().Contains(mousePosition))
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
      CycleEngineerAbility(-1);
    }
    else if (GetEngineerNextButtonBounds().Contains(mousePosition))
    {
      CycleEngineerAbility(1);
    }

    return true;
  }

  private bool HandleMercenaryPanelClick(Point mousePosition)
  {
    if (selectedPiece?.Definition.Type != PieceType.Mercenary ||
        !GetSelectedPiecePanelBounds().Contains(mousePosition))
    {
      return false;
    }

    if (GetMercenaryFireButtonBounds().Contains(mousePosition) && CanFireSelectedMercenary())
    {
      bool fired = _onlineClient is null
        ? TryUseSpecialAbility(selectedPiece, selectedPiece.Position, selectedPiece, Keyboard.GetState())
        : TrySendOnlineSpecialAbility(selectedPiece, selectedPiece.Position, selectedPiece);
      if (fired)
      {
        selectedPiece = null;
      }
    }

    return true;
  }

  private void CycleEngineerAbility(int direction)
  {
    EngineerAbility[] abilities = [EngineerAbility.Road, EngineerAbility.Barrier, EngineerAbility.Mine, EngineerAbility.Demolish];
    int selectedIndex = Array.IndexOf(abilities, _selectedEngineerAbility);
    if (selectedIndex < 0) selectedIndex = 0;
    _selectedEngineerAbility = abilities[(selectedIndex + direction + abilities.Length) % abilities.Length];
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

  private void DrawWorldLine(Vector2 start, Vector2 end, Color colour, float layerDepth, float thickness = 3f)
  {
    Vector2 delta = end - start;
    float length = delta.Length();
    if (length < 0.5f) return;
    _spriteBatch.Draw(
      _pixel,
      start,
      null,
      colour,
      MathF.Atan2(delta.Y, delta.X),
      Vector2.Zero,
      new Vector2(length, thickness),
      SpriteEffects.None,
      layerDepth
    );
  }

  private void DrawSpyMarkIndicators(int cellSize)
  {
    foreach (Piece spy in pieceSetup.Pieces.Where(piece => piece.Definition.Type == PieceType.Spy && piece.MarkedTarget is not null))
    {
      Piece target = spy.MarkedTarget;
      if (!pieceSetup.Pieces.Contains(target) || spy.AttachedTo is not null) continue;
      Rectangle spyBounds = GetPieceWorldBounds(spy, cellSize);
      Rectangle targetBounds = GetPieceWorldBounds(target, cellSize);
      if (!IsVisibleWorldBounds(Rectangle.Union(spyBounds, targetBounds))) continue;
      DrawWorldLine(
        new Vector2(spyBounds.Center.X, spyBounds.Center.Y),
        new Vector2(targetBounds.Center.X, targetBounds.Center.Y),
        UiTheme.GoldBright,
        0.13f,
        2f
      );
      targetBounds.Inflate(-3, -3);
      DrawWorldOutline(targetBounds, UiTheme.GoldBright, 0.131f);
      Rectangle marker = new(targetBounds.Right - 14, targetBounds.Y + 3, 11, 11);
      DrawWorldRectangle(marker, UiTheme.GoldBright, 0.132f);
      DrawWorldOutline(marker, UiTheme.Shadow, 0.133f);
    }
  }

  private void DrawPlanningMarks(int cellSize)
  {
    foreach (PlanningMark mark in _planningMarks)
    {
      Rectangle startBounds = new(
        (mark.Start.x - _board.MinX) * cellSize,
        (mark.Start.y - _board.MinY) * cellSize,
        cellSize,
        cellSize
      );
      Rectangle markBounds = mark.End.HasValue
        ? Rectangle.Union(startBounds, new Rectangle(
          (mark.End.Value.x - _board.MinX) * cellSize,
          (mark.End.Value.y - _board.MinY) * cellSize,
          cellSize,
          cellSize
        ))
        : startBounds;
      if (!IsVisibleWorldBounds(markBounds)) continue;
      DrawPlanningMark(mark.Start, mark.End, mark.Path, cellSize, UiTheme.GoldBright, 0.136f);
    }

    if (_planningGestureActive && _planningStart is (int x, int y) start)
    {
      Vector2 mouseWorld = Vector2.Transform(Mouse.GetState().Position.ToVector2(), Matrix.Invert(CreateCameraTransform()));
      if (TryGetBoardPosition(mouseWorld, out (int x, int y) end))
      {
        DrawPlanningMark(start, start == end ? null : end, _planningPath, cellSize, UiTheme.TextPrimary, 0.137f);
      }
    }
  }

  private void DrawPlanningMark(
    (int x, int y) start,
    (int x, int y)? end,
    PlanningPath path,
    int cellSize,
    Color colour,
    float depth
  )
  {
    if (!end.HasValue)
    {
      Rectangle highlight = new(
        (start.x - _board.MinX) * cellSize + 4,
        (start.y - _board.MinY) * cellSize + 4,
        cellSize - 8,
        cellSize - 8
      );
      DrawWorldOutline(highlight, colour, depth);
      return;
    }

    Vector2 first = GetBoardSquareCenter(start, cellSize);
    Vector2 last = GetBoardSquareCenter(end.Value, cellSize);
    if (path == PlanningPath.Straight && first.X != last.X && first.Y != last.Y)
    {
      Vector2 corner = new(last.X, first.Y);
      DrawWorldLine(first, corner, colour, depth, 3f);
      DrawWorldLine(corner, last, colour, depth, 3f);
    }
    else
    {
      DrawWorldLine(first, last, colour, depth, 3f);
    }

    Vector2 direction = Vector2.Normalize(last - (path == PlanningPath.Straight && first.X != last.X && first.Y != last.Y
      ? new Vector2(last.X, first.Y)
      : first));
    if (float.IsNaN(direction.X)) return;
    Vector2 perpendicular = new(-direction.Y, direction.X);
    DrawWorldLine(last, last - direction * 12f + perpendicular * 7f, colour, depth + 0.001f, 3f);
    DrawWorldLine(last, last - direction * 12f - perpendicular * 7f, colour, depth + 0.001f, 3f);
  }

  private void DrawWorldPieceText(Matrix cameraTransform, int cellSize)
  {
    // Piece information is a screen-space overlay, so it always reads upright.
    const float textRotation = 0f;
    foreach (Piece piece in pieceSetup.Pieces.OrderBy(piece => piece.Definition.Type == PieceType.Farm ? 0 : 1))
    {
      if (piece.AttachedTo != null)
      {
        if (!IsVisibleWorldBounds(GetAttachmentBadgeWorldBounds(piece, cellSize))) continue;
        Rectangle badgeBounds = GetScreenBounds(GetAttachmentBadgeWorldBounds(piece, cellSize), cameraTransform);
        DrawRotatedWorldText(
          UiText.BuildPieceLabel(piece.Definition),
          new Vector2(badgeBounds.Center.X, badgeBounds.Center.Y),
          0.54f,
          Vector2.One * 0.5f,
          textRotation,
          Matrix.Identity
        );
        int healthWidth = Math.Max(1, badgeBounds.Width - (int)(6 * _zoom));
        int healthHeight = Math.Max(2, (int)(3 * _zoom));
        Rectangle healthBounds = new(
          badgeBounds.X + (int)(3 * _zoom),
          badgeBounds.Bottom - (int)(5 * _zoom),
          healthWidth,
          healthHeight
        );
        DrawWorldRectangle(healthBounds, UiTheme.Shadow, 0.121f);
        DrawWorldRectangle(
          new Rectangle(
            healthBounds.X,
            healthBounds.Y,
            (int)(healthBounds.Width * MathHelper.Clamp(piece.CurrentHealth / (float)Math.Max(1, piece.Definition.Health), 0f, 1f)),
            healthBounds.Height
          ),
          UiTheme.Health,
          0.122f
        );
        continue;
      }

      Rectangle pieceBounds = GetPieceWorldBounds(piece, cellSize);
      if (!IsVisibleWorldBounds(pieceBounds)) continue;
      Rectangle screenBounds = GetScreenBounds(pieceBounds, cameraTransform);
      Vector2 screenCenter = new(screenBounds.Center.X, screenBounds.Center.Y);
      DrawRotatedWorldText(
        UiText.BuildPieceLabel(piece.Definition),
        screenCenter,
        1f,
        Vector2.One * 0.5f,
        textRotation,
        Matrix.Identity
      );
      DrawRotatedWorldText(
        $"HP {piece.CurrentHealth}",
        new Vector2(screenBounds.Center.X, screenBounds.Y + 6 * _zoom),
        0.6f,
        new Vector2(0.5f, 0f),
        textRotation,
        Matrix.Identity
      );

      int healthBarWidth = Math.Max(1, screenBounds.Width - (int)(16 * _zoom));
      int healthBarHeight = Math.Max(2, (int)(5 * _zoom));
      Rectangle healthBarBounds = new(
        screenBounds.X + (int)(8 * _zoom),
        screenBounds.Bottom - (int)(12 * _zoom),
        healthBarWidth,
        healthBarHeight
      );
      DrawWorldRectangle(healthBarBounds, UiTheme.Shadow, 0.121f);
      DrawWorldRectangle(
        new Rectangle(
          healthBarBounds.X,
          healthBarBounds.Y,
          (int)(healthBarBounds.Width * MathHelper.Clamp(piece.CurrentHealth / (float)Math.Max(1, piece.Definition.Health), 0f, 1f)),
          healthBarBounds.Height
        ),
        UiTheme.Health,
        0.122f
      );
    }
  }

  private static Rectangle GetScreenBounds(Rectangle worldBounds, Matrix transform)
  {
    Vector2[] corners =
    [
      Vector2.Transform(new Vector2(worldBounds.Left, worldBounds.Top), transform),
      Vector2.Transform(new Vector2(worldBounds.Right, worldBounds.Top), transform),
      Vector2.Transform(new Vector2(worldBounds.Left, worldBounds.Bottom), transform),
      Vector2.Transform(new Vector2(worldBounds.Right, worldBounds.Bottom), transform)
    ];
    float left = corners.Min(corner => corner.X);
    float right = corners.Max(corner => corner.X);
    float top = corners.Min(corner => corner.Y);
    float bottom = corners.Max(corner => corner.Y);
    int roundedLeft = (int)MathF.Floor(left);
    int roundedTop = (int)MathF.Floor(top);
    return new Rectangle(
      roundedLeft,
      roundedTop,
      Math.Max(1, (int)MathF.Ceiling(right) - roundedLeft),
      Math.Max(1, (int)MathF.Ceiling(bottom) - roundedTop)
    );
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

  private Rectangle GetAttachmentBadgeWorldBounds(Piece attachment, int cellSize)
  {
    Rectangle hostBounds = GetPieceWorldBounds(attachment.AttachedTo, cellSize);
    int size = Math.Clamp(Math.Min(hostBounds.Width, hostBounds.Height) / 2, 28, 36);
    const int inset = 4;
    return attachment.AttachmentKind switch
    {
      AttachmentKind.Guard => new Rectangle(hostBounds.Right - size - inset, hostBounds.Bottom - size - inset, size, size),
      _ => new Rectangle(hostBounds.Right - size - inset, hostBounds.Y + inset, size, size)
    };
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
       piece.AttachmentKind == AttachmentKind.Carried);
    if (!followsAnimatedPiece)
    {
      return new Vector2(piece.Position.x, piece.Position.y);
    }

    float pathProgress = _movementAnimation.ElapsedSeconds / MovementAnimation.SecondsPerStep;
    int segmentIndex = Math.Min((int)pathProgress, _movementAnimation.Path.Count - 1);
    float segmentProgress = MathHelper.Clamp(pathProgress - segmentIndex, 0f, 1f);
    (int x, int y) segmentStart = segmentIndex == 0
      ? _movementAnimation.StartPosition
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
    PieceDefinition definition = GetPurchasablePieces()[_selectedPurchaseIndex];
    Color teamColour = UiTheme.GetTeamColour(Team.CurrentTurn);

    DrawPanel(panel, UiTheme.Panel, _isPurchaseMode ? UiTheme.Gold : UiTheme.PanelBorder);
    _ui.Text(_initialBuyPhase == null ? "PURCHASE PIECE" : "INITIAL PURCHASE", new Vector2(content.X, content.Y), UiTheme.Gold);
    _ui.Divider(content, content.Y + 30);

    Rectangle previewBounds = new(content.X, content.Y + 46, panel.Height < 500 ? 76 : 84, panel.Height < 500 ? 76 : 84);
    string label = UiText.BuildPieceLabel(definition);
    _ui.PiecePreview(previewBounds, teamColour, label);
    float detailX = previewBounds.Right + UiTheme.SpaceMd;
    _ui.Text(definition.DisplayName.ToUpperInvariant(), new Vector2(detailX, previewBounds.Y + 4), UiTheme.TextPrimary);
    _ui.Text(definition.Category.ToString(), new Vector2(detailX, previewBounds.Y + 31), UiTheme.TextMuted, 0.82f);
    bool isOpeningFarmPlacement = _initialBuyPhase?.IsFarmPlacementPhase == true && definition.Type == PieceType.Farm;
    _ui.Text(
      isOpeningFarmPlacement ? "FREE OPENING FARM" : $"{GetUnitPrice(definition)} GOLD",
      new Vector2(detailX, previewBounds.Y + 56),
      UiTheme.Gold,
      0.84f
    );
    const int statHeight = 44;
    const int statRowGap = 4;
    Rectangle statGrid = new(content.X, previewBounds.Bottom + UiTheme.SpaceLg, content.Width, statHeight * 3 + statRowGap * 2);
    Rectangle leftColumn = UiLayout.HorizontalSlot(statGrid, 2, 0, UiTheme.SpaceSm);
    Rectangle rightColumn = UiLayout.HorizontalSlot(statGrid, 2, 1, UiTheme.SpaceSm);
    const float purchaseStatFontScale = 1.50f;
    _ui.StatBlock(new Rectangle(leftColumn.X, statGrid.Y, leftColumn.Width, statHeight), "HEALTH", definition.Health.ToString(), UiTheme.Health, purchaseStatFontScale);
    _ui.StatBlock(new Rectangle(rightColumn.X, statGrid.Y, rightColumn.Width, statHeight), "ATTACK", definition.Attack.ToString(), UiTheme.Attack, purchaseStatFontScale);
    _ui.StatBlock(new Rectangle(leftColumn.X, statGrid.Y + statHeight + statRowGap, leftColumn.Width, statHeight), "MOVE RANGE", UiText.FormatAction(definition.Movement), UiTheme.Move, purchaseStatFontScale);
    _ui.StatBlock(new Rectangle(rightColumn.X, statGrid.Y + statHeight + statRowGap, rightColumn.Width, statHeight), "ATTACK RANGE", UiText.FormatAction(definition.AttackRange, definition.AttackPattern), UiTheme.TextPrimary, purchaseStatFontScale);
    _ui.StatBlock(new Rectangle(leftColumn.X, statGrid.Y + (statHeight + statRowGap) * 2, leftColumn.Width, statHeight), "SIZE", $"{definition.Size.x} x {definition.Size.y}", UiTheme.TextPrimary, purchaseStatFontScale);
    _ui.StatBlock(new Rectangle(rightColumn.X, statGrid.Y + (statHeight + statRowGap) * 2, rightColumn.Width, statHeight), "TEAM", UiText.GetTeamDisplayName(Team.CurrentTurn), teamColour, purchaseStatFontScale);

    string purchaseHint = definition.Type == PieceType.Mercenary
      ? _initialBuyPhase != null
        ? "Mercenaries are unavailable during the initial buy phase."
        : "Place anywhere in No-Man's-Land, or hire a neutral Mercenary for 15 gold."
      : _initialBuyPhase?.IsFarmPlacementPhase == true
        ? "Place two free farms on your side before normal buying."
        : _initialBuyPhase != null
        ? $"{_initialBuyPhase.PurchasesThisTurn}/{_initialBuyPhase.PurchasesPerTurn} bought. Select a square on your side."
        : "Buy, then select a square. Click a neutral Mercenary to hire it for 15 gold.";
    int unitInfoY = statGrid.Bottom + UiTheme.SpaceSm;
    const float abilityScale = 0.58f;
    const float hintScale = 0.58f;
    string abilityText = $"ABILITY: {GetUnitAbilityText(definition)}";
    int availableInfoHeight = Math.Max(0, previousButton.Y - unitInfoY - UiTheme.SpaceSm);
    int hintRequiredHeight = _ui.WrappedTextHeight(purchaseHint, content.Width, hintScale);
    int reservedHintHeight = Math.Min(hintRequiredHeight, Math.Max(0, availableInfoHeight / 3));
    int abilityHeight = Math.Max(0, availableInfoHeight - reservedHintHeight - UiTheme.SpaceXs);
    _ui.TextWrapped(
      abilityText,
      new Rectangle(content.X, unitInfoY, content.Width, abilityHeight),
      UiTheme.TextPrimary,
      abilityScale
    );
    _ui.TextWrapped(
      purchaseHint,
      new Rectangle(content.X, unitInfoY + abilityHeight + UiTheme.SpaceXs, content.Width, reservedHintHeight),
      UiTheme.TextMuted,
      hintScale
    );
    DrawMenuButton(previousButton, "<", UiButtonTone.Neutral);
    DrawMenuButton(nextButton, ">", UiButtonTone.Neutral);
    DrawMenuButton(
      purchaseButton,
      _initialBuyPhase != null ? "BUY MODE" : _isPurchaseMode ? "CANCEL" : "BUY",
      _initialBuyPhase != null ? UiButtonTone.Accent : _isPurchaseMode ? UiButtonTone.Danger : UiButtonTone.Primary,
      _isPurchaseMode
    );
    DrawPurchaseUnitList();
  }

  private void DrawPurchaseUnitList()
  {
    IReadOnlyList<PieceDefinition> purchasablePieces = GetPurchasablePieces();
    Rectangle toggle = GetPurchaseUnitListToggleBounds();
    string toggleLabel = $"UNITS {_selectedPurchaseIndex + 1}/{purchasablePieces.Count}  {(_isPurchaseUnitListExpanded ? "HIDE" : "SHOW")}";
    if (!_isPurchaseUnitListExpanded)
    {
      DrawMenuButton(toggle, toggleLabel, UiButtonTone.Neutral, false, 0.72f);
      return;
    }

    Rectangle list = GetPurchaseUnitListBounds(out int columnCount, out _);
    DrawPanel(list, UiTheme.PanelRaised, UiTheme.Gold);
    DrawMenuButton(toggle, toggleLabel, UiButtonTone.Accent, true, 0.72f);

    bool farmPlacementOnly = _initialBuyPhase?.IsFarmPlacementPhase == true;
    bool initialBuyActive = _initialBuyPhase != null;
    float labelScale = columnCount <= 2 ? 0.68f : 0.56f;
    for (int index = 0; index < purchasablePieces.Count; index++)
    {
      PieceDefinition unit = purchasablePieces[index];
      bool unavailable = (farmPlacementOnly && unit.Type != PieceType.Farm) ||
        (initialBuyActive && unit.Type == PieceType.Mercenary);
      DrawMenuButton(
        GetPurchaseUnitListItemBounds(index),
        unit.DisplayName,
        unavailable ? UiButtonTone.Danger : UiButtonTone.Neutral,
        index == _selectedPurchaseIndex,
        labelScale
      );
    }
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

  private Rectangle GetVisibleWorldBounds(Matrix cameraTransform, int margin = 96)
  {
    Matrix inverse = Matrix.Invert(cameraTransform);
    Viewport viewport = GraphicsDevice.Viewport;
    Vector2[] corners =
    [
      Vector2.Transform(Vector2.Zero, inverse),
      Vector2.Transform(new Vector2(viewport.Width, 0), inverse),
      Vector2.Transform(new Vector2(0, viewport.Height), inverse),
      Vector2.Transform(new Vector2(viewport.Width, viewport.Height), inverse)
    ];
    float left = corners.Min(point => point.X) - margin;
    float right = corners.Max(point => point.X) + margin;
    float top = corners.Min(point => point.Y) - margin;
    float bottom = corners.Max(point => point.Y) + margin;
    return new Rectangle(
      (int)MathF.Floor(left), (int)MathF.Floor(top),
      Math.Max(1, (int)MathF.Ceiling(right - left)), Math.Max(1, (int)MathF.Ceiling(bottom - top))
    );
  }

  private bool IsVisibleWorldBounds(Rectangle bounds) => _visibleWorldBounds.Intersects(bounds);

  private void EnsureStaticBattlefield()
  {
    int stamp = HashCode.Combine(_board, _terrain, _gameMode, _playerCount, _campaignTerritories);
    if (!_staticBattlefieldDirty && _staticBattlefield is not null && !_staticBattlefield.IsDisposed &&
        stamp == _staticBattlefieldStamp) return;

    _staticBattlefield?.Dispose();
    _staticBattlefieldStamp = stamp;
    _staticBattlefieldDirty = false;
    _staticBattlefield = new RenderTarget2D(
      GraphicsDevice,
      _board.BoardArray.GetLength(1) * 64,
      _board.BoardArray.GetLength(0) * 64,
      false,
      SurfaceFormat.Color,
      DepthFormat.None
    );

    GraphicsDevice.SetRenderTarget(_staticBattlefield);
    GraphicsDevice.Clear(Color.Transparent);
    _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
    for (int y = 0; y < _board.BoardArray.GetLength(0); y++)
    for (int x = 0; x < _board.BoardArray.GetLength(1); x++)
    {
      if (_board.BoardArray[y, x] != 1) continue;
      var boardPosition = (x: x + _board.MinX, y: y + _board.MinY);
      Rectangle cellBounds = new(x * 64, y * 64, 64, 64);
      Color baseCellColour = (x + y) % 2 == 0 ? UiTheme.DarkBoardCell : UiTheme.LightBoardCell;
      TeamName? squareOwner = GetSquareOwner(boardPosition);
      Color territoryColour = squareOwner.HasValue ? UiTheme.GetTeamColour(squareOwner.Value) : UiTheme.NoMansLand;
      DrawWorldRectangle(cellBounds, Color.Lerp(baseCellColour, territoryColour, territoryTintAmount), 0f);

      if (_gameMode == GameMode.Conquest && IsConquestSquare(boardPosition))
      {
        DrawWorldRectangle(cellBounds, new Color(218, 180, 91, 46), 0f);
        DrawWorldOutline(cellBounds, new Color(246, 214, 123, 170), 0f);
      }
      else if (_gameMode == GameMode.Dominion && MatchRules.GetDominionControlPoints(_board).Contains(boardPosition))
      {
        Rectangle objective = new(cellBounds.Center.X - 13, cellBounds.Center.Y - 13, 26, 26);
        DrawWorldRectangle(objective, new Color(218, 180, 91, 150), 0f);
        DrawWorldOutline(objective, UiTheme.GoldBright, 0f);
      }

      if (_terrain.IsLake(boardPosition))
      {
        DrawWorldRectangle(cellBounds, UiTheme.Lake, 0f);
        DrawWorldRectangle(new Rectangle(cellBounds.X + 11, cellBounds.Y + 14, cellBounds.Width - 28, 3), UiTheme.LakeHighlight, 0f);
        DrawWorldRectangle(new Rectangle(cellBounds.X + 24, cellBounds.Y + 35, cellBounds.Width - 34, 3), UiTheme.LakeHighlight, 0f);
      }
      else if (_terrain.IsForest(boardPosition))
      {
        DrawWorldRectangle(cellBounds, UiTheme.Forest, 0f);
        DrawWorldRectangle(new Rectangle(cellBounds.X + 12, cellBounds.Y + 10, 14, 24), UiTheme.ForestDark, 0f);
        DrawWorldRectangle(new Rectangle(cellBounds.Right - 26, cellBounds.Bottom - 34, 14, 24), UiTheme.ForestDark, 0f);
      }

      var rightPosition = (x: boardPosition.x + 1, y: boardPosition.y);
      var belowPosition = (x: boardPosition.x, y: boardPosition.y + 1);
      if (_terrain.HasRiverBetween(boardPosition, rightPosition))
        DrawWorldRectangle(new Rectangle(cellBounds.Right - 3, cellBounds.Y, 6, cellBounds.Height), UiTheme.River, 0f);
      if (_terrain.HasRiverBetween(boardPosition, belowPosition))
        DrawWorldRectangle(new Rectangle(cellBounds.X, cellBounds.Bottom - 3, cellBounds.Width, 6), UiTheme.River, 0f);
    }
    _spriteBatch.End();
    GraphicsDevice.SetRenderTarget(null);
  }

  private Matrix GetBoardRotationTransform()
  {
    return Matrix.CreateRotationZ(GetBoardRotationAngle());
  }

  private float GetBoardRotationAngle()
  {
    float teamFacing = _onlineClient?.Team switch
    {
      NetworkTeam.Blue => MathHelper.Pi,
      NetworkTeam.Green => -MathHelper.PiOver2,
      NetworkTeam.Yellow => MathHelper.PiOver2,
      _ => 0f
    };
    return teamFacing + (_rotateBoard ? MathHelper.PiOver2 : 0f);
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
      Keys.D0 or Keys.NumPad0 => '0',
      Keys.D1 or Keys.NumPad1 => '1',
      Keys.D2 or Keys.NumPad2 => '2',
      Keys.D3 or Keys.NumPad3 => '3',
      Keys.D4 or Keys.NumPad4 => '4',
      Keys.D5 or Keys.NumPad5 => '5',
      Keys.D6 or Keys.NumPad6 => '6',
      Keys.D7 or Keys.NumPad7 => '7',
      Keys.D8 or Keys.NumPad8 => '8',
      Keys.D9 or Keys.NumPad9 => '9',
      Keys.OemPeriod or Keys.Decimal => '.',
      Keys.OemMinus or Keys.Subtract => '-',
      Keys.OemSemicolon => shiftHeld ? ':' : ';',
      Keys.OemQuestion => shiftHeld ? '?' : '/',
      _ => default
    };
    return character != default;
  }

  private static bool IsEditableEconomyRow(int index) => index is 0 or 1 or 2 or 3 or 4 or 6 or 7 or 9;

  private bool TryBeginEconomyTextInput(Point position)
  {
    for (int index = 0; index <= 9; index++)
    {
      if (IsEditableEconomyRow(index) && GetEconomyValueBounds(index).Contains(position))
      {
        _economyInputIndex = index;
        // Treat a click as selecting the current value, so typing replaces it.
        _economyInputText = string.Empty;
        return true;
      }
    }

    return false;
  }

  private void UpdateEconomyTextInput(Keys key)
  {
    if (key == Keys.Enter)
    {
      CommitEconomyTextInput();
      return;
    }

    if (key == Keys.Back)
    {
      if (_economyInputText.Length > 0)
      {
        _economyInputText = _economyInputText[..^1];
      }
      return;
    }

    if (!TryGetOnlineInputCharacter(key, false, out char character)) return;
    bool acceptsDecimal = _economyInputIndex is 1 or 2;
    int decimalIndex = _economyInputText.IndexOf('.');
    bool canAddDecimalDigit = decimalIndex < 0 || _economyInputText.Length - decimalIndex - 1 < 2;
    bool validCharacter = char.IsDigit(character) ||
      (character == '-' && _economyInputText.Length == 0) ||
      (character == '.' && acceptsDecimal && !_economyInputText.Contains('.'));
    if (validCharacter && (!char.IsDigit(character) || !acceptsDecimal || canAddDecimalDigit))
    {
      _economyInputText += character;
    }
  }

  private void CommitEconomyTextInput()
  {
    if (_economyInputIndex < 0)
    {
      return;
    }

    switch (_economyInputIndex)
    {
      case 0 when int.TryParse(_economyInputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int startingCash):
        _startingCash = Math.Max(0, startingCash);
        break;
      case 1 when float.TryParse(_economyInputText, NumberStyles.Float, CultureInfo.InvariantCulture, out float killerRefund) && float.IsFinite(killerRefund):
        _killerRefundMultiplier = TruncateRefundMultiplier(killerRefund);
        break;
      case 2 when float.TryParse(_economyInputText, NumberStyles.Float, CultureInfo.InvariantCulture, out float defeatedRefund) && float.IsFinite(defeatedRefund):
        _defeatedTeamRefundMultiplier = TruncateRefundMultiplier(defeatedRefund);
        break;
      case 3 when int.TryParse(_economyInputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int buysPerTurn):
        _initialBuysPerTurn = Math.Max(1, buysPerTurn);
        break;
      case 4 when int.TryParse(_economyInputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int buyTurns):
        _initialBuyTurnsPerTeam = Math.Max(1, buyTurns);
        break;
      case 6 when int.TryParse(_economyInputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int farmIncome):
        _farmIncomePerTurn = farmIncome;
        break;
      case 7 when int.TryParse(_economyInputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int unitPrice):
        _unitPricePercent = unitPrice;
        break;
      case 9 when int.TryParse(_economyInputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int interest):
        _interestPercent = Math.Clamp(interest, -100, 200);
        break;
    }

    _economyInputIndex = -1;
    _economyInputText = string.Empty;
  }

  private void CancelEconomyTextInput()
  {
    _economyInputIndex = -1;
    _economyInputText = string.Empty;
  }

  private string GetEconomyEditedValue(int index, string normalValue) =>
    _economyInputIndex == index ? (_economyInputText.Length == 0 ? "|" : _economyInputText) : normalValue;

  private bool TryBeginTimerTextInput(Point position)
  {
    int timerStartIndex = GetModeRuleSettingCount();
    for (int timerIndex = 0; timerIndex < 3; timerIndex++)
    {
      if (GetModeSettingsValueBounds(timerStartIndex + timerIndex + 1).Contains(position))
      {
        _timerInputIndex = timerIndex;
        // A click selects the existing value, so typing replaces it.
        _timerInputText = string.Empty;
        return true;
      }
    }

    return false;
  }

  private void UpdateTimerTextInput(Keys key)
  {
    if (key == Keys.Enter)
    {
      CommitTimerTextInput();
      return;
    }

    if (key == Keys.Back)
    {
      if (_timerInputText.Length > 0)
      {
        _timerInputText = _timerInputText[..^1];
      }
      return;
    }

    if (TryGetOnlineInputCharacter(key, false, out char character) && char.IsDigit(character))
    {
      _timerInputText += character;
    }
  }

  private void CommitTimerTextInput()
  {
    if (_timerInputIndex < 0)
    {
      return;
    }

    if (int.TryParse(_timerInputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
    {
      switch (_timerInputIndex)
      {
        case 0:
          _chessTimerMinutes = Math.Clamp(value, 0, 180);
          break;
        case 1:
          _chessTimerSeconds = Math.Clamp(value, 0, 59);
          break;
        case 2:
          _chessTimerIncrementSeconds = Math.Clamp(value, 0, 120);
          break;
      }
    }

    CancelTimerTextInput();
  }

  private void CancelTimerTextInput()
  {
    _timerInputIndex = -1;
    _timerInputText = string.Empty;
  }

  private string GetTimerEditedValue(int timerIndex, string normalValue) =>
    _timerInputIndex == timerIndex ? (_timerInputText.Length == 0 ? "|" : _timerInputText) : normalValue;

  private static int AdjustInteger(int value, int delta) =>
    (int)Math.Clamp((long)value + delta, int.MinValue, int.MaxValue);

  private static float TruncateRefundMultiplier(float value) =>
    (float)(Math.Truncate(value * 100d) / 100d);

  private void ResetMatchConfigurationValues()
  {
    CancelEconomyTextInput();
    CancelTimerTextInput();
    _startingCash = Globals.StartingCash;
    _killerRefundMultiplier = Globals.KillerDeathRefundMultiplier;
    _defeatedTeamRefundMultiplier = Globals.DefeatedTeamDeathRefundMultiplier;
    _initialBuysPerTurn = Globals.InitialBuysPerTurn;
    _initialBuyTurnsPerTeam = Globals.InitialBuyTurnsPerTeam;
    _farmsEnabled = Globals.FarmsEnabled;
    _farmIncomePerTurn = Globals.FarmIncomePerTurn;
    _unitPricePercent = Globals.UnitPricePercent;
    _interestEnabled = Globals.InterestEnabled;
    _interestPercent = Globals.InterestPercent;
    _conquestWinScore = MatchRules.DefaultConquestWinScore;
    _escortRoyalHealthPercent = Globals.DefaultEscortRoyalHealthPercent;
    _dominionWinScore = Globals.DefaultDominionWinScore;
    _plunderWinScore = Globals.DefaultPlunderWinScore;
    _plunderDeliveryScore = Globals.DefaultPlunderDeliveryScore;
    _plunderRoyalKillPenalty = Globals.DefaultPlunderRoyalKillPenalty;
    _chessTimerEnabled = false;
    _chessTimerMinutes = 10;
    _chessTimerSeconds = 0;
    _chessTimerIncrementSeconds = 0;
    EnsurePurchaseSelectionIsValid();
    CancelEconomyTextInput();
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
    return UiLayout.Centered(viewport, 660, 760, UiTheme.SpaceLg);
  }

  private Rectangle GetSettingsBindingBounds(int index)
  {
    Rectangle panel = GetSettingsPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    int actionCount = Enum.GetValues<BindingAction>().Length;
    int rowsTop = content.Y + 72;
    int rowsBottom = GetSettingsUiScaleButtonBounds().Y - UiTheme.SpaceMd;
    int rowHeight = Math.Clamp(
      (rowsBottom - rowsTop - UiTheme.SpaceXs * (actionCount - 1)) / actionCount,
      18,
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
    Rectangle resolution = GetSettingsResolutionButtonBounds();
    return new Rectangle(resolution.X, resolution.Bottom + UiTheme.SpaceSm, resolution.Width, settingsControlHeight);
  }

  private Rectangle GetSettingsZoomAnchorButtonBounds()
  {
    Rectangle panel = GetSettingsPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(
      content.X,
      content.Bottom - settingsControlHeight * 6 - UiTheme.SpaceSm * 5,
      content.Width,
      settingsControlHeight
    );
  }

  private Rectangle GetSettingsUiScaleButtonBounds()
  {
    Rectangle zoom = GetSettingsZoomAnchorButtonBounds();
    return new Rectangle(zoom.X, zoom.Y - settingsControlHeight - UiTheme.SpaceSm, zoom.Width, settingsControlHeight);
  }

  private Rectangle GetSettingsUiScaleDecreaseButtonBounds()
  {
    Rectangle row = GetSettingsUiScaleButtonBounds();
    return new Rectangle(row.X, row.Y, row.Height, row.Height);
  }

  private Rectangle GetSettingsUiScaleValueBounds()
  {
    Rectangle row = GetSettingsUiScaleButtonBounds();
    return new Rectangle(row.X + row.Height + UiTheme.SpaceSm, row.Y, row.Width - (row.Height + UiTheme.SpaceSm) * 2, row.Height);
  }

  private Rectangle GetSettingsUiScaleIncreaseButtonBounds()
  {
    Rectangle row = GetSettingsUiScaleButtonBounds();
    return new Rectangle(row.Right - row.Height, row.Y, row.Height, row.Height);
  }

  private Rectangle GetSettingsFpsCapButtonBounds()
  {
    Rectangle zoom = GetSettingsZoomAnchorButtonBounds();
    return new Rectangle(zoom.X, zoom.Bottom + UiTheme.SpaceSm, zoom.Width, settingsControlHeight);
  }

  private Rectangle GetSettingsResolutionButtonBounds()
  {
    Rectangle fps = GetSettingsFpsCapButtonBounds();
    return new Rectangle(fps.X, fps.Bottom + UiTheme.SpaceSm, fps.Width, settingsControlHeight);
  }

  private Rectangle GetSettingsBackButtonBounds()
  {
    Rectangle panel = GetSettingsPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    Rectangle rotation = GetSettingsRotationButtonBounds();
    return new Rectangle(rotation.X, rotation.Bottom + UiTheme.SpaceSm, rotation.Width, settingsControlHeight);
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
    int height = _setupStage is SetupStage.Economy or SetupStage.ModeSettings ? 870 : 620;
    return UiLayout.Centered(viewport, 640, height, UiTheme.SpaceLg);
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

  private Rectangle GetSetupResetButtonBounds()
  {
    Rectangle backButton = GetSetupBackButtonBounds();
    return new Rectangle(backButton.X - 104, backButton.Y, 96, backButton.Height);
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

  private Rectangle GetPlayerCountRowBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 386, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetModeOptionBounds(int index)
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    Rectangle row = new(content.X, content.Y + 324, content.Width, UiTheme.ButtonHeight);
    return UiLayout.HorizontalSlot(row, Enum.GetValues<GameMode>().Length, index, UiTheme.SpaceSm);
  }

  private Rectangle GetPlayerCountDecreaseButtonBounds()
  {
    return GetStepperDecreaseButtonBounds(GetPlayerCountRowBounds());
  }

  private Rectangle GetPlayerCountValueBounds()
  {
    return GetStepperValueBounds(GetPlayerCountRowBounds());
  }

  private Rectangle GetPlayerCountIncreaseButtonBounds()
  {
    return GetStepperIncreaseButtonBounds(GetPlayerCountRowBounds());
  }

  private Rectangle GetCpuDifficultyRowBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 438, content.Width, 34);
  }

  private Rectangle GetCpuPersonalityRowBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 478, content.Width, 34);
  }

  private static Rectangle GetStepperDecreaseButtonBounds(Rectangle row)
  {
    return new Rectangle(row.Right - 228, row.Y, 44, row.Height);
  }

  private static Rectangle GetStepperValueBounds(Rectangle row)
  {
    return new Rectangle(row.Right - 176, row.Y, 124, row.Height);
  }

  private static Rectangle GetStepperIncreaseButtonBounds(Rectangle row)
  {
    return new Rectangle(row.Right - 44, row.Y, 44, row.Height);
  }

  private Rectangle GetDebugRoyalSwitchButtonBounds()
  {
    Rectangle previousButton = GetSetupPreviousButtonBounds();
    Rectangle confirmButton = GetSetupConfirmButtonBounds();
    return new Rectangle(
      previousButton.X,
      previousButton.Y - UiTheme.ButtonHeight - UiTheme.SpaceSm,
      confirmButton.Right - previousButton.X,
      UiTheme.ButtonHeight
    );
  }

  private Rectangle GetEconomyRowBounds(int index)
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 92 + index * 52, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetEconomyDecreaseButtonBounds(int index)
  {
    return GetStepperDecreaseButtonBounds(GetEconomyRowBounds(index));
  }

  private Rectangle GetEconomyValueBounds(int index)
  {
    return GetStepperValueBounds(GetEconomyRowBounds(index));
  }

  private Rectangle GetEconomyIncreaseButtonBounds(int index)
  {
    return GetStepperIncreaseButtonBounds(GetEconomyRowBounds(index));
  }

  private Rectangle GetBattlefieldRowBounds(int index)
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 92 + index * 72, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetBattlefieldDecreaseButtonBounds(int index)
  {
    return GetStepperDecreaseButtonBounds(GetBattlefieldRowBounds(index));
  }

  private Rectangle GetBattlefieldValueBounds(int index)
  {
    return GetStepperValueBounds(GetBattlefieldRowBounds(index));
  }

  private Rectangle GetBattlefieldIncreaseButtonBounds(int index)
  {
    return GetStepperIncreaseButtonBounds(GetBattlefieldRowBounds(index));
  }

  private Rectangle GetTerrainPresetBrowserButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetSetupPanelBounds(), UiTheme.SpaceLg);
    Rectangle confirm = GetSetupConfirmButtonBounds();
    return new Rectangle(content.X, confirm.Y - 82, content.Width, 36);
  }

  private Rectangle GetTerrainPresetBrowserPanelBounds() => UiLayout.Centered(
    UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
    1400,
    840,
    UiTheme.SpaceLg
  );

  private Rectangle GetTerrainPresetBrowserGridBounds()
  {
    Rectangle content = UiLayout.Inset(GetTerrainPresetBrowserPanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 70, content.Width, Math.Max(1, content.Height - 138));
  }

  private int GetTerrainPresetBrowserRows() => Math.Max(1, Math.Min(3, GetTerrainPresetBrowserGridBounds().Height / 180));

  private int GetTerrainPresetBrowserPageSize() => 4 * GetTerrainPresetBrowserRows();

  private Rectangle GetTerrainPresetBrowserCardBounds(int visibleIndex)
  {
    const int columns = 4;
    Rectangle grid = GetTerrainPresetBrowserGridBounds();
    int rowCount = GetTerrainPresetBrowserRows();
    int row = visibleIndex / columns;
    int column = visibleIndex % columns;
    int gap = UiTheme.SpaceSm;
    int height = Math.Max(1, (grid.Height - gap * Math.Max(0, rowCount - 1)) / rowCount);
    int y = grid.Y + row * (height + gap);
    if (row == rowCount - 1)
    {
      height = Math.Max(1, grid.Bottom - y);
    }
    Rectangle rowBounds = new(grid.X, y, grid.Width, height);
    return UiLayout.HorizontalSlot(rowBounds, columns, column, gap);
  }

  private Rectangle GetTerrainPresetBrowserPreviousButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetTerrainPresetBrowserPanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Bottom - 44, 112, UiTheme.ButtonHeight);
  }

  private Rectangle GetTerrainPresetBrowserNextButtonBounds()
  {
    Rectangle previous = GetTerrainPresetBrowserPreviousButtonBounds();
    return new Rectangle(previous.Right + UiTheme.SpaceSm, previous.Y, 112, previous.Height);
  }

  private Rectangle GetTerrainPresetBrowserCloseButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetTerrainPresetBrowserPanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.Right - 112, content.Bottom - 44, 112, UiTheme.ButtonHeight);
  }

  private Rectangle GetModeSettingsRowBounds(int index)
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 168 + index * 58, content.Width, UiTheme.ButtonHeight);
  }

  private int GetModeRuleSettingCount() => _gameMode == GameMode.Plunder ? 3 :
    _gameMode == GameMode.Regicide ? 0 : 1;

  private Rectangle GetModeSettingsDecreaseButtonBounds(int index)
  {
    return GetStepperDecreaseButtonBounds(GetModeSettingsRowBounds(index));
  }

  private Rectangle GetModeSettingsValueBounds(int index)
  {
    return GetStepperValueBounds(GetModeSettingsRowBounds(index));
  }

  private Rectangle GetModeSettingsIncreaseButtonBounds(int index)
  {
    return GetStepperIncreaseButtonBounds(GetModeSettingsRowBounds(index));
  }

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

  private void ClearTerrainPresetSelection()
  {
    _selectedTerrainPresetId = null;
    _selectedTerrainPresetName = null;
  }

  private void OpenTerrainPresetBrowser()
  {
    _terrainPresetBrowserBoard = new Board(GetBoardFileName(_selectedBoardSize));
    _terrainPresetBrowserPresets = BattlefieldTerrain.GetPresets(
      _terrainPresetBrowserBoard,
      _selectedBoardSize.ToString()
    );
    int selectedIndex = _terrainPresetBrowserPresets
      .Select((preset, index) => (preset, index))
      .FirstOrDefault(entry => entry.preset.Id == _selectedTerrainPresetId)
      .index;
    _terrainPresetBrowserPage = Math.Max(0, selectedIndex / GetTerrainPresetBrowserPageSize());
    _terrainPresetBrowserOpen = true;
  }

  private void CloseTerrainPresetBrowser()
  {
    _terrainPresetBrowserOpen = false;
    _terrainPresetBrowserPresets = [];
    _terrainPresetBrowserBoard = null;
    _terrainPresetBrowserPage = 0;
  }

  private void SelectTerrainPreset(BattlefieldTerrainPreset preset)
  {
    _selectedTerrainPresetId = preset.Id;
    _selectedTerrainPresetName = preset.Name;
    if (Enum.TryParse(preset.ForestDensity, ignoreCase: true, out TerrainDensity forestDensity))
    {
      _forestDensity = forestDensity;
    }
    if (Enum.TryParse(preset.WaterwayDensity, ignoreCase: true, out TerrainDensity waterwayDensity))
    {
      _waterwayDensity = waterwayDensity;
    }
    _terrainSource = TerrainSource.Preset;
    CloseTerrainPresetBrowser();
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
    _terrain = TerrainRules.Create(
      _board,
      terrainSeed,
      forestDensity.ToString(),
      waterwayDensity.ToString(),
      _playerCount,
      _terrainSource.ToString(),
      boardSize.ToString(),
      _selectedTerrainPresetId
    );
    _roads.Clear();
    _barricades.Clear();
    _mines.Clear();
    _restoredLakeTiles.Clear();
    _riverBridges.Clear();
  }

  private void ConfigureTeamsForPlayerCount()
  {
    TeamName[] activeTeams = TeamRules.GetActiveTeams(_playerCount)
      .Select(team => team.ToTeamName())
      .ToArray();
    Team.ConfigureTurnOrder(activeTeams);
    _teams = pieceSetup.CreateTeams(_playerCount);
    _setupTeam = activeTeams[0];
    _conquestScores.Clear();
    foreach (TeamName team in activeTeams) _conquestScores[team] = 0;
    _modeScores.Clear();
    foreach (TeamName team in activeTeams) _modeScores[team] = 0;
  }

  private void SetPlayerCount(int playerCount)
  {
    _playerCount = Math.Clamp(playerCount, 2, 4);
    pieceSetup.ClearPieces();
    ConfigureTeamsForPlayerCount();
    _selectedRoyalIndex = 0;
  }

  private void ConfigureCpuOpponents()
  {
    _cpuProfiles.Clear();
    foreach (TeamName team in Team.ActiveTeams.Skip(1))
    {
      int seed = HashCode.Combine(_terrainSeed, _cpuMatchVariationSeed, (int)team);
      _cpuProfiles[team] = CpuProfile.ForDifficulty(_selectedCpuDifficulty, seed, _selectedCpuPersonality);
    }
  }

  private void PlaceCpuRoyal(TeamName teamName)
  {
    CpuProfile profile = _cpuProfiles[teamName];
    PieceDefinition[] eligibleRoyals = GetAllowedRoyals()
      .Where(royal => _gameMode != GameMode.Escort || royal.Type != PieceType.Palace)
      .ToArray();
    Random random = new(profile.RandomSeed ^ _terrainSeed ^ ((int)teamName * 7919));
    PieceDefinition royal = ChooseCpuRoyal(eligibleRoyals, profile, random);
    (int x, int y) position = ChooseCpuRoyalPlacement(teamName, royal, profile, random);
    PlaceRoyal(teamName, royal, position);
  }

  private void ContinueRoyalSelection()
  {
    int currentIndex = Team.ActiveTeams.ToList().IndexOf(_setupTeam);
    for (int index = currentIndex + 1; index < Team.ActiveTeams.Count; index++)
    {
      TeamName nextTeam = Team.ActiveTeams[index];
      if (pieceSetup.Pieces.Any(piece => piece.Team == nextTeam && piece.Definition.Category == PieceCategory.Royal))
      {
        continue;
      }
      if (_cpuProfiles.ContainsKey(nextTeam))
      {
        PlaceCpuRoyal(nextTeam);
        continue;
      }

      _setupTeam = nextTeam;
      _selectedRoyalIndex = 0;
      _screen = Screen.Setup;
      return;
    }

    StartInitialBuyPhase();
  }

  private static IReadOnlyDictionary<TeamName, (int buyTurnsUsed, bool stopped, int farmsPlaced)> GetInitialBuyTeamStates(
    NetworkInitialBuyState initialBuy
  )
  {
    IReadOnlyList<NetworkInitialBuyTeamState> states = initialBuy.TeamStates ??
    [
      new NetworkInitialBuyTeamState(NetworkTeam.Red, initialBuy.RedBuyTurnsUsed, initialBuy.RedStopped),
      new NetworkInitialBuyTeamState(NetworkTeam.Blue, initialBuy.BlueBuyTurnsUsed, initialBuy.BlueStopped)
    ];
    return states.ToDictionary(
      state => state.Team.ToTeamName(),
      state => (state.BuyTurnsUsed, state.Stopped, state.FarmsPlaced)
    );
  }

  private void ReturnToTitle()
  {
    CancelCpuPlanning();
    ClearPlanningMarks();
    _campaignTestPlay = false;
    if (_onlineClient != null)
    {
      _ = _onlineClient.DisposeAsync().AsTask();
      _onlineClient = null;
    }

    _onlineIsHost = false;
    _onlineRoyalChoicePending = false;
    _royalAwaitingPlacement = null;
    _debugTeamSwitchPending = false;
    _onlineHostingSetup = false;
    _onlineMatchConfiguration = null;
    _onlineError = string.Empty;
    _onlineStatus = "OFFLINE";
    _cpuProfiles.Clear();
    _cpuActionQueue.Clear();
    _cpuRecentMoves.Clear();
    _lastCpuDecisionReport = null;
    _cpuActionDelaySeconds = 0f;
    pieceSetup.ClearPieces();
    _playerCount = 2;
    ConfigureTeamsForPlayerCount();
    _terrainSource = TerrainSource.Preset;
    ClearTerrainPresetSelection();
    ConfigureBattlefield(BoardSize.Medium, TerrainDensity.Standard, TerrainDensity.Standard, Random.Shared.Next());
    selectedPiece = null;
    _movementAnimation = null;
    _initialBuyPhase = null;
    _isPurchaseMode = false;
    _selectedPurchaseIndex = 0;
    _selectedEngineerAbility = EngineerAbility.Road;
    _startingCash = Globals.StartingCash;
    _killerRefundMultiplier = Globals.KillerDeathRefundMultiplier;
    _defeatedTeamRefundMultiplier = Globals.DefeatedTeamDeathRefundMultiplier;
    _initialBuysPerTurn = Globals.InitialBuysPerTurn;
    _initialBuyTurnsPerTeam = Globals.InitialBuyTurnsPerTeam;
    _farmsEnabled = Globals.FarmsEnabled;
    _farmIncomePerTurn = Globals.FarmIncomePerTurn;
    _unitMaintenanceEnabled = Globals.UnitMaintenanceEnabled;
    _unitMaintenancePercent = Globals.UnitMaintenancePercent;
    _unitPricePercent = Globals.UnitPricePercent;
    _interestEnabled = Globals.InterestEnabled;
    _interestPercent = Globals.InterestPercent;
    _escortRoyalHealthPercent = Globals.DefaultEscortRoyalHealthPercent;
    _dominionWinScore = Globals.DefaultDominionWinScore;
    _plunderWinScore = Globals.DefaultPlunderWinScore;
    _plunderDeliveryScore = Globals.DefaultPlunderDeliveryScore;
    _plunderRoyalKillPenalty = Globals.DefaultPlunderRoyalKillPenalty;
    _chessTimerEnabled = false;
    _chessTimerMinutes = 10;
    _chessTimerSeconds = 0;
    _chessTimerIncrementSeconds = 0;
    CancelEconomyTextInput();
    CancelTimerTextInput();
    _winningTeam = null;
    _gameMode = GameMode.Regicide;
    _conquestWinScore = MatchRules.DefaultConquestWinScore;
    _conquestScore = 0;
    _cpuTurnNumber = 0;
    _setupTeam = TeamName.Red;
    _selectedRoyalIndex = 0;
    _setupStage = SetupStage.Mode;
    _cameraPosition = Vector2.Zero;
    _zoom = 1f;
    Team.ResetTurn();
    _screen = Screen.Title;
  }

  private void BeginMatchSetup(bool onlineHost = false, bool cpuOpponent = false)
  {
    CancelCpuPlanning();
    ClearPlanningMarks();
    _cpuMatchVariationSeed = Random.Shared.Next();
    _screen = Screen.Setup;
    _allowedPacks.Clear();
    _allowedPacks.Add(Pack.Base);
    SetPlayerCount(2);
    _selectedRoyalIndex = 0;
    _royalAwaitingPlacement = null;
    _setupStage = SetupStage.Mode;
    _gameMode = GameMode.Regicide;
    _conquestWinScore = MatchRules.DefaultConquestWinScore;
    _conquestScore = 0;
    _cpuTurnNumber = 0;
    _selectedBoardSize = BoardSize.Medium;
    _forestDensity = TerrainDensity.Standard;
    _waterwayDensity = TerrainDensity.Standard;
    _terrainSource = TerrainSource.Preset;
    ClearTerrainPresetSelection();
    _startingCash = Globals.StartingCash;
    _killerRefundMultiplier = Globals.KillerDeathRefundMultiplier;
    _defeatedTeamRefundMultiplier = Globals.DefeatedTeamDeathRefundMultiplier;
    _initialBuysPerTurn = Globals.InitialBuysPerTurn;
    _initialBuyTurnsPerTeam = Globals.InitialBuyTurnsPerTeam;
    _farmsEnabled = Globals.FarmsEnabled;
    _farmIncomePerTurn = Globals.FarmIncomePerTurn;
    CancelEconomyTextInput();
    CancelTimerTextInput();
    _unitMaintenanceEnabled = Globals.UnitMaintenanceEnabled;
    _unitMaintenancePercent = Globals.UnitMaintenancePercent;
    _unitPricePercent = Globals.UnitPricePercent;
    _interestEnabled = Globals.InterestEnabled;
    _interestPercent = Globals.InterestPercent;
    _escortRoyalHealthPercent = Globals.DefaultEscortRoyalHealthPercent;
    _dominionWinScore = Globals.DefaultDominionWinScore;
    _plunderWinScore = Globals.DefaultPlunderWinScore;
    _plunderDeliveryScore = Globals.DefaultPlunderDeliveryScore;
    _plunderRoyalKillPenalty = Globals.DefaultPlunderRoyalKillPenalty;
    _initialBuyPhase = null;
    _isPurchaseMode = false;
    _selectedEngineerAbility = EngineerAbility.Road;
    _onlineHostingSetup = onlineHost;
    _cpuOpponentSetup = cpuOpponent;
    _selectedCpuDifficulty = CpuDifficultyLevel.Medium;
    _selectedCpuPersonality = CpuPersonality.Balanced;
    _cpuActionQueue.Clear();
    _cpuRecentMoves.Clear();
    _lastCpuDecisionReport = null;
    _cpuActionDelaySeconds = 0f;
    if (onlineHost || !cpuOpponent)
    {
      _cpuProfiles.Clear();
    }
    else
    {
      ConfigureCpuOpponents();
    }
    _onlineMatchConfiguration = null;
    Team.ResetTurn();
  }

  private void PrepareOnlineRoom()
  {
    ClearPlanningMarks();
    pieceSetup.ClearPieces();
    ConfigureTeamsForPlayerCount();
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
      _conquestWinScore,
      _farmsEnabled,
      _farmIncomePerTurn,
      _unitMaintenanceEnabled,
      _unitMaintenancePercent,
      _unitPricePercent,
      _playerCount,
      _interestEnabled,
      _interestPercent,
      _escortRoyalHealthPercent,
      _dominionWinScore,
      _plunderWinScore,
      _plunderDeliveryScore,
      _plunderRoyalKillPenalty,
      _chessTimerEnabled,
      _chessTimerMinutes,
      _chessTimerSeconds,
      _chessTimerIncrementSeconds,
      _terrainSource.ToString(),
      _selectedTerrainPresetId,
      _allowedPacks.Select(pack => pack.ToString()).ToArray()
    );
  }

  private void UpdateMenu(
    KeyboardState keyboard,
    MouseState mouse,
    bool wasLeftClick,
    bool wasEscapePressed
  )
  {
    if (_terrainPresetBrowserOpen)
    {
      if (wasEscapePressed)
      {
        CloseTerrainPresetBrowser();
      }
      else if (wasLeftClick)
      {
        UpdateTerrainPresetBrowser(ToUiPoint(mouse.Position));
      }
      return;
    }

    if (wasEscapePressed)
    {
      if (_economyInputIndex >= 0)
      {
        CancelEconomyTextInput();
        return;
      }

      if (_timerInputIndex >= 0)
      {
        CancelTimerTextInput();
        return;
      }

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

    if (_screen == Screen.Setup && _setupStage == SetupStage.Economy && _economyInputIndex >= 0)
    {
      foreach (Keys key in keyboard.GetPressedKeys())
      {
        if (!_previousKeyboardState.IsKeyDown(key))
        {
          UpdateEconomyTextInput(key);
        }
      }
    }

    if (_screen == Screen.Setup && _setupStage == SetupStage.ModeSettings && _timerInputIndex >= 0)
    {
      foreach (Keys key in keyboard.GetPressedKeys())
      {
        if (!_previousKeyboardState.IsKeyDown(key))
        {
          UpdateTimerTextInput(key);
        }
      }
    }

    if (!wasLeftClick)
    {
      return;
    }

    Point mousePosition = ToUiPoint(mouse.Position);

    switch (_screen)
    {
      case Screen.Title:
        if (GetTitleButtonBounds(0).Contains(mousePosition))
        {
          BeginMatchSetup();
        }
        else if (GetTitleButtonBounds(1).Contains(mousePosition))
        {
          BeginMatchSetup(cpuOpponent: true);
        }
        else if (GetTitleButtonBounds(2).Contains(mousePosition))
        {
          _screen = Screen.OnlineLobby;
        }
        else if (GetTitleButtonBounds(3).Contains(mousePosition))
        {
          _levelEditor ??= new LevelEditorScreen(_ui, _spriteBatch, _pixel);
          _screen = Screen.LevelEditor;
        }
        else if (GetTitleButtonBounds(4).Contains(mousePosition))
        {
          _settingsReturnScreen = Screen.Title;
          _screen = Screen.Settings;
        }
        else if (GetTitleButtonBounds(5).Contains(mousePosition))
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
        if (IsDebugOnlineMatch && GetDebugRoyalSwitchButtonBounds().Contains(mousePosition))
        {
          _ = SwitchDebugTeamAsync();
          break;
        }

        if (_onlineRoyalChoicePending)
        {
          break;
        }

        if (GetSetupBackButtonBounds().Contains(mousePosition))
        {
          ReturnToTitle();
        }
        else if (GetSetupPreviousButtonBounds().Contains(mousePosition))
        {
          _selectedRoyalIndex =
            (_selectedRoyalIndex - 1 + GetAllowedRoyals().Length) % GetAllowedRoyals().Length;
        }
        else if (GetSetupNextButtonBounds().Contains(mousePosition))
        {
          _selectedRoyalIndex =
            (_selectedRoyalIndex + 1) % GetAllowedRoyals().Length;
        }
        else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
        {
          BeginOnlineRoyalPlacement();
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

        if (GetSettingsUiScaleDecreaseButtonBounds().Contains(mousePosition))
        {
          AdjustUiScale(-1);
        }
        else if (GetSettingsUiScaleIncreaseButtonBounds().Contains(mousePosition))
        {
          AdjustUiScale(1);
        }
        else if (GetSettingsZoomAnchorButtonBounds().Contains(mousePosition))
        {
          _zoomTowardsMouse = !_zoomTowardsMouse;
        }
        else if (GetSettingsFpsCapButtonBounds().Contains(mousePosition))
        {
          CycleFpsCap();
        }
        else if (GetSettingsResolutionButtonBounds().Contains(mousePosition))
        {
          CycleResolution();
        }
        else if (GetSettingsRotationButtonBounds().Contains(mousePosition))
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
        if (_setupStage == SetupStage.Economy && _economyInputIndex >= 0 &&
            !GetEconomyValueBounds(_economyInputIndex).Contains(mousePosition))
        {
          CommitEconomyTextInput();
        }
        if (_setupStage == SetupStage.ModeSettings && _timerInputIndex >= 0 &&
            !GetModeSettingsValueBounds(GetModeRuleSettingCount() + _timerInputIndex + 1).Contains(mousePosition))
        {
          CommitTimerTextInput();
        }
        if (GetSetupBackButtonBounds().Contains(mousePosition))
        {
          NavigateSetupBack();
        }
        else if (_setupStage == SetupStage.Mode)
        {
          if (_cpuOpponentSetup && GetCpuDifficultyRowBounds().Contains(mousePosition))
          {
            CpuDifficultyLevel[] difficulties = [CpuDifficultyLevel.Easy, CpuDifficultyLevel.Medium, CpuDifficultyLevel.Hard, CpuDifficultyLevel.Best];
            int current = Array.IndexOf(difficulties, _selectedCpuDifficulty);
            _selectedCpuDifficulty = difficulties[(Math.Max(0, current) + 1) % difficulties.Length];
            ConfigureCpuOpponents();
          }
          else if (_cpuOpponentSetup && GetCpuPersonalityRowBounds().Contains(mousePosition))
          {
            CpuPersonality[] personalities = [
              CpuPersonality.Balanced, CpuPersonality.Aggressive, CpuPersonality.Defensive,
              CpuPersonality.Greedy, CpuPersonality.Reckless, CpuPersonality.ObjectiveFocused, CpuPersonality.Swarmer
            ];
            int current = Array.FindIndex(personalities, personality => ReferenceEquals(personality, _selectedCpuPersonality));
            _selectedCpuPersonality = personalities[(Math.Max(0, current) + 1) % personalities.Length];
            ConfigureCpuOpponents();
          }
          else
          {
            GameMode? selectedMode = Enum.GetValues<GameMode>()
              .Where(mode => GetModeOptionBounds((int)mode).Contains(mousePosition))
              .Select(mode => (GameMode?)mode)
              .FirstOrDefault();
            if (selectedMode is GameMode mode)
            {
              _gameMode = mode;
            }
            else if (GetSetupPreviousButtonBounds().Contains(mousePosition))
            {
              _gameMode = (GameMode)(((int)_gameMode - 1 + Enum.GetValues<GameMode>().Length) % Enum.GetValues<GameMode>().Length);
            }
            else if (GetSetupNextButtonBounds().Contains(mousePosition))
            {
              _gameMode = (GameMode)(((int)_gameMode + 1) % Enum.GetValues<GameMode>().Length);
            }
            else if (GetPlayerCountDecreaseButtonBounds().Contains(mousePosition))
            {
              SetPlayerCount(_playerCount - 1);
              if (_cpuOpponentSetup) ConfigureCpuOpponents();
            }
            else if (GetPlayerCountIncreaseButtonBounds().Contains(mousePosition))
            {
              SetPlayerCount(_playerCount + 1);
              if (_cpuOpponentSetup) ConfigureCpuOpponents();
            }
            else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
            {
              _setupStage = SetupStage.Packs;
            }
          }
        }
        else if (_setupStage == SetupStage.Packs)
        {
          bool handledPack = false;
          foreach (Pack pack in PackRules.All)
          {
            if (!GetSetupPackButtonBounds(pack).Contains(mousePosition)) continue;
            ToggleSetupPack(pack);
            handledPack = true;
            break;
          }
          if (!handledPack && GetSetupConfirmButtonBounds().Contains(mousePosition) && GetAllowedRoyals().Length > 0)
          {
            _selectedRoyalIndex = 0;
            _setupStage = SetupStage.Battlefield;
          }
        }
        else if (_setupStage == SetupStage.Battlefield)
        {
          if (GetBattlefieldDecreaseButtonBounds(0).Contains(mousePosition))
          {
            BoardSize boardSize = (BoardSize)Math.Max((int)BoardSize.Small, (int)_selectedBoardSize - 1);
            if (boardSize != _selectedBoardSize)
            {
              _selectedBoardSize = boardSize;
              ClearTerrainPresetSelection();
            }
          }
          else if (GetBattlefieldIncreaseButtonBounds(0).Contains(mousePosition))
          {
            BoardSize boardSize = (BoardSize)Math.Min((int)BoardSize.Large, (int)_selectedBoardSize + 1);
            if (boardSize != _selectedBoardSize)
            {
              _selectedBoardSize = boardSize;
              ClearTerrainPresetSelection();
            }
          }
          else if (GetBattlefieldDecreaseButtonBounds(1).Contains(mousePosition))
          {
            _terrainSource = (TerrainSource)Math.Max((int)TerrainSource.Preset, (int)_terrainSource - 1);
          }
          else if (GetBattlefieldIncreaseButtonBounds(1).Contains(mousePosition))
          {
            _terrainSource = (TerrainSource)Math.Min((int)TerrainSource.None, (int)_terrainSource + 1);
          }
          else if (GetBattlefieldDecreaseButtonBounds(2).Contains(mousePosition))
          {
            TerrainDensity density = (TerrainDensity)Math.Max((int)TerrainDensity.Light, (int)_forestDensity - 1);
            if (density != _forestDensity)
            {
              _forestDensity = density;
              ClearTerrainPresetSelection();
            }
          }
          else if (GetBattlefieldIncreaseButtonBounds(2).Contains(mousePosition))
          {
            TerrainDensity density = (TerrainDensity)Math.Min((int)TerrainDensity.Heavy, (int)_forestDensity + 1);
            if (density != _forestDensity)
            {
              _forestDensity = density;
              ClearTerrainPresetSelection();
            }
          }
          else if (GetBattlefieldDecreaseButtonBounds(3).Contains(mousePosition))
          {
            TerrainDensity density = (TerrainDensity)Math.Max((int)TerrainDensity.Light, (int)_waterwayDensity - 1);
            if (density != _waterwayDensity)
            {
              _waterwayDensity = density;
              ClearTerrainPresetSelection();
            }
          }
          else if (GetBattlefieldIncreaseButtonBounds(3).Contains(mousePosition))
          {
            TerrainDensity density = (TerrainDensity)Math.Min((int)TerrainDensity.Heavy, (int)_waterwayDensity + 1);
            if (density != _waterwayDensity)
            {
              _waterwayDensity = density;
              ClearTerrainPresetSelection();
            }
          }
          else if (GetTerrainPresetBrowserButtonBounds().Contains(mousePosition))
          {
            OpenTerrainPresetBrowser();
          }
          else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
          {
            ApplyBattlefieldSetup();
            _setupStage = SetupStage.Economy;
          }
        }
        else if (_setupStage == SetupStage.Economy)
        {
          if (GetSetupResetButtonBounds().Contains(mousePosition))
          {
            ResetMatchConfigurationValues();
          }
          else if (TryBeginEconomyTextInput(mousePosition))
          {
            break;
          }
          else if (GetEconomyDecreaseButtonBounds(0).Contains(mousePosition))
          {
            _startingCash = Math.Max(0, AdjustInteger(_startingCash, -15));
          }
          else if (GetEconomyIncreaseButtonBounds(0).Contains(mousePosition))
          {
            _startingCash = AdjustInteger(_startingCash, 15);
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
          else if (GetEconomyDecreaseButtonBounds(5).Contains(mousePosition))
          {
            _farmsEnabled = false;
            EnsurePurchaseSelectionIsValid();
          }
          else if (GetEconomyIncreaseButtonBounds(5).Contains(mousePosition))
          {
            _farmsEnabled = true;
            EnsurePurchaseSelectionIsValid();
          }
          else if (GetEconomyDecreaseButtonBounds(6).Contains(mousePosition))
          {
            _farmIncomePerTurn = AdjustInteger(_farmIncomePerTurn, -1);
          }
          else if (GetEconomyIncreaseButtonBounds(6).Contains(mousePosition))
          {
            _farmIncomePerTurn = AdjustInteger(_farmIncomePerTurn, 1);
          }
          else if (GetEconomyDecreaseButtonBounds(7).Contains(mousePosition))
          {
            _unitPricePercent = AdjustInteger(_unitPricePercent, -10);
          }
          else if (GetEconomyIncreaseButtonBounds(7).Contains(mousePosition))
          {
            _unitPricePercent = AdjustInteger(_unitPricePercent, 10);
          }
          else if (GetEconomyDecreaseButtonBounds(8).Contains(mousePosition))
          {
            _interestEnabled = false;
          }
          else if (GetEconomyIncreaseButtonBounds(8).Contains(mousePosition))
          {
            _interestEnabled = true;
          }
          else if (GetEconomyDecreaseButtonBounds(9).Contains(mousePosition))
          {
            _interestPercent = Math.Max(-100, _interestPercent - 5);
          }
          else if (GetEconomyIncreaseButtonBounds(9).Contains(mousePosition))
          {
            _interestPercent = Math.Min(200, _interestPercent + 5);
          }
          else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
          {
            _setupStage = SetupStage.ModeSettings;
          }
        }
        else if (_setupStage == SetupStage.ModeSettings)
        {
          if (TryBeginTimerTextInput(mousePosition))
          {
            break;
          }
          else if (_gameMode == GameMode.Conquest && GetModeSettingsDecreaseButtonBounds(0).Contains(mousePosition))
          {
            _conquestWinScore = Math.Max(1, _conquestWinScore - 1);
          }
          else if (_gameMode == GameMode.Conquest && GetModeSettingsIncreaseButtonBounds(0).Contains(mousePosition))
          {
            _conquestWinScore++;
          }
          else if (_gameMode == GameMode.Escort && GetModeSettingsDecreaseButtonBounds(0).Contains(mousePosition))
          {
            _escortRoyalHealthPercent = Math.Max(1, _escortRoyalHealthPercent - 5);
          }
          else if (_gameMode == GameMode.Escort && GetModeSettingsIncreaseButtonBounds(0).Contains(mousePosition))
          {
            _escortRoyalHealthPercent = Math.Min(100, _escortRoyalHealthPercent + 5);
          }
          else if (_gameMode == GameMode.Dominion && GetModeSettingsDecreaseButtonBounds(0).Contains(mousePosition))
          {
            _dominionWinScore = Math.Max(1, _dominionWinScore - 1);
          }
          else if (_gameMode == GameMode.Dominion && GetModeSettingsIncreaseButtonBounds(0).Contains(mousePosition))
          {
            _dominionWinScore++;
          }
          else if (_gameMode == GameMode.Plunder && GetModeSettingsDecreaseButtonBounds(0).Contains(mousePosition))
          {
            _plunderWinScore = Math.Max(1, _plunderWinScore - 1);
          }
          else if (_gameMode == GameMode.Plunder && GetModeSettingsIncreaseButtonBounds(0).Contains(mousePosition))
          {
            _plunderWinScore++;
          }
          else if (_gameMode == GameMode.Plunder && GetModeSettingsDecreaseButtonBounds(1).Contains(mousePosition))
          {
            _plunderDeliveryScore = Math.Max(1, _plunderDeliveryScore - 1);
          }
          else if (_gameMode == GameMode.Plunder && GetModeSettingsIncreaseButtonBounds(1).Contains(mousePosition))
          {
            _plunderDeliveryScore++;
          }
          else if (_gameMode == GameMode.Plunder && GetModeSettingsDecreaseButtonBounds(2).Contains(mousePosition))
          {
            _plunderRoyalKillPenalty = Math.Max(0, _plunderRoyalKillPenalty - 1);
          }
          else if (_gameMode == GameMode.Plunder && GetModeSettingsIncreaseButtonBounds(2).Contains(mousePosition))
          {
            _plunderRoyalKillPenalty++;
          }
          else if (GetModeSettingsDecreaseButtonBounds(GetModeRuleSettingCount()).Contains(mousePosition))
          {
            _chessTimerEnabled = false;
          }
          else if (GetModeSettingsIncreaseButtonBounds(GetModeRuleSettingCount()).Contains(mousePosition))
          {
            _chessTimerEnabled = true;
          }
          else if (GetModeSettingsDecreaseButtonBounds(GetModeRuleSettingCount() + 1).Contains(mousePosition))
          {
            _chessTimerMinutes = Math.Max(0, _chessTimerMinutes - 1);
          }
          else if (GetModeSettingsIncreaseButtonBounds(GetModeRuleSettingCount() + 1).Contains(mousePosition))
          {
            _chessTimerMinutes = Math.Min(180, _chessTimerMinutes + 1);
          }
          else if (GetModeSettingsDecreaseButtonBounds(GetModeRuleSettingCount() + 2).Contains(mousePosition))
          {
            _chessTimerSeconds = Math.Max(0, _chessTimerSeconds - 1);
          }
          else if (GetModeSettingsIncreaseButtonBounds(GetModeRuleSettingCount() + 2).Contains(mousePosition))
          {
            _chessTimerSeconds = Math.Min(59, _chessTimerSeconds + 1);
          }
          else if (GetModeSettingsDecreaseButtonBounds(GetModeRuleSettingCount() + 3).Contains(mousePosition))
          {
            _chessTimerIncrementSeconds = Math.Max(0, _chessTimerIncrementSeconds - 1);
          }
          else if (GetModeSettingsIncreaseButtonBounds(GetModeRuleSettingCount() + 3).Contains(mousePosition))
          {
            _chessTimerIncrementSeconds = Math.Min(120, _chessTimerIncrementSeconds + 1);
          }
          else if (GetSetupConfirmButtonBounds().Contains(mousePosition))
          {
            if (_chessTimerEnabled && _chessTimerMinutes == 0 && _chessTimerSeconds == 0)
            {
              _chessTimerSeconds = 1;
            }
            foreach (Team team in _teams)
            {
              team.Money = _startingCash;
              team.ActionPoints = team.ActionLimit;
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
          PieceDefinition royal = GetAllowedRoyals()[_selectedRoyalIndex];
          if (_gameMode == GameMode.Escort && royal.Type == PieceType.Palace)
          {
            _selectedRoyalIndex = GetNextSelectableRoyalIndex(_selectedRoyalIndex, 1);
            return;
          }
          BeginRoyalPlacement(royal);
        }
        break;

      case Screen.GameOver:
        if (GetTitleButtonBounds(4).Contains(mousePosition))
        {
          if (_campaignTestPlay) ReturnToEditorFromTestPlay();
          else ReturnToTitle();
        }
        else if (GetTitleButtonBounds(5).Contains(mousePosition))
        {
          Exit();
        }
        break;
    }
  }

  private (int x, int y) FindRoyalSpawn(TeamName teamName, PieceDefinition definition)
  {
    NetworkTeam networkTeam = teamName.ToNetworkTeam();
    foreach ((int x, int y) position in MatchRules.GetRoyalSpawnCandidates(
      _board,
      networkTeam,
      definition.Size.x,
      definition.Size.y,
      _playerCount
    ))
    {
      if (CanPlacePiece(definition, position, teamName))
      {
        return position;
      }
    }

    throw new InvalidOperationException("Could not find an empty royal spawn square.");
  }

  private void BeginRoyalPlacement(PieceDefinition royal)
  {
    _royalAwaitingPlacement = royal;
    Team.SetCurrentTurn(_setupTeam);
    selectedPiece = null;
    _isPurchaseMode = false;
    _screen = Screen.Playing;
    Console.WriteLine($"Choose a starting position for {_setupTeam}'s {royal.Type}.");
  }

  private void BeginOnlineRoyalPlacement()
  {
    if (_onlineClient?.Team is not NetworkTeam localTeam || _onlineRoyalChoicePending)
    {
      return;
    }

    _setupTeam = localTeam.ToTeamName();
    _royalAwaitingPlacement = GetAllowedRoyals()[_selectedRoyalIndex];
    selectedPiece = null;
    _isPurchaseMode = false;
    _onlineStatus = "CHOOSE YOUR ROYAL'S STARTING SQUARE";
    _screen = Screen.Playing;
  }

  private void TryPlaceSelectedRoyal((int x, int y) position)
  {
    PieceDefinition royal = _royalAwaitingPlacement;
    if (royal is null)
    {
      return;
    }

    TeamName placementTeam = _onlineClient?.Team?.ToTeamName() ?? _setupTeam;
    if (!CanPlacePiece(royal, position, placementTeam))
    {
      Console.WriteLine("Royals must be placed on empty, traversable squares in their own territory.");
      return;
    }

    if (_onlineClient is not null)
    {
      _setupTeam = placementTeam;
      _ = SendOnlineRoyalChoiceAsync(position);
      return;
    }

    PlaceRoyal(_setupTeam, royal, position);
    _royalAwaitingPlacement = null;
    ContinueRoyalSelection();
  }

  private void PlaceRoyal(TeamName teamName, PieceDefinition royal, (int x, int y) position)
  {
    Team setupTeam = _teams.Find(team => team.TeamName == teamName);
    setupTeam.ChooseRoyal(royal.Type);
    pieceSetup.AddPiece(new Piece(royal, position, teamName)
    {
      CurrentHealth = GetRoyalStartingHealth(royal)
    });
  }

  private (int x, int y) ChooseCpuRoyalPlacement(
    TeamName teamName,
    PieceDefinition royal,
    CpuProfile profile,
    Random random
  )
  {
    List<(int x, int y)> candidates = MatchRules.GetRoyalSpawnCandidates(
      _board,
      teamName.ToNetworkTeam(),
      royal.Size.x,
      royal.Size.y,
      _playerCount
    ).Where(position => CanPlacePiece(royal, position, teamName)).ToList();

    if (candidates.Count == 0)
    {
      throw new InvalidOperationException("Could not find an empty royal spawn square.");
    }

    float Score((int x, int y) position) => CpuRoyalPlacementHeuristics.Score(
      _board,
      _terrain,
      teamName.ToNetworkTeam(),
      position,
      royal.Size.x,
      royal.Size.y,
      _playerCount,
      profile,
      _gameMode.ToString()
    );

    float bestScore = candidates.Max(Score);
    List<(int x, int y)> bestCandidates = candidates
      .Where(candidate => Score(candidate) >= bestScore - 0.001f)
      .ToList();
    return profile.Difficulty == CpuDifficultyLevel.Best
      ? bestCandidates.OrderBy(candidate => candidate.y).ThenBy(candidate => candidate.x).First()
      : bestCandidates[random.Next(bestCandidates.Count)];
  }

  private PieceDefinition ChooseCpuRoyal(
    IReadOnlyList<PieceDefinition> eligibleRoyals,
    CpuProfile profile,
    Random random
  )
  {
    // In Regicide, Best should select a genuinely resilient win-condition rather than roll a
    // weak 80-health royal. Palace is static but has the most health and generates income; Hard
    // uses the King for its adjacent-unit protection while retaining some tactical mobility.
    if (profile.Difficulty == CpuDifficultyLevel.Best)
    {
      return _gameMode == GameMode.Regicide
        ? eligibleRoyals.FirstOrDefault(royal => royal.Type == PieceType.Palace) ?? eligibleRoyals[0]
        : eligibleRoyals.FirstOrDefault(royal => royal.Type == PieceType.King) ?? eligibleRoyals[0];
    }
    if (_gameMode == GameMode.Regicide)
    {
      if (profile.Difficulty == CpuDifficultyLevel.Hard)
      {
        return eligibleRoyals.FirstOrDefault(royal => royal.Type == PieceType.King) ?? eligibleRoyals[0];
      }
    }

    return eligibleRoyals[random.Next(eligibleRoyals.Count)];
  }

  private int GetRoyalStartingHealth(PieceDefinition royal) =>
    _gameMode == GameMode.Escort
      ? Math.Max(1, (int)Math.Ceiling(royal.Health * (_escortRoyalHealthPercent / 100d)))
      : royal.Health;

  private int GetNextSelectableRoyalIndex(int currentIndex, int direction)
  {
    for (int offset = 1; offset <= GetAllowedRoyals().Length; offset++)
    {
      int index = (currentIndex + direction * offset + GetAllowedRoyals().Length) % GetAllowedRoyals().Length;
      if (_gameMode != GameMode.Escort || GetAllowedRoyals()[index].Type != PieceType.Palace)
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
      case SetupStage.Packs:
        _setupStage = SetupStage.Mode;
        break;
      case SetupStage.Battlefield:
        _setupStage = SetupStage.Packs;
        break;
      case SetupStage.Economy:
        _setupStage = SetupStage.Battlefield;
        break;
      case SetupStage.ModeSettings:
        _setupStage = SetupStage.Economy;
        break;
      case SetupStage.RoyalSelection:
        int setupIndex = Team.ActiveTeams.ToList().IndexOf(_setupTeam);
        if (setupIndex <= 0)
        {
          _setupStage = SetupStage.ModeSettings;
          break;
        }
        TeamName previousTeam = Team.ActiveTeams[setupIndex - 1];
        Piece previousRoyal = pieceSetup.Pieces.FirstOrDefault(piece =>
          piece.Team == previousTeam && piece.Definition.Category == PieceCategory.Royal);
        if (previousRoyal != null)
        {
          pieceSetup.RemovePiece(previousRoyal);
          _teams.Find(team => team.TeamName == previousTeam).ClearRoyal();
        }
        _setupTeam = previousTeam;
        _selectedRoyalIndex = 0;
        break;
      default:
        _setupStage = SetupStage.ModeSettings;
        break;
    }
  }

  private bool IsConquestSquare((int x, int y) position)
  {
    return MatchRules.IsConquestSquare(_board, position);
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
      BindingAction.EndTurn => _endTurnKey,
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
      case BindingAction.EndTurn: _endTurnKey = key; break;
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
      BindingAction.EndTurn => "End turn",
      _ => action.ToString()
    };
  }

  private void DrawMenuButton(
    Rectangle bounds,
    string label,
    UiButtonTone tone,
    bool selected = false,
    float textScale = 1f
  )
  {
    _ui.Button(bounds, label, tone, selected, textScale);
  }

  private void DrawSetupProgress(Rectangle content)
  {
    SetupStage[] stages = [SetupStage.Mode, SetupStage.Packs, SetupStage.Battlefield, SetupStage.Economy, SetupStage.ModeSettings, SetupStage.RoyalSelection];
    string[] labels = ["MODE", "PACKS", "MAP", "ECONOMY", "RULES", "ROYAL"];
    Rectangle row = new(content.X, content.Y + 62, content.Width, 18);
    int currentIndex = Array.IndexOf(stages, _setupStage);

    for (int index = 0; index < stages.Length; index++)
    {
      bool isCurrent = index == currentIndex;
      Color colour = index < currentIndex
        ? UiTheme.TextPrimary
        : isCurrent ? UiTheme.GoldBright : UiTheme.TextDim;
      _ui.CenterText(
        $"{index + 1}. {labels[index]}",
        UiLayout.HorizontalSlot(row, stages.Length, index, UiTheme.SpaceSm),
        colour,
        isCurrent ? 0.6f : 0.54f
      );
    }
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

    DrawMenuButton(GetTitleButtonBounds(0), "PLAY LOCAL", UiButtonTone.Primary);
    DrawMenuButton(GetTitleButtonBounds(1), "PLAY VS CPU", UiButtonTone.Accent);
    DrawMenuButton(GetTitleButtonBounds(2), "ONLINE MULTIPLAYER", UiButtonTone.Accent);
    DrawMenuButton(GetTitleButtonBounds(3), "CAMPAIGN LEVEL BUILDER", UiButtonTone.Primary);
    DrawMenuButton(GetTitleButtonBounds(4), "SETTINGS", UiButtonTone.Neutral);
    DrawMenuButton(GetTitleButtonBounds(5), "QUIT GAME", UiButtonTone.Danger);
  }

  private void HandleLevelEditorRequests()
  {
    if (_levelEditor.RequestTestPlay)
    {
      _levelEditor.ClearRequests();
      StartCampaignTestPlay();
      return;
    }
    if (_levelEditor.RequestBrowse)
    {
      _customLevels = CustomLevelBrowser.Browse();
      _levelEditor.ClearRequests();
      _screen = Screen.CustomLevels;
      return;
    }
    if (_levelEditor.RequestNew)
    {
      _levelEditor.ClearRequests();
      if (_levelEditor.State.HasUnsavedChanges)
      {
        _editorConfirmAction = EditorConfirmAction.New;
        _screen = Screen.EditorDiscardConfirm;
      }
      else
      {
        _levelEditor.ReplaceState(LevelEditorState.CreateNew());
      }
      return;
    }
    if (_levelEditor.RequestExit)
    {
      _levelEditor.ClearRequests();
      if (_levelEditor.State.HasUnsavedChanges)
      {
        _editorConfirmAction = EditorConfirmAction.Exit;
        _screen = Screen.EditorDiscardConfirm;
      }
      else
      {
        _screen = Screen.Title;
      }
    }
  }

  private void StartCampaignTestPlay()
  {
    _cpuMatchVariationSeed = Random.Shared.Next();
    CampaignLevelLoadResult snapshot = _levelEditor.State.CreateTestPlaySnapshot();
    if (!snapshot.IsSuccess || snapshot.Level is null)
    {
      return;
    }
    CampaignPlayableStateResult converted = CampaignLevelConverter.CreatePlayableState(snapshot.Level);
    if (!converted.IsSuccess || converted.State is null)
    {
      return;
    }

    CampaignPlayableState state = converted.State;
    CancelCpuPlanning();
    ClearPlanningMarks();
    _cpuProfiles.Clear();
    _cpuActionQueue.Clear();
    pieceSetup.ClearPieces();
    _board = state.Board;
    _campaignTerritories = state.Territories;
    _terrain = state.Terrain;
    _roads.Clear();
    foreach ((int x, int y) road in state.Roads) _roads[road] = TeamName.Neutral;
    _barricades.Clear();
    foreach (KeyValuePair<(int x, int y), int> entry in state.Barricades) _barricades[entry.Key] = entry.Value;
    _mines.Clear();
    foreach (KeyValuePair<(int x, int y), TeamName> entry in state.Mines) _mines[entry.Key] = entry.Value;
    _riverBridges.Clear();
    _riverBridges.UnionWith(state.RiverBridges);
    _restoredLakeTiles.Clear();
    _teams = state.Teams.ToList();
    _playerCount = _teams.Count;
    Team.ConfigureTurnOrder(_teams.Select(team => team.TeamName));
    Team.SetCurrentTurn(state.FirstTeam.ToTeamName());
    foreach (Piece piece in state.Pieces) pieceSetup.AddPiece(piece);
    selectedPiece = null;
    _movementAnimation = null;
    _initialBuyPhase = null;
    _royalAwaitingPlacement = null;
    _isPurchaseMode = false;
    _winningTeam = null;
    _conquestScore = 0;
    _conquestScores.Clear();
    _modeScores.Clear();
    foreach (Team team in _teams)
    {
      _conquestScores[team.TeamName] = 0;
      _modeScores[team.TeamName] = 0;
    }
    foreach (CampaignTeamDefinition team in snapshot.Level.Teams.Where(team => team.Controller == CampaignTeamController.Cpu))
    {
      _cpuProfiles[team.Team.ToTeamName()] = CreateCampaignCpuProfile(team.CpuProfile,
        HashCode.Combine(_terrainSeed, _cpuMatchVariationSeed, (int)team.Team));
    }
    if (Enum.TryParse(state.GameMode, ignoreCase: false, out GameMode mode)) _gameMode = mode;
    // Campaign test play now opens exactly like a normal local match. Custom levels can still
    // include pre-placed units, terrain, and objectives, but every team without a royal chooses
    // its starting royal first and then completes the standard free-farm / buy opening.
    _initialBuysPerTurn = Globals.InitialBuysPerTurn;
    _initialBuyTurnsPerTeam = Globals.InitialBuyTurnsPerTeam;
    _farmsEnabled = Globals.FarmsEnabled;
    _cameraPosition = Vector2.Zero;
    _zoom = 1f;
    _campaignTestDefinition = CampaignLevelCloner.Clone(snapshot.Level);
    _allowedPacks.Clear();
    _allowedPacks.UnionWith(PackRules.GetAllowedPacks(_campaignTestDefinition.Restrictions.AllowedPacks));
    _campaignCompletedRounds = 0;
    _campaignTestPlay = true;
    _screen = Screen.Playing;
    StartCampaignOpeningSetup();
  }

  private static CpuProfile CreateCampaignCpuProfile(CampaignCpuProfileDefinition definition, int seed)
  {
    CpuDifficultyLevel difficulty = definition.Difficulty switch
    {
      "Easy" => CpuDifficultyLevel.Easy,
      "Hard" => CpuDifficultyLevel.Hard,
      "Best" => CpuDifficultyLevel.Best,
      _ => CpuDifficultyLevel.Medium
    };
    CpuPersonality personality = definition.Personality switch
    {
      "Aggressive" => CpuPersonality.Aggressive,
      "Defensive" => CpuPersonality.Defensive,
      "Greedy" => CpuPersonality.Greedy,
      "Reckless" => CpuPersonality.Reckless,
      "ObjectiveFocused" => CpuPersonality.ObjectiveFocused,
      "Swarmer" => CpuPersonality.Swarmer,
      _ => CpuPersonality.Balanced
    };
    CpuProfile baseline = CpuProfile.ForDifficulty(difficulty, seed);
    return new CpuProfile
    {
      Name = $"{baseline.Difficulty} {definition.Personality} CPU",
      Difficulty = baseline.Difficulty,
      Search = baseline.Search,
      Weights = baseline.Weights,
      Personality = personality,
      RandomSeed = baseline.RandomSeed,
      StrategyVariationChance = baseline.StrategyVariationChance,
      MistakeChance = baseline.MistakeChance,
      TopChoicesForRandomSelection = baseline.TopChoicesForRandomSelection
    };
  }

  private void ReturnToEditorFromTestPlay()
  {
    CancelCpuPlanning();
    ClearPlanningMarks();
    _cpuProfiles.Clear();
    _cpuActionQueue.Clear();
    selectedPiece = null;
    _movementAnimation = null;
    _royalAwaitingPlacement = null;
    _isPurchaseMode = false;
    _campaignTestDefinition = null;
    _campaignTerritories = null;
    _campaignCompletedRounds = 0;
    _campaignTestPlay = false;
    _screen = Screen.LevelEditor;
  }

  /// <summary>Starts the same royal-then-opening-buy flow used by normal local matches.</summary>
  private void StartCampaignOpeningSetup()
  {
    if (Team.ActiveTeams.Count == 0) return;
    _setupTeam = Team.ActiveTeams[0];
    StartCampaignRoyalPlacementForCurrentTeam();
  }

  private void StartCampaignRoyalPlacementForCurrentTeam()
  {
    if (pieceSetup.Pieces.Any(piece => piece.Team == _setupTeam && piece.Definition.Category == PieceCategory.Royal))
    {
      ContinueRoyalSelection();
      return;
    }
    if (_cpuProfiles.ContainsKey(_setupTeam))
    {
      PlaceCpuRoyal(_setupTeam);
      ContinueRoyalSelection();
      return;
    }

    BeginRoyalPlacement(GetCampaignStartingRoyal(_setupTeam));
  }

  private PieceDefinition GetCampaignStartingRoyal(TeamName team)
  {
    string chosenRoyal = _campaignTestDefinition?.Teams
      .FirstOrDefault(candidate => candidate.Team == team.ToNetworkTeam())?.ChosenRoyal;
    if (Enum.TryParse(chosenRoyal, ignoreCase: false, out PieceType type))
    {
      PieceDefinition configured = GetAllowedRoyals().FirstOrDefault(candidate => candidate.Type == type);
      if (configured is not null && !(_gameMode == GameMode.Escort && configured.Type == PieceType.Palace)) return configured;
    }
    return PieceDefinitions.King;
  }

  private Rectangle GetCustomLevelPanelBounds() => UiLayout.Centered(
    UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
    820,
    620,
    UiTheme.SpaceLg
  );

  private Rectangle GetCustomLevelButtonBounds(int index)
  {
    Rectangle content = UiLayout.Inset(GetCustomLevelPanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Y + 88 + index * 58, content.Width, 50);
  }

  private Rectangle GetCustomLevelsBackButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetCustomLevelPanelBounds(), UiTheme.SpaceLg);
    return new Rectangle(content.X, content.Bottom - UiTheme.ButtonHeight, 170, UiTheme.ButtonHeight);
  }

  private void UpdateCustomLevels(MouseState mouse, bool wasLeftClick, bool wasEscapePressed)
  {
    if (wasEscapePressed)
    {
      _screen = Screen.LevelEditor;
      return;
    }
    if (!wasLeftClick) return;
    Point point = ToUiPoint(mouse.Position);
    if (GetCustomLevelsBackButtonBounds().Contains(point))
    {
      _screen = Screen.LevelEditor;
      return;
    }
    for (int index = 0; index < _customLevels.Count && index < 8; index++)
    {
      if (!GetCustomLevelButtonBounds(index).Contains(point)) continue;
      CustomLevelSummary summary = _customLevels[index];
      CampaignLevelLoadResult result = CampaignLevelSerializer.Load(summary.Path);
      if (result.IsSuccess && result.Level is not null)
      {
        _levelEditor.ReplaceState(new LevelEditorState(result.Level, summary.Path));
        _screen = Screen.LevelEditor;
      }
      return;
    }
  }

  private void DrawCustomLevelsScreen()
  {
    Rectangle panel = GetCustomLevelPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("CUSTOM CAMPAIGN LEVELS", new Vector2(content.X, content.Y), UiTheme.GoldBright, 1.05f);
    _ui.Text("Saved levels are validated before they can be opened.", new Vector2(content.X, content.Y + 30), UiTheme.TextMuted, 0.66f);
    if (_customLevels.Count == 0)
    {
      _ui.Text($"No local levels yet. Save one to {CampaignLevelSerializer.LocalLevelDirectory}", new Vector2(content.X, content.Y + 90), UiTheme.TextMuted, 0.62f);
    }
    for (int index = 0; index < _customLevels.Count && index < 8; index++)
    {
      CustomLevelSummary summary = _customLevels[index];
      Rectangle bounds = GetCustomLevelButtonBounds(index);
      DrawMenuButton(bounds, string.Empty, summary.IsValid ? UiButtonTone.Neutral : UiButtonTone.Danger);
      _ui.TextFitted(summary.Name, new Vector2(bounds.X + 12, bounds.Y + 7), bounds.Width - 240, UiTheme.TextPrimary, 0.75f);
      _ui.TextFitted($"{summary.Author} - {summary.Difficulty} - v{summary.FormatVersion?.ToString() ?? "?"}", new Vector2(bounds.X + 12, bounds.Y + 28), bounds.Width - 240, UiTheme.TextMuted, 0.56f);
      _ui.RightText(summary.IsValid ? "VALID" : "INVALID", new Rectangle(bounds.X, bounds.Y, bounds.Width - 12, bounds.Height), summary.IsValid ? UiTheme.Health : UiTheme.Attack, 0.65f);
    }
    DrawMenuButton(GetCustomLevelsBackButtonBounds(), "BACK TO EDITOR", UiButtonTone.Primary);
  }

  private Rectangle GetEditorConfirmationPanelBounds() => UiLayout.Centered(
    UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
    480,
    230,
    UiTheme.SpaceLg
  );

  private Rectangle GetEditorConfirmationButtonBounds(int index)
  {
    Rectangle panel = GetEditorConfirmationPanelBounds();
    int width = (panel.Width - UiTheme.SpaceLg * 3) / 2;
    return new Rectangle(panel.X + UiTheme.SpaceLg + index * (width + UiTheme.SpaceLg), panel.Bottom - UiTheme.SpaceLg - UiTheme.ButtonHeight, width, UiTheme.ButtonHeight);
  }

  private void UpdateEditorDiscardConfirmation(MouseState mouse, bool wasLeftClick, bool wasEscapePressed)
  {
    if (wasEscapePressed)
    {
      _screen = Screen.LevelEditor;
      return;
    }
    if (!wasLeftClick) return;
    Point point = ToUiPoint(mouse.Position);
    if (GetEditorConfirmationButtonBounds(0).Contains(point))
    {
      if (_editorConfirmAction == EditorConfirmAction.New) _levelEditor.ReplaceState(LevelEditorState.CreateNew());
      else _screen = Screen.Title;
      if (_editorConfirmAction == EditorConfirmAction.New) _screen = Screen.LevelEditor;
    }
    else if (GetEditorConfirmationButtonBounds(1).Contains(point))
    {
      _screen = Screen.LevelEditor;
    }
  }

  private void DrawEditorDiscardConfirmation()
  {
    Rectangle panel = GetEditorConfirmationPanelBounds();
    DrawPanel(panel, UiTheme.Panel, UiTheme.Attack);
    _ui.CenterText("DISCARD UNSAVED LEVEL CHANGES?", new Rectangle(panel.X, panel.Y + 28, panel.Width, 28), UiTheme.GoldBright, 0.9f);
    _ui.CenterText(_editorConfirmAction == EditorConfirmAction.New
      ? "Starting a new level will lose unsaved edits."
      : "Leaving the editor will lose unsaved edits.", new Rectangle(panel.X + 20, panel.Y + 74, panel.Width - 40, 38), UiTheme.TextMuted, 0.7f);
    DrawMenuButton(GetEditorConfirmationButtonBounds(0), "DISCARD", UiButtonTone.Danger);
    DrawMenuButton(GetEditorConfirmationButtonBounds(1), "KEEP EDITING", UiButtonTone.Primary);
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
    string joinHint = string.IsNullOrWhiteSpace(_onlineError)
      ? ""
      : _onlineError;
    _ui.Text(
      joinHint,
      new Vector2(content.X, codeBounds.Bottom + 4),
      string.IsNullOrWhiteSpace(_onlineError) ? UiTheme.TextDim : UiTheme.Attack,
      0.56f
    );
    DrawMenuButton(GetOnlineJoinButtonBounds(), "JOIN", UiButtonTone.Primary);
    DrawMenuButton(GetOnlineBackButtonBounds(), "BACK", UiButtonTone.Neutral);
  }

  private void DrawOnlineWaitingScreen()
  {
    Rectangle panel = GetOnlinePanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    string roomCode = string.IsNullOrWhiteSpace(_onlineClient?.JoinCode) ? "-----" : _onlineClient.JoinCode;
    string team = _onlineClient?.Team?.ToString().ToUpperInvariant() ?? "";
    int remainingPlayers = Math.Max(0, _playerCount - 1);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.CenterText(_onlineIsHost ? "PRIVATE ROOM CREATED" : "JOINED PRIVATE ROOM", new Rectangle(content.X, content.Y, content.Width, 30), UiTheme.GoldBright, 1.05f);
    _ui.CenterText(_onlineIsHost ? "SHARE THIS ROOM CODE" : "ROOM CODE", new Rectangle(content.X, content.Y + 64, content.Width, 22), UiTheme.TextMuted, 0.76f);
    _ui.CenterText(roomCode, new Rectangle(content.X, content.Y + 94, content.Width, 52), UiTheme.GoldBright, 1.45f);
    _ui.CenterText($"YOU WILL PLAY {team}", new Rectangle(content.X, content.Y + 166, content.Width, 24), UiTheme.TextPrimary, 0.82f);
    _ui.CenterText($"Waiting for {remainingPlayers} more player(s) to join...", new Rectangle(content.X, content.Y + 214, content.Width, 22), UiTheme.TextMuted, 0.74f);
    DrawMenuButton(GetOnlineWaitingCancelButtonBounds(), "CANCEL", UiButtonTone.Neutral);
  }

  private void DrawOnlineRoyalSelectionScreen()
  {
    Rectangle panel = GetSetupPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    PieceDefinition royal = GetAllowedRoyals()[_selectedRoyalIndex];
    TeamName localTeam = _onlineClient?.Team?.ToTeamName() ?? TeamName.Red;
    Color teamColour = UiTheme.GetTeamColour(localTeam);
    bool waitingForOpponent = _onlineRoyalChoicePending;

    DrawPanel(panel, UiTheme.Panel, teamColour);
    _ui.Text(waitingForOpponent ? "ROYAL CHOSEN" : "CHOOSE YOUR ROYAL", new Vector2(content.X, content.Y), teamColour);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    _ui.Text(
      waitingForOpponent ? "Waiting for the other players to choose theirs." : "You choose only your own royal. It is placed on your back row.",
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
    _ui.StatBlock(UiLayout.HorizontalSlot(actionGrid, 2, 0, UiTheme.SpaceSm), "MOVE RANGE", UiText.FormatAction(royal.Movement), UiTheme.Move);
    _ui.StatBlock(UiLayout.HorizontalSlot(actionGrid, 2, 1, UiTheme.SpaceSm), "ATTACK RANGE", UiText.FormatAction(royal.AttackRange, royal.AttackPattern), UiTheme.Attack);
    DrawRoyalAbility(royal, content, actionGrid.Bottom + UiTheme.SpaceLg);

    if (IsDebugOnlineMatch)
    {
      DrawMenuButton(GetDebugRoyalSwitchButtonBounds(), GetDebugTeamSwitchLabel(), UiButtonTone.Accent, _debugTeamSwitchPending);
    }

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

    DrawMenuButton(GetSettingsUiScaleDecreaseButtonBounds(), "-", UiButtonTone.Neutral);
    DrawPanel(GetSettingsUiScaleValueBounds(), UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    _ui.CenterText($"UI SCALE: {MathF.Round(_uiScale * 100f):0}%", GetSettingsUiScaleValueBounds(), UiTheme.TextPrimary, 0.82f);
    DrawMenuButton(GetSettingsUiScaleIncreaseButtonBounds(), "+", UiButtonTone.Neutral);
    DrawMenuButton(
      GetSettingsZoomAnchorButtonBounds(),
      _zoomTowardsMouse ? "ZOOM TOWARDS: MOUSE" : "ZOOM TOWARDS: CAMERA CENTRE",
      _zoomTowardsMouse ? UiButtonTone.Accent : UiButtonTone.Neutral,
      _zoomTowardsMouse
    );
    DrawMenuButton(
      GetSettingsFpsCapButtonBounds(),
      $"FRAME CAP: {GetFpsCapLabel()}",
      UiButtonTone.Neutral
    );
    DrawMenuButton(
      GetSettingsResolutionButtonBounds(),
      $"RESOLUTION: {GetResolutionLabel()}",
      UiButtonTone.Neutral
    );
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

    if (_setupStage == SetupStage.Packs)
    {
      DrawPackSetup(panel);
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

    if (_setupStage == SetupStage.ModeSettings)
    {
      DrawModeSettingsSetup(panel);
      return;
    }

    PieceDefinition royal = GetAllowedRoyals()[_selectedRoyalIndex];
    Color teamColour = UiTheme.GetTeamColour(_setupTeam);
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);

    DrawPanel(panel, UiTheme.Panel, teamColour);
    _ui.Text($"{UiText.GetTeamDisplayName(_setupTeam)} CHOOSE YOUR ROYAL", new Vector2(content.X, content.Y), teamColour);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    _ui.Text("Confirm your royal, then choose its starting square on your territory.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.76f);
    _ui.Divider(content, content.Y + 56);
    DrawSetupProgress(content);

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
    _ui.StatBlock(moveStat, "MOVE RANGE", UiText.FormatAction(royal.Movement), UiTheme.Move);
    _ui.StatBlock(rangeStat, "ATTACK RANGE", UiText.FormatAction(royal.AttackRange, royal.AttackPattern), UiTheme.Attack);
    DrawRoyalAbility(royal, content, actionGrid.Bottom + UiTheme.SpaceLg);

    DrawMenuButton(GetSetupPreviousButtonBounds(), "<", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupNextButtonBounds(), ">", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONFIRM", UiButtonTone.Primary);
  }

  private Rectangle GetSetupPackButtonBounds(Pack pack)
  {
    Rectangle content = UiLayout.Inset(GetSetupPanelBounds(), UiTheme.SpaceLg);
    int index = Array.IndexOf(Enum.GetValues<Pack>(), pack);
    const int columns = 2;
    const int gap = 10;
    const int buttonHeight = 46;
    int width = (content.Width - gap) / columns;
    int row = index / columns;
    int column = index % columns;
    return new Rectangle(content.X + column * (width + gap), content.Y + 112 + row * (buttonHeight + gap), width, buttonHeight);
  }

  private PieceDefinition[] GetAllowedRoyals() => PieceDefinitions.Royals
    .Where(royal => _allowedPacks.Contains(royal.Pack))
    .ToArray();

  private void ToggleSetupPack(Pack pack)
  {
    if (_allowedPacks.Contains(pack))
    {
      if (_allowedPacks.Count <= 1) return;
      _allowedPacks.Remove(pack);
      if (GetAllowedRoyals().Length == 0)
      {
        _allowedPacks.Add(pack);
        return;
      }
      if (pack == Pack.Base) _farmsEnabled = false;
    }
    else
    {
      _allowedPacks.Add(pack);
    }
    _selectedRoyalIndex = 0;
  }

  private void DrawPackSetup(Rectangle panel)
  {
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("ALLOWED PACKS", new Vector2(content.X, content.Y), UiTheme.Gold);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    _ui.Text("Choose which unit packs can be bought and which Royals can be selected.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.72f);
    _ui.Divider(content, content.Y + 56);
    DrawSetupProgress(content);

    foreach (Pack pack in PackRules.All)
    {
      bool selected = _allowedPacks.Contains(pack);
      DrawMenuButton(GetSetupPackButtonBounds(pack), pack.ToString().ToUpperInvariant(), selected ? UiButtonTone.Primary : UiButtonTone.Neutral, selected, 0.76f);
    }

    string hint = GetAllowedRoyals().Length == 0
      ? "Select a pack containing a complete Royal before continuing."
      : $"{_allowedPacks.Count} pack{(_allowedPacks.Count == 1 ? string.Empty : "s")} enabled.";
    _ui.Text(hint, new Vector2(content.X, content.Bottom - 92), GetAllowedRoyals().Length == 0 ? UiTheme.Attack : UiTheme.TextMuted, 0.66f);
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONTINUE", GetAllowedRoyals().Length == 0 ? UiButtonTone.Danger : UiButtonTone.Primary);
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
        $"Royals start at {_escortRoyalHealthPercent}% health and respawn at their own back edge. Palace is unavailable."
      ),
      GameMode.Dominion => (
        "DOMINION",
        "Hold any of three control points across No-Man's-Land to score at the end of your turn.",
        $"Each uncontested point scores 1. First to {_dominionWinScore} wins."
      ),
      GameMode.Plunder => (
        "PLUNDER",
        "Claim the central treasure, then carry it back into your own territory.",
        $"Each delivery scores {_plunderDeliveryScore}; first to {_plunderWinScore} wins."
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
    DrawSetupProgress(content);

    Rectangle modeCard = new(content.X, content.Y + 86, content.Width, 216);
    DrawPanel(modeCard, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    _ui.CenterText(title, new Rectangle(modeCard.X, modeCard.Y + 20, modeCard.Width, 34), UiTheme.GoldBright, 1.08f);
    _ui.CenterTextWrapped(
      objective,
      new Rectangle(modeCard.X + UiTheme.SpaceLg, modeCard.Y + 64, modeCard.Width - UiTheme.SpaceLg * 2, 52),
      UiTheme.TextPrimary,
      0.68f
    );
    _ui.CenterTextWrapped(
      detail,
      new Rectangle(modeCard.X + UiTheme.SpaceLg, modeCard.Y + 124, modeCard.Width - UiTheme.SpaceLg * 2, 54),
      UiTheme.TextMuted,
      0.62f
    );
    _ui.CenterText($"{(int)_gameMode + 1}/{Enum.GetValues<GameMode>().Length}", new Rectangle(modeCard.X, modeCard.Bottom - 30, modeCard.Width, 18), UiTheme.TextDim, 0.64f);

    foreach (GameMode mode in Enum.GetValues<GameMode>())
    {
      DrawMenuButton(
        GetModeOptionBounds((int)mode),
        mode.ToString().ToUpperInvariant(),
        mode == _gameMode ? UiButtonTone.Accent : UiButtonTone.Neutral,
        mode == _gameMode
      );
    }

    Rectangle playerCountRow = GetPlayerCountRowBounds();
    DrawPanel(playerCountRow, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    _ui.Text("PLAYERS", new Vector2(playerCountRow.X + UiTheme.SpaceMd, playerCountRow.Center.Y - 10), UiTheme.TextPrimary, 0.8f);
    DrawMenuButton(GetPlayerCountDecreaseButtonBounds(), "-", UiButtonTone.Neutral);
    DrawPanel(GetPlayerCountValueBounds(), UiTheme.Panel, UiTheme.Gold);
    _ui.CenterTextFitted($"{_playerCount} PLAYERS", GetPlayerCountValueBounds(), UiTheme.GoldBright, 0.7f);
    DrawMenuButton(GetPlayerCountIncreaseButtonBounds(), "+", UiButtonTone.Neutral);
    _ui.Text("Green and Gold join from the left and right edges.", new Vector2(content.X, playerCountRow.Bottom + 8), UiTheme.TextMuted, 0.62f);

    if (_cpuOpponentSetup)
    {
      DrawMenuButton(GetCpuDifficultyRowBounds(), $"CPU DIFFICULTY: {_selectedCpuDifficulty}".ToUpperInvariant(), UiButtonTone.Accent);
      DrawMenuButton(GetCpuPersonalityRowBounds(), $"CPU STYLE: {GetCpuPersonalityName(_selectedCpuPersonality)}".ToUpperInvariant(), UiButtonTone.Neutral);
    }

    DrawMenuButton(GetSetupPreviousButtonBounds(), "<", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupNextButtonBounds(), ">", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONTINUE", UiButtonTone.Primary);
  }

  private static string GetCpuPersonalityName(CpuPersonality personality) =>
    ReferenceEquals(personality, CpuPersonality.Aggressive) ? "Aggressive" :
    ReferenceEquals(personality, CpuPersonality.Defensive) ? "Defensive" :
    ReferenceEquals(personality, CpuPersonality.Greedy) ? "Greedy" :
    ReferenceEquals(personality, CpuPersonality.Reckless) ? "Reckless" :
    ReferenceEquals(personality, CpuPersonality.ObjectiveFocused) ? "Objective Focused" :
    ReferenceEquals(personality, CpuPersonality.Swarmer) ? "Swarmer" : "Balanced";

  private void DrawBattlefieldSetup(Rectangle panel)
  {
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("BATTLEFIELD SETUP", new Vector2(content.X, content.Y), UiTheme.Gold);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    _ui.Text("Choose the battlefield before setting the match economy.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.76f);
    _ui.Divider(content, content.Y + 56);
    DrawSetupProgress(content);

    string[] labels = ["Board size", "Terrain source", "Forests", "Waterways"];
    string[] details =
    [
      "Small is compact; Large gives armies more room to manoeuvre.",
      _terrainSource switch
      {
        TerrainSource.Preset => "Selects an authored preset matching the forest and waterway headers.",
        TerrainSource.Procedural => "Generates a new terrain layout from the density settings.",
        _ => "Clears forests, lakes, and waterways for an open battlefield."
      },
      "More forests slow movement and block direct ranged fire.",
      "Waterways create choke points; bridges are the only crossings."
    ];
    string[] values =
    [
      _selectedBoardSize.ToString(),
      _terrainSource.ToString(),
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
      _ui.CenterTextFitted(values[index].ToUpperInvariant(), valueBounds, UiTheme.GoldBright, 0.76f);
      DrawMenuButton(GetBattlefieldIncreaseButtonBounds(index), "+", UiButtonTone.Neutral);
      _ui.TextFitted(details[index], new Vector2(row.X, row.Bottom + 5), content.Width, UiTheme.TextMuted, 0.6f);
    }

    string terrainHint = !string.IsNullOrWhiteSpace(_selectedTerrainPresetId)
      ? $"Selected preset: {_selectedTerrainPresetName ?? "authored map"}. Its layout is locked in."
      : _terrainSource == TerrainSource.Procedural
      ? "Light waterways use 1 river; Standard uses 2; Heavy uses 3."
      : _terrainSource == TerrainSource.Preset
        ? "Preset selection matches #! forest and #! water headers to the density settings."
        : _terrainSource == TerrainSource.None
          ? "No terrain ignores the density settings."
          : string.Empty;
    Rectangle presetButton = GetTerrainPresetBrowserButtonBounds();
    _ui.TextFitted(terrainHint, new Vector2(content.X, presetButton.Y - 27), content.Width, UiTheme.TextMuted, 0.62f);
    DrawMenuButton(
      presetButton,
      _selectedBoardSize.ToString().ToUpperInvariant() + " PRESETS",
      string.IsNullOrWhiteSpace(_selectedTerrainPresetId) ? UiButtonTone.Neutral : UiButtonTone.Accent,
      !string.IsNullOrWhiteSpace(_selectedTerrainPresetId),
      0.75f
    );
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONTINUE", UiButtonTone.Primary);
  }

  private void UpdateTerrainPresetBrowser(Point mousePosition)
  {
    int pageSize = GetTerrainPresetBrowserPageSize();
    int pageCount = Math.Max(1, (_terrainPresetBrowserPresets.Count + pageSize - 1) / pageSize);
    if (GetTerrainPresetBrowserCloseButtonBounds().Contains(mousePosition))
    {
      CloseTerrainPresetBrowser();
      return;
    }
    if (GetTerrainPresetBrowserPreviousButtonBounds().Contains(mousePosition))
    {
      _terrainPresetBrowserPage = Math.Max(0, _terrainPresetBrowserPage - 1);
      return;
    }
    if (GetTerrainPresetBrowserNextButtonBounds().Contains(mousePosition))
    {
      _terrainPresetBrowserPage = Math.Min(pageCount - 1, _terrainPresetBrowserPage + 1);
      return;
    }

    int firstIndex = _terrainPresetBrowserPage * pageSize;
    int visibleCount = Math.Min(pageSize, _terrainPresetBrowserPresets.Count - firstIndex);
    for (int visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
    {
      if (GetTerrainPresetBrowserCardBounds(visibleIndex).Contains(mousePosition))
      {
        SelectTerrainPreset(_terrainPresetBrowserPresets[firstIndex + visibleIndex]);
        return;
      }
    }
  }

  private void DrawTerrainPresetBrowser()
  {
    Rectangle panel = GetTerrainPresetBrowserPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    int pageSize = GetTerrainPresetBrowserPageSize();
    int pageCount = Math.Max(1, (_terrainPresetBrowserPresets.Count + pageSize - 1) / pageSize);
    _terrainPresetBrowserPage = Math.Clamp(_terrainPresetBrowserPage, 0, pageCount - 1);

    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text(
      $"{_selectedBoardSize.ToString().ToUpperInvariant()} TERRAIN PRESETS",
      new Vector2(content.X, content.Y),
      UiTheme.GoldBright,
      1.04f
    );
    _ui.Text(
      _terrainPresetBrowserPresets.Count == 0
        ? "No authored maps are available for this board size yet."
        : "Click a preview to choose that authored layout for this match.",
      new Vector2(content.X, content.Y + 30),
      UiTheme.TextMuted,
      0.67f
    );
    _ui.RightText(
      $"{_terrainPresetBrowserPresets.Count} MAPS  {(_terrainPresetBrowserPage + 1)}/{pageCount}",
      new Rectangle(content.X, content.Y, content.Width, 24),
      UiTheme.TextDim,
      0.62f
    );
    _ui.Divider(content, content.Y + 58);

    if (_terrainPresetBrowserPresets.Count == 0)
    {
      _ui.CenterText(
        "Use Procedural terrain or add .mctrn files for this board size.",
        GetTerrainPresetBrowserGridBounds(),
        UiTheme.TextMuted,
        0.72f
      );
    }
    else
    {
      int firstIndex = _terrainPresetBrowserPage * pageSize;
      int visibleCount = Math.Min(pageSize, _terrainPresetBrowserPresets.Count - firstIndex);
      for (int visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
      {
        BattlefieldTerrainPreset preset = _terrainPresetBrowserPresets[firstIndex + visibleIndex];
        Rectangle card = GetTerrainPresetBrowserCardBounds(visibleIndex);
        bool isSelected = string.Equals(preset.Id, _selectedTerrainPresetId, StringComparison.Ordinal);
        DrawMenuButton(card, string.Empty, isSelected ? UiButtonTone.Accent : UiButtonTone.Neutral, isSelected);
        _ui.CenterTextFitted(
          preset.Name.ToUpperInvariant(),
          new Rectangle(card.X + 8, card.Y + 7, card.Width - 16, 24),
          UiTheme.GoldBright,
          0.68f
        );
        Rectangle preview = new(
          card.X + UiTheme.SpaceSm,
          card.Y + 36,
          Math.Max(1, card.Width - UiTheme.SpaceSm * 2),
          Math.Max(20, card.Height - 84)
        );
        DrawTerrainPresetPreview(preview, preset.Terrain);
        _ui.CenterTextFitted(
          $"FORESTS: {preset.ForestDensity.ToUpperInvariant()}  WATER: {preset.WaterwayDensity.ToUpperInvariant()}",
          new Rectangle(card.X + 8, card.Bottom - 37, card.Width - 16, 16),
          UiTheme.TextMuted,
          0.49f,
          0.4f
        );
        if (isSelected)
        {
          _ui.CenterText("SELECTED", new Rectangle(card.X + 8, card.Bottom - 20, card.Width - 16, 14), UiTheme.GoldBright, 0.45f);
        }
      }
    }

    DrawMenuButton(GetTerrainPresetBrowserPreviousButtonBounds(), "< PREVIOUS", UiButtonTone.Neutral, _terrainPresetBrowserPage > 0, 0.66f);
    DrawMenuButton(GetTerrainPresetBrowserNextButtonBounds(), "NEXT >", UiButtonTone.Neutral, _terrainPresetBrowserPage < pageCount - 1, 0.66f);
    DrawMenuButton(GetTerrainPresetBrowserCloseButtonBounds(), "CLOSE", UiButtonTone.Primary, false, 0.72f);
  }

  private void DrawTerrainPresetPreview(Rectangle bounds, BattlefieldTerrain terrain)
  {
    if (_terrainPresetBrowserBoard is null)
    {
      return;
    }

    DrawPanel(bounds, UiTheme.BoardBackground, UiTheme.PanelBorderSubtle);
    int rows = _terrainPresetBrowserBoard.BoardArray.GetLength(0);
    int columns = _terrainPresetBrowserBoard.BoardArray.GetLength(1);
    int cellSize = Math.Max(1, Math.Min(
      Math.Max(1, (bounds.Width - 8) / Math.Max(1, columns)),
      Math.Max(1, (bounds.Height - 8) / Math.Max(1, rows))
    ));
    int width = columns * cellSize;
    int height = rows * cellSize;
    int startX = bounds.Center.X - width / 2;
    int startY = bounds.Center.Y - height / 2;

    for (int y = 0; y < rows; y++)
    {
      for (int x = 0; x < columns; x++)
      {
        if (_terrainPresetBrowserBoard.BoardArray[y, x] != 1)
        {
          continue;
        }

        (int x, int y) position = (x + _terrainPresetBrowserBoard.MinX, y + _terrainPresetBrowserBoard.MinY);
        Rectangle cell = new(startX + x * cellSize, startY + y * cellSize, cellSize, cellSize);
        Color colour = (x + y) % 2 == 0 ? UiTheme.DarkBoardCell : UiTheme.LightBoardCell;
        if (terrain.IsLake(position)) colour = UiTheme.Lake;
        else if (terrain.IsForest(position)) colour = UiTheme.Forest;
        DrawWorldRectangle(cell, colour, 0f);

        int riverWidth = Math.Max(1, cellSize / 4);
        if (terrain.HasRiverBetween(position, (position.x + 1, position.y)))
        {
          DrawWorldRectangle(new Rectangle(cell.Right - riverWidth, cell.Y, riverWidth, cell.Height), UiTheme.River, 0f);
        }
        if (terrain.HasRiverBetween(position, (position.x, position.y + 1)))
        {
          DrawWorldRectangle(new Rectangle(cell.X, cell.Bottom - riverWidth, cell.Width, riverWidth), UiTheme.River, 0f);
        }
      }
    }
  }

  private void DrawEconomySetup(Rectangle panel)
  {
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text("MATCH ECONOMY", new Vector2(content.X, content.Y), UiTheme.Gold);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    DrawMenuButton(GetSetupResetButtonBounds(), "RESET", UiButtonTone.Neutral);
    _ui.Text("Click a number to replace it; press Enter to keep it or use + / -.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.72f);
    _ui.Divider(content, content.Y + 56);
    DrawSetupProgress(content);

    List<string> labels = [
      "Starting cash", "Killer refund", "Defeated team refund", "Buys per buy turn", "Buy turns per team",
      "Farms", "Farm income per turn", "Unit price", "Interest", "Interest rate"
    ];
    List<string> values = [
      _startingCash.ToString(CultureInfo.InvariantCulture),
      $"{TruncateRefundMultiplier(_killerRefundMultiplier).ToString("0.##", CultureInfo.InvariantCulture)}x",
      $"{TruncateRefundMultiplier(_defeatedTeamRefundMultiplier).ToString("0.##", CultureInfo.InvariantCulture)}x",
      _initialBuysPerTurn.ToString(), _initialBuyTurnsPerTeam.ToString(),
      _farmsEnabled ? "ON" : "OFF", $"{_farmIncomePerTurn} GOLD",
      $"{_unitPricePercent}%",
      _interestEnabled ? "ON" : "OFF", $"{_interestPercent}%"
    ];
    for (int index = 0; index < labels.Count; index++)
    {
      Rectangle row = GetEconomyRowBounds(index);
      Rectangle valueBounds = GetEconomyValueBounds(index);
      DrawPanel(row, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
      _ui.Text(labels[index].ToUpperInvariant(), new Vector2(row.X + UiTheme.SpaceMd, row.Center.Y - 10), UiTheme.TextPrimary, 0.8f);
      bool isToggle = index is 5 or 8;
      bool toggleEnabled = index switch
      {
        5 => _farmsEnabled,
        8 => _interestEnabled,
        _ => false
      };
      DrawMenuButton(
        GetEconomyDecreaseButtonBounds(index),
        isToggle ? "OFF" : "-",
        isToggle && !toggleEnabled ? UiButtonTone.Danger : UiButtonTone.Neutral
      );
      bool isEditing = _economyInputIndex == index;
      DrawPanel(valueBounds, UiTheme.Panel, isEditing ? UiTheme.GoldBright : UiTheme.Gold);
      _ui.CenterTextFitted(GetEconomyEditedValue(index, values[index]), valueBounds, UiTheme.GoldBright);
      DrawMenuButton(
        GetEconomyIncreaseButtonBounds(index),
        isToggle ? "ON" : "+",
        isToggle && toggleEnabled ? UiButtonTone.Primary : UiButtonTone.Neutral
      );
    }

    string economyHint = _farmsEnabled
      ? $"Each player places two 3 x 3 farms before normal buying. Farms earn {_farmIncomePerTurn} gold at the start of each owner turn."
      : "Disable Farms to skip the opening farm placement phase.";
    if (_interestEnabled)
    {
      economyHint += $" Interest applies at the start of normal turns only: {_interestPercent}%.";
    }
    int hintY = GetEconomyRowBounds(labels.Count - 1).Bottom + UiTheme.SpaceSm;
    _ui.TextWrapped(economyHint, new Rectangle(content.X, hintY, content.Width, GetSetupConfirmButtonBounds().Y - hintY - UiTheme.SpaceSm), UiTheme.TextMuted, 0.68f);
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONTINUE", UiButtonTone.Primary);
  }

  private void DrawModeSettingsSetup(Rectangle panel)
  {
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);
    (string title, string description, string[] labels, string[] values) = _gameMode switch
    {
      GameMode.Conquest => (
        "CONQUEST RULES",
        "Units in the central zone push the control score at the end of their team's turn.",
        ["Control to win"],
        [_conquestWinScore.ToString(CultureInfo.InvariantCulture)]
      ),
      GameMode.Escort => (
        "ESCORT RULES",
        "Royals begin at the selected health and respawn at their own back edge when defeated.",
        ["Royal starting health"],
        [$"{_escortRoyalHealthPercent}%"]
      ),
      GameMode.Dominion => (
        "DOMINION RULES",
        "Three points span No-Man's-Land. Touch an uncontested point with any non-attached unit to score it.",
        ["Score to win"],
        [_dominionWinScore.ToString(CultureInfo.InvariantCulture)]
      ),
      GameMode.Plunder => (
        "PLUNDER RULES",
        "A 1 x 1 non-Royal unit spends an action beside the treasure to carry it home. Carriers move 1 less and drop it if defeated; destroying a royal costs the attacker points.",
        ["Score to win", "Points per delivery", "Royal kill penalty"],
        [
          _plunderWinScore.ToString(CultureInfo.InvariantCulture),
          _plunderDeliveryScore.ToString(CultureInfo.InvariantCulture),
          _plunderRoyalKillPenalty.ToString(CultureInfo.InvariantCulture)
        ]
      ),
      _ => (
        "REGICIDE RULES",
        "Destroy the opposing royal to win. This classic mode has no extra objective settings.",
        Array.Empty<string>(),
        Array.Empty<string>()
      )
    };

    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text(title, new Vector2(content.X, content.Y), UiTheme.Gold);
    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);
    _ui.Text("Click a timer number to type a value; press Enter to save it.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.74f);
    _ui.Divider(content, content.Y + 56);
    DrawSetupProgress(content);
    _ui.TextWrapped(description, new Rectangle(content.X, content.Y + 88, content.Width, 68), UiTheme.TextPrimary, 0.62f);

    string[] timerLabels = ["Timer", "Minutes", "Seconds", "Increment (s)"];
    string[] timerValues =
    [
      _chessTimerEnabled ? "ON" : "OFF",
      GetTimerEditedValue(0, _chessTimerMinutes.ToString(CultureInfo.InvariantCulture)),
      GetTimerEditedValue(1, _chessTimerSeconds.ToString("00", CultureInfo.InvariantCulture)),
      GetTimerEditedValue(2, _chessTimerIncrementSeconds.ToString(CultureInfo.InvariantCulture))
    ];
    string[] allLabels = labels.Concat(timerLabels).ToArray();
    string[] allValues = values.Concat(timerValues).ToArray();
    for (int index = 0; index < allLabels.Length; index++)
    {
      Rectangle row = GetModeSettingsRowBounds(index);
      Rectangle valueBounds = GetModeSettingsValueBounds(index);
      bool isTimerToggle = index == labels.Length;
      int timerInputIndex = index - labels.Length - 1;
      bool isEditingTimer = timerInputIndex >= 0 && _timerInputIndex == timerInputIndex;
      DrawPanel(row, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
      _ui.Text(allLabels[index].ToUpperInvariant(), new Vector2(row.X + UiTheme.SpaceMd, row.Center.Y - 10), UiTheme.TextPrimary, 0.8f);
      DrawMenuButton(GetModeSettingsDecreaseButtonBounds(index), isTimerToggle ? "OFF" : "-", isTimerToggle && !_chessTimerEnabled ? UiButtonTone.Danger : UiButtonTone.Neutral);
      DrawPanel(valueBounds, UiTheme.Panel, isEditingTimer ? UiTheme.GoldBright : UiTheme.Gold);
      _ui.CenterTextFitted(allValues[index], valueBounds, UiTheme.GoldBright, 0.78f);
      DrawMenuButton(GetModeSettingsIncreaseButtonBounds(index), isTimerToggle ? "ON" : "+", isTimerToggle && _chessTimerEnabled ? UiButtonTone.Primary : UiButtonTone.Neutral);
    }

    DrawMenuButton(GetSetupConfirmButtonBounds(), _onlineHostingSetup ? "HOST ROOM" : "CHOOSE ROYALS", UiButtonTone.Primary);
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
    _ui.StatBlock(statThree, "MOVE RANGE", UiText.FormatAction(definition.Movement), UiTheme.Move);
    _ui.StatBlock(statFour, "ATTACK RANGE", UiText.FormatAction(definition.AttackRange, definition.AttackPattern), UiTheme.TextPrimary);
    _ui.StatBlock(statFive, "SIZE", $"{definition.Size.x} x {definition.Size.y}", UiTheme.TextPrimary);
    _ui.StatBlock(statSix, "COST", definition.Cost == 0 ? "START" : definition.Cost.ToString(), UiTheme.GoldBright);

    int abilityY = statRowTwo.Bottom + 5;
    if (abilityY + 16 <= details.Bottom)
    {
      _ui.Text(GetEncyclopediaAbilityText(definition), new Vector2(details.X, abilityY), UiTheme.TextMuted, 0.64f);
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
      PieceType.Crossbowman => "CROSSBOWMAN",
      PieceType.Knight => "KNIGHT",
      _ => type.ToString().ToUpperInvariant()
    };
  }

  private string GetEncyclopediaAbilityText(PieceDefinition definition)
  {
    return GetUnitAbilityText(definition);
  }

  private string GetUnitAbilityText(PieceDefinition definition)
  {
    if (definition.Type == PieceType.Farm)
    {
      return $"Earns {_farmIncomePerTurn} gold at the start of each owner turn. Units may move and attack over it.";
    }

    return string.IsNullOrWhiteSpace(definition.AbilityDescription)
      ? "No special ability."
      : definition.AbilityDescription;
  }

  private void DrawRoyalAbility(PieceDefinition royal, Rectangle content, int y)
  {
    Rectangle previousButton = GetSetupPreviousButtonBounds();
    int abilityBottom = IsDebugOnlineMatch
      ? GetDebugRoyalSwitchButtonBounds().Y - UiTheme.SpaceMd
      : previousButton.Y - UiTheme.SpaceMd;
    _ui.Text("ROYAL ABILITY", new Vector2(content.X, y), UiTheme.Gold, 0.74f);
    _ui.TextWrapped(
      GetUnitAbilityText(royal),
      new Rectangle(
        content.X,
        y + 24,
        content.Width,
        Math.Max(0, abilityBottom - y - 24)
      ),
      UiTheme.TextPrimary,
      0.72f
    );
  }

  private void DrawGameOverScreen()
  {
    TeamName winner = _winningTeam ?? TeamName.Red;
    string message = $"{UiText.GetTeamDisplayName(winner)} WINS";
    string reason = _gameMode switch
    {
      GameMode.Conquest => "Their control pushed the conquest bar to its side.",
      GameMode.Escort => "Their royal reached the opposing back edge.",
      GameMode.Dominion => "They held enough uncontested control points.",
      GameMode.Plunder => "They returned enough treasure to their territory.",
      _ => "The opposing royal has fallen."
    };
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    Color winnerColour = UiTheme.GetTeamColour(winner);
    _ui.CenterText(message, new Rectangle(viewport.X, viewport.Center.Y - 110, viewport.Width, 42), winnerColour, 1.3f);
    _ui.CenterText(reason, new Rectangle(viewport.X, viewport.Center.Y - 54, viewport.Width, 24), UiTheme.TextPrimary, 0.85f);
    DrawMenuButton(GetTitleButtonBounds(4), _campaignTestPlay ? "RETURN TO EDITOR" : "RETURN TO TITLE", UiButtonTone.Primary);
    DrawMenuButton(GetTitleButtonBounds(5), "QUIT GAME", UiButtonTone.Danger);
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
      case Screen.LevelEditor: _levelEditor.Draw(GetUiViewport()); break;
      case Screen.CustomLevels: DrawCustomLevelsScreen(); break;
      case Screen.EditorDiscardConfirm: DrawEditorDiscardConfirmation(); break;
    }

    if (_terrainPresetBrowserOpen)
    {
      DrawTerrainPresetBrowser();
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
    int desiredHeight;
    if (_initialBuyPhase is not null)
    {
      desiredHeight = 100 + _teams.Count * 36 + UiTheme.SpaceSm + UiTheme.ButtonHeight + UiTheme.SpaceMd * 2;
    }
    else
    {
      int contentHeight = 94 + _teams.Count * 36;
      if (_gameMode == GameMode.Conquest)
      {
        contentHeight += _playerCount > 2 ? 20 + _teams.Count * 26 : 60;
      }
      else if (_gameMode is GameMode.Dominion or GameMode.Plunder)
      {
        contentHeight += 20 + _teams.Count * 26;
      }
      if (_onlineClient is not null)
      {
        contentHeight += 18;
      }
      contentHeight += UiTheme.SpaceSm + UiTheme.ButtonHeight;
      desiredHeight = contentHeight + UiTheme.SpaceMd * 2;
    }
    if (IsDebugOnlineMatch)
    {
      desiredHeight += UiTheme.ButtonHeight + UiTheme.SpaceSm;
    }
    int height = Math.Min(desiredHeight, Math.Max(1, viewport.Height - UiTheme.SpaceLg * 2));
    return new Rectangle(UiTheme.SpaceLg, UiTheme.SpaceLg, width, height);
  }

  private Rectangle GetChessClockPanelBounds()
  {
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    int width = Math.Min(300, Math.Max(190, viewport.Width - UiTheme.SpaceLg * 2));
    int height = 42 + _teams.Count * 32 + UiTheme.SpaceMd * 2;
    return new Rectangle(
      viewport.Right - UiTheme.SpaceLg - width,
      viewport.Bottom - UiTheme.SpaceLg - height,
      width,
      height
    );
  }

  private Rectangle GetInitialBuyStopButtonBounds()
  {
    Rectangle panel = GetStatusPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceMd);
    int bottomOffset = IsDebugOnlineMatch ? UiTheme.ButtonHeight * 2 + UiTheme.SpaceSm : UiTheme.ButtonHeight;
    return new Rectangle(content.X, content.Bottom - bottomOffset, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetSkipTurnButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetStatusPanelBounds(), UiTheme.SpaceMd);
    int bottomOffset = IsDebugOnlineMatch ? UiTheme.ButtonHeight * 2 + UiTheme.SpaceSm : UiTheme.ButtonHeight;
    return new Rectangle(content.X, content.Bottom - bottomOffset, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetDebugTeamSwitchButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetStatusPanelBounds(), UiTheme.SpaceMd);
    return new Rectangle(content.X, content.Bottom - UiTheme.ButtonHeight, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetSelectedPiecePanelBounds()
  {
    Rectangle status = GetStatusPanelBounds();
    Rectangle viewport = UiLayout.Viewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    int desiredHeight = selectedPiece == null
      ? 124
      : selectedPiece.Definition.Type is PieceType.Engineer or PieceType.Ox or PieceType.Guard ? 520 : 400;
    int height = Math.Min(desiredHeight, Math.Max(1, viewport.Bottom - status.Bottom - UiTheme.SpaceLg * 2));
    return new Rectangle(status.X, status.Bottom + UiTheme.SpaceMd, status.Width, height);
  }

  private Rectangle GetOxCargoButtonBounds()
  {
    Rectangle control = GetOxCargoControlBounds();
    return new Rectangle(control.X + UiTheme.SpaceSm, control.Bottom - UiTheme.ButtonHeight - UiTheme.SpaceSm, control.Width - UiTheme.SpaceSm * 2, UiTheme.ButtonHeight);
  }

  private Rectangle GetOxCargoControlBounds()
  {
    Rectangle content = UiLayout.Inset(GetSelectedPiecePanelBounds(), UiTheme.SpaceMd);
    int height = Math.Min(122, Math.Max(80, content.Height - 280));
    return new Rectangle(content.X, content.Bottom - height, content.Width, height);
  }

  private Rectangle GetGuardControlBounds()
  {
    Rectangle content = UiLayout.Inset(GetSelectedPiecePanelBounds(), UiTheme.SpaceMd);
    int height = Math.Min(92, Math.Max(72, content.Height - 280));
    return new Rectangle(content.X, content.Bottom - height, content.Width, height);
  }

  private Rectangle GetMercenaryFireButtonBounds()
  {
    Rectangle content = UiLayout.Inset(GetSelectedPiecePanelBounds(), UiTheme.SpaceMd);
    return new Rectangle(content.X, content.Bottom - UiTheme.ButtonHeight, content.Width, UiTheme.ButtonHeight);
  }

  private Rectangle GetEngineerAbilityBounds()
  {
    Rectangle content = UiLayout.Inset(GetSelectedPiecePanelBounds(), UiTheme.SpaceMd);
    const int height = 82;
    return new Rectangle(content.X, Math.Min(content.Y + 362, content.Bottom - height), content.Width, height);
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

    if (_royalAwaitingPlacement is not null)
    {
      _ui.Text("PLACE YOUR ROYAL", new Vector2(content.X, content.Y), UiTheme.Gold);
      _ui.Divider(content, content.Y + 30);
      _ui.Text(
        $"{UiText.GetTeamDisplayName(_setupTeam)} {_royalAwaitingPlacement.Type.ToString().ToUpperInvariant()}",
        new Vector2(content.X, content.Y + 43),
        UiTheme.GetTeamColour(_setupTeam),
        0.78f
      );
      _ui.TextWrapped(
        "Click an empty, traversable square in your territory to choose its starting position. The opening farm placement begins next.",
        new Rectangle(content.X, content.Y + 74, content.Width, panel.Bottom - content.Y - 86),
        UiTheme.TextPrimary,
        0.66f
      );
      return;
    }

    if (_initialBuyPhase != null)
    {
      int buyTurnNumber = _initialBuyPhase.GetBuyTurnsUsed(Team.CurrentTurn) + 1;
      _ui.TextFitted(
        _initialBuyPhase.IsFarmPlacementPhase ? "OPENING FARM PLACEMENT" : "INITIAL BUY PHASE",
        new Vector2(content.X, content.Y),
        content.Width,
        UiTheme.Gold
      );
      _ui.Divider(content, content.Y + 30);
      _ui.Text(
        _initialBuyPhase.IsFarmPlacementPhase
          ? $"{UiText.GetTeamDisplayName(Team.CurrentTurn)}: {_initialBuyPhase.GetFarmsPlaced(Team.CurrentTurn)}/2 FARMS"
          : $"{UiText.GetTeamDisplayName(Team.CurrentTurn)} BUY TURN {buyTurnNumber}/{_initialBuyPhase.BuyTurnsPerTeam}",
        new Vector2(content.X, content.Y + 43),
        turnColour,
        0.7f
      );
      _ui.TextFitted(
        _initialBuyPhase.IsFarmPlacementPhase
          ? "PLACE TWO FARMS ON YOUR SIDE"
          : $"{_initialBuyPhase.PurchasesThisTurn}/{_initialBuyPhase.PurchasesPerTurn} UNITS THIS TURN",
        new Vector2(content.X, content.Y + 66),
        content.Width,
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

      if (_initialBuyPhase.CanStopCurrentBuyer)
      {
        DrawMenuButton(GetInitialBuyStopButtonBounds(), "STOP BUYING", UiButtonTone.Danger);
      }
      if (IsDebugOnlineMatch)
      {
        DrawMenuButton(GetDebugTeamSwitchButtonBounds(), GetDebugTeamSwitchLabel(), UiButtonTone.Accent, _debugTeamSwitchPending);
      }
      return;
    }

    _ui.Text($"{UiText.GetTeamDisplayName(Team.CurrentTurn)} TURN", new Vector2(content.X, content.Y), turnColour);
    _ui.Divider(content, content.Y + 30);
    if (!Globals.ActionLimitsEnabled)
    {
      _ui.Text("UNLIMITED ACTIONS", new Vector2(content.X, content.Y + 43), UiTheme.TextMuted, 0.74f);
    }
    else
    {
      _ui.Text("ACTION POINTS", new Vector2(content.X, content.Y + 43), UiTheme.TextMuted, 0.74f);

      for (int index = 0; index < currentTeam.ActionLimit; index++)
      {
        Rectangle actionPoint = new(content.X + index * 34, content.Y + 66, 26, 12);
        _spriteBatch.Draw(
          _pixel,
          actionPoint,
          index < currentTeam.ActionPoints ? turnColour : UiTheme.PanelBorderSubtle
        );
      }

      _ui.Text(
        $"{currentTeam.ActionPoints}/{currentTeam.ActionLimit} REMAINING",
        new Vector2(content.X + 116, content.Y + 61),
        UiTheme.TextPrimary,
        0.76f
      );
    }

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
      int roomStatusY = GetSkipTurnButtonBounds().Y - 18;
      _ui.Text($"ONLINE  ROOM: {roomCode}", new Vector2(content.X, roomStatusY), UiTheme.Gold, 0.68f);
      if (IsDebugOnlineMatch)
      {
        DrawMenuButton(GetDebugTeamSwitchButtonBounds(), GetDebugTeamSwitchLabel(), UiButtonTone.Accent, _debugTeamSwitchPending);
      }
    }

    bool canSkipTurn = IsOnlineLocalTurn() && CanSkipCurrentTurn(currentTeam);
    DrawMenuButton(
      GetSkipTurnButtonBounds(),
      canSkipTurn ? "END TURN" : "END TURN",
      canSkipTurn ? UiButtonTone.Primary : UiButtonTone.Neutral
    );

    if (_gameMode == GameMode.Conquest)
    {
      DrawConquestControlBar(new Rectangle(content.X, moneyY + UiTheme.SpaceXs, content.Width, _playerCount > 2 ? 20 + _teams.Count * 26 : 48));
    }
    else if (_gameMode == GameMode.Dominion)
    {
      DrawModeScoreboard(new Rectangle(content.X, moneyY + UiTheme.SpaceXs, content.Width, 20 + _teams.Count * 26), "DOMINION SCORE", _dominionWinScore);
    }
    else if (_gameMode == GameMode.Plunder)
    {
      DrawModeScoreboard(new Rectangle(content.X, moneyY + UiTheme.SpaceXs, content.Width, 20 + _teams.Count * 26), "PLUNDER SCORE", _plunderWinScore);
    }
  }

  private void DrawChessClockPanel()
  {
    if (!_chessTimerEnabled)
    {
      return;
    }

    Rectangle panel = GetChessClockPanelBounds();
    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceMd);
    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);
    _ui.Text(
      _chessTimerIncrementSeconds > 0 ? $"CHESS CLOCK  +{_chessTimerIncrementSeconds}s" : "CHESS CLOCK",
      new Vector2(content.X, content.Y),
      UiTheme.Gold,
      0.72f
    );

    int rowY = content.Y + 28;
    foreach (Team team in _teams)
    {
      Color teamColour = UiTheme.GetTeamColour(team.TeamName);
      Rectangle row = new(content.X, rowY, content.Width, 26);
      DrawPanel(row, UiTheme.PanelRaised, team.TeamName == Team.CurrentTurn ? teamColour : UiTheme.PanelBorderSubtle);
      _ui.LabelValueRow(row, UiText.GetTeamDisplayName(team.TeamName), FormatClock(team.TeamName), teamColour);
      rowY += 32;
    }
  }

  private void DrawModeScoreboard(Rectangle bounds, string title, int scoreToWin)
  {
    _ui.Text(title, new Vector2(bounds.X, bounds.Y), UiTheme.Gold, 0.7f);
    int rowY = bounds.Y + 20;
    foreach (Team team in _teams)
    {
      Color colour = UiTheme.GetTeamColour(team.TeamName);
      Rectangle row = new(bounds.X, rowY, bounds.Width, 22);
      DrawPanel(row, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
      _ui.LabelValueRow(row, UiText.GetTeamDisplayName(team.TeamName), $"{_modeScores.GetValueOrDefault(team.TeamName)}/{scoreToWin}", colour);
      rowY += 26;
    }
  }

  private void DrawConquestControlBar(Rectangle bounds)
  {
    if (_playerCount > 2)
    {
      _ui.Text("CONQUEST SCORE", new Vector2(bounds.X, bounds.Y), UiTheme.Gold, 0.7f);
      int rowY = bounds.Y + 20;
      foreach (Team team in _teams)
      {
        Color colour = UiTheme.GetTeamColour(team.TeamName);
        Rectangle row = new(bounds.X, rowY, bounds.Width, 22);
        DrawPanel(row, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
        _ui.LabelValueRow(row, UiText.GetTeamDisplayName(team.TeamName), $"{_conquestScores.GetValueOrDefault(team.TeamName)}/{_conquestWinScore}", colour);
        rowY += 26;
      }
      return;
    }

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
        _ui.TextFitted(
          _initialBuyPhase.IsFarmPlacementPhase ? "Place two farms from the right panel." : "Choose a unit from the right panel.",
          new Vector2(content.X, content.Y + 44),
          content.Width,
          UiTheme.Gold,
          0.76f
        );
        _ui.TextFitted(
          _initialBuyPhase.CanStopCurrentBuyer ? "Use STOP BUYING when this team is done." : "Normal buying begins after all farms are placed.",
          new Vector2(content.X, content.Y + 68),
          content.Width,
          UiTheme.TextMuted,
          0.68f
        );
        return;
      }

      _ui.Text("Gold squares: move", new Vector2(content.X, content.Y + 44), UiTheme.Move, 0.8f);
      _ui.Text("Red squares: attack", new Vector2(content.X, content.Y + 68), UiTheme.Attack, 0.8f);
      _ui.Text("ALT + RIGHT-CLICK: plan", new Vector2(content.X, content.Y + 92), UiTheme.TextMuted, 0.66f);
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

    bool canActWithSelectedPiece = CanActWithPiece(selectedPiece);
    Rectangle actionGrid = new(content.X, preview.Bottom + UiTheme.SpaceMd, content.Width, 48);
    _ui.StatBlock(
      UiLayout.HorizontalSlot(actionGrid, 2, 0, UiTheme.SpaceSm),
      "MOVE RANGE",
      UiText.FormatAction(selectedPiece.Definition.Movement),
      UiTheme.Move
    );
    _ui.StatBlock(
      UiLayout.HorizontalSlot(actionGrid, 2, 1, UiTheme.SpaceSm),
      selectedPiece.Definition.Type == PieceType.Engineer ? "ABILITY" : "ATTACK",
      canActWithSelectedPiece && selectedPiece.HasAttackedThisTurn
        ? "USED"
        : selectedPiece.Definition.Type == PieceType.Engineer
          ? $"{_selectedEngineerAbility.ToString().ToUpperInvariant()} ({2 - selectedPiece.EngineerBuildsThisTurn})"
          : selectedPiece.Definition.Attack.ToString(),
      canActWithSelectedPiece && selectedPiece.HasAttackedThisTurn ? UiTheme.TextDim : UiTheme.Attack
    );
    Rectangle rangeRow = new(content.X, actionGrid.Bottom + UiTheme.SpaceSm, content.Width, 44);
    _ui.StatBlock(
      rangeRow,
      selectedPiece.Definition.Type == PieceType.Engineer ? "BUILD RANGE" : "ATTACK RANGE",
      UiText.FormatAction(selectedPiece.Definition.AttackRange, selectedPiece.Definition.AttackPattern),
      UiTheme.TextPrimary
    );
    _ui.Text(
      !canActWithSelectedPiece ? "VIEWING PIECE - ACTIONS UNAVAILABLE" : selectedPiece.HasMovedThisTurn ? "MOVE USED THIS TURN" : "LEFT-CLICK gold to move",
      new Vector2(content.X, rangeRow.Bottom + UiTheme.SpaceMd),
      !canActWithSelectedPiece || selectedPiece.HasMovedThisTurn ? UiTheme.TextDim : UiTheme.Move,
      0.78f
    );
    _ui.Text(
      canActWithSelectedPiece ? GetSelectedPieceControlHint(selectedPiece) : "SELECT AN ACTIVE TEAM PIECE TO ACT",
      new Vector2(content.X, rangeRow.Bottom + UiTheme.SpaceMd + 23),
      canActWithSelectedPiece ? UiTheme.Attack : UiTheme.TextMuted,
      0.72f
    );

    int abilityInfoY = rangeRow.Bottom + UiTheme.SpaceMd + 44;
    int abilityInfoBottom = selectedPiece.Definition.Type switch
    {
      PieceType.Engineer => GetEngineerAbilityBounds().Y - UiTheme.SpaceSm,
      PieceType.Ox => GetOxCargoButtonBounds().Y - UiTheme.SpaceSm,
      PieceType.Guard => GetGuardControlBounds().Y - UiTheme.SpaceSm,
      PieceType.Mercenary => GetMercenaryFireButtonBounds().Y - UiTheme.SpaceSm,
      _ => content.Bottom - UiTheme.SpaceSm
    };
    string abilityText = $"ABILITY: {GetUnitAbilityText(selectedPiece.Definition)}";
    _ui.TextWrapped(
      abilityText,
      new Rectangle(content.X, abilityInfoY, content.Width, Math.Max(0, abilityInfoBottom - abilityInfoY)),
      UiTheme.TextPrimary,
      0.58f
    );

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

    if (selectedPiece.Definition.Type == PieceType.Guard)
    {
      DrawGuardControls();
      return;
    }

    if (selectedPiece.Definition.Type == PieceType.Mercenary)
    {
      DrawMercenaryFireControl();
    }

  }

  private void DrawOxCarryControls()
  {
    Piece cargo = GetOxCargo(selectedPiece);
    Rectangle control = GetOxCargoControlBounds();
    DrawPanel(control, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    if (cargo == null)
    {
      _ui.Text("ATTACHMENT: NONE", new Vector2(control.X + UiTheme.SpaceSm, control.Y + UiTheme.SpaceSm), UiTheme.Gold, 0.72f);
      _ui.TextWrapped(
        "RIGHT-CLICK a friendly 1 x 1 unit to attach. That unit gains +2 Movement; when it is attacked, the Ox takes the same damage.",
        new Rectangle(control.X + UiTheme.SpaceSm, control.Y + 30, control.Width - UiTheme.SpaceSm * 2, Math.Max(0, control.Height - 36)),
        UiTheme.TextMuted,
        0.62f
      );
      return;
    }

    Rectangle button = GetOxCargoButtonBounds();
    _ui.Text($"ATTACHED TO: {cargo.Definition.Type.ToString().ToUpperInvariant()}", new Vector2(control.X + UiTheme.SpaceSm, control.Y + UiTheme.SpaceSm), UiTheme.Gold, 0.66f);
    _ui.TextWrapped(
      "The host gains +2 Movement. The Ox moves with the host and takes the same incoming damage. Select the host below.",
      new Rectangle(control.X + UiTheme.SpaceSm, control.Y + 30, control.Width - UiTheme.SpaceSm * 2, Math.Max(0, button.Y - control.Y - 34)),
      UiTheme.TextMuted,
      0.56f
    );
    DrawMenuButton(button, "SELECT HOST", UiButtonTone.Accent);
  }

  private void DrawGuardControls()
  {
    Rectangle control = GetGuardControlBounds();
    DrawPanel(control, UiTheme.PanelRaised, UiTheme.PanelBorderSubtle);
    Piece protectedPiece = selectedPiece.AttachedTo;
    if (protectedPiece == null)
    {
      _ui.Text("PROTECTION: UNASSIGNED", new Vector2(control.X + UiTheme.SpaceSm, control.Y + UiTheme.SpaceSm), UiTheme.Gold, 0.68f);
      _ui.TextWrapped(
        "RIGHT-CLICK an adjacent friendly non-Royal unit to protect it.",
        new Rectangle(control.X + UiTheme.SpaceSm, control.Y + 30, control.Width - UiTheme.SpaceSm * 2, Math.Max(0, control.Height - 36)),
        UiTheme.TextMuted,
        0.64f
      );
      return;
    }

    _ui.Text($"PROTECTING: {protectedPiece.Definition.Type.ToString().ToUpperInvariant()}", new Vector2(control.X + UiTheme.SpaceSm, control.Y + UiTheme.SpaceSm), UiTheme.Gold, 0.68f);
    _ui.TextWrapped(
      "The Guard follows this unit and takes incoming damage before it does.",
      new Rectangle(control.X + UiTheme.SpaceSm, control.Y + 30, control.Width - UiTheme.SpaceSm * 2, Math.Max(0, control.Height - 36)),
      UiTheme.TextMuted,
      0.64f
    );
  }

  private void DrawMercenaryFireControl()
  {
    bool canFire = CanFireSelectedMercenary();
    DrawMenuButton(
      GetMercenaryFireButtonBounds(),
      canFire ? "FIRE" : "FIRE UNAVAILABLE",
      canFire ? UiButtonTone.Danger : UiButtonTone.Neutral
    );
  }

  private bool CanFireSelectedMercenary() => selectedPiece?.Definition.Type == PieceType.Mercenary &&
    selectedPiece.Team == Team.CurrentTurn && !selectedPiece.HasAttackedThisTurn && IsOnlineLocalTurn();

  private void DrawEngineerAbilityControls()
  {
    Rectangle row = GetEngineerAbilityBounds();
    Rectangle valueBounds = GetEngineerAbilityValueBounds();
    (string title, string detail) = _selectedEngineerAbility switch
    {
      EngineerAbility.Barrier => ("BARRIER", "20 HP wall; blocks movement and attacks."),
      EngineerAbility.Mine => ("MINE", "Place on an adjacent empty square; a triggering mine deals 30 damage in a 3 x 3 area."),
      EngineerAbility.Demolish => ("DEMOLISH", "Remove an adjacent road, barrier, or mine without triggering it."),
      _ => ("ROAD", "Build on an adjacent empty square; only your team gains the road's movement benefit.")
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
    return ox.AttachmentKind == AttachmentKind.Carried ? ox.AttachedTo : null;
  }

  private string GetSelectedPieceControlHint(Piece piece)
  {
    if (IsTreasureCarrier(piece))
    {
      return "CARRYING TREASURE - MOVE IS REDUCED BY 1; REACH YOUR TERRITORY";
    }

    if (_treasurePosition is (int treasureX, int treasureY) treasure && CanPickUpTreasure(piece, treasure))
    {
      return "RIGHT-CLICK THE TREASURE TO PICK IT UP";
    }

    if (piece.Definition.Type == PieceType.Farm)
    {
      return $"STRUCTURE - EARNS {_farmIncomePerTurn} GOLD EACH OWNER TURN; UNITS PASS THROUGH";
    }

    if (piece.Definition.Type == PieceType.Engineer)
    {
      return piece.HasAttackedThisTurn && _selectedEngineerAbility != EngineerAbility.Demolish
        ? "ABILITY USED THIS TURN"
        : "RIGHT-CLICK to use the selected ability";
    }

    if (piece.HasAttackedThisTurn)
    {
      return "ATTACK USED THIS TURN";
    }

    if (piece.Definition.Type == PieceType.Elephant)
    {
      return "MOVE over red squares to attack";
    }

    return piece.Definition.Type switch
    {
      PieceType.Guard => "RIGHT-CLICK ally to protect",
      PieceType.Ox => GetOxCargo(piece) == null
        ? "RIGHT-CLICK friendly 1 x 1 unit to attach"
        : "ATTACHED - HOST GAINS +2 MOVE",
      PieceType.Spy => "RIGHT-CLICK to use special",
      PieceType.Mercenary => "RIGHT-CLICK this unit to fire; enemies to attack",
      _ => "RIGHT-CLICK red to attack"
    };
  }

  protected override void Draw(GameTime gameTime)
  {
    bool drawsGameView = _screen == Screen.Playing || IsInGameOverlayScreen();
    GraphicsDevice.Clear(drawsGameView ? UiTheme.BoardBackground : UiTheme.MenuBackground);

    if (!drawsGameView)
    {
      _spriteBatch.Begin(transformMatrix: Matrix.CreateScale(_uiScale));
      DrawMenuScreen();
      _spriteBatch.End();
      base.Draw(gameTime);
      return;
    }

    Matrix cameraTransform = CreateCameraTransform();
    _visibleWorldBounds = GetVisibleWorldBounds(cameraTransform);
    EnsureStaticBattlefield();

    _spriteBatch.Begin(SpriteSortMode.FrontToBack, transformMatrix: cameraTransform);

    /* Build Board */
    var BoardArray = _board.BoardArray;
    int cellSize = 64;
    bool hasControllableSelectedCache = selectedPiece is not null &&
      selectedPiece == _cachedSelectedPiece && CanActWithPiece(selectedPiece);
    HashSet<(int x, int y)> validMovementSquares = hasControllableSelectedCache
      ? _cachedSelectedMovementSquares
      : [];
    HashSet<(int x, int y)> validAttackSquares = hasControllableSelectedCache
      ? _cachedSelectedAttackSquares
      : [];

    Rectangle cachedSource = Rectangle.Intersect(
      _visibleWorldBounds,
      new Rectangle(0, 0, _staticBattlefield.Width, _staticBattlefield.Height)
    );
    if (!cachedSource.IsEmpty)
    {
      _spriteBatch.Draw(_staticBattlefield, cachedSource, cachedSource, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.1f);
    }

    for (int y = 0; y < BoardArray.GetLength(0); y++)
    {
      for (int x = 0; x < BoardArray.GetLength(1); x++)
      {
        if (BoardArray[y, x] == 1)
        {
          var boardPosition = (x: x + _board.MinX, y: y + _board.MinY);
          Rectangle cellBounds = new(x * cellSize, y * cellSize, cellSize, cellSize);
          if (!IsVisibleWorldBounds(cellBounds)) continue;
          bool isValidMove = validMovementSquares.Contains(boardPosition);
          bool isValidAttack = validAttackSquares.Contains(boardPosition);

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
            if (HasRiverBridgeBetween(boardPosition, belowPosition))
            {
              DrawWorldRectangle(
                new Rectangle(cellBounds.X + 6, cellBounds.Bottom - 7, cellBounds.Width - 12, 14),
                UiTheme.Bridge,
                0.106f
              );
            }
          }

          if (_roads.TryGetValue(boardPosition, out TeamName roadOwner))
          {
            bool roadIsInForest = _terrain.IsForest(boardPosition);
            DrawWorldRectangle(
              new Rectangle(
                roadIsInForest ? cellBounds.X + 6 : cellBounds.X,
                cellBounds.Center.Y - (roadIsInForest ? 8 : 5),
                roadIsInForest ? cellBounds.Width - 12 : cellBounds.Width,
                roadIsInForest ? 16 : 10
              ),
              roadOwner == TeamName.Neutral
                ? roadIsInForest ? UiTheme.ForestRoad : UiTheme.Road
                : Color.Lerp(UiTheme.GetTeamColour(roadOwner), roadIsInForest ? UiTheme.ForestRoad : UiTheme.Road, 0.55f),
              0.11f
            );
            if (roadIsInForest)
            {
              DrawWorldRectangle(
                new Rectangle(cellBounds.X + 8, cellBounds.Center.Y - 2, cellBounds.Width - 16, 4),
                UiTheme.RoadHighlight,
                0.111f
              );
            }
          }

          if (_barricades.ContainsKey(boardPosition))
          {
            DrawWorldRectangle(
              new Rectangle(cellBounds.X + 8, cellBounds.Y + 16, cellBounds.Width - 16, cellBounds.Height - 32),
              UiTheme.Barricade,
              0.115f
            );
            int barrierHealthWidth = (cellBounds.Width - 16) * _barricades[boardPosition] / AbilityRules.EngineerBarrierHealth;
            DrawWorldRectangle(
              new Rectangle(cellBounds.X + 8, cellBounds.Bottom - 13, barrierHealthWidth, 3),
              UiTheme.Health,
              0.116f
            );
          }

          if (_mines.TryGetValue(boardPosition, out TeamName mineOwner))
          {
            DrawWorldRectangle(
              new Rectangle(cellBounds.Center.X - 7, cellBounds.Center.Y - 7, 14, 14),
              UiTheme.GetTeamColour(mineOwner),
              0.116f
            );
            DrawWorldOutline(
              new Rectangle(cellBounds.Center.X - 9, cellBounds.Center.Y - 9, 18, 18),
              UiTheme.MineOutline,
              0.117f
            );
          }

          if (_gameMode == GameMode.Plunder && _treasurePosition == boardPosition)
          {
            Rectangle treasure = new(cellBounds.Center.X - 10, cellBounds.Center.Y - 10, 20, 20);
            DrawWorldRectangle(treasure, UiTheme.GoldBright, 0.117f);
            DrawWorldOutline(treasure, UiTheme.Shadow, 0.118f);
          }

          if (isValidMove)
          {
            DrawWorldRectangle(cellBounds, UiTheme.MoveOverlay, 0.118f);
          }

          if (isValidAttack)
          {
            DrawWorldOutline(cellBounds, UiTheme.AttackOutline, 0.119f);
          }

        }
      }
    }

    DrawPurchasePlacementPreview(cellSize);
    DrawRoyalPlacementPreview(cellSize);

    if (selectedPiece != null && IsVisibleWorldBounds(GetPieceWorldBounds(selectedPiece, cellSize)))
    {
      DrawWorldOutline(GetPieceWorldBounds(selectedPiece, cellSize), UiTheme.SelectionOutline, 0.134f);
    }

    /* Draw Pieces */

    foreach (Piece piece in pieceSetup.Pieces
      .Where(piece => piece.AttachedTo is null)
      .OrderBy(piece => piece.Definition.Type == PieceType.Farm ? 0 : 1))
    {
      Rectangle pieceBounds = GetPieceWorldBounds(piece, cellSize);
      if (!IsVisibleWorldBounds(pieceBounds)) continue;
      Color colour = UiTheme.GetTeamColour(piece.Team);
      float pieceDepth = piece.Definition.Type == PieceType.Farm ? 0.108f : 0.12f;

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
          pieceDepth
      );

      DrawWorldOutline(pieceBounds, Color.Lerp(colour, UiTheme.Shadow, 0.45f), pieceDepth + 0.001f);
      if (IsTreasureCarrier(piece))
      {
        Rectangle treasureBadge = new(pieceBounds.Right - 20, pieceBounds.Y + 5, 15, 15);
        DrawWorldRectangle(treasureBadge, UiTheme.GoldBright, 0.127f);
        DrawWorldOutline(treasureBadge, UiTheme.Shadow, 0.128f);
      }
    }

    // Attached units share their host's board position. Rendering them as badges keeps
    // both identities visible without stacking full sprites on top of each other.
    foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo != null))
    {
      Rectangle badge = GetAttachmentBadgeWorldBounds(attachment, cellSize);
      if (!IsVisibleWorldBounds(badge)) continue;
      Color outline = attachment.AttachmentKind == AttachmentKind.Guard ? UiTheme.Gold : UiTheme.TextPrimary;
      DrawWorldRectangle(badge, UiTheme.GetTeamColour(attachment.Team), 0.125f);
      DrawWorldOutline(badge, outline, 0.126f);
    }

    DrawSpyMarkIndicators(cellSize);
    DrawPlanningMarks(cellSize);
    DrawAvailableUnitHighlights(cellSize);

    _spriteBatch.End();

    _spriteBatch.Begin();

    DrawWorldPieceText(cameraTransform, cellSize);

    _spriteBatch.End();
    _spriteBatch.Begin(transformMatrix: Matrix.CreateScale(_uiScale));

    if (_screen == Screen.Playing)
    {
      DrawStatusPanel();
      DrawSelectedPiecePanel();
      if (_royalAwaitingPlacement is null)
      {
        DrawPurchasePanel();
      }
      DrawChessClockPanel();
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
