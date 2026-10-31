using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

public static class RingLocalTime
{
	// The current absolute tick in a game. Before play it is vanilla's configured start tick for the selected starting
	// tile and season (6 am local time there), as on a globe, so the night shadows show the selected site at its start
	// time. 0 while a save loads and before a new game's start is configured.
	public static int NowTicksAbs()
	{
		if (Current.ProgramState == ProgramState.Playing)
		{
			return Find.TickManager.TicksAbs;
		}
		return HasStartTick ? GenTicks.ConfiguredTicksAbsAtGameStart : 0;
	}

	// Whether vanilla's start tick is defined: not while a save loads or before the game's init data exists.
	public static bool HasStartTick => Find.GameInitData is { } initData && initData.gameToLoad.NullOrEmpty() && Find.World != null;
}

// Local time for the night zones. Vanilla takes a tile's local time (and date) from its longitude, so on a ring-shaped layer
// the longitude helper reports RingGeometry.LocalTimeLongitudeOf instead of the plain angle around the ring. Day length
// and the share of night on each map stay vanilla (ADR 0001): only which tiles share a time of day changes. Vanilla
// reads the longitude for nothing but local time, apart from the coordinates shown in the tile inspector, which follow
// it like the latitude.
//
// Every ring-shaped layer: an atmosphere layer has the surface ring's shadow-square count and its night zones at the
// same angles round the axis, so a map there has the time of day the night shadow over it shows. (The climate
// gradient is the surface ring's only; see Patches_ClimateGradient.cs.)
//
// Both overloads are patched, as for the climate gradient; the value is set from the tile, so running both is harmless.
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.LongLatOf), typeof(int))]
public static class Patch_PlanetLayer_LongLatOf_NightZones
{
	public static void Postfix(PlanetLayer __instance, int __0, ref Vector2 __result)
	{
		if (__instance is RingLayer { Geometry: not null } ring)
		{
			__result.x = ring.Geometry.LocalTimeLongitudeOf(__0);
		}
	}
}

[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.LongLatOf), typeof(PlanetTile))]
public static class Patch_PlanetLayer_LongLatOf_PlanetTile_NightZones
{
	public static void Postfix(PlanetLayer __instance, PlanetTile __0, ref Vector2 __result)
	{
		Patch_PlanetLayer_LongLatOf_NightZones.Postfix(__instance, __0.tileId, ref __result);
	}
}
