from pathlib import Path

# Keep comments/tests aligned with the user's updated CPU thinking times.
path = Path('MedivalChess.CPU/CpuProfiles.cs')
text = path.read_text()
text = text.replace('/// <summary>The campaign profile: the full CPU logic with a responsive 1.4-second budget.</summary>',
                    '/// <summary>The campaign profile: the full CPU logic with a three-second analysis budget.</summary>')
text = text.replace('/// <summary>The full CPU logic with a five-second analysis budget.</summary>',
                    '/// <summary>The full CPU logic with an eight-second analysis budget.</summary>')
path.write_text(text)

path = Path('MedivalChess.Tests/CpuPlayerTests.cs')
text = path.read_text()
old = '''    Assert.Equal(250, easy.Search.MaxSearchMilliseconds);\n    Assert.Equal(700, medium.Search.MaxSearchMilliseconds);\n    Assert.Equal(1_400, hard.Search.MaxSearchMilliseconds);\n    Assert.Equal(5_000, best.Search.MaxSearchMilliseconds);'''
new = '''    Assert.Equal(500, easy.Search.MaxSearchMilliseconds);\n    Assert.Equal(1_000, medium.Search.MaxSearchMilliseconds);\n    Assert.Equal(3_000, hard.Search.MaxSearchMilliseconds);\n    Assert.Equal(8_000, best.Search.MaxSearchMilliseconds);'''
if old not in text:
    raise SystemExit('CpuPlayerTests timing assertions not found')
path.write_text(text.replace(old, new, 1))

path = Path('MedivalChess.Tests/CpuAdvancedTests.cs')
text = path.read_text()
old = '''    Assert.Equal(CpuDifficultyLevel.Normal, profile.Difficulty);\n    Assert.Equal(700, profile.Search.MaxSearchMilliseconds);'''
new = '''    Assert.Equal(CpuDifficultyLevel.Normal, profile.Difficulty);\n    Assert.Equal(1_000, profile.Search.MaxSearchMilliseconds);'''
if old not in text:
    raise SystemExit('CpuAdvancedTests timing assertion not found')
path.write_text(text.replace(old, new, 1))
