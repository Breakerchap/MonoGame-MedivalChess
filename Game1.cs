using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MedivalChess.GameBoard;

namespace MedivalChess;

public class Game1 : Game
{
  private GraphicsDeviceManager _graphics;
  private SpriteBatch _spriteBatch;
  private Texture2D _pixel;
  private Board _board;
  private Color darkCellColour = new Color(181, 136, 99);
  private Color lightCellColour = new Color(240, 217, 181);
  private Vector2 _cameraPosition = Vector2.Zero;
  private float _zoom = 1f;

  public Game1()
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

    base.Update(gameTime);
  }

  protected override void Draw(GameTime gameTime)
  {
    GraphicsDevice.Clear(Color.CornflowerBlue);

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

    _spriteBatch.Begin(transformMatrix: cameraTransform);

    var BoardArray = _board.BoardArray;
    int cellSize = 64;

    for (int y = 0; y < BoardArray.GetLength(0); y++)
    {
      for (int x = 0; x < BoardArray.GetLength(1); x++)
      {
        if (BoardArray[y, x] == 1)
        {
          Color cellColour =
            (x + y) % 2 == 0
            ? darkCellColour
            : lightCellColour;

          _spriteBatch.Draw(
            _pixel,
            new Rectangle(
              x * cellSize,
              y * cellSize,
              cellSize,
              cellSize
            ),
            cellColour
          );
        }
      }
    }

    _spriteBatch.End();

    base.Draw(gameTime);
  }
}