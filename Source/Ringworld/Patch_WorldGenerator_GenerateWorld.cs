using HarmonyLib;
using RimWorld.Planet;

namespace Ringworld;

// Planet coverage means nothing on a ring, and the page hides it, but vanilla terrain still reads it: below 100% it
// sinks everything farther than coverage x 180 degrees from the sphere's view centre into ocean. A ring world is
// always generated at 100%.
[HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld))]
public static class Patch_WorldGenerator_GenerateWorld
{
	public static void Prefix(ref float planetCoverage)
	{
		if (WorldShape.Ring)
		{
			planetCoverage = 1f;
		}
	}
}
