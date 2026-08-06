#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MedivalChess.Shared;

namespace MedivalChess.Campaign;

/// <summary>
/// Uses the native Windows dialog when it is present, without making the cross-platform
/// MonoGame target depend on Windows Forms. Other platforms retain the local-level browser.
/// </summary>
internal static class LevelFilePicker
{
  internal static string? PickImportPath()
  {
    return ShowWindowsDialog("System.Windows.Forms.OpenFileDialog", "OK", null);
  }

  internal static string? PickExportPath(string suggestedName)
  {
    string? path = ShowWindowsDialog("System.Windows.Forms.SaveFileDialog", "OK", suggestedName);
    if (path is not null) return EnsureExpectedExtension(path);

    // DesktopGL builds do not always ship WinForms. On Windows 10 and 11, a missing native
    // dialog should not turn EXPORT into a no-op: save to Downloads with a collision-free name.
    // A real dialog cancellation still returns null because the dialog type was available.
    return !OperatingSystem.IsWindows() || IsWindowsFormsAvailable()
      ? null
      : GetDownloadsFallbackPath(suggestedName);
  }

  private static string? ShowWindowsDialog(string typeName, string acceptedResult, string? suggestedName)
  {
    if (!OperatingSystem.IsWindows()) return null;
    Type? dialogType = Type.GetType($"{typeName}, System.Windows.Forms", throwOnError: false);
    if (dialogType is null) return null;

    object? dialog = null;
    try
    {
      dialog = Activator.CreateInstance(dialogType);
      dialogType.GetProperty("Filter")?.SetValue(dialog, $"Crown & Siege levels (*{CampaignLevelFormat.Extension})|*{CampaignLevelFormat.Extension}");
      dialogType.GetProperty("DefaultExt")?.SetValue(dialog, CampaignLevelFormat.Extension.TrimStart('.'));
      dialogType.GetProperty("AddExtension")?.SetValue(dialog, true);
      if (!string.IsNullOrWhiteSpace(suggestedName))
      {
        dialogType.GetProperty("FileName")?.SetValue(dialog, suggestedName);
      }

      object? result = dialogType.GetMethod("ShowDialog", Type.EmptyTypes)?.Invoke(dialog, null);
      if (!string.Equals(result?.ToString(), acceptedResult, StringComparison.OrdinalIgnoreCase)) return null;
      return dialogType.GetProperty("FileName")?.GetValue(dialog) as string;
    }
    catch (TargetInvocationException)
    {
      return null;
    }
    finally
    {
      if (dialog is IDisposable disposable) disposable.Dispose();
    }
  }

  private static bool IsWindowsFormsAvailable() =>
    Type.GetType("System.Windows.Forms.SaveFileDialog, System.Windows.Forms", throwOnError: false) is not null;

  private static string EnsureExpectedExtension(string path) => CampaignLevelSerializer.HasExpectedExtension(path)
    ? path
    : path + CampaignLevelFormat.Extension;

  private static string GetDownloadsFallbackPath(string suggestedName)
  {
    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string downloads = Path.Combine(
      string.IsNullOrWhiteSpace(userProfile) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : userProfile,
      "Downloads"
    );
    string fileName = CreateSafeLevelFileName(suggestedName);
    string candidate = Path.Combine(downloads, fileName);
    int suffix = 2;
    while (File.Exists(candidate))
    {
      candidate = Path.Combine(downloads, $"{Path.GetFileNameWithoutExtension(fileName)} ({suffix++}){CampaignLevelFormat.Extension}");
    }
    return candidate;
  }

  internal static string CreateSafeLevelFileName(string? name)
  {
    string baseName = string.IsNullOrWhiteSpace(name) ? "Untitled" : Path.GetFileNameWithoutExtension(name);
    string safe = string.Concat(baseName.Select(character =>
      Path.GetInvalidFileNameChars().Contains(character) || char.IsControl(character) ? '_' : character)).Trim(' ', '.');
    if (string.IsNullOrWhiteSpace(safe)) safe = "Untitled";
    if (IsWindowsReservedName(safe)) safe = "_" + safe;
    return safe + CampaignLevelFormat.Extension;
  }

  private static bool IsWindowsReservedName(string value)
  {
    string upper = value.ToUpperInvariant();
    return upper is "CON" or "PRN" or "AUX" or "NUL" ||
      (upper.StartsWith("COM", StringComparison.Ordinal) && upper.Length == 4 && upper[3] is >= '1' and <= '9') ||
      (upper.StartsWith("LPT", StringComparison.Ordinal) && upper.Length == 4 && upper[3] is >= '1' and <= '9');
  }
}
