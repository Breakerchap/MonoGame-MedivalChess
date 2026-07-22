import json
import math
import re
import sys
from collections import deque
from pathlib import Path
from tkinter import Tk, filedialog

import pygame


WINDOW_WIDTH = 1400
WINDOW_HEIGHT = 900
FPS = 120

BASE_TILE_SIZE = 48
MIN_ZOOM = 0.2
MAX_ZOOM = 4.0
ZOOM_STEP = 1.1

BACKGROUND_COLOUR = (235, 235, 235)
GRID_COLOUR = (170, 170, 170)
LIGHT_TILE_COLOUR = (240, 179, 90)
DARK_TILE_COLOUR = (37, 169, 190)
TEXT_COLOUR = (25, 25, 25)
ORIGIN_COLOUR = (220, 60, 60)
SELECTION_COLOUR = (80, 80, 80)
SELECTION_FILL_COLOUR = (80, 80, 80, 40)
PASTE_PREVIEW_COLOUR = (120, 220, 120)
RECTANGLE_PREVIEW_COLOUR = (90, 90, 90)
RECTANGLE_PREVIEW_FILL = (90, 90, 90, 35)
STATUS_OK_COLOUR = (20, 120, 20)
STATUS_ERROR_COLOUR = (170, 40, 40)

SAVE_JSON_PATH = Path("painted_shape.json")
SAVE_GDSCRIPT_PATH = Path("painted_shape_data.gd")

TOOL_PAINT = "paint"
TOOL_ERASE = "erase"
TOOL_SELECT = "select"
TOOL_RECTANGLE = "rectangle"
TOOL_FILL = "fill"

MAX_HISTORY = 100


