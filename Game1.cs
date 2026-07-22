using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics.PackedVector;

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
  private Board _board;
  private readonly PieceSetup pieceSetup = new();
  private List<Team> _teams = [];
  private Piece selectedPiece;
  private static readonly Color darkCellColour = new(181, 136, 99);
  private static readonly Color lightCellColour = new(240, 217, 181);
  private static readonly Color redTerritoryColour = new(220, 80, 80);
  private static readonly Color blueTerritoryColour = new(80, 125, 220);
  private static readonly Color noMansLandColour = new(130, 130, 130);
  private static readonly Color uiPanelColour = new(20, 24, 34, 238);
  private static readonly Color uiPanelBorderColour = new(111, 151, 192);
  private static readonly Color uiMutedTextColour = new(185, 198, 214);
  private static readonly Color moveOverlayColour = new(246, 214, 88, 150);
  private static readonly Color attackOverlayColour = new(232, 76, 76, 175);
  private static readonly Color selectedOutlineColour = new(255, 224, 104);
  private const int noMansLandHalfHeight = 2;
  private const float territoryTintAmount = 0.2f;
  private const int purchasePanelWidth = 340;
  private const int purchasePanelHeight = 450;
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
          TryPurchaseAndPlace(targetPosition, pieceAtTarget);
        }
      }
      else if (selectedPiece == null)
      {
        if (pieceAtTarget?.Team == Team.CurrentTurn)
        {
          selectedPiece = pieceAtTarget;

          Console.WriteLine(
            $"Selected {selectedPiece.Team} {selectedPiece.Definition.Type}."
          );
        }
      }
      else if (pieceAtTarget == selectedPiece)
      {
        selectedPiece = null;
      }
      else if (
        pieceAtTarget != null &&
        pieceAtTarget.Team == Team.CurrentTurn
      )
      {
        selectedPiece = pieceAtTarget;

        Console.WriteLine(
          $"Selected {selectedPiece.Team} {selectedPiece.Definition.Type}."
        );
      }
      else
      {
        if (wasLeftClick)
        {
          var movementOffset = (
                  x: targetPosition.x - selectedPiece.Position.x,
                  y: targetPosition.y - selectedPiece.Position.y
                );

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
            Actions.ValidActionSquares(selectedPiece, true)
              .Contains(movementOffset) &&
            pieceAtTarget == null;

          if (isValidMove)
          {
            selectedPiece.Position = targetPosition;

            Console.WriteLine(
              $"Moved piece to ({boardX}, {boardY})."
            );

            CompleteAction();
          }

          selectedPiece = null;
        }
        else if (wasRightClick)
        {
          var attackOffset = (
           x: targetPosition.x - selectedPiece.Position.x,
           y: targetPosition.y - selectedPiece.Position.y
          );

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
            Actions.ValidActionSquares(selectedPiece, false)
              .Contains(attackOffset) &&
            pieceAtTarget != null &&
            pieceAtTarget.Team != selectedPiece.Team;

          if (isValidAttack)
          {
            Actions.Attack(selectedPiece, pieceAtTarget);

            Team attackingTeam = _teams.Find(team => team.TeamName == selectedPiece.Team);
            Team defeatedTeam = _teams.Find(team => team.TeamName == pieceAtTarget.Team);
            if (Actions.HandlePieceDeath(
              pieceAtTarget,
              attackingTeam,
              defeatedTeam,
              _killerRefundMultiplier,
              _defeatedTeamRefundMultiplier
            ))
            {
              pieceSetup.RemovePiece(pieceAtTarget);

              if (pieceAtTarget.Definition.Category == PieceCategory.Royal)
              {
                _winningTeam = selectedPiece.Team;
                _screen = Screen.GameOver;
              }
            }

            Console.WriteLine(
              $"Attacked {pieceAtTarget.Team} {pieceAtTarget.Definition.Type} at ({boardX}, {boardY})."
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

    _previousMouseState = mouse;
    _previousKeyboardState = keyboard;

    base.Update(gameTime);
  }

  private void TryPurchaseAndPlace((int x, int y) targetPosition, Piece pieceAtTarget)
  {
    int arrayX = targetPosition.x - _board.MinX;
    int arrayY = targetPosition.y - _board.MinY;
    PieceDefinition definition = PieceDefinitions.Purchasable[_selectedPurchaseIndex];
    Team buyingTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);

    bool canPlace =
      IsBoardCell(arrayX, arrayY) &&
      GetSquareOwner(arrayY) == Team.CurrentTurn &&
      pieceAtTarget == null &&
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
    int panelHeight = Math.Min(
      purchasePanelHeight,
      Math.Max(200, GraphicsDevice.Viewport.Height - 40)
    );

    return new Rectangle(
      Math.Max(20, GraphicsDevice.Viewport.Width - purchasePanelWidth - 20),
      20,
      purchasePanelWidth,
      panelHeight
    );
  }

  private Rectangle GetPreviousPurchaseButtonBounds()
  {
    Rectangle panel = GetPurchasePanelBounds();
    return new Rectangle(panel.X + 20, panel.Bottom - 115, 60, 40);
  }

  private Rectangle GetNextPurchaseButtonBounds()
  {
    Rectangle panel = GetPurchasePanelBounds();
    return new Rectangle(panel.Right - 80, panel.Bottom - 115, 60, 40);
  }

  private Rectangle GetPurchaseButtonBounds()
  {
    Rectangle panel = GetPurchasePanelBounds();
    return new Rectangle(panel.X + 100, panel.Bottom - 115, 140, 40);
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
    const int borderThickness = 2;
    _spriteBatch.Draw(_pixel, bounds, fill);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, borderThickness), border);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Bottom - borderThickness, bounds.Width, borderThickness), border);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, borderThickness, bounds.Height), border);
    _spriteBatch.Draw(_pixel, new Rectangle(bounds.Right - borderThickness, bounds.Y, borderThickness, bounds.Height), border);
  }

  private void DrawProgressBar(Rectangle bounds, float progress, Color fill)
  {
    progress = MathHelper.Clamp(progress, 0f, 1f);
    _spriteBatch.Draw(_pixel, bounds, new Color(8, 10, 15, 220));
    _spriteBatch.Draw(
      _pixel,
      new Rectangle(bounds.X + 2, bounds.Y + 2, (int)((bounds.Width - 4) * progress), bounds.Height - 4),
      fill
    );
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

  private void DrawPurchasePanel()
  {
    Rectangle panel = GetPurchasePanelBounds();
    Rectangle previousButton = GetPreviousPurchaseButtonBounds();
    Rectangle nextButton = GetNextPurchaseButtonBounds();
    Rectangle purchaseButton = GetPurchaseButtonBounds();
    PieceDefinition definition = PieceDefinitions.Purchasable[_selectedPurchaseIndex];

    DrawPanel(panel, uiPanelColour, _isPurchaseMode ? Color.Gold : uiPanelBorderColour);

    _spriteBatch.DrawString(
      _pieceLabelFont,
      "PURCHASE PIECE",
      new Vector2(panel.X + 20, panel.Y + 18),
      Color.Gold
    );
    _spriteBatch.Draw(
      _pixel,
      new Rectangle(panel.X + 20, panel.Y + 45, panel.Width - 40, 1),
      uiPanelBorderColour
    );

    Color previewColour = Team.CurrentTurn == TeamName.Red ? Color.Red : Color.Blue;
    Rectangle previewBounds = new(panel.X + 20, panel.Y + 55, 80, 80);
    _spriteBatch.Draw(_pixel, previewBounds, previewColour);

    string label = UiText.BuildPieceLabel(definition);
    Vector2 labelSize = _pieceLabelFont.MeasureString(label);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      label,
      new Vector2(
        previewBounds.Center.X - labelSize.X / 2f,
        previewBounds.Center.Y - labelSize.Y / 2f
      ),
      Color.White
    );

    _spriteBatch.DrawString(
      _pieceLabelFont,
      definition.Type.ToString(),
      new Vector2(panel.X + 120, panel.Y + 60),
      Color.White
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      definition.Category.ToString(),
      new Vector2(panel.X + 120, panel.Y + 84),
      uiMutedTextColour
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"Cost: {definition.Cost}",
      new Vector2(panel.X + 120, panel.Y + 108),
      Color.Gold
    );

    float statY = panel.Y + 160;
    _spriteBatch.DrawString(_pieceLabelFont, "HEALTH", new Vector2(panel.X + 20, statY), uiMutedTextColour);
    _spriteBatch.DrawString(_pieceLabelFont, definition.Health.ToString(), new Vector2(panel.X + 105, statY), Color.White);
    _spriteBatch.DrawString(_pieceLabelFont, "ATTACK", new Vector2(panel.X + 180, statY), new Color(255, 155, 155));
    _spriteBatch.DrawString(_pieceLabelFont, definition.Attack.ToString(), new Vector2(panel.X + 266, statY), Color.White);

    _spriteBatch.DrawString(_pieceLabelFont, "MOVE", new Vector2(panel.X + 20, statY + 36), uiMutedTextColour);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      UiText.FormatAction(definition.Movement),
      new Vector2(panel.X + 20, statY + 58),
      Color.White
    );
    _spriteBatch.DrawString(_pieceLabelFont, "RANGE", new Vector2(panel.X + 180, statY + 36), uiMutedTextColour);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      UiText.FormatAction(definition.AttackShape),
      new Vector2(panel.X + 180, statY + 58),
      Color.White
    );

    _spriteBatch.DrawString(_pieceLabelFont, "SIZE", new Vector2(panel.X + 20, statY + 94), uiMutedTextColour);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"{definition.Size.x} x {definition.Size.y}",
      new Vector2(panel.X + 82, statY + 94),
      Color.White
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "BUY, THEN CLICK A SQUARE",
      new Vector2(panel.X + 20, panel.Bottom - 170),
      uiMutedTextColour
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "ON YOUR SIDE.",
      new Vector2(panel.X + 20, panel.Bottom - 146),
      uiMutedTextColour
    );

    _spriteBatch.Draw(_pixel, previousButton, new Color(65, 70, 85));
    _spriteBatch.Draw(_pixel, nextButton, new Color(65, 70, 85));
    _spriteBatch.Draw(
      _pixel,
      purchaseButton,
      _isPurchaseMode ? new Color(135, 65, 65) : new Color(65, 125, 75)
    );

    DrawCenteredString("<", previousButton, Color.White);
    DrawCenteredString(">", nextButton, Color.White);
    DrawCenteredString(_isPurchaseMode ? "CANCEL" : "BUY", purchaseButton, Color.White);
  }

  private void DrawCenteredString(string text, Rectangle bounds, Color colour)
  {
    Vector2 textSize = _pieceLabelFont.MeasureString(text);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      text,
      new Vector2(
        bounds.Center.X - textSize.X / 2f,
        bounds.Center.Y - textSize.Y / 2f
      ),
      colour
    );
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
    const int buttonWidth = 280;
    const int buttonHeight = 50;
    return new Rectangle(
      GraphicsDevice.Viewport.Width / 2 - buttonWidth / 2,
      GraphicsDevice.Viewport.Height / 2 + index * 65,
      buttonWidth,
      buttonHeight
    );
  }

  private Rectangle GetSettingsPanelBounds()
  {
    const int width = 600;
    const int height = 500;
    return new Rectangle(
      GraphicsDevice.Viewport.Width / 2 - width / 2,
      GraphicsDevice.Viewport.Height / 2 - height / 2,
      width,
      height
    );
  }

  private Rectangle GetSettingsBindingBounds(int index)
  {
    Rectangle panel = GetSettingsPanelBounds();
    return new Rectangle(panel.X + 20, panel.Y + 70 + index * 40, panel.Width - 40, 34);
  }

  private Rectangle GetSettingsRotationButtonBounds()
  {
    Rectangle panel = GetSettingsPanelBounds();
    return new Rectangle(panel.X + 20, panel.Bottom - 105, panel.Width - 40, 38);
  }

  private Rectangle GetSettingsBackButtonBounds()
  {
    Rectangle panel = GetSettingsPanelBounds();
    return new Rectangle(panel.X + 20, panel.Bottom - 55, panel.Width - 40, 38);
  }

  private Rectangle GetSetupPanelBounds()
  {
    const int width = 560;
    const int height = 440;
    return new Rectangle(
      GraphicsDevice.Viewport.Width / 2 - width / 2,
      GraphicsDevice.Viewport.Height / 2 - height / 2,
      width,
      height
    );
  }

  private Rectangle GetSetupPreviousButtonBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    return new Rectangle(panel.X + 35, panel.Bottom - 105, 80, 44);
  }

  private Rectangle GetSetupNextButtonBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    return new Rectangle(panel.Right - 115, panel.Bottom - 105, 80, 44);
  }

  private Rectangle GetSetupConfirmButtonBounds()
  {
    Rectangle panel = GetSetupPanelBounds();
    return new Rectangle(panel.X + 150, panel.Bottom - 105, panel.Width - 300, 44);
  }

  private Rectangle GetEconomyDecreaseButtonBounds(int index)
  {
    Rectangle panel = GetSetupPanelBounds();
    return new Rectangle(panel.X + 330, panel.Y + 95 + index * 55, 55, 38);
  }

  private Rectangle GetEconomyIncreaseButtonBounds(int index)
  {
    Rectangle panel = GetSetupPanelBounds();
    return new Rectangle(panel.X + 460, panel.Y + 95 + index * 55, 55, 38);
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
          pieceSetup.AddPiece(new Piece(royal, FindRoyalSpawn(_setupTeam), _setupTeam));

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

  private (int x, int y) FindRoyalSpawn(TeamName teamName)
  {
    int arrayY = teamName == TeamName.Red
      ? _board.BoardArray.GetLength(0) - 1
      : 0;
    int centreX = _board.BoardArray.GetLength(1) / 2;

    for (int offset = 0; offset < _board.BoardArray.GetLength(1); offset++)
    {
      int[] candidateXs = offset == 0
        ? [centreX]
        : [centreX - offset, centreX + offset];

      foreach (int arrayX in candidateXs)
      {
        if (arrayX >= 0 &&
            arrayX < _board.BoardArray.GetLength(1) &&
            IsBoardCell(arrayX, arrayY))
        {
          var position = (x: arrayX + _board.MinX, y: arrayY + _board.MinY);
          if (pieceSetup.GetPieceAt(position) == null)
          {
            return position;
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

  private void DrawMenuButton(Rectangle bounds, string label, Color colour)
  {
    DrawPanel(bounds, colour, Color.Lerp(colour, Color.White, 0.35f));
    DrawCenteredString(label, bounds, Color.White);
  }

  private void DrawTitleScreen()
  {
    Vector2 titleSize = _pieceLabelFont.MeasureString("MEDIEVAL CHESS");
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "MEDIEVAL CHESS",
      new Vector2(
        GraphicsDevice.Viewport.Width / 2f - titleSize.X / 2f,
        GraphicsDevice.Viewport.Height / 2f - 120
      ),
      Color.Gold
    );

    DrawMenuButton(GetTitleButtonBounds(0), "START GAME", new Color(65, 125, 75));
    DrawMenuButton(GetTitleButtonBounds(1), "SETTINGS", new Color(65, 70, 85));
    DrawMenuButton(GetTitleButtonBounds(2), "QUIT GAME", new Color(135, 65, 65));
  }

  private void DrawSettingsScreen()
  {
    Rectangle panel = GetSettingsPanelBounds();
    DrawPanel(panel, uiPanelColour, uiPanelBorderColour);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "SETTINGS",
      new Vector2(panel.X + 20, panel.Y + 20),
      Color.Gold
    );

    BindingAction[] actions = Enum.GetValues<BindingAction>();
    for (int index = 0; index < actions.Length; index++)
    {
      BindingAction action = actions[index];
      Rectangle bounds = GetSettingsBindingBounds(index);
      bool isWaitingForKey = _bindingToChange == action;
      _spriteBatch.Draw(
        _pixel,
        bounds,
        isWaitingForKey ? new Color(145, 110, 45) : new Color(65, 70, 85)
      );

      string text = isWaitingForKey
        ? $"{GetBindingLabel(action)}: press a key"
        : $"{GetBindingLabel(action)}: {GetBinding(action)}";
      _spriteBatch.DrawString(
        _pieceLabelFont,
        text,
        new Vector2(bounds.X + 10, bounds.Y + 7),
        Color.White
      );
    }

    DrawMenuButton(
      GetSettingsRotationButtonBounds(),
      _rotateBoard ? "BOARD ROTATION: 90 degrees" : "BOARD ROTATION: 0 degrees",
      new Color(65, 70, 85)
    );
    DrawMenuButton(GetSettingsBackButtonBounds(), "BACK", new Color(65, 125, 75));
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
    Color teamColour = _setupTeam == TeamName.Red ? Color.Red : Color.Blue;

    DrawPanel(panel, uiPanelColour, teamColour);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"{_setupTeam} CHOOSE YOUR ROYAL",
      new Vector2(panel.X + 20, panel.Y + 20),
      teamColour
    );

    Rectangle preview = new(panel.X + 35, panel.Y + 70, 110, 110);
    _spriteBatch.Draw(_pixel, preview, teamColour);
    string label = UiText.BuildPieceLabel(royal);
    Vector2 labelSize = _pieceLabelFont.MeasureString(label);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      label,
      new Vector2(preview.Center.X - labelSize.X / 2f, preview.Center.Y - labelSize.Y / 2f),
      Color.White
    );

    float statX = panel.X + 180;
    _spriteBatch.DrawString(_pieceLabelFont, royal.Type.ToString(), new Vector2(statX, panel.Y + 75), Color.White);
    _spriteBatch.DrawString(_pieceLabelFont, $"Health: {royal.Health}", new Vector2(statX, panel.Y + 105), Color.White);
    _spriteBatch.DrawString(_pieceLabelFont, $"Attack: {royal.Attack}", new Vector2(statX, panel.Y + 135), Color.White);
    _spriteBatch.DrawString(_pieceLabelFont, $"Move: {UiText.FormatAction(royal.Movement)}", new Vector2(panel.X + 35, panel.Y + 215), Color.White);
    _spriteBatch.DrawString(_pieceLabelFont, $"Attack range: {UiText.FormatAction(royal.AttackShape)}", new Vector2(panel.X + 35, panel.Y + 245), Color.White);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "Your royal will spawn on your back row.",
      new Vector2(panel.X + 35, panel.Y + 295),
      Color.LightGray
    );

    DrawMenuButton(GetSetupPreviousButtonBounds(), "<", new Color(65, 70, 85));
    DrawMenuButton(GetSetupNextButtonBounds(), ">", new Color(65, 70, 85));
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONFIRM", new Color(65, 125, 75));
  }

  private void DrawEconomySetup(Rectangle panel)
  {
    DrawPanel(panel, uiPanelColour, Color.Gold);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "MATCH ECONOMY",
      new Vector2(panel.X + 20, panel.Y + 20),
      Color.Gold
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "Choose the starting resources and unit-death refunds.",
      new Vector2(panel.X + 20, panel.Y + 55),
      Color.LightGray
    );

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
      float rowY = panel.Y + 104 + index * 55;
      _spriteBatch.DrawString(
        _pieceLabelFont,
        labels[index],
        new Vector2(panel.X + 25, rowY + 7),
        Color.White
      );
      DrawMenuButton(GetEconomyDecreaseButtonBounds(index), "-", new Color(65, 70, 85));
      DrawMenuButton(GetEconomyIncreaseButtonBounds(index), "+", new Color(65, 70, 85));
      DrawCenteredString(
        values[index],
        new Rectangle(panel.X + 390, (int)rowY, 65, 38),
        Color.Gold
      );
    }

    _spriteBatch.DrawString(
      _pieceLabelFont,
      "Refunds are based on the defeated unit's cost.",
      new Vector2(panel.X + 25, panel.Bottom - 165),
      Color.LightGray
    );
    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONTINUE", new Color(65, 125, 75));
  }

  private void DrawGameOverScreen()
  {
    string message = $"{_winningTeam} WINS";
    Vector2 messageSize = _pieceLabelFont.MeasureString(message);
    Color winnerColour = _winningTeam == TeamName.Red ? Color.Red : Color.Blue;
    _spriteBatch.DrawString(
      _pieceLabelFont,
      message,
      new Vector2(
        GraphicsDevice.Viewport.Width / 2f - messageSize.X / 2f,
        GraphicsDevice.Viewport.Height / 2f - 80
      ),
      winnerColour
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "The opposing royal has fallen.",
      new Vector2(
        GraphicsDevice.Viewport.Width / 2f - _pieceLabelFont.MeasureString("The opposing royal has fallen.").X / 2f,
        GraphicsDevice.Viewport.Height / 2f - 45
      ),
      Color.White
    );
    DrawMenuButton(GetTitleButtonBounds(2), "QUIT GAME", new Color(135, 65, 65));
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

  private void DrawStatusPanel()
  {
    Rectangle panel = new(20, 20, 320, 185);
    Team currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);
    Color turnColour = Team.CurrentTurn == TeamName.Red ? Color.Red : Color.Blue;

    DrawPanel(panel, uiPanelColour, turnColour);
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"{Team.CurrentTurn.ToString().ToUpperInvariant()} TURN",
      new Vector2(panel.X + 16, panel.Y + 14),
      turnColour
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "ACTION POINTS",
      new Vector2(panel.X + 16, panel.Y + 48),
      uiMutedTextColour
    );

    for (int index = 0; index < Team.ActionsPerTurn; index++)
    {
      Rectangle actionPoint = new(panel.X + 16 + index * 34, panel.Y + 74, 26, 12);
      _spriteBatch.Draw(
        _pixel,
        actionPoint,
        index < currentTeam.ActionPoints ? turnColour : new Color(59, 67, 80)
      );
    }

    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"{currentTeam.ActionPoints}/{Team.ActionsPerTurn} remaining",
      new Vector2(panel.X + 16, panel.Y + 96),
      uiMutedTextColour
    );

    float moneyY = panel.Y + 130;
    foreach (Team team in _teams)
    {
      Color teamColour = team.TeamName == TeamName.Red ? Color.Red : Color.Blue;
      _spriteBatch.DrawString(
        _pieceLabelFont,
        $"{team.TeamName}: {team.Money}",
        new Vector2(panel.X + 16, moneyY),
        teamColour
      );
      moneyY += 22;
    }
  }

  private void DrawSelectedPiecePanel()
  {
    Rectangle panel = new(20, 225, 320, selectedPiece == null ? 115 : 270);
    DrawPanel(panel, uiPanelColour, selectedPiece == null ? uiPanelBorderColour : selectedOutlineColour);

    if (selectedPiece == null)
    {
      _spriteBatch.DrawString(
        _pieceLabelFont,
        "SELECT A PIECE",
        new Vector2(panel.X + 16, panel.Y + 16),
        Color.White
      );
      _spriteBatch.DrawString(
        _pieceLabelFont,
        "Gold squares: move",
        new Vector2(panel.X + 16, panel.Y + 48),
        new Color(255, 226, 115)
      );
      _spriteBatch.DrawString(
        _pieceLabelFont,
        "Red squares: attack",
        new Vector2(panel.X + 16, panel.Y + 72),
        new Color(255, 140, 140)
      );
      return;
    }

    Color teamColour = selectedPiece.Team == TeamName.Red ? Color.Red : Color.Blue;
    Rectangle preview = new(panel.X + 16, panel.Y + 48, 62, 62);
    _spriteBatch.Draw(_pixel, preview, teamColour);
    string label = UiText.BuildPieceLabel(selectedPiece.Definition);
    DrawCenteredString(label, preview, Color.White);

    _spriteBatch.DrawString(
      _pieceLabelFont,
      selectedPiece.Definition.Type.ToString().ToUpperInvariant(),
      new Vector2(panel.X + 94, panel.Y + 16),
      Color.White
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      selectedPiece.Team.ToString(),
      new Vector2(panel.X + 94, panel.Y + 42),
      teamColour
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"HP {selectedPiece.CurrentHealth}/{selectedPiece.Definition.Health}",
      new Vector2(panel.X + 94, panel.Y + 68),
      Color.White
    );
    DrawProgressBar(
      new Rectangle(panel.X + 94, panel.Y + 94, 180, 12),
      selectedPiece.CurrentHealth / (float)Math.Max(1, selectedPiece.Definition.Health),
      Color.LimeGreen
    );

    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"MOVE: {UiText.FormatAction(selectedPiece.Definition.Movement)}",
      new Vector2(panel.X + 16, panel.Y + 132),
      new Color(255, 226, 115)
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"ATTACK: {selectedPiece.Definition.Attack} damage",
      new Vector2(panel.X + 16, panel.Y + 158),
      new Color(255, 140, 140)
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"RANGE: {UiText.FormatAction(selectedPiece.Definition.AttackShape)}",
      new Vector2(panel.X + 16, panel.Y + 184),
      uiMutedTextColour
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "LEFT-CLICK gold to move",
      new Vector2(panel.X + 16, panel.Y + 218),
      new Color(255, 226, 115)
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      "RIGHT-CLICK red to attack",
      new Vector2(panel.X + 16, panel.Y + 242),
      new Color(255, 140, 140)
    );
  }

  protected override void Draw(GameTime gameTime)
  {
    GraphicsDevice.Clear(_screen == Screen.Playing ? Color.CornflowerBlue : Color.Black);

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
    var validMovementOffsets = selectedPiece == null
      ? null
      : Actions.ValidActionSquares(selectedPiece, true);
    var validAttackOffsets = selectedPiece == null
      ? null
      : Actions.ValidActionSquares(selectedPiece, false);

    for (int y = 0; y < BoardArray.GetLength(0); y++)
    {
      for (int x = 0; x < BoardArray.GetLength(1); x++)
      {
        if (BoardArray[y, x] == 1)
        {
          var boardPosition = (x: x + _board.MinX, y: y + _board.MinY);
          Piece pieceAtBoardPosition = pieceSetup.GetPieceAt(boardPosition);
          bool isValidMove =
            selectedPiece != null &&
            validMovementOffsets != null &&
            validMovementOffsets.Contains((
              boardPosition.x - selectedPiece.Position.x,
              boardPosition.y - selectedPiece.Position.y
            )) &&
            pieceAtBoardPosition == null;
          bool isValidAttack =
            selectedPiece != null &&
            validAttackOffsets != null &&
            validAttackOffsets.Contains((
              boardPosition.x - selectedPiece.Position.x,
              boardPosition.y - selectedPiece.Position.y
            )) &&
            pieceAtBoardPosition != null &&
            pieceAtBoardPosition.Team != selectedPiece.Team;

          Color baseCellColour =
            (x + y) % 2 == 0
            ? darkCellColour
            : lightCellColour;
          TeamName? squareOwner = GetSquareOwner(y);
          Color territoryColour =
            squareOwner == TeamName.Red
            ? redTerritoryColour
            : squareOwner == TeamName.Blue
              ? blueTerritoryColour
              : noMansLandColour;

          Rectangle cellBounds = new(x * cellSize, y * cellSize, cellSize, cellSize);
          DrawWorldRectangle(
            cellBounds,
            Color.Lerp(baseCellColour, territoryColour, territoryTintAmount),
            0.1f
          );

          if (isValidMove)
          {
            DrawWorldRectangle(cellBounds, moveOverlayColour, 0.102f);
          }

          if (isValidAttack)
          {
            DrawWorldRectangle(cellBounds, attackOverlayColour, 0.103f);
          }

          if (selectedPiece != null && selectedPiece.Position == boardPosition)
          {
            DrawWorldOutline(cellBounds, selectedOutlineColour, 0.106f);
          }
        }
      }
    }

    /* Draw Pieces */

    foreach (Piece piece in pieceSetup.Pieces)
    {
      int pieceX = piece.Position.x - _board.MinX;
      int pieceY = piece.Position.y - _board.MinY;
      Color colour;

      if (piece.Team == TeamName.Red) { colour = Color.Red; }
      else if (piece.Team == TeamName.Blue) { colour = Color.Blue; }
      else { Console.WriteLine($"{piece} doesnt have a team"); colour = Color.White; }

      _spriteBatch.Draw(
          _pixel,
          new Rectangle(
              pieceX * cellSize + 5,
              pieceY * cellSize + 5,
              cellSize - 10,
              cellSize - 10
          ),
          null,
          colour,
          0f,
          Vector2.Zero,
          SpriteEffects.None,
          0.11f
      );

      int pieceHealthBarWidth = cellSize - 16;
      float healthRatio = piece.CurrentHealth / (float)Math.Max(1, piece.Definition.Health);
      DrawWorldRectangle(
        new Rectangle(pieceX * cellSize + 8, pieceY * cellSize + cellSize - 12, pieceHealthBarWidth, 5),
        new Color(10, 12, 16, 220),
        0.121f
      );
      DrawWorldRectangle(
        new Rectangle(
          pieceX * cellSize + 8,
          pieceY * cellSize + cellSize - 12,
          (int)(pieceHealthBarWidth * MathHelper.Clamp(healthRatio, 0f, 1f)),
          5
        ),
        Color.LimeGreen,
        0.122f
      );

      string label = UiText.BuildPieceLabel(piece.Definition);
      Vector2 labelSize = _pieceLabelFont.MeasureString(label);

      _spriteBatch.DrawString(
        _pieceLabelFont,
        label,
        new Vector2(
          pieceX * cellSize + cellSize / 2f - labelSize.X / 2f,
          pieceY * cellSize + cellSize / 2f - labelSize.Y / 2f
        ),
        Color.White,
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
          pieceX * cellSize + cellSize / 2f - healthSize.X / 2f,
          pieceY * cellSize + 6
        ),
        Color.White,
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
