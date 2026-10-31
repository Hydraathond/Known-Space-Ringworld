using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;

namespace Ringworld;

// The climate gradient. On a ring every tile is within a degree or two of vanilla's equator, so on the surface ring the
// latitude helpers report the climate gradient's vanilla-equivalent latitude (RingGeometry.VanillaLatitudeOf) instead. Everything vanilla derives
// from latitude follows it: base temperature in the terrain world-gen step (which still adds elevation, noise and the
// global temperature setting), the size of the seasonal swing, the hemisphere, and winter movement difficulty.
// The longitude is set for the night zones (Patches_NightZones.cs).
// Only the surface ring: the climate gradient is the ground's. Another ring-shaped layer (an atmosphere layer) keeps
// vanilla's values until it's given a rule of its own.
//
// The PlanetTile overloads only forward to the int ones, which are small enough for the JIT to inline, so both are patched.
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.LongLatOf), typeof(int))]
public static class Patch_PlanetLayer_LongLatOf
{
	public static void Postfix(PlanetLayer __instance, int __0, ref Vector2 __result)
	{
		if (__instance is RingLayer { IsSurfaceRing: true, Geometry: not null } ring)
		{
			__result.y = ring.Geometry.VanillaLatitudeOf(__0);
		}
	}
}

[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.LongLatOf), typeof(PlanetTile))]
public static class Patch_PlanetLayer_LongLatOf_PlanetTile
{
	public static void Postfix(PlanetLayer __instance, PlanetTile __0, ref Vector2 __result)
	{
		Patch_PlanetLayer_LongLatOf.Postfix(__instance, __0.tileId, ref __result);
	}
}

// Vanilla's value is |y| / radius, the sine of the latitude, so the vanilla-equivalent latitude goes in the same way.
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.DistanceFromEquatorNormalized), typeof(int))]
public static class Patch_PlanetLayer_DistanceFromEquatorNormalized
{
	public static void Postfix(PlanetLayer __instance, int __0, ref float __result)
	{
		if (__instance is RingLayer { IsSurfaceRing: true, Geometry: not null } ring)
		{
			__result = Mathf.Abs(Mathf.Sin(ring.Geometry.VanillaLatitudeOf(__0) * Mathf.Deg2Rad));
		}
	}
}

[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.DistanceFromEquatorNormalized), typeof(PlanetTile))]
public static class Patch_PlanetLayer_DistanceFromEquatorNormalized_PlanetTile
{
	public static void Postfix(PlanetLayer __instance, PlanetTile __0, ref float __result)
	{
		Patch_PlanetLayer_DistanceFromEquatorNormalized.Postfix(__instance, __0.tileId, ref __result);
	}
}
