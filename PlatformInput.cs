using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#if ANDROID
using Microsoft.Xna.Framework.Input.Touch;
using System;
using System.Collections.Generic;
using System.Diagnostics;
#endif

namespace MedivalChess;

/// <summary>
/// Platform input facade. Desktop builds continue to use MonoGame's native mouse,
/// while Android translates touch gestures into the existing mouse-driven game UI.
/// </summary>
internal static class Mouse
{
  internal static MouseState GetState()
  {
#if ANDROID
    return AndroidTouchInput.GetMouseState();
#else
    return Microsoft.Xna.Framework.Input.Mouse.GetState();
#endif
  }
}

/// <summary>
/// Keeps the existing keyboard camera controls intact and adds virtual camera keys
/// for touch drags/pinches on Android.
/// </summary>
internal static class Keyboard
{
  internal static KeyboardState GetState()
  {
#if ANDROID
    return AndroidTouchInput.GetKeyboardState();
#else
    return Microsoft.Xna.Framework.Input.Keyboard.GetState();
#endif
  }
}

#if ANDROID
internal static class AndroidTouchInput
{
  private const float DragThreshold = 18f;
  private const float PanDeadZone = 1.5f;
  private const float PinchDeadZone = 4f;
  private const double LongPressSeconds = 0.45d;

  private static readonly Stopwatch Clock = Stopwatch.StartNew();
  private static readonly List<Keys> VirtualKeys = [];

  private static int? _primaryTouchId;
  private static Vector2 _touchStart;
  private static Vector2 _lastTouch;
  private static Vector2 _pointer;
  private static double _touchStartedAt;
  private static bool _dragging;
  private static bool _rightHeld;
  private static bool _tapPulse;
  private static bool _suppressTapUntilAllReleased;
  private static float _lastPinchDistance;
  private static Vector2 _lastPinchMidpoint;

  internal static KeyboardState GetKeyboardState()
  {
    Refresh();

    KeyboardState native = Microsoft.Xna.Framework.Input.Keyboard.GetState();
    HashSet<Keys> pressed = new(native.GetPressedKeys());
    foreach (Keys key in VirtualKeys)
    {
      pressed.Add(key);
    }

    Keys[] keys = new Keys[pressed.Count];
    pressed.CopyTo(keys);
    return new KeyboardState(keys);
  }

  internal static MouseState GetMouseState() => new(
    (int)MathF.Round(_pointer.X),
    (int)MathF.Round(_pointer.Y),
    0,
    _tapPulse ? ButtonState.Pressed : ButtonState.Released,
    ButtonState.Released,
    _rightHeld ? ButtonState.Pressed : ButtonState.Released,
    ButtonState.Released,
    ButtonState.Released,
    0
  );

  private static void Refresh()
  {
    TouchCollection touches = TouchPanel.GetState();
    List<TouchLocation> active = [];
    TouchLocation? releasedPrimary = null;

    foreach (TouchLocation touch in touches)
    {
      if (touch.State == TouchLocationState.Released)
      {
        if (_primaryTouchId == touch.Id)
        {
          releasedPrimary = touch;
        }
      }
      else
      {
        active.Add(touch);
      }
    }

    VirtualKeys.Clear();

    if (active.Count >= 2)
    {
      UpdateMultiTouch(active[0], active[1]);
      return;
    }

    _lastPinchDistance = 0f;

    if (active.Count == 1)
    {
      UpdateSingleTouch(active[0]);
      return;
    }

    if (releasedPrimary is TouchLocation released)
    {
      _pointer = released.Position;
      _tapPulse = !_dragging && !_rightHeld && !_suppressTapUntilAllReleased;
    }
    else
    {
      _tapPulse = false;
    }

    _primaryTouchId = null;
    _rightHeld = false;
    _dragging = false;
    _suppressTapUntilAllReleased = false;
  }

  private static void UpdateSingleTouch(TouchLocation touch)
  {
    _tapPulse = false;

    if (_primaryTouchId != touch.Id)
    {
      _primaryTouchId = touch.Id;
      _touchStart = touch.Position;
      _lastTouch = touch.Position;
      _pointer = touch.Position;
      _touchStartedAt = Clock.Elapsed.TotalSeconds;
      _dragging = false;
      _rightHeld = false;
      return;
    }

    Vector2 delta = touch.Position - _lastTouch;
    _pointer = touch.Position;

    if (!_dragging && Vector2.Distance(_touchStart, touch.Position) >= DragThreshold)
    {
      _dragging = true;
      _rightHeld = false;
      _suppressTapUntilAllReleased = true;
    }

    if (_dragging)
    {
      AddPanKeys(delta);
    }
    else if (!_rightHeld && Clock.Elapsed.TotalSeconds - _touchStartedAt >= LongPressSeconds)
    {
      // Long press is the mobile equivalent of right click: attack, use abilities,
      // and any other secondary action already handled by Game1.
      _rightHeld = true;
      _suppressTapUntilAllReleased = true;
    }

    _lastTouch = touch.Position;
  }

  private static void UpdateMultiTouch(TouchLocation first, TouchLocation second)
  {
    _tapPulse = false;
    _rightHeld = false;
    _dragging = true;
    _suppressTapUntilAllReleased = true;
    _primaryTouchId = first.Id;

    Vector2 midpoint = (first.Position + second.Position) * 0.5f;
    float distance = Vector2.Distance(first.Position, second.Position);
    _pointer = midpoint;

    if (_lastPinchDistance > 0f)
    {
      float pinchDelta = distance - _lastPinchDistance;
      if (pinchDelta > PinchDeadZone)
      {
        VirtualKeys.Add(Keys.E); // Default Zoom In binding.
      }
      else if (pinchDelta < -PinchDeadZone)
      {
        VirtualKeys.Add(Keys.Q); // Default Zoom Out binding.
      }

      AddPanKeys(midpoint - _lastPinchMidpoint);
    }

    _lastPinchDistance = distance;
    _lastPinchMidpoint = midpoint;
    _lastTouch = first.Position;
  }

  private static void AddPanKeys(Vector2 delta)
  {
    // Dragging the board follows the finger, hence the camera moves in the
    // opposite direction. These mirror the game's default WASD bindings.
    if (delta.X > PanDeadZone) VirtualKeys.Add(Keys.A);
    else if (delta.X < -PanDeadZone) VirtualKeys.Add(Keys.D);

    if (delta.Y > PanDeadZone) VirtualKeys.Add(Keys.W);
    else if (delta.Y < -PanDeadZone) VirtualKeys.Add(Keys.S);
  }
}
#endif
