using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedivalChess.Shared;

namespace MedivalChess.TerrainPainter;

/// <summary>Standalone editor for the compact .mctrn terrain format used by Crown &amp; Siege.</summary>
internal sealed class TerrainPainterForm : Form
{
  private sealed record TerrainSnapshot(
    HashSet<(int x, int y)> Forests,
    HashSet<(int x, int y)> Lakes,
    HashSet<TileEdge> Rivers
  );

  private enum PaintTool
  {
    Forest,
    Lake,
    River,
    Erase
  }

  private const int DefaultCellSize = 38;
  private const int MinimumCellSize = 16;
  private const int MaximumCellSize = 72;
  private static readonly Regex SectionPattern = new(
    @"(?ims)^\s*(?<name>forest|lake|river)\s*:\s*\[(?<contents>.*?)\]",
    RegexOptions.Compiled
  );
  private static readonly Regex PositionPattern = new(
    @"\(\s*(?<x>-?\d+)\s*,\s*(?<y>-?\d+)\s*\)",
    RegexOptions.Compiled
  );
  private static readonly Regex RiverPattern = new(
    @"\(\s*(?<firstX>-?\d+)\s*,\s*(?<firstY>-?\d+)\s*\)\s*-\s*\(\s*(?<secondX>-?\d+)\s*,\s*(?<secondY>-?\d+)\s*\)",
    RegexOptions.Compiled
  );

