using Verse;

namespace RimScentReworked
{
    public class RimScentReworkedSettings : ModSettings
    {
        public int scentTickInterval = 500;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref scentTickInterval, "scentTickInterval", 400);
            base.ExposeData();
        }
    }
}
