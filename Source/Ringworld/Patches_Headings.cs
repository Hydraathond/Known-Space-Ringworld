using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;

namespace Ringworld;

// World headings on a ring, mirrored east-west to match the ring view (ADR 0004): starboard is North, anti-spinward
// East, port South and spinward West. Vanilla measures the heading in a frame around the outward direction, as a planet
// is seen from outside; the ring's inner surface is seen from the centre, so taken unchanged east lands on spinward,
// which is screen-left. Every other heading helper (GetRotFromTo, GetDirection8WayFromTo, the tile overloads and the
// WorldGrid wrappers) goes through this overload, so coasts, rivers, roads and caravan edges on the local map follow.
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.GetHeadingFromTo), typeof(Vector3), typeof(Vector3))]
public static class Patch_PlanetLayer_GetHeadingFromTo
{
	public static void Postfix(PlanetLayer __instance, ref float __result)
	{
		if (__instance is RingLayer)
		{
			__result = Mirror(__result);
		}
	}

	// A heading of 0 (the same tile or position) stays 0.
	public static float Mirror(float heading)
	{
		return (360f - heading) % 360f;
	}
}
