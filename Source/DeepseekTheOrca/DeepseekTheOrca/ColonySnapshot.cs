using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class ColonySnapshot
    {
        public int colonists;
        public int downedColonists;
        public int mentalStateColonists;
        public float averageMood;
        public float playerWealth;
        public float threatPoints;
        public float humanEdibleNutrition;
        public string recentIncidents;

        public static ColonySnapshot Capture(IIncidentTarget target)
        {
            ColonySnapshot snapshot = new ColonySnapshot();
            snapshot.playerWealth = target.PlayerWealthForStoryteller;
            snapshot.threatPoints = StorytellerUtility.DefaultThreatPointsNow(target);

            List<Pawn> pawns = target.PlayerPawnsForStoryteller.Where(p => p != null && !p.Dead).ToList();
            snapshot.colonists = pawns.Count;
            snapshot.downedColonists = pawns.Count(p => p.Downed);
            snapshot.mentalStateColonists = pawns.Count(p => p.InMentalState);

            List<float> moods = pawns
                .Where(p => p.needs != null && p.needs.mood != null)
                .Select(p => p.needs.mood.CurLevel)
                .ToList();
            snapshot.averageMood = moods.Count == 0 ? 0.5f : moods.Average();

            Map map = target as Map;
            snapshot.humanEdibleNutrition = map == null ? -1f : map.resourceCounter.TotalHumanEdibleNutrition;

            if (target.StoryState != null && target.StoryState.RecentRandomIncidents != null)
            {
                snapshot.recentIncidents = string.Join(", ", target.StoryState.RecentRandomIncidents.Select(i => i.defName).ToArray());
            }
            else
            {
                snapshot.recentIncidents = "";
            }

            return snapshot;
        }
    }
}
