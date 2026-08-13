using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Xna.Framework;
using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace MedivalChess.Android;

[Activity(
  Label = "Crown & Siege",
  MainLauncher = true,
  Icon = "@drawable/icon",
  Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
  AlwaysRetainTaskState = true,
  LaunchMode = LaunchMode.SingleInstance,
  ScreenOrientation = ScreenOrientation.SensorLandscape,
  ConfigurationChanges = ConfigChanges.Orientation |
                         ConfigChanges.ScreenSize |
                         ConfigChanges.Keyboard |
                         ConfigChanges.KeyboardHidden
)]
public sealed class MainActivity : AndroidGameActivity
{
  private Game1 _game;
  private View _gameView;

  protected override void OnCreate(Bundle bundle)
  {
    base.OnCreate(bundle);

    ConfigureImmersiveMode();

    _game = new Game1();
    ConfigureMobileBackBuffer(_game);

    _gameView = _game.Services.GetService(typeof(View)) as View
      ?? throw new InvalidOperationException("MonoGame did not provide an Android game view.");

    SetContentView(_gameView);

    // Never block startup on the network. With no connection the check simply
    // fails silently and the installed game starts normally.
    _ = GitHubUpdateChecker.CheckForUpdateAsync(this);

    _game.Run();
  }

  protected override void OnWindowFocusChanged(bool hasFocus)
  {
    base.OnWindowFocusChanged(hasFocus);
    if (hasFocus)
    {
      ConfigureImmersiveMode();
    }
  }

  private void ConfigureImmersiveMode()
  {
    if (Window is null)
    {
      return;
    }

    Window.AddFlags(WindowManagerFlags.KeepScreenOn);
    Window.SetFlags(WindowManagerFlags.Fullscreen, WindowManagerFlags.Fullscreen);
#pragma warning disable CS0618
    Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
      SystemUiFlags.ImmersiveSticky |
      SystemUiFlags.Fullscreen |
      SystemUiFlags.HideNavigation |
      SystemUiFlags.LayoutStable |
      SystemUiFlags.LayoutFullscreen |
      SystemUiFlags.LayoutHideNavigation
    );
#pragma warning restore CS0618
  }

  private void ConfigureMobileBackBuffer(Game game)
  {
    int physicalWidth = Resources?.DisplayMetrics?.WidthPixels ?? 1920;
    int physicalHeight = Resources?.DisplayMetrics?.HeightPixels ?? 1080;
    int landscapeWidth = Math.Max(physicalWidth, physicalHeight);
    int landscapeHeight = Math.Max(1, Math.Min(physicalWidth, physicalHeight));

    // Render to a consistent logical height instead of a phone's full native
    // resolution. Existing UI therefore remains readable/touchable on 1080p,
    // 1440p and high-DPI phones, while the width follows each device's aspect ratio.
    const int logicalHeight = 720;
    int logicalWidth = (int)Math.Round(logicalHeight * (landscapeWidth / (double)landscapeHeight));
    logicalWidth = Math.Clamp(logicalWidth, 960, 1680);

    if (game.Services.GetService(typeof(IGraphicsDeviceManager)) is GraphicsDeviceManager graphics)
    {
      graphics.PreferredBackBufferWidth = logicalWidth;
      graphics.PreferredBackBufferHeight = logicalHeight;
      graphics.IsFullScreen = true;
    }
  }
}

internal static class GitHubUpdateChecker
{
  private const string LatestCommitUrl =
    "https://api.github.com/repos/Breakerchap/MonoGame-MedivalChess/commits/master";
  private const string RepositoryUrl =
    "https://github.com/Breakerchap/MonoGame-MedivalChess";

  internal static async Task CheckForUpdateAsync(Activity activity)
  {
    try
    {
      using HttpClient client = new()
      {
        Timeout = TimeSpan.FromSeconds(4)
      };
      client.DefaultRequestHeaders.UserAgent.ParseAdd("CrownAndSiege-Android/1.0");

      using HttpResponseMessage response = await client.GetAsync(LatestCommitUrl).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        return;
      }

      await using System.IO.Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
      using JsonDocument document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
      if (!document.RootElement.TryGetProperty("sha", out JsonElement shaElement))
      {
        return;
      }

      string latestSha = shaElement.GetString() ?? string.Empty;
      string installedSha = GetInstalledCommit();
      if (string.IsNullOrWhiteSpace(latestSha) ||
          string.IsNullOrWhiteSpace(installedSha) ||
          installedSha.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
          latestSha.Equals(installedSha, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      activity.RunOnUiThread(() => ShowUpdateDialog(activity));
    }
    catch (HttpRequestException)
    {
      // Offline is a normal startup state.
    }
    catch (TaskCanceledException)
    {
      // Slow/no network: do not delay the game.
    }
    catch (Exception exception)
    {
      System.Diagnostics.Debug.WriteLine($"GitHub update check failed: {exception.Message}");
    }
  }

  private static string GetInstalledCommit()
  {
    foreach (AssemblyMetadataAttribute metadata in
             typeof(MainActivity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
    {
      if (metadata.Key == "GitCommit")
      {
        return metadata.Value ?? "unknown";
      }
    }

    return "unknown";
  }

  private static void ShowUpdateDialog(Activity activity)
  {
    if (activity.IsFinishing)
    {
      return;
    }

    new AlertDialog.Builder(activity)
      .SetTitle("Update available")
      .SetMessage("A newer Crown & Siege build is available on GitHub.")
      .SetNegativeButton("Later", (_, _) => { })
      .SetPositiveButton("Open GitHub", (_, _) =>
      {
        Intent intent = new(Intent.ActionView, global::Android.Net.Uri.Parse(RepositoryUrl));
        activity.StartActivity(intent);
      })
      .Show();
  }
}
