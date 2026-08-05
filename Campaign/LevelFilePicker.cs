#nullable enable

using System;
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
    return ShowWindowsDialog("System.Windows.Forms.SaveFileDialog", "OK", suggestedName);
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
}
