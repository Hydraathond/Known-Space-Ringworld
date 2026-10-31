using HarmonyLib;
using RimWorld.Planet;

namespace Ringworld;

// Saves and loads the ring size, land coverage and shadow-square count with the ring layer. It has to be a prefix:
// while loading, vanilla's ExposeData calls InitializeLayer partway through, and the ring rebuild there needs them already in place.
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.ExposeData))]
public static class Patch_PlanetLayer_ExposeData
{
	public static void Prefix(PlanetLayer __instance)
	{
		if (__instance is RingLayer ring)
		{
			ring.ExposeRingSettings();
		}
	}
}
