using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RimAgent.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using Mouse = UnityEngine.InputSystem.Mouse;
using Verse;

namespace RimAgent;

internal static class OrcaVisualInput
{
    private sealed record Frame(Game Game, Map Map, int Tick, int ScreenWidth, int ScreenHeight,
        int Width, int Height, string Scene, DateTime Expires);
    private static readonly Dictionary<string, Frame> frames = new();
    private static bool busy;

    internal static string SceneStamp()
    {
        var camera = Find.Camera;
        var position = camera?.transform.position ?? Vector3.zero;
        return MiniJson.Serialize(new object[] { Screen.width, Screen.height, Prefs.UIScale,
            position.x, position.y, position.z, camera?.orthographicSize,
            Find.WindowStack.Windows.Where(w => w is not ImmediateWindow).Select(w => new object[] {
                RuntimeHelpers.GetHashCode(w), w.windowRect.x, w.windowRect.y, w.windowRect.width, w.windowRect.height }).ToArray(),
            Find.Selector?.SelectedObjects.Select(RuntimeHelpers.GetHashCode).ToArray() });
    }

    internal static string Remember(Game game, Map map, int screenWidth, int screenHeight, int width, int height)
    {
        foreach (var key in frames.Where(p => p.Value.Game != game || p.Value.Expires < DateTime.UtcNow).Select(p => p.Key).ToArray()) frames.Remove(key);
        if (frames.Count >= 8) frames.Remove(frames.Keys.First());
        var id = Guid.NewGuid().ToString("N");
        frames[id] = new(game, map, Find.TickManager.TicksGame, screenWidth, screenHeight, width, height, SceneStamp(), DateTime.UtcNow.AddSeconds(60));
        return id;
    }

    internal static Task<object> Begin(JsonObject a, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (busy) throw new BridgeException("input_busy", "Wait for the current pointer task to complete.");
        string id = a.Text("inputFrameId", 64);
        if (!frames.TryGetValue(id, out var frame) || frame.Expires < DateTime.UtcNow || a.Number("mapId", 0, int.MaxValue) != frame.Map.uniqueID)
            throw new BridgeException("frame_expired", "Capture a new game frame before interacting.");
        Validate(frame, scene: true);
        if (Enumerable.Range(0, 3).Any(Input.GetMouseButton) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
            || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            throw new BridgeException("input_busy", "A physical mouse button or modifier is held.");
        var context = SynchronizationContext.Current;
        if (context?.GetType().FullName != "Waylith.MainThreadSynchronizationContext")
            throw new BridgeException("input_unavailable", "The supported game frame dispatcher is unavailable.");
        string action = a.Text("action", 12);
        if (action is not ("click" or "move" or "drag" or "scroll")) throw new BridgeException("invalid_arguments", "Unknown pointer action.");
        string buttonName = a["button"] == null ? "left" : a.Text("button", 8);
        int button = buttonName switch { "left" => 0, "right" => 1, "middle" => 2, _ => throw new BridgeException("invalid_arguments", "Unknown mouse button.") };
        Vector2 Point(string x, string y) => new((a.Number(x, 0, frame.Width - 1) + 0.5f) * frame.ScreenWidth / frame.Width,
            frame.ScreenHeight - (a.Number(y, 0, frame.Height - 1) + 0.5f) * frame.ScreenHeight / frame.Height);
        var start = Point("x", "y"); var end = action == "drag" ? Point("endX", "endY") : start;
        int duration = a.Number("durationMs", 100, 2000, 300), scroll = action == "scroll" ? a.Number("scrollY", -20, 20) : 0;
        if (action == "scroll" && scroll == 0) throw new BridgeException("invalid_arguments", "scrollY must be nonzero.");
        frames.Clear(); busy = true;
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var elapsed = Stopwatch.StartNew(); var dragClock = new Stopwatch();
        bool pressed = false, started = false, delivered = false;
        Vector2 last = start;
        Exception failure = null;
        // Waylith pumps a bounded snapshot of this context before IMGUI and MonoBehaviour.Update.
        // Reposting schedules the next input phase on the next frame, not a desktop mouse event.
        void Pump(object ignored)
        {
            try
            {
                if (delivered)
                {
                    if (failure != null) completion.TrySetException(failure);
                    else if (cancellation.IsCancellationRequested) completion.TrySetCanceled(cancellation);
                    else completion.TrySetResult(new { delivered = true, action, button = buttonName,
                        completion = "Game input delivered. Capture again and verify the resulting GUI; cancellation cannot undo native effects already triggered." });
                    busy = false; return;
                }
                cancellation.ThrowIfCancellationRequested();
                if (elapsed.ElapsedMilliseconds > 5000) throw new BridgeException("input_timeout", "Game input did not complete in time.");
                Validate(frame, scene: !started);
                if (started && (Mouse.current.position.ReadValue() - last).sqrMagnitude > 1f)
                    throw new BridgeException("input_interrupted", "Another input source moved the pointer.");
                if (!started)
                {
                    InputSystem.QueueMouseMove(start); started = true; dragClock.Start();
                    if (action is "click" or "drag") { InputSystem.QueueMouseButton(button, true, start); pressed = true; }
                    else if (action == "scroll") InputSystem.QueueMouseScroll(new Vector2(0, scroll), start);
                    else delivered = true;
                    if (action == "scroll") delivered = true;
                }
                else if (action == "drag" && dragClock.ElapsedMilliseconds < duration)
                {
                    last = Vector2.Lerp(start, end, (float)dragClock.ElapsedMilliseconds / duration);
                    InputSystem.QueueMouseMove(last);
                }
                else
                {
                    // Deliver the final drag position while held before releasing on a later frame.
                    if (action == "drag" && last != end) { last = end; InputSystem.QueueMouseMove(last); }
                    else { InputSystem.QueueMouseButton(button, false, last); pressed = false; delivered = true; }
                }
            }
            catch (Exception ex)
            {
                failure = ex; delivered = true;
                if (pressed) { InputSystem.QueueMouseButton(button, false, Mouse.current.position.ReadValue()); pressed = false; }
            }
            context.Post(Pump, null);
        }
        context.Post(Pump, null);
        return completion.Task;
    }

    private static void Validate(Frame frame, bool scene)
    {
        if (!ReferenceEquals(Current.Game, frame.Game) || Current.Game.CurrentMap != frame.Map)
            throw new BridgeException("frame_stale", "The game or current map changed.");
        if (!Find.TickManager.Paused) throw new BridgeException("pause_required", "Pause simulation before visual GUI interaction.");
        if (Screen.width != frame.ScreenWidth || Screen.height != frame.ScreenHeight
            || (scene && (Find.TickManager.TicksGame != frame.Tick || SceneStamp() != frame.Scene)))
            throw new BridgeException("frame_stale", "The observed scene changed. Capture again.");
    }
}
