using HarmonyLib;
using RimWorld.Planet;

namespace Ringworld;

// Switches the world materials to the ring's shaders or back to vanilla's, then runs the self-check, once the world is
// complete. Vanilla calls World.FinalizeInit at the end of world generation (after every layer's gen steps) and right
// after a save's world is loaded.
[HarmonyPatch(typeof(World), nameof(World.FinalizeInit))]
public static class Patch_World_FinalizeInit
{
	public static void Postfix(World __instance, bool fromLoad)
	{
		RingMaterials.Sync(__instance);
		RingSelfCheck.Run(__instance, fromLoad ? "after loading a save" : "after world generation");
	}
}
