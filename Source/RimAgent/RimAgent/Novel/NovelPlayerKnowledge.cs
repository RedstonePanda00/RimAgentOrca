using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimAgent
{
    // This policy belongs to novel evidence; it does not change storyteller knowledge or game state.
    public static class NovelPlayerKnowledge
    {
        public const int Version = 1;
        public const string StateMarker = "playerKnowledgeVersion";

        private static bool PlayerPawn(Pawn pawn)
        { return (Faction.OfPlayer != null && pawn.Faction == Faction.OfPlayer) || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony; }

        public static bool CanObserve(Pawn pawn)
        {
            if (pawn == null) return false;
            bool player = PlayerPawn(pawn);
            var caravan = pawn.GetCaravan();
            if (caravan != null) return caravan.Faction == Faction.OfPlayer;
            if (pawn.Spawned) return !pawn.Position.Fogged(pawn.Map);
            var corpse = pawn.Corpse;
            if (corpse != null && corpse.Spawned) return !corpse.Position.Fogged(corpse.Map);
            var holder = pawn.SpawnedParentOrMe;
            if (holder == null || !holder.Spawned || holder.Position.Fogged(holder.Map)) return false;
            // Owning/knowing a pawn and seeing a casket are different permissions.
            return player || (pawn.CarriedBy != null && pawn.relations?.everSeenByPlayer == true);
        }

        public static bool CanReference(Pawn pawn)
        {
            if (pawn == null) return false;
            if (CanObserve(pawn)) return true;
            // Public social relations can name a previously met off-map person, but never reveal fresh state.
            return pawn.MapHeld == null && (PlayerPawn(pawn) || pawn.relations?.everSeenByPlayer == true);
        }

        public static bool PublicRelation(Pawn owner, Pawn other)
        {
            return owner?.relations != null && !owner.relations.hidePawnRelations && other?.relations != null
                && !other.relations.hidePawnRelations && other.relations.everSeenByPlayer
                && other.Name != null && !other.Name.Numerical
                && !(other.RaceProps.Animal && other.Dead && other.Corpse == null) && CanReference(other);
        }

        public static bool KnownBaseline(string json)
        {
            var fields = MiniJson.Deserialize(json ?? "") as Dictionary<string, object>;
            return fields != null && (fields.ContainsKey(StateMarker) || PlayerProfile(fields));
        }

        public static bool CurrentBaseline(string json)
        {
            var fields = MiniJson.Deserialize(json ?? "") as Dictionary<string, object>;
            return fields != null && fields.TryGetValue(StateMarker, out var version) && Convert.ToInt32(version) == Version;
        }

        private static bool PlayerProfile(Dictionary<string, object> fields)
        { return new[] { "isColonist", "isPrisonerOfColony", "isSlaveOfColony" }.Any(k => fields.TryGetValue(k, out var v) && v is bool b && b); }

        // Do not infer old visibility from today's map: discovering someone later must not rewrite earlier knowledge.
        public static List<NovelRecord> ForWriting(IEnumerable<NovelRecord> source)
        {
            var records = source.ToList();
            var playerProfiles = new Dictionary<string, Dictionary<string, object>>();
            var playerAt = new Dictionary<string, int>();
            foreach (var record in records)
            {
                if (record.category != "profile" || record.playerKnowledgeVersion != 0) continue;
                var fields = MiniJson.Deserialize(record.stateAfter) as Dictionary<string, object>;
                if (fields == null || !PlayerProfile(fields)) continue;
                playerProfiles[record.id] = fields;
                if (!playerAt.TryGetValue(record.subject, out int old) || record.observedTick < old) playerAt[record.subject] = record.observedTick;
            }
            var result = new List<NovelRecord>(records.Count);
            foreach (var record in records)
            {
                if (record.playerKnowledgeVersion == Version) { result.Add(record); continue; }
                if (record.playerKnowledgeVersion != 0) continue;
                if (playerProfiles.TryGetValue(record.id, out var fields))
                {
                    // Old flattened relation/persona text has no visibility provenance. Keep the archive, not that text in prompts.
                    fields.Remove("relations"); fields.Remove("rimtalkPersona");
                    var clean = record.Snapshot();
                    clean.stateBefore = ""; clean.stateAfter = MiniJson.Serialize(fields);
                    clean.summary = "Known player pawn profile: " + clean.stateAfter;
                    clean.detail = clean.summary; result.Add(clean); continue;
                }
                bool publicRecord = record.source == "letters" && record.category == "letter"
                    || record.source == "rimtalk" && record.category == "dialogue"
                    || record.category == "gap"
                    || record.source == "world" && new[] { "colony_state", "construction", "research", "caravan" }.Contains(record.category)
                    || record.source == "rimagent_incidents" && (record.category == "incident" || record.category == "incident_outcome");
                if (publicRecord) { result.Add(record); continue; }
                var people = record.PersonReferences.ToList();
                // A player's first encounter is public evidence even before the other actor has a dossier.
                if (record.source == "logs" && record.category == "log"
                    && people.Any(id => playerAt.TryGetValue(id, out int t) && t <= record.observedTick)) { result.Add(record); continue; }
                // Legacy tales only saved DominantPawn, so their secondary actors cannot be vetted.
                if (record.source != "pawns" || (record.category != "pawn_state" && record.category != "daily_records")) continue;
                // Only player pawns had reliably inspectable legacy state; old world-pawn updates were omniscient.
                if (!playerAt.TryGetValue(record.subject, out int playerTick) || playerTick > record.observedTick) continue;
                if (record.category == "pawn_state")
                {
                    var state = MiniJson.Deserialize(record.stateAfter) as Dictionary<string, object>;
                    if (state == null) continue;
                    // Old collectors did not honor hidden hediff/thought flags. Public combat logs still retain observed wounds.
                    state.Remove("health"); state.Remove("moodReasons");
                    var clean = record.Snapshot(); clean.stateBefore = ""; clean.stateAfter = MiniJson.Serialize(state);
                    clean.summary = "Known player pawn state: " + clean.stateAfter; clean.detail = clean.summary;
                    result.Add(clean);
                }
                else result.Add(record);
            }
            return result;
        }

        public static void PrepareChapter(NovelChapter chapter)
        {
            if (chapter.playerKnowledgeVersion == Version || chapter.complete) return;
            chapter.playerKnowledgeVersion = Version; chapter.frozen = false;
            chapter.outline = ""; chapter.selectedIds.Clear(); chapter.personIds.Clear();
            chapter.organizeCompression = chapter.writeCompression = chapter.reviewCompression = null;
            chapter.draftTitle = chapter.draftText = chapter.draftContinuity = chapter.reviewFeedback = chapter.reviewResponse = "";
            chapter.lastResponse = ""; chapter.reviewPending = false; chapter.factsReviewed = false;
            chapter.draftingContinuity = ""; chapter.draftingContinuityCaptured = false;
            chapter.error = ""; chapter.outputTruncated = false; chapter.recordIds.Clear();
            foreach (var step in chapter.recovery.steps.Values)
            { step.detail = ""; step.kind = NovelFailureKind.None; step.needsRetry = false; step.manualPermits = 0; }
            // Preserve retry counters; upgrading perspective cannot grant an unlimited paid retry budget.
        }
    }
}
