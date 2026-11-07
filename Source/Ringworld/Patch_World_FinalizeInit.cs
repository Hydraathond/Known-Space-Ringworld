using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Switches the world materials to the ring's shaders or back to vanilla's, then, in dev mode, runs the self-check, once
// the world is complete. Vanilla calls World.FinalizeInit at the end of world generation (after every layer's gen steps)
// and right after a save's world is loaded.
//
// The self-check waits for the long event to finish (LongEventHandler.ExecuteWhenFinished): vanilla loads a save on a
// background thread, so this postfix runs off the main thread, and the checks build draw layers whose materials copy
// vanilla's world materials. Those resolve on first use and can only be made on the main thread: made here, they fail,
// and the thickness and cloud band stayed empty for the session (v0.1.3 ticket 03). Players skip the self-check; the
// "Ringworld: self-check" debug action and the dev harness still run it.
[HarmonyPatch(typeof(World), nameof(World.FinalizeInit))]
public static class Patch_World_FinalizeInit
{
	public static void Postfix(World __instance, bool fromLoad)
	{
		RingMaterials.Sync(__instance);
		if (!Prefs.DevMode)
		{
			return;
		}
		string reason = fromLoad ? "after loading a save" : "after world generation";
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			// A load that failed, or a world generated for a preview, may leave another world current by then.
			if (Find.World == __instance)
			{
				RingSelfCheck.Run(__instance, reason);
			}
		});
	}
}
