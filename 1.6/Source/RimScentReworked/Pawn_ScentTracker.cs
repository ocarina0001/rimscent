using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimScentReworked
{
    public class Pawn_ScentTracker : ThingComp
    {
        private static PawnCapacityDef SmellCapacityDef;
        private static StatDef SmellStatDef;
        private static HashSet<ThingDef> ScentThingDefs;
        private static HashSet<HediffDef> ScentHediffDefs;
        private static bool cacheBuilt;

        private static void EnsureCacheBuilt()
        {
            if (cacheBuilt) return;
            SmellCapacityDef = DefDatabase<PawnCapacityDef>.GetNamedSilentFail("RimScent_Smell");
            SmellStatDef = DefDatabase<StatDef>.GetNamedSilentFail("RimScent_SmellSensitivity");
            ScentThingDefs = new HashSet<ThingDef>();
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
            {
                var ext = d.GetModExtension<ModExtension_Scent>();
                if (ext?.thought != null) ScentThingDefs.Add(d);
            }
            ScentHediffDefs = new HashSet<HediffDef>();
            foreach (HediffDef d in DefDatabase<HediffDef>.AllDefs)
            {
                var ext = d.GetModExtension<ModExtension_Scent>();
                if (ext?.thought != null) ScentHediffDefs.Add(d);
            }
            cacheBuilt = true;
        }
        private int scentTickOffset;
        private ThoughtDef activeThought;
        private Pawn Pawn => parent as Pawn;

        public override void CompTick()
        {
            int interval = RimScentReworkedMod.Settings?.scentTickInterval ?? 500;
            if (interval <= 0) interval = 500;
            if (scentTickOffset == 0)
                scentTickOffset = Rand.Range(0, interval);
            if ((Find.TickManager.TicksGame + scentTickOffset) % interval != 0)
                return;
            Pawn pawn = Pawn;
            if (pawn.IsAnimal) return;
            if (pawn == null || !pawn.Spawned || pawn.needs?.mood == null) return;
            if (!PawnAllowedToSmell(pawn))
            {
                ClearThought(pawn);
                return;
            }
            UpdateScent(pawn);
        }

        private void UpdateScent(Pawn pawn)
        {
            EnsureCacheBuilt();
            float capacityFactor = SmellCapacityDef != null ? pawn.health.capacities.GetLevel(SmellCapacityDef) : 1f;
            float statFactor = SmellStatDef != null ? pawn.GetStatValue(SmellStatDef) : 1f;
            float smellFactor = capacityFactor * statFactor;
            if (smellFactor <= 0f)
            {
                ClearThought(pawn);
                return;
            }
            var storyTraits = pawn.story?.traits?.allTraits;
            bool pawnHasDysosmicTrait = false;
            if (storyTraits != null)
            {
                foreach (Trait t in storyTraits)
                {
                    if (t.def.GetModExtension<ModExtension_Dysosmic>() != null)
                    {
                        pawnHasDysosmicTrait = true;
                        break;
                    }
                }
            }
            bool pawnHasDysosmicGene = false;
            bool pawnHasAnosmicGene = false;
            HashSet<string> pawnTraitNames = null;
            HashSet<string> pawnGeneNames = null;
            if (pawn.genes != null)
            {
                foreach (Gene g in pawn.genes.GenesListForReading)
                {
                    string name = g.def.defName;
                    if (!pawnHasDysosmicGene && (name.StartsWith("Dysosmic") || name.Contains("Dysosmic")))
                        pawnHasDysosmicGene = true;
                    if (!pawnHasAnosmicGene && (name.StartsWith("Anosmic") || name.Contains("Anosmic")))
                        pawnHasAnosmicGene = true;
                }
            }
            if (storyTraits != null)
            {
                pawnTraitNames = new HashSet<string>();
                foreach (Trait t in storyTraits)
                    pawnTraitNames.Add(t.def.defName);
            }
            if (pawn.genes != null)
            {
                pawnGeneNames = new HashSet<string>();
                foreach (Gene g in pawn.genes.GenesListForReading)
                    pawnGeneNames.Add(g.def.defName);
            }
            Room pawnRoom = pawn.GetRoom();
            bool pawnOutdoors = pawnRoom == null || pawnRoom.PsychologicallyOutdoors;
            var scentsToApply = new List<ThoughtDef>();
            var scentDysosmicStatus = new Dictionary<ThoughtDef, bool>();
            var scentAnosmicStatus = new Dictionary<ThoughtDef, bool>();
            int radius = RimScentReworkedMod.Settings?.scentRadius ?? 8;
            bool homeOnly = RimScentReworkedMod.Settings?.homeOnly ?? false;
            Area homeArea = homeOnly ? pawn.Map?.areaManager?.Home : null;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, radius, true))
            {
                if (!cell.InBounds(pawn.Map)) continue;
                if (homeOnly && homeArea != null && !homeArea[cell]) continue;
                if (!GenSight.LineOfSight(pawn.Position, cell, pawn.Map, true)) continue;
                Room cellRoom = cell.GetRoom(pawn.Map);
                bool cellOutdoors = cellRoom == null || cellRoom.PsychologicallyOutdoors;
                if (pawnOutdoors)
                    if (!cellOutdoors) continue;
                else if (cellRoom != pawnRoom)
                    continue;
                List<Thing> things = pawn.Map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0, count = things.Count; i < count; i++)
                {
                    Thing thing = things[i];
                    if (thing is Pawn otherPawn && otherPawn != pawn)
                    {
                        HediffSet hediffs = otherPawn.health?.hediffSet;
                        if (hediffs == null) continue;
                        List<Hediff> allHediffs = hediffs.hediffs;
                        for (int h = 0, hCount = allHediffs.Count; h < hCount; h++)
                        {
                            Hediff hediff = allHediffs[h];
                            if (!ScentHediffDefs.Contains(hediff.def)) continue;
                            ModExtension_Scent ext = hediff.def.GetModExtension<ModExtension_Scent>();
                            if (ext?.thought == null) continue;
                            scentsToApply.Add(ext.thought);
                            bool isDysosmic = pawnHasDysosmicTrait || pawnHasDysosmicGene || ScentIsDysosmic(pawnTraitNames, pawnGeneNames, ext);
                            bool isAnosmic = pawnHasAnosmicGene || ScentIsAnosmic(pawnTraitNames, pawnGeneNames, ext);
                            scentDysosmicStatus[ext.thought] = scentDysosmicStatus.TryGetValue(ext.thought, out bool d) ? (d || isDysosmic) : isDysosmic;
                            scentAnosmicStatus[ext.thought] = scentAnosmicStatus.TryGetValue(ext.thought, out bool a) ? (a || isAnosmic) : isAnosmic;
                        }
                        continue;
                    }
                    if (!ScentThingDefs.Contains(thing.def)) continue;
                    CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
                    if (refuelable != null && !refuelable.HasFuel) continue;
                    CompPowerTrader power = thing.TryGetComp<CompPowerTrader>();
                    if (power != null && !power.PowerOn) continue;
                    ModExtension_Scent thingExt = thing.def.GetModExtension<ModExtension_Scent>();
                    if (thingExt?.thought == null) continue;
                    scentsToApply.Add(thingExt.thought);
                    bool isDys = pawnHasDysosmicTrait || pawnHasDysosmicGene || ScentIsDysosmic(pawnTraitNames, pawnGeneNames, thingExt);
                    scentDysosmicStatus[thingExt.thought] = scentDysosmicStatus.TryGetValue(thingExt.thought, out bool x) ? (x || isDys) : isDys;
                    bool isAnos = pawnHasAnosmicGene || ScentIsAnosmic(pawnTraitNames, pawnGeneNames, thingExt);
                    scentAnosmicStatus[thingExt.thought] = scentAnosmicStatus.TryGetValue(thingExt.thought, out bool y) ? (y || isAnos) : isAnos;
                }
            }

            foreach (GameCondition condition in pawn.Map.gameConditionManager.ActiveConditions)
            {
                var ext = condition.def.GetModExtension<ModExtension_Scent>();
                if (ext?.thought != null)
                    scentsToApply.Add(ext.thought);
            }
            WeatherDef weather = pawn.Map.weatherManager.curWeather;
            if (weather != null)
            {
                var ext = weather.GetModExtension<ModExtension_Scent>();
                if (ext?.thought != null)
                    scentsToApply.Add(ext.thought);
            }
            if (scentsToApply.Count == 0) return;
            bool uncapped = RimScentReworkedMod.Settings?.uncappedScents ?? false;
            if (!uncapped)
            {
                var counts = new Dictionary<ThoughtDef, int>();
                foreach (ThoughtDef t in scentsToApply)
                {
                    counts.TryGetValue(t, out int c);
                    counts[t] = c + 1;
                }
                ThoughtDef winner = null;
                float winnerMag = 0f;
                foreach (var kv in counts)
                {
                    float mag = ThoughtMagnitude(pawn, kv.Key, kv.Value);
                    if (mag > winnerMag)
                    {
                        winnerMag = mag;
                        winner = kv.Key;
                    }
                }
                if (winner == null) return;
                int winnerCount = counts[winner];
                if (activeThought != null)
                {
                    int existingCount = CountThought(pawn, activeThought);
                    float existingMag = ThoughtMagnitude(pawn, activeThought, existingCount);
                    if (winnerMag <= existingMag) return;
                }
                ClearThought(pawn);
                activeThought = winner;
                bool dys = scentDysosmicStatus.TryGetValue(winner, out bool dVal) ? dVal : false;
                bool anos = scentAnosmicStatus.TryGetValue(winner, out bool aVal) ? aVal : false;
                AddMemory(pawn, winner, winnerCount, smellFactor, dys, anos);
                RemoveExcessMemory(pawn, winner, winnerCount);
            }
            else
            {
                ClearThought(pawn);
                activeThought = null;
                var sourceCounts = new Dictionary<ThoughtDef, int>();
                foreach (ThoughtDef def in scentsToApply)
                {
                    sourceCounts.TryGetValue(def, out int c);
                    sourceCounts[def] = c + 1;
                }
                foreach (var pair in sourceCounts)
                {
                    bool dys = scentDysosmicStatus.TryGetValue(pair.Key, out bool d) ? d : false;
                    bool anos = scentAnosmicStatus.TryGetValue(pair.Key, out bool a) ? a : false;
                    AddMemory(pawn, pair.Key, pair.Value, smellFactor, dys, anos);
                    RemoveExcessMemory(pawn, pair.Key, pair.Value);
                }
            }
        }

        private static bool ScentIsDysosmic(HashSet<string> traitNames, HashSet<string> geneNames, ModExtension_Scent ext)
        {
            if (ext == null) return false;
            if (ext.dysosmicTraits != null && traitNames != null)
                for (int i = 0; i < ext.dysosmicTraits.Count; i++)
                    if (traitNames.Contains(ext.dysosmicTraits[i]))
                        return true;
            if (ext.dysosmicTraitDegrees != null)
                return true;
            if (ext.dysosmicGenes != null && geneNames != null)
                for (int i = 0; i < ext.dysosmicGenes.Count; i++)
                    if (geneNames.Contains(ext.dysosmicGenes[i]))
                        return true;
            return false;
        }

        private static bool ScentIsAnosmic(HashSet<string> traitNames, HashSet<string> geneNames,
            ModExtension_Scent ext)
        {
            if (ext == null) return false;
            if (ext.anosmicTraits != null && traitNames != null)
                for (int i = 0; i < ext.anosmicTraits.Count; i++)
                    if (traitNames.Contains(ext.anosmicTraits[i]))
                        return true;
            if (ext.anosmicTraitDegrees != null)
                return true;
            if (ext.anosmicGenes != null && geneNames != null)
                for (int i = 0; i < ext.anosmicGenes.Count; i++)
                    if (geneNames.Contains(ext.anosmicGenes[i]))
                        return true;
            return false;
        }

        private void ClearThought(Pawn pawn)
        {
            if (activeThought == null) return;
            pawn.needs.mood.thoughts.memories.RemoveMemoriesOfDef(activeThought);
            activeThought = null;
        }

        private void AddMemory(Pawn pawn, ThoughtDef def, int desired, float smellFactor, bool dysosmic, bool anosmic)
        {
            int existing = CountThought(pawn, def);
            bool allowStacking = RimScentReworkedMod.Settings?.allowMoodStacking ?? true;
            int stackLimit = allowStacking ? (def.stackLimit > 0 ? def.stackLimit : 1) : 1;
            int target = Mathf.Min(desired, stackLimit);
            int toAdd = target - existing;
            if (toAdd <= 0) return;
            for (int i = 0; i < toAdd; i++)
            {
                Thought_Memory mem = (Thought_Memory)ThoughtMaker.MakeThought(def);
                float baseMood = def.stages[0].baseMoodEffect;
                float offset = baseMood * (smellFactor - 1f);
                if (dysosmic) offset -= baseMood * 2f;
                if (anosmic) offset += baseMood * 2f;
                mem.moodOffset = Mathf.RoundToInt(offset);
                pawn.needs.mood.thoughts.memories.TryGainMemory(mem);
            }
        }

        private void RemoveExcessMemory(Pawn pawn, ThoughtDef def, int desired)
        {
            bool allowStacking = RimScentReworkedMod.Settings?.allowMoodStacking ?? true;
            int stackLimit = allowStacking ? (def.stackLimit > 0 ? def.stackLimit : 1) : 1;
            int target = Mathf.Min(desired, stackLimit);
            List<Thought_Memory> memories = pawn.needs.mood.thoughts.memories.Memories.Where(m => m.def == def).ToList();
            int excess = memories.Count - target;
            for (int i = 0; i < excess; i++)
                pawn.needs.mood.thoughts.memories.RemoveMemory(memories[i]);
        }

        private int CountThought(Pawn pawn, ThoughtDef def)
        {
            return pawn.needs.mood.thoughts.memories.Memories.Count(m => m.def == def);
        }

        private bool PawnAllowedToSmell(Pawn pawn)
        {
            var settings = RimScentReworkedMod.Settings;
            if (settings == null) return true;
            if (pawn.IsColonist) return settings.colonistsCanSmell;
            if (pawn.IsPrisoner) return settings.prisonersCanSmell;
            if (pawn.IsSlave) return settings.slavesCanSmell;
            if (pawn.Faction != null)
                return pawn.Faction.HostileTo(Faction.OfPlayer) ? settings.enemyFactionsCanSmell : settings.friendlyFactionsCanSmell;
            return true;
        }

        private float ThoughtMagnitude(Pawn pawn, ThoughtDef def, int sourceCount)
        {
            bool allowStacking = RimScentReworkedMod.Settings?.allowMoodStacking ?? true;
            int stackLimit = allowStacking ? (def.stackLimit > 0 ? def.stackLimit : 1) : 1;
            int effective = Mathf.Min(sourceCount, stackLimit);
            return Mathf.Abs(def.stages[0].baseMoodEffect) * effective;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref scentTickOffset, "scentTickOffset", 0);
            Scribe_Defs.Look(ref activeThought, "activeThought");
        }
    }
}