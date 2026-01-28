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
            PawnCapacityDef smellCap = DefDatabase<PawnCapacityDef>.GetNamedSilentFail("RimScent_Smell");
            StatDef smellStat = DefDatabase<StatDef>.GetNamedSilentFail("RimScent_SmellSensitivity");
            float capacityFactor = smellCap != null ? pawn.health.capacities.GetLevel(smellCap) : 1f;
            float statFactor = smellStat != null ? pawn.GetStatValue(smellStat) : 1f;
            float smellFactor = capacityFactor * statFactor;
            if (smellFactor <= 0f)
            {
                ClearThought(pawn);
                return;
            }
            bool dysomic = false;
            foreach (Trait t in pawn.story?.traits?.allTraits ?? new List<Trait>())
            {
                if (t.def.GetModExtension<ModExtension_Dysomic>() != null)
                {
                    dysomic = true;
                    break;
                }
            }
            Room pawnRoom = pawn.GetRoom();
            bool pawnOutdoors = pawnRoom == null || pawnRoom.PsychologicallyOutdoors;
            List<ThoughtDef> scentsToApply = new List<ThoughtDef>();
            int radius = RimScentReworkedMod.Settings?.scentRadius?? 8;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, radius, true))
            {
                if (!cell.InBounds(pawn.Map)) continue;
                if (!GenSight.LineOfSight(pawn.Position, cell, pawn.Map, true)) continue;
                Room cellRoom = cell.GetRoom(pawn.Map);
                bool cellOutdoors = cellRoom == null || cellRoom.PsychologicallyOutdoors;
                if (pawnOutdoors)
                    if (!cellOutdoors) continue;
                    else
                    if (cellRoom != pawnRoom) continue;
                List<Thing> things = pawn.Map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing is Pawn otherPawn && otherPawn != pawn)
                    {
                        HediffSet hediffs = otherPawn.health?.hediffSet;
                        if (hediffs != null)
                        {
                            List<Hediff> allHediffs = hediffs.hediffs;
                            for (int h = 0; h < allHediffs.Count; h++)
                            {
                                Hediff hediff = allHediffs[h];
                                ModExtension_Scent ext = hediff.def.GetModExtension<ModExtension_Scent>();
                                if (ext?.thought == null) continue;
                                scentsToApply.Add(ext.thought);
                            }
                        }
                        continue;
                    }
                    CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
                    if (refuelable != null && !refuelable.HasFuel) continue;
                    CompPowerTrader power = thing.TryGetComp<CompPowerTrader>();
                    if (power != null && !power.PowerOn) continue;
                    ModExtension_Scent thingExt = thing.def.GetModExtension<ModExtension_Scent>();
                    if (thingExt?.thought == null) continue;
                    scentsToApply.Add(thingExt.thought);
                }
            }
            foreach (GameCondition condition in pawn.Map.gameConditionManager.ActiveConditions)
            {
                ModExtension_Scent ext = condition.def.GetModExtension<ModExtension_Scent>();
                if (ext?.thought == null) continue;
                scentsToApply.Add(ext.thought);
            }
            WeatherDef weather = pawn.Map.weatherManager.curWeather;
            if (weather != null)
            {
                ModExtension_Scent ext = weather.GetModExtension<ModExtension_Scent>();
                if (ext?.thought != null)
                    scentsToApply.Add(ext.thought);
            }
            if (scentsToApply.Count == 0)
                return;
            bool uncapped = RimScentReworkedMod.Settings?.uncappedScents ?? false;
            if (!uncapped)
            {
                ThoughtDef winner = scentsToApply.GroupBy(t => t).OrderByDescending(g => ThoughtMagnitude(pawn, g.Key, g.Count())).Select(g => g.Key).FirstOrDefault();
                if (winner == null) return;
                int winnerCount = scentsToApply.Count(t => t == winner);
                float winnerMagnitude = ThoughtMagnitude(pawn, winner, winnerCount);
                if (activeThought != null)
                {
                    int existingCount = CountThought(pawn, activeThought);
                    float existingMagnitude = ThoughtMagnitude(pawn, activeThought, existingCount);
                    if (winnerMagnitude <= existingMagnitude)
                        return;
                }
                ClearThought(pawn);
                activeThought = winner;
                AddMemory(pawn, winner, winnerCount, smellFactor, dysomic);
                RemoveExcessMemory(pawn, winner, winnerCount);
            }
            else
            {
                ClearThought(pawn);
                activeThought = null;
                Dictionary<ThoughtDef, int> sourceCounts = new Dictionary<ThoughtDef, int>();
                foreach (ThoughtDef def in scentsToApply)
                    sourceCounts[def] = sourceCounts.TryGetValue(def, out int c) ? c + 1 : 1;
                foreach (var pair in sourceCounts)
                {
                    AddMemory(pawn, pair.Key, pair.Value, smellFactor, dysomic);
                    RemoveExcessMemory(pawn, pair.Key, pair.Value);
                }
            }
        }

        private void ClearThought(Pawn pawn)
        {
            if (activeThought == null) return;
            pawn.needs.mood.thoughts.memories.RemoveMemoriesOfDef(activeThought);
            activeThought = null;
        }

        private void AddMemory(Pawn pawn, ThoughtDef def, int desired, float smellFactor, bool dysomic)
        {
            int existing = CountThought(pawn, def);
            int stackLimit = def.stackLimit > 0 ? def.stackLimit : 1;
            int target = Mathf.Min(desired, stackLimit);
            int toAdd = target - existing;
            if (toAdd <= 0) return;
            for (int i = 0; i < toAdd; i++)
            {
                Thought_Memory mem = (Thought_Memory)ThoughtMaker.MakeThought(def);
                float baseMood = def.stages[0].baseMoodEffect;
                float offset = baseMood * (smellFactor - 1f);
                if (dysomic)
                    offset -= baseMood * 2f;
                mem.moodOffset = Mathf.RoundToInt(offset);
                pawn.needs.mood.thoughts.memories.TryGainMemory(mem);
            }
        }

        private void RemoveExcessMemory(Pawn pawn, ThoughtDef def, int desired)
        {
            int stackLimit = def.stackLimit > 0 ? def.stackLimit : 1;
            int target = Mathf.Min(desired, stackLimit);
            List<Thought_Memory> memories = pawn.needs.mood.thoughts.memories.Memories.Where(m => m.def == def).ToList();
            int excess = memories.Count - target;
            if (excess <= 0) return;
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
            if (pawn.IsColonist)
                return settings.colonistsCanSmell;
            if (pawn.IsPrisoner)
                return settings.prisonersCanSmell;
            if (pawn.IsSlave)
                return settings.slavesCanSmell;
            if (pawn.Faction != null)
            {
                if (pawn.Faction.HostileTo(Faction.OfPlayer))
                    return settings.enemyFactionsCanSmell;
                else
                    return settings.friendlyFactionsCanSmell;
            }
            return true;
        }

        private float ThoughtMagnitude(Pawn pawn, ThoughtDef def, int sourceCount)
        {
            int stackLimit = def.stackLimit > 0 ? def.stackLimit : 1;
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
