using HarmonyLib;
using System.Reflection;
using Verse;
using UnityEngine;

namespace RimScentReworked
{
    [StaticConstructorOnStartup]
    public class RimScentReworkedMod : Mod
    {
        public static RimScentReworkedSettings Settings;
        public RimScentReworkedMod(ModContentPack content) : base(content)
        {
            var h = new Harmony("reo.RimScent");
            h.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("RIMSCENT: startup succesful!");
            Settings = GetSettings<RimScentReworkedSettings>();
        }
        public override string SettingsCategory()
        {
            return "RimScent Reworked";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);
            list.Label($"Smell check interval (default: 500): {Settings.scentTickInterval} ticks");
            list.Label("How often pawns evaluate nearby scents.\nLower values are more responsive but cost more performance.");
            Settings.scentTickInterval = (int)list.Slider(Settings.scentTickInterval, 60, 1000);
            list.Label($"Smell radius (default: 8): {Settings.scentRadius}");
            list.Label("The radius in which a pawn can smell.\nLower values can increase performance but limit smelling range.");
            Settings.scentRadius = (int)list.Slider(Settings.scentRadius, 2, 32);
            list.CheckboxLabeled("Uncapped mode", ref Settings.uncappedScents, "If enabled, pawns can experience multiple scent mood effects simultaneously instead of only the strongest one.");
            list.CheckboxLabeled("Allow mood stacking", ref Settings.allowMoodStacking, "If enabled, scent thoughts can stack up to their stack limit. If disabled, only a single instance of each scent thought is allowed.");
            list.CheckboxLabeled("Home area only", ref Settings.homeOnly, "If enabled, pawns will only smell things located within the home area. Does not affect weather or events.");
            list.GapLine();
            list.Label("Pawn scent eligibility:");
            list.CheckboxLabeled("Colonists can smell", ref Settings.colonistsCanSmell);
            list.CheckboxLabeled("Prisoners can smell", ref Settings.prisonersCanSmell);
            list.CheckboxLabeled("Slaves can smell", ref Settings.slavesCanSmell);
            list.CheckboxLabeled("Friendly faction pawns can smell", ref Settings.friendlyFactionsCanSmell);
            list.CheckboxLabeled("Enemy faction pawns can smell", ref Settings.enemyFactionsCanSmell);
            list.GapLine();
            list.End();
        }
    }
}
