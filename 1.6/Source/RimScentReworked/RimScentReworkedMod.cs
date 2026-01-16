using HarmonyLib;
using System.Reflection;
using Verse;

namespace RimScentReworked
{
    [StaticConstructorOnStartup]
    public class RimScentReworkedMod : Mod
    {
        public RimScentReworkedMod(ModContentPack content) : base(content)
        {
            var h = new Harmony("reo.RimScent");
            h.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("RIMSCENT: startup succesful!");
        }
    }
}
