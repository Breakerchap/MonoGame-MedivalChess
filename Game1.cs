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
  private Board _board;
  private readonly PieceSetup pieceSetup = new();
  private List<Team> _teams = [];
  private Piece selectedPiece;
  private static readonly Color darkCellColour = new(181, 136, 99);
  private static readonly Color darkHighlightCellColour = new(220, 195, 75);
  private static readonly Color lightCellColour = new(240, 217, 181);
  private static readonly Color lightHighlightCellColour = new(246, 235, 114);
  private static readonly Color attackableCellColour = new(245, 56, 56);
  private Vector2 _cameraPosition = Vector2.Zero;
  private float _zoom = 1f;
  private MouseState _previousMouseState;

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

    if (wasLeftClick || wasRightClick)
    {
      const int cellSize = 64;
      int boardX = (int)MathF.Floor(mouseWorldBefore.X / cellSize) + _board.MinX;
      int boardY = (int)MathF.Floor(mouseWorldBefore.Y / cellSize) + _board.MinY;
      var targetPosition = (x: boardX, y: boardY);
      Piece pieceAtTarget = pieceSetup.GetPieceAt(targetPosition);

      if (selectedPiece == null)
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

            Team.AdvanceTurn();
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

            Team.AdvanceTurn();
          }

          selectedPiece = null;
        }
      }
    }

    _previousMouseState = mouse;

    base.Update(gameTime);
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

          Color cellColour =
            isValidAttack
            ? attackableCellColour
            : isValidMove
            ? (x + y) % 2 == 0
              ? darkHighlightCellColour
              : lightHighlightCellColour
            : (x + y) % 2 == 0
              ? darkCellColour
              : lightCellColour;

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
    }

    _spriteBatch.End();

    base.Draw(gameTime);
  }
}
