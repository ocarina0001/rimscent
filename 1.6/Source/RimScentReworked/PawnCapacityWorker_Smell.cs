using RimWorld;
using Verse;
using System.Collections.Generic;

namespace RimScentReworked
{
    public class PawnCapacityWorker_Smell : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet, List<PawnCapacityUtility.CapacityImpactor> impactors = null)
        {
            if (diffSet == null || diffSet.pawn == null) return 0f;
            BodyPartRecord nose = null;
            foreach (var part in diffSet.GetNotMissingParts())
            {
                if (part.def.defName.ToLower().Contains("nose") || part.def.defName.ToLower().Contains("beak"))
                {
                    nose = part;
                    break;
                }
            }
            if (nose == null) return 0f;
            return PawnCapacityUtility.CalculatePartEfficiency(diffSet, nose, false);
        }
    }
}
