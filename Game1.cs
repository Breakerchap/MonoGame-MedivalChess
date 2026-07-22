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
  private readonly GraphicsDeviceManager _graphics;
  private SpriteBatch _spriteBatch;
  private Texture2D _pixel;
  private SpriteFont _pieceLabelFont;
  private Board _board;
  private readonly PieceSetup pieceSetup = new();
  private List<Team> _teams = [];
  private Piece selectedPiece;
  private static readonly Color darkCellColour = new(181, 136, 99);
  private static readonly Color darkHighlightCellColour = new(220, 195, 75);
  private static readonly Color lightCellColour = new(240, 217, 181);
  private static readonly Color lightHighlightCellColour = new(246, 235, 114);
  private static readonly Color attackableCellColour = new(245, 56, 56);
  private static readonly Color redTerritoryColour = new(220, 80, 80);
  private static readonly Color blueTerritoryColour = new(80, 125, 220);
  private static readonly Color noMansLandColour = new(130, 130, 130);
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

    float cameraSpeed = 500f;
    float zoomSpeed = 1f;

    // Move camera
    if (keyboard.IsKeyDown(Keys.A))
      _cameraPosition.X -= cameraSpeed * deltaTime / _zoom;

    if (keyboard.IsKeyDown(Keys.D))
      _cameraPosition.X += cameraSpeed * deltaTime / _zoom;

    if (keyboard.IsKeyDown(Keys.W))
      _cameraPosition.Y -= cameraSpeed * deltaTime / _zoom;

    if (keyboard.IsKeyDown(Keys.S))
      _cameraPosition.Y += cameraSpeed * deltaTime / _zoom;

    // Screen centre
    Vector2 screenCentre = new Vector2(
      GraphicsDevice.Viewport.Width / 2f,
      GraphicsDevice.Viewport.Height / 2f
    );

    // Current camera transform
    Matrix cameraTransform =
      Matrix.CreateTranslation(
        -_cameraPosition.X,
        -_cameraPosition.Y,
        0
      )
      * Matrix.CreateScale(_zoom)
      * Matrix.CreateTranslation(
        screenCentre.X,
        screenCentre.Y,
        0
      );

    Vector2 mouseScreen = mouse.Position.ToVector2();

    // Find which world position is currently under the mouse
    Vector2 mouseWorldBefore = Vector2.Transform(
      mouseScreen,
      Matrix.Invert(cameraTransform)
    );

    // Change zoom
    if (keyboard.IsKeyDown(Keys.E))
      _zoom += zoomSpeed * deltaTime * _zoom;

    if (keyboard.IsKeyDown(Keys.Q))
      _zoom -= zoomSpeed * deltaTime * _zoom;

    _zoom = MathHelper.Clamp(_zoom, 0.2f, 5f);

    // Rebuild transform after changing zoom
    cameraTransform =
      Matrix.CreateTranslation(
        -_cameraPosition.X,
        -_cameraPosition.Y,
        0
      )
      * Matrix.CreateScale(_zoom)
      * Matrix.CreateTranslation(
        screenCentre.X,
        screenCentre.Y,
        0
      );

    // Find where the mouse points after zooming
    Vector2 mouseWorldAfter = Vector2.Transform(
      mouseScreen,
      Matrix.Invert(cameraTransform)
    );

    // Move camera so the same world point stays under the mouse
    _cameraPosition += mouseWorldBefore - mouseWorldAfter;

    bool wasLeftClick =
      mouse.LeftButton == ButtonState.Pressed &&
      _previousMouseState.LeftButton == ButtonState.Released;

    bool wasRightClick =
      mouse.RightButton == ButtonState.Pressed &&
      _previousMouseState.RightButton == ButtonState.Released;

    bool wasPurchaseModeToggle =
      keyboard.IsKeyDown(Keys.B) &&
      !_previousKeyboardState.IsKeyDown(Keys.B);
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
            if (Actions.HandlePieceDeath(pieceAtTarget, attackingTeam))
            {
              pieceSetup.RemovePiece(pieceAtTarget);
            }

            Console.WriteLine(
              $"Attacked {pieceAtTarget.Team} {pieceAtTarget.Definition.Type} at ({boardX}, {boardY})."
            );

            CompleteAction();
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

  private void DrawPurchasePanel()
  {
    Rectangle panel = GetPurchasePanelBounds();
    Rectangle previousButton = GetPreviousPurchaseButtonBounds();
    Rectangle nextButton = GetNextPurchaseButtonBounds();
    Rectangle purchaseButton = GetPurchaseButtonBounds();
    PieceDefinition definition = PieceDefinitions.Purchasable[_selectedPurchaseIndex];

    _spriteBatch.Draw(_pixel, panel, new Color(24, 26, 34, 240));
    _spriteBatch.Draw(_pixel, new Rectangle(panel.X, panel.Y, panel.Width, 2), Color.Gold);
    _spriteBatch.Draw(_pixel, new Rectangle(panel.X, panel.Bottom - 2, panel.Width, 2), Color.Gold);
    _spriteBatch.Draw(_pixel, new Rectangle(panel.X, panel.Y, 2, panel.Height), Color.Gold);
    _spriteBatch.Draw(_pixel, new Rectangle(panel.Right - 2, panel.Y, 2, panel.Height), Color.Gold);

    _spriteBatch.DrawString(
      _pieceLabelFont,
      "PURCHASE PIECE",
      new Vector2(panel.X + 20, panel.Y + 18),
      Color.Gold
    );

    Color previewColour = Team.CurrentTurn == TeamName.Red ? Color.Red : Color.Blue;
    Rectangle previewBounds = new(panel.X + 20, panel.Y + 55, 80, 80);
    _spriteBatch.Draw(_pixel, previewBounds, previewColour);

    string label = definition.Type.ToString()[..2];
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
      Color.LightGray
    );
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"Cost: {definition.Cost}",
      new Vector2(panel.X + 120, panel.Y + 108),
      Color.Gold
    );

    float statY = panel.Y + 160;
    _spriteBatch.DrawString(_pieceLabelFont, $"Health: {definition.Health}", new Vector2(panel.X + 20, statY), Color.White);
    _spriteBatch.DrawString(_pieceLabelFont, $"Attack: {definition.Attack}", new Vector2(panel.X + 20, statY + 24), Color.White);
    _spriteBatch.DrawString(_pieceLabelFont, $"Move: {definition.Movement.range} {definition.Movement.shape}", new Vector2(panel.X + 20, statY + 48), Color.White);
    _spriteBatch.DrawString(_pieceLabelFont, $"Attack range: {definition.AttackShape.range} {definition.AttackShape.shape}", new Vector2(panel.X + 20, statY + 72), Color.White);
    _spriteBatch.DrawString(_pieceLabelFont, $"Size: {definition.Size.x} x {definition.Size.y}", new Vector2(panel.X + 20, statY + 96), Color.White);

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

  protected override void Draw(GameTime gameTime)
  {
    GraphicsDevice.Clear(Color.CornflowerBlue);

    /* Camera Logic */
    Vector2 screenCentre = new Vector2(
      GraphicsDevice.Viewport.Width / 2f,
      GraphicsDevice.Viewport.Height / 2f
    );

    Matrix cameraTransform =
      Matrix.CreateTranslation(
        -_cameraPosition.X,
        -_cameraPosition.Y,
        0
      )
      * Matrix.CreateScale(_zoom)
      * Matrix.CreateTranslation(
        screenCentre.X,
        screenCentre.Y,
        0
      );

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

          Color cellColour =
            isValidAttack
            ? attackableCellColour
            : isValidMove
            ? (x + y) % 2 == 0
              ? darkHighlightCellColour
              : lightHighlightCellColour
            : Color.Lerp(baseCellColour, territoryColour, territoryTintAmount);

          _spriteBatch.Draw(
              _pixel,
              new Rectangle(x * cellSize, y * cellSize, cellSize, cellSize),
              null,
              cellColour,
              0f,
              Vector2.Zero,
              SpriteEffects.None,
              0.1f
          );
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

      string label = piece.Definition.Type.ToString()[..2];
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
    }

    _spriteBatch.End();

    _spriteBatch.Begin();

    Color turnColour = Team.CurrentTurn == TeamName.Red ? Color.Red : Color.Blue;
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"Turn: {Team.CurrentTurn}",
      new Vector2(20, 20),
      turnColour
    );

    Team currentTeam = _teams.Find(team => team.TeamName == Team.CurrentTurn);
    float actionPointsY = 20 + _pieceLabelFont.LineSpacing + 4;
    _spriteBatch.DrawString(
      _pieceLabelFont,
      $"Action Points: {currentTeam.ActionPoints}/{Team.ActionsPerTurn}",
      new Vector2(20, actionPointsY),
      turnColour
    );

    float moneyY = actionPointsY + _pieceLabelFont.LineSpacing + 8;
    foreach (Team team in _teams)
    {
      Color teamColour = team.TeamName == TeamName.Red ? Color.Red : Color.Blue;
      _spriteBatch.DrawString(
        _pieceLabelFont,
        $"{team.TeamName} Money: {team.Money}",
        new Vector2(20, moneyY),
        teamColour
      );

      moneyY += _pieceLabelFont.LineSpacing;
    }

    DrawPurchasePanel();

    _spriteBatch.End();

    base.Draw(gameTime);
  }
}
