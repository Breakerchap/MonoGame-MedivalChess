import json

INPUT_FILE = r"C:\Users\Remy\Documents\CodingProjects\MonoGameMedivalChess\MedivalChess\tools\painted_shape.json"
OUTPUT_FILE = "board.json"


def format_json(data):
  lines = ["{"]

  items = list(data.items())

  for index, (key, value) in enumerate(items):
    comma = "," if index < len(items) - 1 else ""

    if key == "cells" and isinstance(value, list):
      lines.append(f'  "{key}": [')

      for i, cell in enumerate(value):
        cell_comma = "," if i < len(value) - 1 else ""
        lines.append(f"    {json.dumps(cell)}{cell_comma}")

      lines.append(f"  ]{comma}")

    else:
      lines.append(
        f'  {json.dumps(key)}: {json.dumps(value)}{comma}'
      )

  lines.append("}")

  return "\n".join(lines)


def main():
  with open(INPUT_FILE, "r", encoding="utf-8") as file:
    data = json.load(file)

  formatted = format_json(data)

  with open(OUTPUT_FILE, "w", encoding="utf-8") as file:
    file.write(formatted)

  print(f"Cleaned JSON written to {OUTPUT_FILE}")


if __name__ == "__main__":
  main()