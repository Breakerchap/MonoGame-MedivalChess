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

  public Game1()
  {
    _graphics = new GraphicsDeviceManager(this);
    Content.RootDirectory = "Content";
    IsMouseVisible = true;
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
    if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
      Exit();

    // TODO: Add your update logic here

    base.Update(gameTime);
  }

  protected override void Draw(GameTime gameTime)
  {
    GraphicsDevice.Clear(Color.CornflowerBlue);

    var BoardArray = _board.BoardArray;

    int cellSize = 64;

    for (int y = 0; y < BoardArray.GetLength(0); y++)
    {
      for (int x = 0; x < BoardArray.GetLength(1); x++)
      {
        if (BoardArray[y, x] == 1 && (x + y) % 2 == 0)
        {
          _spriteBatch.Begin();
          _spriteBatch.Draw(
            _pixel,
            new Rectangle(x * cellSize, y * cellSize, cellSize, cellSize),
            darkCellColour
          );
          _spriteBatch.End();
        } else if (BoardArray[y, x] == 1 && (x + y) % 2 != 0)
        {
          _spriteBatch.Begin();
          _spriteBatch.Draw(
            _pixel,
            new Rectangle(x * cellSize, y * cellSize, cellSize, cellSize),
            lightCellColour
          );
          _spriteBatch.End();
        }
      }

      base.Draw(gameTime);
    }
  }
}