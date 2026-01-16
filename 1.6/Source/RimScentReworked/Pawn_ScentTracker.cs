using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimScentReworked
{
    public class Pawn_ScentTracker : ThingComp
    {
        private const int TickInterval = 400;
        private const float ScentRadius = 8f;
        private ThoughtDef activeThought;
        private Pawn Pawn => parent as Pawn;

        public override void CompTick()
        {
            if (Find.TickManager.TicksGame % TickInterval != 0) return;
            Pawn pawn = Pawn;
            if (pawn == null || !pawn.Spawned || pawn.needs?.mood == null) return;
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
            Dictionary<ThoughtDef, float> totals = new Dictionary<ThoughtDef, float>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, ScentRadius, true))
            {
                if (!cell.InBounds(pawn.Map)) continue;
                Room cellRoom = cell.GetRoom(pawn.Map);
                bool cellOutdoors = cellRoom == null || cellRoom.PsychologicallyOutdoors;
                if (pawnOutdoors)
                {
                    if (!cellOutdoors) continue;
                }
                else
                {
                    if (cellRoom != pawnRoom) continue;
                }
                List<Thing> things = pawn.Map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
                    if (refuelable != null && !refuelable.HasFuel) continue;
                    CompPowerTrader power = thing.TryGetComp<CompPowerTrader>();
                    if (power != null && !power.PowerOn) continue;
                    ModExtension_Scent ext = thing.def.GetModExtension<ModExtension_Scent>();
                    if (ext?.thought == null) continue;
                    float mood = ext.thought.stages[0].baseMoodEffect;
                    totals[ext.thought] = totals.TryGetValue(ext.thought, out float v) ? v + mood : mood;
                }
            }
            foreach (GameCondition condition in pawn.Map.gameConditionManager.ActiveConditions)
            {
                ModExtension_Scent ext = condition.def.GetModExtension<ModExtension_Scent>();
                if (ext?.thought == null) continue;
                float mood = ext.thought.stages[0].baseMoodEffect;
                totals[ext.thought] = totals.TryGetValue(ext.thought, out float v) ? v + mood : mood;
            }
            WeatherDef weather = pawn.Map.weatherManager.curWeather;
            if (weather != null)
            {
                ModExtension_Scent ext = weather.GetModExtension<ModExtension_Scent>();
                if (ext?.thought != null)
                {
                    float mood = ext.thought.stages[0].baseMoodEffect;
                    totals[ext.thought] = totals.TryGetValue(ext.thought, out float v) ? v + mood : mood;
                }
            }
            if (totals.Count == 0)
            {
                ClearThought(pawn);
                return;
            }
            ThoughtDef winner = null;
            float winnerValue = 0f;
            foreach (var pair in totals)
            {
                if (Math.Abs(pair.Value) > Math.Abs(winnerValue))
                {
                    winner = pair.Key;
                    winnerValue = pair.Value;
                }
            }
            if (winner == null || winner == activeThought) return;
            ClearThought(pawn);
            activeThought = winner;
            Thought_Memory mem = (Thought_Memory)ThoughtMaker.MakeThought(winner);
            float baseMood = winner.stages[0].baseMoodEffect;
            float offset = baseMood * (smellFactor - 1f);
            if (dysomic)
            {
                offset -= baseMood * 2f; // cancel + invert
            }
            mem.moodOffset = Mathf.RoundToInt(offset);
            pawn.needs.mood.thoughts.memories.TryGainMemory(mem);
        }

        private void ClearThought(Pawn pawn)
        {
            if (activeThought == null) return;
            pawn.needs.mood.thoughts.memories.RemoveMemoriesOfDef(activeThought);
            activeThought = null;
        }

        public override void PostExposeData()
        {
            Scribe_Defs.Look(ref activeThought, "activeThought");
        }
    }
}
