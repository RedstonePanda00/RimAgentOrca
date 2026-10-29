using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RimAgent.Core;
using RimWorld;
using Verse;

namespace RimAgent;

internal sealed partial class OrcaAgentGameAdapter
{
    private sealed record WatchedFrame(string Id, string Reason, int EventTick, GameImage Image);
    private readonly List<WatchedFrame> watchedFrames = new();
    private Game watchOwner;
    private Map watchMap;
    private bool watchEnabled, watchEvents;
    private int watchInterval, watchLastTick, watchRaids, watchHostiles, watchEventTick;
    private string watchWindows, watchPendingReason, watchError;
    private Task<GameImage> watchCapture;
    private int watchVersion, captureVersion;
    private object SetVisualWatch(JsonObject a)
    {
        watchVersion++;
        watchMap = Map(a.Number("mapId",0,int.MaxValue)); watchOwner = Current.Game;
        watchEnabled = a.Flag("enabled"); watchEvents = a["events"] == null || a.Flag("events");
        watchInterval = a.Number("intervalTicks",0,60000,0); watchLastTick = Find.TickManager.TicksGame;
        watchRaids = Find.StoryWatcher.statsRecord.numRaidsEnemy; watchHostiles = VisibleHostiles(); watchWindows = BlockingSignature();
        watchPendingReason = null; watchedFrames.Clear(); watchError = null;
        return VisualWatch(a);
    }
    private int VisibleHostiles() => watchMap.mapPawns.AllPawnsSpawned.Count(p => !p.Position.Fogged(watchMap) && !p.Downed && p.HostileTo(Faction.OfPlayer));
    private static string BlockingSignature() => string.Join("|",Find.WindowStack.Windows.Where(w => w.forcePause).Select(w => w.GetType().Name + ":" + w.ID));
    private object VisualWatch(JsonObject a)
    {
        if (watchMap != null && watchMap.uniqueID != a.Number("mapId",0,int.MaxValue)) throw new BridgeException("wrong_watch_map","This watch belongs to another map.");
        return new { enabled = watchEnabled, mapId = watchMap?.uniqueID, events = watchEvents, intervalTicks = watchInterval,
        pendingReason = watchPendingReason, lastError = watchError, frames = watchedFrames.Select(f => new { frameId = f.Id, reason = f.Reason, eventTick = f.EventTick, captureTick = f.Image.Tick, mapId = f.Image.MapId }).ToArray() };
    }
    private Task<object> WatchedImage(JsonObject a)
    {
        int map = a.Number("mapId",0,int.MaxValue); var id = a.Text("frameId",64);
        var frame = watchedFrames.FirstOrDefault(f => f.Id == id && f.Image.MapId == map)
            ?? throw new BridgeException("frame_expired","This bounded frame was replaced; query visual_watch_get or take a new capture.");
        return Task.FromResult(frame.Image.View());
    }
    private void UpdateVisualWatch()
    {
        if (watchCapture?.IsCompleted == true)
        {
            try { var img = watchCapture.GetAwaiter().GetResult(); if (captureVersion == watchVersion && watchEnabled && watchOwner == Current.Game && img.MapId == watchMap.uniqueID) {
                watchedFrames.Add(new(Guid.NewGuid().ToString("N"),watchPendingReason,watchEventTick,img));
                if (watchedFrames.Count > 4) watchedFrames.RemoveAt(0);
            } }
            catch (Exception e) { watchError = e.Message; }
            finally { watchCapture = null; watchPendingReason = null; }
        }
        if (!watchEnabled) return;
        if (watchOwner != Current.Game || !Find.Maps.Contains(watchMap)) { watchEnabled = false; watchedFrames.Clear(); return; }
        int tick = Find.TickManager.TicksGame, raids = Find.StoryWatcher.statsRecord.numRaidsEnemy, hostiles = VisibleHostiles(); var windows = BlockingSignature();
        string reason = watchEvents && raids > watchRaids ? "raid_started" : watchEvents && hostiles > watchHostiles ? "hostiles_appeared" : watchEvents && watchHostiles > 0 && hostiles == 0 ? "hostiles_cleared" : watchEvents && windows != watchWindows ? "blocking_windows_changed" : watchInterval > 0 && tick - watchLastTick >= watchInterval ? "interval" : null;
        watchRaids = raids; watchHostiles = hostiles; watchWindows = windows;
        if (reason != null && watchCapture == null) { watchPendingReason = reason; watchEventTick = tick; watchLastTick = tick; }
        if (watchCapture != null || watchPendingReason == null || Current.Game.CurrentMap != watchMap) return;
        try { captureVersion = watchVersion; watchCapture = OrcaVisualCapture.Capture(watchMap,960,CancellationToken.None); watchError = null; }
        catch (Exception e) { watchError = e.Message; watchPendingReason = null; }
    }
}