class ShapePainter:
  def __init__(self) -> None:
    pygame.init()
    pygame.display.set_caption("Tile Shape Painter")
    self.screen = pygame.display.set_mode((WINDOW_WIDTH, WINDOW_HEIGHT))
    self.clock = pygame.time.Clock()
    self.font = pygame.font.SysFont("consolas", 18)
    self.big_font = pygame.font.SysFont("consolas", 24)

    self.zoom = 1.0
    self.camera_x = WINDOW_WIDTH / 2
    self.camera_y = WINDOW_HEIGHT / 2

    self.filled_cells: set[tuple[int, int]] = set()

    self.current_tool = TOOL_PAINT

    self.painting = False
    self.erasing = False
    self.panning = False
    self.selecting = False
    self.rectangle_dragging = False

    self.last_painted_cell: tuple[int, int] | None = None
    self.last_erased_cell: tuple[int, int] | None = None
    self.pan_last_mouse: tuple[int, int] | None = None

    self.selection_start: tuple[int, int] | None = None
    self.selection_end: tuple[int, int] | None = None

    self.rectangle_start: tuple[int, int] | None = None
    self.rectangle_end: tuple[int, int] | None = None

    self.clipboard_cells: list[tuple[int, int]] = []

    self.undo_stack: list[set[tuple[int, int]]] = []
    self.redo_stack: list[set[tuple[int, int]]] = []

    self.status_message = ""
    self.status_colour = STATUS_OK_COLOUR

  def run(self) -> None:
    while True:
      self.handle_events()
      self.draw()
      pygame.display.flip()
      self.clock.tick(FPS)

  def handle_events(self) -> None:
    for event in pygame.event.get():
      if event.type == pygame.QUIT:
        pygame.quit()
        sys.exit()

      elif event.type == pygame.KEYDOWN:
        mods = pygame.key.get_mods()

        if event.key == pygame.K_ESCAPE:
          pygame.quit()
          sys.exit()

        elif event.key == pygame.K_p:
          self.current_tool = TOOL_PAINT

        elif event.key == pygame.K_e:
          self.current_tool = TOOL_ERASE

        elif event.key == pygame.K_v and not (mods & pygame.KMOD_CTRL):
          self.current_tool = TOOL_SELECT

        elif event.key == pygame.K_b:
          self.current_tool = TOOL_RECTANGLE

        elif event.key == pygame.K_f:
          self.current_tool = TOOL_FILL

        elif event.key == pygame.K_h:
          self.flip_clipboard_horizontal()

        elif event.key == pygame.K_j:
          self.flip_clipboard_vertical()

        elif event.key == pygame.K_z and (mods & pygame.KMOD_CTRL):
          self.undo()

        elif event.key == pygame.K_y and (mods & pygame.KMOD_CTRL):
          self.redo()

        elif event.key == pygame.K_o and (mods & pygame.KMOD_CTRL) and (mods & pygame.KMOD_SHIFT):
          self.import_gdscript(SAVE_GDSCRIPT_PATH)

        elif event.key == pygame.K_o and (mods & pygame.KMOD_CTRL):
          self.choose_and_import_json()

        elif event.key == pygame.K_c and not (mods & pygame.KMOD_CTRL):
          self.save_history_state()
          self.filled_cells.clear()
          self.clear_selection()
          self.clear_rectangle_preview()
          self.set_status("Cleared canvas", STATUS_OK_COLOUR)

        elif event.key == pygame.K_s:
          self.export_json(SAVE_JSON_PATH)

        elif event.key == pygame.K_g:
          self.export_gdscript(SAVE_GDSCRIPT_PATH)

        elif event.key == pygame.K_r:
          self.reset_camera()

        elif event.key == pygame.K_DELETE:
          self.clear_selected_tiles()

        elif event.key == pygame.K_c and (mods & pygame.KMOD_CTRL):
          self.copy_selection()

        elif event.key == pygame.K_v and (mods & pygame.KMOD_CTRL):
          self.paste_clipboard_at_mouse()

      elif event.type == pygame.DROPFILE:
        dropped_path = Path(event.file)
        if dropped_path.suffix.lower() == ".json":
          self.import_json(dropped_path)
        else:
          self.set_status("Drop a .json file to import it", STATUS_ERROR_COLOUR)

      elif event.type == pygame.MOUSEBUTTONDOWN:
        if event.button == 1:
          if self.current_tool == TOOL_PAINT:
            self.save_history_state()
            self.painting = True
            self.paint_at_mouse()
          elif self.current_tool == TOOL_ERASE:
            self.save_history_state()
            self.erasing = True
            self.erase_at_mouse()
          elif self.current_tool == TOOL_SELECT:
            self.selecting = True
            cell = self.screen_to_cell(event.pos)
            self.selection_start = cell
            self.selection_end = cell
          elif self.current_tool == TOOL_RECTANGLE:
            self.rectangle_dragging = True
            cell = self.screen_to_cell(event.pos)
            self.rectangle_start = cell
            self.rectangle_end = cell
          elif self.current_tool == TOOL_FILL:
            self.fill_at_mouse()

        elif event.button == 2:
          self.panning = True
          self.pan_last_mouse = event.pos

        elif event.button == 3:
          if self.current_tool == TOOL_PAINT:
            self.save_history_state()
            self.erasing = True
            self.erase_at_mouse()
          elif self.current_tool == TOOL_ERASE:
            self.save_history_state()
            self.painting = True
            self.paint_at_mouse()

        elif event.button == 4:
          self.zoom_at_screen_pos(ZOOM_STEP, event.pos)

        elif event.button == 5:
          self.zoom_at_screen_pos(1.0 / ZOOM_STEP, event.pos)

      elif event.type == pygame.MOUSEBUTTONUP:
        if event.button == 1:
          self.painting = False
          self.erasing = False
          self.selecting = False
          self.last_painted_cell = None
          self.last_erased_cell = None

          if self.rectangle_dragging:
            self.apply_rectangle()
            self.rectangle_dragging = False

        elif event.button == 2:
          self.panning = False
          self.pan_last_mouse = None

        elif event.button == 3:
          self.painting = False
          self.erasing = False
          self.last_painted_cell = None
          self.last_erased_cell = None

      elif event.type == pygame.MOUSEMOTION:
        if self.painting:
          self.paint_at_mouse()

        if self.erasing:
          self.erase_at_mouse()

        if self.selecting and self.current_tool == TOOL_SELECT:
          self.selection_end = self.screen_to_cell(event.pos)

        if self.rectangle_dragging and self.current_tool == TOOL_RECTANGLE:
          self.rectangle_end = self.screen_to_cell(event.pos)

        if self.panning and self.pan_last_mouse is not None:
          mx, my = event.pos
          lx, ly = self.pan_last_mouse
          self.camera_x += mx - lx
          self.camera_y += my - ly
          self.pan_last_mouse = event.pos

  def set_status(self, message: str, colour: tuple[int, int, int]) -> None:
    self.status_message = message
    self.status_colour = colour

  def save_history_state(self) -> None:
    self.undo_stack.append(set(self.filled_cells))
    if len(self.undo_stack) > MAX_HISTORY:
      self.undo_stack.pop(0)
    self.redo_stack.clear()

  def undo(self) -> None:
    if not self.undo_stack:
      return

    self.redo_stack.append(set(self.filled_cells))
    self.filled_cells = self.undo_stack.pop()
    self.set_status("Undo", STATUS_OK_COLOUR)

  def redo(self) -> None:
    if not self.redo_stack:
      return

    self.undo_stack.append(set(self.filled_cells))
    if len(self.undo_stack) > MAX_HISTORY:
      self.undo_stack.pop(0)
    self.filled_cells = self.redo_stack.pop()
    self.set_status("Redo", STATUS_OK_COLOUR)

  def reset_camera(self) -> None:
    self.zoom = 1.0
    self.camera_x = WINDOW_WIDTH / 2
    self.camera_y = WINDOW_HEIGHT / 2
    self.set_status("Reset camera", STATUS_OK_COLOUR)

  def clear_selection(self) -> None:
    self.selection_start = None
    self.selection_end = None

  def clear_rectangle_preview(self) -> None:
    self.rectangle_start = None
    self.rectangle_end = None
    self.rectangle_dragging = False

  def zoom_at_screen_pos(self, factor: float, screen_pos: tuple[int, int]) -> None:
    old_zoom = self.zoom
    new_zoom = max(MIN_ZOOM, min(MAX_ZOOM, self.zoom * factor))
    if math.isclose(old_zoom, new_zoom):
      return

    world_x, world_y = self.screen_to_world(screen_pos)
    self.zoom = new_zoom
    self.camera_x = screen_pos[0] - world_x * self.get_tile_size()
    self.camera_y = screen_pos[1] - world_y * self.get_tile_size()

  def get_tile_size(self) -> float:
    return BASE_TILE_SIZE * self.zoom

  def screen_to_world(self, screen_pos: tuple[int, int]) -> tuple[float, float]:
    tile_size = self.get_tile_size()
    world_x = (screen_pos[0] - self.camera_x) / tile_size
    world_y = (screen_pos[1] - self.camera_y) / tile_size
    return world_x, world_y

  def screen_to_cell(self, screen_pos: tuple[int, int]) -> tuple[int, int]:
    world_x, world_y = self.screen_to_world(screen_pos)
    return math.floor(world_x), math.floor(world_y)

  def cell_to_screen_rect(self, cell_x: int, cell_y: int) -> pygame.Rect:
    tile_size = self.get_tile_size()
    screen_x = self.camera_x + cell_x * tile_size
    screen_y = self.camera_y + cell_y * tile_size
    return pygame.Rect(round(screen_x), round(screen_y), math.ceil(tile_size), math.ceil(tile_size))

  def get_selection_bounds(self) -> tuple[int, int, int, int] | None:
    if self.selection_start is None or self.selection_end is None:
      return None

    x1 = min(self.selection_start[0], self.selection_end[0])
    y1 = min(self.selection_start[1], self.selection_end[1])
    x2 = max(self.selection_start[0], self.selection_end[0])
    y2 = max(self.selection_start[1], self.selection_end[1])

    return x1, y1, x2, y2

  def get_rectangle_bounds(self) -> tuple[int, int, int, int] | None:
    if self.rectangle_start is None or self.rectangle_end is None:
      return None

    x1 = min(self.rectangle_start[0], self.rectangle_end[0])
    y1 = min(self.rectangle_start[1], self.rectangle_end[1])
    x2 = max(self.rectangle_start[0], self.rectangle_end[0])
    y2 = max(self.rectangle_start[1], self.rectangle_end[1])

    return x1, y1, x2, y2

  def get_bounds_dimensions(self, bounds: tuple[int, int, int, int] | None) -> tuple[int, int]:
    if bounds is None:
      return 0, 0

    x1, y1, x2, y2 = bounds
    return (x2 - x1 + 1, y2 - y1 + 1)

  def get_selected_cells(self) -> set[tuple[int, int]]:
    bounds = self.get_selection_bounds()
    if bounds is None:
      return set()

    x1, y1, x2, y2 = bounds
    selected = set()

    for y in range(y1, y2 + 1):
      for x in range(x1, x2 + 1):
        selected.add((x, y))

    return selected

  def get_selected_filled_cells(self) -> list[tuple[int, int]]:
    selected_cells = self.get_selected_cells()
    return [cell for cell in self.filled_cells if cell in selected_cells]

  def clear_selected_tiles(self) -> None:
    selected_cells = self.get_selected_cells()
    if not selected_cells:
      return

    before = set(self.filled_cells)
    self.filled_cells = {cell for cell in self.filled_cells if cell not in selected_cells}

    if self.filled_cells != before:
      self.undo_stack.append(before)
      if len(self.undo_stack) > MAX_HISTORY:
        self.undo_stack.pop(0)
      self.redo_stack.clear()
      self.set_status("Cleared selection", STATUS_OK_COLOUR)

  def copy_selection(self) -> None:
    filled = self.get_selected_filled_cells()
    if not filled:
      self.clipboard_cells = []
      self.set_status("Selection was empty", STATUS_ERROR_COLOUR)
      return

    min_x = min(x for x, _ in filled)
    min_y = min(y for _, y in filled)

    self.clipboard_cells = [(x - min_x, y - min_y) for x, y in filled]
    self.set_status(f"Copied {len(self.clipboard_cells)} tiles", STATUS_OK_COLOUR)

  def paste_clipboard_at_mouse(self) -> None:
    if not self.clipboard_cells:
      self.set_status("Clipboard is empty", STATUS_ERROR_COLOUR)
      return

    self.save_history_state()

    base_x, base_y = self.screen_to_cell(pygame.mouse.get_pos())

    for rel_x, rel_y in self.clipboard_cells:
      self.filled_cells.add((base_x + rel_x, base_y + rel_y))

    self.set_status(f"Pasted {len(self.clipboard_cells)} tiles", STATUS_OK_COLOUR)

  def get_clipboard_preview_cells(self) -> list[tuple[int, int]]:
    if not self.clipboard_cells:
      return []

    base_x, base_y = self.screen_to_cell(pygame.mouse.get_pos())
    return [(base_x + rel_x, base_y + rel_y) for rel_x, rel_y in self.clipboard_cells]

  def flip_clipboard_horizontal(self) -> None:
    if not self.clipboard_cells:
      self.set_status("Clipboard is empty", STATUS_ERROR_COLOUR)
      return

    max_x = max(x for x, _ in self.clipboard_cells)
    self.clipboard_cells = [(max_x - x, y) for x, y in self.clipboard_cells]
    self.normalise_clipboard()
    self.set_status("Flipped clipboard horizontally", STATUS_OK_COLOUR)

  def flip_clipboard_vertical(self) -> None:
    if not self.clipboard_cells:
      self.set_status("Clipboard is empty", STATUS_ERROR_COLOUR)
      return

    max_y = max(y for _, y in self.clipboard_cells)
    self.clipboard_cells = [(x, max_y - y) for x, y in self.clipboard_cells]
    self.normalise_clipboard()
    self.set_status("Flipped clipboard vertically", STATUS_OK_COLOUR)

  def normalise_clipboard(self) -> None:
    if not self.clipboard_cells:
      return

    min_x = min(x for x, _ in self.clipboard_cells)
    min_y = min(y for _, y in self.clipboard_cells)
    self.clipboard_cells = [(x - min_x, y - min_y) for x, y in self.clipboard_cells]

  def paint_at_mouse(self) -> None:
    cell = self.screen_to_cell(pygame.mouse.get_pos())
    if cell != self.last_painted_cell:
      self.filled_cells.add(cell)
      self.last_painted_cell = cell

  def erase_at_mouse(self) -> None:
    cell = self.screen_to_cell(pygame.mouse.get_pos())
    if cell != self.last_erased_cell:
      self.filled_cells.discard(cell)
      self.last_erased_cell = cell

  def apply_rectangle(self) -> None:
    bounds = self.get_rectangle_bounds()
    if bounds is None:
      return

    self.save_history_state()

    x1, y1, x2, y2 = bounds

    for y in range(y1, y2 + 1):
      for x in range(x1, x2 + 1):
        self.filled_cells.add((x, y))

    self.clear_rectangle_preview()
    self.set_status("Applied rectangle", STATUS_OK_COLOUR)

  def fill_at_mouse(self) -> None:
    start_cell = self.screen_to_cell(pygame.mouse.get_pos())

    if start_cell in self.filled_cells:
      return

    self.save_history_state()

    min_x, max_x, min_y, max_y = self.get_fill_bounds_with_margin(start_cell, 2)

    queue = deque([start_cell])
    visited: set[tuple[int, int]] = {start_cell}

    while queue:
      x, y = queue.popleft()

      if (x, y) in self.filled_cells:
        continue

      self.filled_cells.add((x, y))

      neighbours = [
        (x + 1, y),
        (x - 1, y),
        (x, y + 1),
        (x, y - 1),
      ]

      for nx, ny in neighbours:
        if nx < min_x or nx > max_x or ny < min_y or ny > max_y:
          continue
        if (nx, ny) in visited:
          continue
        if (nx, ny) in self.filled_cells:
          continue

        visited.add((nx, ny))
        queue.append((nx, ny))

    self.set_status("Filled area", STATUS_OK_COLOUR)

  def get_fill_bounds_with_margin(self, start_cell: tuple[int, int], margin: int) -> tuple[int, int, int, int]:
    if not self.filled_cells:
      x, y = start_cell
      return x - 20, x + 20, y - 20, y + 20

    xs = [x for x, _ in self.filled_cells]
    ys = [y for _, y in self.filled_cells]

    min_x = min(min(xs), start_cell[0]) - margin
    max_x = max(max(xs), start_cell[0]) + margin
    min_y = min(min(ys), start_cell[1]) - margin
    max_y = max(max(ys), start_cell[1]) + margin

    return min_x, max_x, min_y, max_y

  def choose_and_import_json(self) -> None:
    root = Tk()
    root.withdraw()
    root.attributes("-topmost", True)

    try:
      filename = filedialog.askopenfilename(
        title="Import tile shape JSON",
        filetypes=[
          ("JSON files", "*.json"),
          ("All files", "*.*"),
        ],
      )
    finally:
      root.destroy()

    if filename:
      self.import_json(Path(filename))

  def import_json(self, path: Path) -> None:
    try:
      if not path.exists():
        self.set_status(f"JSON not found: {path.name}", STATUS_ERROR_COLOUR)
        return

      raw = json.loads(path.read_text(encoding="utf-8-sig"))

      # Accept either {"cells": [[x, y], ...]} or a bare [[x, y], ...] list.
      if isinstance(raw, dict):
        cells_raw = raw.get("cells")
      else:
        cells_raw = raw

      if not isinstance(cells_raw, list):
        self.set_status("Invalid JSON: expected a cells list", STATUS_ERROR_COLOUR)
        return

      imported_cells: set[tuple[int, int]] = set()

      for item in cells_raw:
        if not isinstance(item, list) or len(item) != 2:
          self.set_status("Invalid JSON: bad cell entry", STATUS_ERROR_COLOUR)
          return

        x, y = item
        if not isinstance(x, int) or not isinstance(y, int):
          self.set_status("Invalid JSON: cell coords must be ints", STATUS_ERROR_COLOUR)
          return

        imported_cells.add((x, y))

      self.save_history_state()
      self.filled_cells = imported_cells
      self.clear_selection()
      self.clear_rectangle_preview()
      self.set_status(
        f"Imported {len(imported_cells)} tiles from {path.name}",
        STATUS_OK_COLOUR,
      )

    except Exception as exc:
      self.set_status(f"JSON import failed: {exc}", STATUS_ERROR_COLOUR)

  def import_gdscript(self, path: Path) -> None:
    try:
      if not path.exists():
        self.set_status(f"GDScript not found: {path.name}", STATUS_ERROR_COLOUR)
        return

      text = path.read_text(encoding="utf-8")
      matches = re.findall(r"Vector2i\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", text)

      if not matches:
        self.set_status("No Vector2i entries found", STATUS_ERROR_COLOUR)
        return

      imported_cells = {(int(x), int(y)) for x, y in matches}

      self.save_history_state()
      self.filled_cells = imported_cells
      self.clear_selection()
      self.clear_rectangle_preview()
      self.set_status(f"Imported GDScript: {len(imported_cells)} tiles", STATUS_OK_COLOUR)

    except Exception as exc:
      self.set_status(f"GDScript import failed: {exc}", STATUS_ERROR_COLOUR)

  def draw(self) -> None:
    self.screen.fill(BACKGROUND_COLOUR)
    self.draw_grid()
    self.draw_tiles()
    self.draw_origin()
    self.draw_selection_overlay()
    self.draw_rectangle_overlay()
    self.draw_clipboard_preview()

    if self.current_tool == TOOL_SELECT:
      self.draw_selection_count_only()
    else:
      self.draw_ui()

    self.draw_status_bar()

  def draw_grid(self) -> None:
    tile_size = self.get_tile_size()

    if tile_size < 4:
      return

    start_world_x = math.floor((0 - self.camera_x) / tile_size) - 1
    end_world_x = math.ceil((WINDOW_WIDTH - self.camera_x) / tile_size) + 1
    start_world_y = math.floor((0 - self.camera_y) / tile_size) - 1
    end_world_y = math.ceil((WINDOW_HEIGHT - self.camera_y) / tile_size) + 1

    for gx in range(start_world_x, end_world_x + 1):
      x = round(self.camera_x + gx * tile_size)
      pygame.draw.line(self.screen, GRID_COLOUR, (x, 0), (x, WINDOW_HEIGHT), 1)

    for gy in range(start_world_y, end_world_y + 1):
      y = round(self.camera_y + gy * tile_size)
      pygame.draw.line(self.screen, GRID_COLOUR, (0, y), (WINDOW_WIDTH, y), 1)

  def draw_tiles(self) -> None:
    visible_cells = self.get_visible_cells()

    for cell_x, cell_y in visible_cells:
      if (cell_x, cell_y) not in self.filled_cells:
        continue

      rect = self.cell_to_screen_rect(cell_x, cell_y)
      colour = LIGHT_TILE_COLOUR if (cell_x + cell_y) % 2 == 0 else DARK_TILE_COLOUR
      pygame.draw.rect(self.screen, colour, rect)
      pygame.draw.rect(self.screen, GRID_COLOUR, rect, 1)

  def draw_origin(self) -> None:
    origin_rect = self.cell_to_screen_rect(0, 0)
    pygame.draw.rect(self.screen, ORIGIN_COLOUR, origin_rect, 2)

  def draw_selection_overlay(self) -> None:
    bounds = self.get_selection_bounds()
    if bounds is None:
      return

    x1, y1, x2, y2 = bounds
    rect1 = self.cell_to_screen_rect(x1, y1)
    rect2 = self.cell_to_screen_rect(x2, y2)

    left = min(rect1.left, rect2.left)
    top = min(rect1.top, rect2.top)
    right = max(rect1.right, rect2.right)
    bottom = max(rect1.bottom, rect2.bottom)

    overlay_surface = pygame.Surface((right - left, bottom - top), pygame.SRCALPHA)
    overlay_surface.fill(SELECTION_FILL_COLOUR)
    self.screen.blit(overlay_surface, (left, top))

    pygame.draw.rect(self.screen, SELECTION_COLOUR, (left, top, right - left, bottom - top), 2)

  def draw_rectangle_overlay(self) -> None:
    bounds = self.get_rectangle_bounds()
    if bounds is None or not self.rectangle_dragging:
      return

    x1, y1, x2, y2 = bounds
    rect1 = self.cell_to_screen_rect(x1, y1)
    rect2 = self.cell_to_screen_rect(x2, y2)

    left = min(rect1.left, rect2.left)
    top = min(rect1.top, rect2.top)
    right = max(rect1.right, rect2.right)
    bottom = max(rect1.bottom, rect2.bottom)

    overlay_surface = pygame.Surface((right - left, bottom - top), pygame.SRCALPHA)
    overlay_surface.fill(RECTANGLE_PREVIEW_FILL)
    self.screen.blit(overlay_surface, (left, top))

    pygame.draw.rect(self.screen, RECTANGLE_PREVIEW_COLOUR, (left, top, right - left, bottom - top), 2)

  def draw_clipboard_preview(self) -> None:
    if not self.clipboard_cells:
      return

    if pygame.key.get_mods() & pygame.KMOD_CTRL:
      preview_cells = self.get_clipboard_preview_cells()

      for cell_x, cell_y in preview_cells:
        rect = self.cell_to_screen_rect(cell_x, cell_y)
        pygame.draw.rect(self.screen, PASTE_PREVIEW_COLOUR, rect, 2)

  def get_visible_cells(self) -> list[tuple[int, int]]:
    tile_size = self.get_tile_size()

    start_world_x = math.floor((0 - self.camera_x) / tile_size) - 2
    end_world_x = math.ceil((WINDOW_WIDTH - self.camera_x) / tile_size) + 2
    start_world_y = math.floor((0 - self.camera_y) / tile_size) - 2
    end_world_y = math.ceil((WINDOW_HEIGHT - self.camera_y) / tile_size) + 2

    cells = []
    for y in range(start_world_y, end_world_y + 1):
      for x in range(start_world_x, end_world_x + 1):
        cells.append((x, y))

    return cells

  def draw_ui(self) -> None:
    tool_name = {
      TOOL_PAINT: "Paint",
      TOOL_ERASE: "Erase",
      TOOL_SELECT: "Select",
      TOOL_RECTANGLE: "Rectangle",
      TOOL_FILL: "Fill",
    }[self.current_tool]

    rectangle_width, rectangle_height = self.get_bounds_dimensions(self.get_rectangle_bounds())

    lines = [
      f"Tool: {tool_name}",
      "P = paint tool",
      "E = erase tool",
      "V = select tool",
      "B = rectangle tool",
      "F = fill tool",
      "LMB drag = use tool",
      "RMB drag = opposite paint/erase",
      "MMB drag = pan camera",
      "Mouse wheel = zoom",
      "Ctrl+Z = undo",
      "Ctrl+Y = redo",
      "Ctrl+C = copy selection",
      "Ctrl+V = paste at mouse",
      "Ctrl+O = choose JSON file",
      "Drop .json file = import JSON",
      "Ctrl+Shift+O = import GDScript",
      "H = flip clipboard horizontally",
      "J = flip clipboard vertically",
      "Delete = clear selection only",
      "S = save JSON",
      "G = export GDScript data",
      "C = clear all",
      "R = reset camera",
      f"Tiles: {len(self.filled_cells)}",
      f"Zoom: {self.zoom:.2f}x",
      f"Undo: {len(self.undo_stack)}",
      f"Redo: {len(self.redo_stack)}",
    ]

    if self.current_tool == TOOL_RECTANGLE and self.rectangle_dragging:
      lines.append(f"Rectangle: {rectangle_width} x {rectangle_height}")

    x = 12
    y = 12
    for line in lines:
      surf = self.font.render(line, True, TEXT_COLOUR)
      self.screen.blit(surf, (x, y))
      y += 22

  def draw_selection_count_only(self) -> None:
    count = len(self.get_selected_filled_cells())
    selection_width, selection_height = self.get_bounds_dimensions(self.get_selection_bounds())

    lines = [
      "SELECT TOOL",
      f"Tiles in selection: {count}",
      f"Selection size: {selection_width} x {selection_height}",
      "Ctrl+C copy",
      "Ctrl+V paste",
      "Ctrl+O import JSON",
      "Ctrl+Shift+O import GDScript",
      "Ctrl+Z undo",
      "Ctrl+Y redo",
      "H flip clipboard horizontally",
      "J flip clipboard vertically",
      "Delete clear selection",
    ]

    x = 12
    y = 12
    for i, line in enumerate(lines):
      font = self.big_font if i == 1 else self.font
      surf = font.render(line, True, TEXT_COLOUR)
      self.screen.blit(surf, (x, y))
      y += 30 if i == 1 else 22

  def draw_status_bar(self) -> None:
    if not self.status_message:
      return

    surf = self.font.render(self.status_message, True, self.status_colour)
    x = 12
    y = WINDOW_HEIGHT - 30
    self.screen.blit(surf, (x, y))

  def export_json(self, path: Path) -> None:
    cells = sorted(self.filled_cells, key=lambda cell: (cell[1], cell[0]))

    data = {
      "tileSize": BASE_TILE_SIZE,
      "cells": [[x, y] for x, y in cells],
    }

    path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    self.set_status(f"Saved JSON to {path.name}", STATUS_OK_COLOUR)

  def export_gdscript(self, path: Path) -> None:
    cells = sorted(self.filled_cells, key=lambda cell: (cell[1], cell[0]))

    lines = []
    lines.append("const SHAPE_CELLS := [")
    for x, y in cells:
      lines.append(f"  Vector2i({x}, {y}),")
    lines.append("]")

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    self.set_status(f"Saved GDScript to {path.name}", STATUS_OK_COLOUR)


if __name__ == "__main__":
  ShapePainter().run()