  private readonly TerrainCanvas _canvas = new() { BackColor = Color.FromArgb(14, 20, 28), TabStop = true };
  private readonly Panel _viewport = new()
  {
    Dock = DockStyle.Fill,
    AutoScroll = true,
    BackColor = Color.FromArgb(14, 20, 28),
    Padding = new Padding(18)
  };
  private readonly ComboBox _boardSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
  private readonly ComboBox _terrainSourceSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92 };
  private readonly ComboBox _forestDensitySelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 82 };
  private readonly ComboBox _waterwayDensitySelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 82 };
  private readonly Label _summary = new() { AutoSize = true, Padding = new Padding(10, 8, 0, 0) };
  private readonly Dictionary<PaintTool, Button> _toolButtons = [];
  private readonly HashSet<(int x, int y)> _forests = [];
  private readonly HashSet<(int x, int y)> _lakes = [];
  private readonly HashSet<TileEdge> _rivers = [];
  private readonly Stack<TerrainSnapshot> _undoStates = [];

  private Board _board = null!;
  private string _boardDisplayName = "Medium";
  private PaintTool _activeTool = PaintTool.Forest;
  private (int x, int y)? _lastDragCell;
  private (int x, int y)? _riverAnchor;
  private bool _painting;
  private bool _rightClickErasing;
  private bool _riverDragChangedTerrain;
  private bool _panning;
  private Point _panStart;
  private Point _scrollAtPanStart;
  private int _cellSize = DefaultCellSize;
  private int? _lastGeneratedSeed;

  public TerrainPainterForm()
  {
    Text = "MedivalChess Terrain Painter";
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(860, 620);
    Size = new Size(1120, 820);
    BackColor = Color.FromArgb(23, 30, 39);

    FlowLayoutPanel toolbar = new()
    {
      Dock = DockStyle.Top,
      Height = 88,
      Padding = new Padding(8),
      BackColor = Color.FromArgb(32, 42, 53),
      WrapContents = true
    };
    toolbar.Controls.Add(new Label { Text = "Board:", AutoSize = true, Padding = new Padding(3, 8, 0, 0), ForeColor = Color.WhiteSmoke });
    _boardSelector.Items.AddRange(["Small", "Medium", "Large"]);
    _boardSelector.SelectedItem = "Medium";
    _boardSelector.SelectedIndexChanged += (_, _) => SelectBoard();
    toolbar.Controls.Add(_boardSelector);
    toolbar.Controls.Add(CreateButton("Load board JSON", (_, _) => LoadBoardJson()));
    toolbar.Controls.Add(CreateSeparator());
    _terrainSourceSelector.Items.AddRange(["Procedural", "Preset", "None"]);
    _terrainSourceSelector.SelectedItem = "Procedural";
    _forestDensitySelector.Items.AddRange(["Light", "Standard", "Heavy"]);
    _forestDensitySelector.SelectedItem = "Standard";
    _waterwayDensitySelector.Items.AddRange(["Light", "Standard", "Heavy"]);
    _waterwayDensitySelector.SelectedItem = "Standard";
    toolbar.Controls.Add(CreateSelector("Terrain", _terrainSourceSelector));
    toolbar.Controls.Add(CreateSelector("Forests", _forestDensitySelector));
    toolbar.Controls.Add(CreateSelector("Water", _waterwayDensitySelector));
    toolbar.Controls.Add(CreateButton("Generate", (_, _) => GenerateTerrain()));
    toolbar.Controls.Add(CreateSeparator());
    toolbar.Controls.Add(CreateToolButton(PaintTool.Forest, "Forest"));
    toolbar.Controls.Add(CreateToolButton(PaintTool.Lake, "Lake"));
    toolbar.Controls.Add(CreateToolButton(PaintTool.River, "River"));
    toolbar.Controls.Add(CreateToolButton(PaintTool.Erase, "Erase"));
    toolbar.Controls.Add(CreateSeparator());
    toolbar.Controls.Add(CreateButton("Load .mctrn", (_, _) => LoadTerrain()));
    toolbar.Controls.Add(CreateButton("Save .mctrn", (_, _) => SaveTerrain()));
    toolbar.Controls.Add(CreateButton("Undo", (_, _) => Undo()));
    toolbar.Controls.Add(CreateButton("Clear", (_, _) => ClearTerrain()));
    toolbar.Controls.Add(_summary);

    _canvas.Paint += DrawCanvas;
    _canvas.MouseDown += CanvasMouseDown;
    _canvas.MouseMove += CanvasMouseMove;
    _canvas.MouseUp += CanvasMouseUp;
    _canvas.MouseLeave += (_, _) => { if (!_panning) FinishPaint(); };
    _canvas.MouseWheel += (_, eventArgs) => ZoomAt(eventArgs.Location, eventArgs.Delta);
    _viewport.MouseWheel += (_, eventArgs) =>
      ZoomAt(_canvas.PointToClient(_viewport.PointToScreen(eventArgs.Location)), eventArgs.Delta);
    _viewport.Controls.Add(_canvas);

    Label instructions = new()
    {
      Dock = DockStyle.Bottom,
      Height = 46,
      Padding = new Padding(12, 8, 12, 0),
      ForeColor = Color.Gainsboro,
      BackColor = Color.FromArgb(32, 42, 53),
      Text = "Forest/Lake: click or drag squares. River: click or drag across the two tiles its edge belongs between. Right-drag erases. Ctrl+Z undoes. Middle-drag (or Space + drag) pans; mouse wheel zooms."
    };

    Controls.Add(_viewport);
    Controls.Add(instructions);
    Controls.Add(toolbar);
    KeyPreview = true;
    KeyDown += TerrainPainterKeyDown;
    SelectBoard();
    UpdateToolButtons();
  }

  private static Control CreateSeparator() => new Panel { Width = 14, Height = 28, Margin = new Padding(2, 3, 2, 0) };

  private static Control CreateSelector(string label, ComboBox selector)
  {
    FlowLayoutPanel container = new()
    {
      AutoSize = true,
      Height = 30,
      WrapContents = false,
      Margin = new Padding(3, 2, 0, 0)
    };
    container.Controls.Add(new Label
    {
      Text = $"{label}:",
      AutoSize = true,
      Padding = new Padding(2, 7, 0, 0),
      ForeColor = Color.WhiteSmoke
    });
    container.Controls.Add(selector);
    return container;
  }

  private Button CreateToolButton(PaintTool tool, string label)
  {
    Button button = CreateButton(label, (_, _) =>
    {
      _activeTool = tool;
      _riverAnchor = null;
      UpdateToolButtons();
      _canvas.Invalidate();
    });
    _toolButtons[tool] = button;
    return button;
  }

  private static Button CreateButton(string label, EventHandler onClick) => new Button
  {
    Text = label,
    AutoSize = true,
    Height = 29,
    FlatStyle = FlatStyle.Flat,
    BackColor = Color.FromArgb(54, 69, 84),
    ForeColor = Color.WhiteSmoke,
    FlatAppearance = { BorderColor = Color.FromArgb(95, 115, 133) },
    Margin = new Padding(3, 3, 0, 0)
  }.Also(button => button.Click += onClick);

  private void SelectBoard()
  {
    string boardSize = _boardSelector.SelectedItem?.ToString() ?? "Medium";
    SetBoard(new Board($"board_{boardSize.ToLowerInvariant()}.json"), boardSize);
  }

  private void SetBoard(Board board, string displayName)
  {
    _board = board;
    _boardDisplayName = displayName;
    _forests.Clear();
    _lakes.Clear();
    _rivers.Clear();
    _undoStates.Clear();
    _riverAnchor = null;
    _lastGeneratedSeed = null;
    _cellSize = DefaultCellSize;
    _canvas.Location = new Point(18, 18);
    ResizeCanvas();
    _viewport.AutoScrollPosition = Point.Empty;
    UpdateSummary();
    _canvas.Invalidate();
  }

  private void ResizeCanvas()
  {
    _canvas.Size = new Size(
      _board.BoardArray.GetLength(1) * _cellSize + 1,
      _board.BoardArray.GetLength(0) * _cellSize + 1
    );
  }

  private void LoadBoardJson()
  {
    using OpenFileDialog dialog = new()
    {
      Filter = "Board JSON (*.json)|*.json|All files (*.*)|*.*",
      InitialDirectory = GetBoardDirectory()
    };
    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    try
    {
      using JsonDocument document = JsonDocument.Parse(File.ReadAllText(dialog.FileName));
      if (!document.RootElement.TryGetProperty("cells", out JsonElement cellsElement) ||
          cellsElement.ValueKind != JsonValueKind.Array)
      {
        throw new InvalidDataException("The board JSON needs a cells array.");
      }

      List<(int x, int y)> cells = [];
      foreach (JsonElement cell in cellsElement.EnumerateArray())
      {
        if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() != 2)
        {
          throw new InvalidDataException("Each board cell must be an [x, y] array.");
        }
        cells.Add((cell[0].GetInt32(), cell[1].GetInt32()));
      }
      SetBoard(new Board(cells), Path.GetFileNameWithoutExtension(dialog.FileName));
      Text = $"MedivalChess Terrain Painter — {_boardDisplayName}";
    }
    catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or ArgumentException)
    {
      MessageBox.Show(this, exception.Message, "Could not load board", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private void UpdateToolButtons()
  {
    foreach ((PaintTool tool, Button button) in _toolButtons)
    {
      bool isActive = tool == _activeTool;
      button.BackColor = isActive ? Color.FromArgb(190, 130, 58) : Color.FromArgb(54, 69, 84);
      button.FlatAppearance.BorderColor = isActive ? Color.Gold : Color.FromArgb(95, 115, 133);
    }
  }

  private void DrawCanvas(object? sender, PaintEventArgs eventArgs)
  {
    Graphics graphics = eventArgs.Graphics;
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    using SolidBrush backgroundBrush = new(Color.FromArgb(14, 20, 28));
    graphics.FillRectangle(backgroundBrush, eventArgs.ClipRectangle);

    foreach ((int x, int y) cell in _board.Cells)
    {
      Rectangle rectangle = GetCellRectangle(cell);
      if (!rectangle.IntersectsWith(eventArgs.ClipRectangle))
      {
        continue;
      }
      Color fill = _lakes.Contains(cell) ? Color.FromArgb(54, 118, 177) :
        _forests.Contains(cell) ? Color.FromArgb(49, 112, 63) :
        Color.FromArgb(70, 72, 65);
      using SolidBrush brush = new(fill);
      graphics.FillRectangle(brush, rectangle);
      using Pen gridPen = new(Color.FromArgb(100, 18, 22, 28));
      graphics.DrawRectangle(gridPen, rectangle);
    }

    using Pen riverPen = new(Color.FromArgb(105, 210, 244), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    foreach (TileEdge river in _rivers)
    {
      Rectangle riverBounds = GetRiverBounds(river);
      if (!riverBounds.IntersectsWith(eventArgs.ClipRectangle))
      {
        continue;
      }
      if (river.First.x != river.Second.x)
      {
        int lineX = (Math.Max(river.First.x, river.Second.x) - _board.MinX) * _cellSize;
        int top = (river.First.y - _board.MinY) * _cellSize;
        graphics.DrawLine(riverPen, lineX, top, lineX, top + _cellSize);
      }
      else
      {
        int lineY = (Math.Max(river.First.y, river.Second.y) - _board.MinY) * _cellSize;
        int left = (river.First.x - _board.MinX) * _cellSize;
        graphics.DrawLine(riverPen, left, lineY, left + _cellSize, lineY);
      }
    }

    if (_activeTool == PaintTool.River && _riverAnchor is { } anchor)
    {
      Rectangle anchorBounds = GetCellRectangle(anchor);
      if (anchorBounds.IntersectsWith(eventArgs.ClipRectangle))
      {
        using Pen anchorPen = new(Color.Gold, 3f);
        graphics.DrawRectangle(anchorPen, Rectangle.Inflate(anchorBounds, -3, -3));
      }
    }
  }

  private void CanvasMouseDown(object? sender, MouseEventArgs eventArgs)
  {
    _canvas.Focus();
    bool panRequested = eventArgs.Button == MouseButtons.Middle ||
      (eventArgs.Button == MouseButtons.Left && ModifierKeys.HasFlag(Keys.Space));
    if (panRequested)
    {
      _panning = true;
      _canvas.Capture = true;
      _panStart = eventArgs.Location;
      _scrollAtPanStart = new Point(-_viewport.AutoScrollPosition.X, -_viewport.AutoScrollPosition.Y);
      _canvas.Cursor = Cursors.SizeAll;
      return;
    }

    if (eventArgs.Button is not (MouseButtons.Left or MouseButtons.Right) ||
        !TryGetCell(eventArgs.Location, out (int x, int y) cell))
    {
      return;
    }

    _rightClickErasing = eventArgs.Button == MouseButtons.Right;
    _painting = true;
    _lastDragCell = cell;
    _riverDragChangedTerrain = false;
    PaintTool tool = CurrentPaintTool;
    if (tool == PaintTool.River)
    {
      if (_riverAnchor is { } start && start != cell)
      {
        CaptureUndoState();
        PaintRiverBetween(start, cell);
        _riverAnchor = null;
        FinishPaint();
      }
      else
      {
        _riverAnchor = _riverAnchor == cell ? null : cell;
        _canvas.Invalidate();
      }
      return;
    }

    CaptureUndoState();
    ApplyAtCell(cell, isInitialCell: true, tool);
  }

  private void CanvasMouseMove(object? sender, MouseEventArgs eventArgs)
  {
    if (_panning)
    {
      _viewport.AutoScrollPosition = new Point(
        Math.Max(0, _scrollAtPanStart.X - (eventArgs.X - _panStart.X)),
        Math.Max(0, _scrollAtPanStart.Y - (eventArgs.Y - _panStart.Y))
      );
      return;
    }

    if (!_painting || (Control.MouseButtons & (MouseButtons.Left | MouseButtons.Right)) == MouseButtons.None ||
        !TryGetCell(eventArgs.Location, out (int x, int y) cell) || cell == _lastDragCell)
    {
      return;
    }

    PaintTool tool = CurrentPaintTool;
    if (tool == PaintTool.River && _lastDragCell is { } previous)
    {
      if (!_riverDragChangedTerrain)
      {
        CaptureUndoState();
        _riverDragChangedTerrain = true;
      }
      _riverAnchor = null;
      PaintRiverBetween(previous, cell);
    }
    else
    {
      ApplyAtCell(cell, isInitialCell: false, tool);
    }
    _lastDragCell = cell;
  }

  private void CanvasMouseUp(object? sender, MouseEventArgs eventArgs)
  {
    if (_panning)
    {
      _panning = false;
      _canvas.Capture = false;
      _canvas.Cursor = Cursors.Default;
      return;
    }
    FinishPaint();
  }

  private void FinishPaint()
  {
    _painting = false;
    _lastDragCell = null;
    _rightClickErasing = false;
    _riverDragChangedTerrain = false;
  }

  private PaintTool CurrentPaintTool => _rightClickErasing ? PaintTool.Erase : _activeTool;

  private void ApplyAtCell((int x, int y) cell, bool isInitialCell, PaintTool tool)
  {
    IEnumerable<(int x, int y)> paintedCells = !isInitialCell && _lastDragCell is { } previous
      ? GetCellsAlongGesture(previous, cell)
      : [cell];
    HashSet<(int x, int y)> dirtyCells = [];

    foreach ((int x, int y) paintedCell in paintedCells)
    {
      if (!_board.ContainsCell(paintedCell))
      {
        continue;
      }

      dirtyCells.Add(paintedCell);
      switch (tool)
      {
        case PaintTool.Forest:
          _lakes.Remove(paintedCell);
          _forests.Add(paintedCell);
          break;
        case PaintTool.Lake:
          _forests.Remove(paintedCell);
          _lakes.Add(paintedCell);
          break;
        case PaintTool.Erase:
          _forests.Remove(paintedCell);
          _lakes.Remove(paintedCell);
          foreach (TileEdge edge in _rivers.Where(edge => edge.First == paintedCell || edge.Second == paintedCell).ToArray())
          {
            _rivers.Remove(edge);
            dirtyCells.Add(edge.First);
            dirtyCells.Add(edge.Second);
          }
          break;
      }
    }

    UpdateSummary();
    InvalidateTerrainCells(dirtyCells);
  }

  private void PaintRiverBetween((int x, int y) start, (int x, int y) end)
  {
    HashSet<(int x, int y)> dirtyCells = [start, end];
    foreach (TileEdge edge in GetEdgesBetween(start, end))
    {
      if (!_board.ContainsCell(edge.First) || !_board.ContainsCell(edge.Second))
      {
        continue;
      }
      _rivers.Add(edge);
      dirtyCells.Add(edge.First);
      dirtyCells.Add(edge.Second);
    }
    UpdateSummary();
    InvalidateTerrainCells(dirtyCells);
  }

  /// <summary>Fills gaps when the pointer crosses several cells between mouse-move events.</summary>
  private static IEnumerable<(int x, int y)> GetCellsAlongGesture((int x, int y) start, (int x, int y) end)
  {
    int x = start.x;
    int y = start.y;
    int deltaX = Math.Abs(end.x - start.x);
    int deltaY = Math.Abs(end.y - start.y);
    int stepX = Math.Sign(end.x - start.x);
    int stepY = Math.Sign(end.y - start.y);
    int error = deltaX - deltaY;

    while (true)
    {
      yield return (x, y);
      if (x == end.x && y == end.y)
      {
        yield break;
      }

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
    }
  }

  private static IEnumerable<TileEdge> GetEdgesBetween((int x, int y) start, (int x, int y) end)
  {
    (int x, int y) current = start;
    while (current.x != end.x)
    {
      (int x, int y) next = (current.x + Math.Sign(end.x - current.x), current.y);
      yield return TileEdge.Between(current, next);
      current = next;
    }
    while (current.y != end.y)
    {
      (int x, int y) next = (current.x, current.y + Math.Sign(end.y - current.y));
      yield return TileEdge.Between(current, next);
      current = next;
    }
  }

  private void ZoomAt(Point canvasPoint, int delta)
  {
    int nextCellSize = Math.Clamp(_cellSize + Math.Sign(delta) * 4, MinimumCellSize, MaximumCellSize);
    if (nextCellSize == _cellSize)
    {
      return;
    }

    Point scroll = new(-_viewport.AutoScrollPosition.X, -_viewport.AutoScrollPosition.Y);
    Point scaledPoint = new(
      (int)Math.Round(canvasPoint.X * (nextCellSize / (double)_cellSize)),
      (int)Math.Round(canvasPoint.Y * (nextCellSize / (double)_cellSize))
    );
    _cellSize = nextCellSize;
    ResizeCanvas();
    _viewport.AutoScrollPosition = new Point(
      Math.Max(0, scroll.X + scaledPoint.X - canvasPoint.X),
      Math.Max(0, scroll.Y + scaledPoint.Y - canvasPoint.Y)
    );
    UpdateSummary();
    _canvas.Invalidate();
  }

  private void InvalidateTerrainCells(IEnumerable<(int x, int y)> cells)
  {
    Rectangle dirtyBounds = Rectangle.Empty;
    bool hasDirtyCell = false;
    foreach ((int x, int y) cell in cells)
    {
      if (!_board.ContainsCell(cell))
      {
        continue;
      }
      dirtyBounds = hasDirtyCell ? Rectangle.Union(dirtyBounds, GetCellRectangle(cell)) : GetCellRectangle(cell);
      hasDirtyCell = true;
    }
    if (hasDirtyCell)
    {
      _canvas.Invalidate(Rectangle.Inflate(dirtyBounds, 8, 8));
    }
  }

  private bool TryGetCell(Point point, out (int x, int y) cell)
  {
    cell = (point.X / _cellSize + _board.MinX, point.Y / _cellSize + _board.MinY);
    return _board.ContainsCell(cell);
  }

  private Rectangle GetCellRectangle((int x, int y) cell) => new(
    (cell.x - _board.MinX) * _cellSize,
    (cell.y - _board.MinY) * _cellSize,
    _cellSize,
    _cellSize
  );

  private Rectangle GetRiverBounds(TileEdge edge)
  {
    if (edge.First.x != edge.Second.x)
    {
      int lineX = (Math.Max(edge.First.x, edge.Second.x) - _board.MinX) * _cellSize;
      int top = (edge.First.y - _board.MinY) * _cellSize;
      return new Rectangle(lineX - 4, top - 4, 8, _cellSize + 8);
    }

    int lineY = (Math.Max(edge.First.y, edge.Second.y) - _board.MinY) * _cellSize;
    int left = (edge.First.x - _board.MinX) * _cellSize;
    return new Rectangle(left - 4, lineY - 4, _cellSize + 8, 8);
  }

  private void ClearTerrain()
  {
    if (_forests.Count == 0 && _lakes.Count == 0 && _rivers.Count == 0)
    {
      return;
    }
    CaptureUndoState();
    _forests.Clear();
    _lakes.Clear();
    _rivers.Clear();
    _riverAnchor = null;
    _lastGeneratedSeed = null;
    UpdateSummary();
    _canvas.Invalidate();
  }

  private void GenerateTerrain()
  {
    CaptureUndoState();
    int seed = Random.Shared.Next();
    string terrainSource = _terrainSourceSelector.SelectedItem?.ToString() ?? "Procedural";
    string forestDensity = _forestDensitySelector.SelectedItem?.ToString() ?? "Standard";
    string waterwayDensity = _waterwayDensitySelector.SelectedItem?.ToString() ?? "Standard";
    string boardSize = _boardSelector.SelectedItem?.ToString() ?? "Custom";
    BattlefieldTerrain generated = TerrainRules.Create(
      _board,
      seed,
      forestDensity,
      waterwayDensity,
      terrainSource: terrainSource,
      boardSize: boardSize
    );

    _forests.Clear();
    _forests.UnionWith(generated.Forests);
    _lakes.Clear();
    _lakes.UnionWith(generated.Lakes);
    _rivers.Clear();
    _rivers.UnionWith(generated.Rivers);
    _riverAnchor = null;
    _lastGeneratedSeed = seed;
    UpdateSummary();
    _canvas.Invalidate();
  }

  private void TerrainPainterKeyDown(object? sender, KeyEventArgs eventArgs)
  {
    if (eventArgs.Control && eventArgs.KeyCode == Keys.Z)
    {
      Undo();
      eventArgs.SuppressKeyPress = true;
    }
  }

  private void CaptureUndoState()
  {
    _undoStates.Push(new TerrainSnapshot([.. _forests], [.. _lakes], [.. _rivers]));
  }

  private void Undo()
  {
    if (!_undoStates.TryPop(out TerrainSnapshot? previous))
    {
      return;
    }

    _forests.Clear();
    _forests.UnionWith(previous.Forests);
    _lakes.Clear();
    _lakes.UnionWith(previous.Lakes);
    _rivers.Clear();
    _rivers.UnionWith(previous.Rivers);
    _riverAnchor = null;
    FinishPaint();
    UpdateSummary();
    _canvas.Invalidate();
  }

  private void SaveTerrain()
  {
    using SaveFileDialog dialog = new()
    {
      Filter = "MedivalChess Terrain (*.mctrn)|*.mctrn|All files (*.*)|*.*",
      DefaultExt = "mctrn",
      AddExtension = true,
      FileName = $"{_boardDisplayName.ToLowerInvariant()}_terrain.mctrn",
      InitialDirectory = AppContext.BaseDirectory
    };
    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    File.WriteAllText(dialog.FileName, BuildTerrainFile());
    Text = $"MedivalChess Terrain Painter — {Path.GetFileName(dialog.FileName)}";
  }

  private string BuildTerrainFile()
  {
    static string FormatPosition((int x, int y) position) => $"({position.x},{position.y})";
    IEnumerable<(int x, int y)> orderedForests = _forests.OrderBy(position => position.y).ThenBy(position => position.x);
    IEnumerable<(int x, int y)> orderedLakes = _lakes.OrderBy(position => position.y).ThenBy(position => position.x);
    IEnumerable<TileEdge> orderedRivers = _rivers
      .OrderBy(edge => edge.First.y).ThenBy(edge => edge.First.x)
      .ThenBy(edge => edge.Second.y).ThenBy(edge => edge.Second.x);
    StringBuilder output = new();
    output.AppendLine($"forest: [{string.Join(", ", orderedForests.Select(FormatPosition))}]");
    output.AppendLine($"lake: [{string.Join(", ", orderedLakes.Select(FormatPosition))}]");
    output.AppendLine("river: [");
    foreach (TileEdge river in orderedRivers)
    {
      output.AppendLine($"  {FormatPosition(river.First)}-{FormatPosition(river.Second)},");
    }
    output.AppendLine("]");
    return output.ToString();
  }

  private void LoadTerrain()
  {
    using OpenFileDialog dialog = new()
    {
      Filter = "MedivalChess Terrain (*.mctrn)|*.mctrn|All files (*.*)|*.*",
      InitialDirectory = GetExistingPresetDirectory()
    };
    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    if (!TryReadTerrainFile(File.ReadAllText(dialog.FileName), out HashSet<(int x, int y)> forests,
        out HashSet<(int x, int y)> lakes, out HashSet<TileEdge> rivers, out string error))
    {
      MessageBox.Show(this, error, "Could not load terrain", MessageBoxButtons.OK, MessageBoxIcon.Error);
      return;
    }

    _forests.Clear();
    _forests.UnionWith(forests.Where(_board.ContainsCell));
    _lakes.Clear();
    _lakes.UnionWith(lakes.Where(_board.ContainsCell));
    _forests.ExceptWith(_lakes);
    _rivers.Clear();
    _rivers.UnionWith(rivers.Where(edge => _board.ContainsCell(edge.First) && _board.ContainsCell(edge.Second)));
    _undoStates.Clear();
    _riverAnchor = null;
    _lastGeneratedSeed = null;
    UpdateSummary();
    _canvas.Invalidate();
    Text = $"MedivalChess Terrain Painter — {Path.GetFileName(dialog.FileName)}";
  }

  private static bool TryReadTerrainFile(
    string content,
    out HashSet<(int x, int y)> forests,
    out HashSet<(int x, int y)> lakes,
    out HashSet<TileEdge> rivers,
    out string error
  )
  {
    forests = [];
    lakes = [];
    rivers = [];
    error = string.Empty;
    Dictionary<string, string> sections = SectionPattern.Matches(content)
      .Cast<Match>()
      .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["contents"].Value, StringComparer.OrdinalIgnoreCase);
    if (!sections.TryGetValue("forest", out string? forestText) ||
        !sections.TryGetValue("lake", out string? lakeText) ||
        !sections.TryGetValue("river", out string? riverText))
    {
      error = "Expected forest, lake, and river sections in the .mctrn file.";
      return false;
    }

    forests = ParsePositions(forestText);
    lakes = ParsePositions(lakeText);
    foreach (Match match in RiverPattern.Matches(riverText))
    {
      (int x, int y) first = (int.Parse(match.Groups["firstX"].Value), int.Parse(match.Groups["firstY"].Value));
      (int x, int y) second = (int.Parse(match.Groups["secondX"].Value), int.Parse(match.Groups["secondY"].Value));
      if (Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y) != 1)
      {
        error = "Every river entry must join orthogonally adjacent squares.";
        return false;
      }
      rivers.Add(TileEdge.Between(first, second));
    }
    return true;
  }

  private static HashSet<(int x, int y)> ParsePositions(string text) => PositionPattern.Matches(text)
    .Cast<Match>()
    .Select(match => (int.Parse(match.Groups["x"].Value), int.Parse(match.Groups["y"].Value)))
    .ToHashSet();

  private string GetExistingPresetDirectory()
  {
    string boardSize = _boardSelector.SelectedItem?.ToString() ?? "Medium";
    string[] candidates =
    [
      Path.Combine(Directory.GetCurrentDirectory(), "GameBoard", "BoardTerrains", boardSize),
      Path.Combine(AppContext.BaseDirectory, "GameBoard", "BoardTerrains", boardSize)
    ];
    return candidates.FirstOrDefault(Directory.Exists) ?? AppContext.BaseDirectory;
  }

  private static string GetBoardDirectory()
  {
    string[] candidates =
    [
      Path.Combine(Directory.GetCurrentDirectory(), "GameBoard"),
      Path.Combine(AppContext.BaseDirectory, "GameBoard")
    ];
    return candidates.FirstOrDefault(Directory.Exists) ?? AppContext.BaseDirectory;
  }

  private void UpdateSummary()
  {
    string seed = _lastGeneratedSeed.HasValue ? $"  Seed {_lastGeneratedSeed.Value}" : string.Empty;
    _summary.Text = $"Forest {_forests.Count}  Lake {_lakes.Count}  River {_rivers.Count}  Zoom {Math.Round(_cellSize / (double)DefaultCellSize * 100)}%{seed}";
  }
}

internal sealed class TerrainCanvas : Panel
{
  internal TerrainCanvas()
  {
    DoubleBuffered = true;
    ResizeRedraw = true;
  }
}

internal static class ControlExtensions
{
  internal static T Also<T>(this T control, Action<T> action) where T : Control
  {
    action(control);
    return control;
  }
}
