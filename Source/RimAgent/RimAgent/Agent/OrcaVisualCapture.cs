using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using RimAgent.Core;
using UnityEngine;
using UnityStubs.Rendering;
using Verse;

namespace RimAgent;

internal sealed record GameImage(int MapId, int Tick, int Width, int Height, string Data)
{
    public string InputFrameId { get; init; }
    public object View() => new { mapId = MapId, tick = Tick, width = Width, height = Height, inputFrameId = InputFrameId, source = "game framebuffer including game UI", image = new { mimeType = "image/jpeg", data = Data } };
}

public sealed class OrcaVisualCapture : MonoBehaviour
{
    private static OrcaVisualCapture runner;
    private bool pending;
    internal static Task<GameImage> Capture(Map map, int maxWidth, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Current.Game.CurrentMap != map) throw new BridgeException("wrong_camera_map", "Use camera_set for this map before capturing its rendered view.");
        if (runner == null)
        {
            var obj = new GameObject("RimAgent.VisualCapture");
            runner = obj.AddComponent<OrcaVisualCapture>();
            DontDestroyOnLoad(obj);
        }
        if (runner.pending) throw new BridgeException("capture_busy", "Another frame capture is pending.");
        var completion = new TaskCompletionSource<GameImage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        runner.pending = true;
        runner.StartCoroutine(runner.CaptureFrame(Current.Game, map, maxWidth, completion, timeout.Token));
        return AwaitFrame();
        async Task<GameImage> AwaitFrame()
        {
            try { return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
            finally { timeout.Dispose(); }
        }
    }
    private IEnumerator CaptureFrame(Game owner, Map map, int maxWidth, TaskCompletionSource<GameImage> completion, CancellationToken cancellation)
    {
        var stamp = OrcaVisualInput.SceneStamp();
        var backend = ScreenCaptureBackend.Current;
        if (backend == null || !backend.RequestCapture())
        {
            pending = false; completion.TrySetException(new BridgeException("capture_unavailable", "Game renderer cannot capture a frame.")); yield break;
        }
        for (int frame = 0; frame < 30; frame++)
        {
            yield return new WaitForEndOfFrame();
            if (cancellation.IsCancellationRequested || !ReferenceEquals(Current.Game, owner) || Current.Game.CurrentMap != map)
            { pending = false; completion.TrySetCanceled(); yield break; }
            if (!backend.TryReadScreen(out var raw, out var width, out var height)) { backend.RequestCapture(); continue; }
            Texture2D texture = null;
            try
            {
                if (width < 1 || height < 1 || raw.Length < checked(width * height * 4)) throw new InvalidOperationException("Renderer returned an invalid frame.");
                float scale = Math.Min(1f, Math.Min((float)maxWidth / width, 1080f / height));
                int w = Math.Max(1, (int)(width * scale)), h = Math.Max(1, (int)(height * scale));
                var pixels = new byte[w * h * 4];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    // Renderer rows are top-down; Unity texture storage is bottom-up.
                    Buffer.BlockCopy(raw, ((y * height / h) * width + x * width / w) * 4, pixels, ((h - 1 - y) * w + x) * 4, 4);
                texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.LoadRawTextureData(pixels); texture.Apply(false);
                var jpeg = texture.EncodeToJPG(75);
                if (jpeg.Length > 600000) jpeg = texture.EncodeToJPG(40);
                if (jpeg.Length > 600000) throw new BridgeException("capture_too_large", "Retry with a lower maxWidth (minimum 320).");
                var inputFrame = stamp == OrcaVisualInput.SceneStamp() ? OrcaVisualInput.Remember(owner, map, width, height, w, h) : null;
                completion.TrySetResult(new GameImage(map.uniqueID, Find.TickManager.TicksGame, w, h, Convert.ToBase64String(jpeg)) { InputFrameId = inputFrame });
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { if (texture != null) Destroy(texture); pending = false; }
            yield break;
        }
        pending = false; completion.TrySetException(new BridgeException("capture_unavailable", "No readable rendered frame arrived; ensure the game is rendering."));
    }
}
