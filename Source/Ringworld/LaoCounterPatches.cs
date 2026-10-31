using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Counter-patches for where Layered Atmosphere and Orbit (LAO) assumes a sphere on ring worlds. LAO's own
// patch methods are patched, by name. All but the site finder's only change anything on a ring world, so planet worlds
// keep LAO's behaviour:
// - Gravship travel. LAO routes a gravship along a radial line through each layer it crosses, timed by the radial
//   distance: about 4x vanilla's on a Small ring and 40x on a Lore ratio ring, drawn off the inner surface in the
//   ring view, and switching the selected layer when the gravship crosses into another layer. On a ring world LAO's
//   TravelTo prefix steps aside and vanilla's travel runs: timed by the angle only, drawn on the destination layer.
// - Selecting a world object on another layer switches to that layer (LAO's AutoSwapLayerOnSelection). Skipped on a
//   ring world, so only the player changes the selected layer. LAO's other layer switches (on changing the current map
//   and on opening the world) fire for a layer of another LAO planet, which a ring world doesn't have, or once in a
//   game LAO has no current planet for yet (one that gained LAO). They're left running: until LAO has a current
//   planet it hides every layer, and that switch sets it.
// - LAO's quest site finder looks the surface up by a random def of its Surface group, vanilla's Surface or the ring's,
//   and throws when the world has no layer of that def. While it runs, either def finds the world's surface. This one
//   fixes planet worlds too: the ring's def is in the group because Ringworld put it there.
internal static class LaoCounterPatches
{
	private const string PatchesTypeName = "LayeredAtmosphereOrbit.HarmonyPatches";

	private static bool inSiteFinder;

	// Null if every counter-patch applied, otherwise what's missing. Those found are applied either way.
	public static string Apply(Harmony harmony)
	{
		Type patches = AccessTools.TypeByName(PatchesTypeName);
		if (patches == null)
		{
			return $"type {PatchesTypeName} not found";
		}
		string problem = null;
		foreach ((string name, string prefix, string finalizer) in new[]
		{
			("GU_TravelTo_Prefix", nameof(VanillaTravelOnRing), null),
			("WS_Select_Postfix", nameof(SkipOnRing), null),
			("TF_TryFindNewSiteTile_Prefix", nameof(EnterSiteFinder), nameof(LeaveSiteFinder)),
		})
		{
			MethodInfo target = AccessTools.Method(patches, name);
			if (target == null)
			{
				problem ??= $"{PatchesTypeName}.{name} not found";
				continue;
			}
			harmony.Patch(target, prefix: new HarmonyMethod(typeof(LaoCounterPatches), prefix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(LaoCounterPatches), finalizer));
		}
		harmony.Patch(AccessTools.Method(typeof(WorldGrid), nameof(WorldGrid.TryGetFirstLayerOfDef)), postfix: new HarmonyMethod(typeof(LaoCounterPatches), nameof(SurfaceForEitherSurfaceDef)));
		return problem;
	}

	// LAO's prefix returns true, so vanilla's TravelTo runs instead of LAO's route.
	private static bool VanillaTravelOnRing(ref bool __result)
	{
		if (!SurfaceRing.InRingWorld)
		{
			return true;
		}
		__result = true;
		return false;
	}

	private static bool SkipOnRing()
	{
		return !SurfaceRing.InRingWorld;
	}

	private static void EnterSiteFinder()
	{
		inSiteFinder = true;
	}

	private static Exception LeaveSiteFinder(Exception __exception)
	{
		inSiteFinder = false;
		return __exception;
	}

	private static void SurfaceForEitherSurfaceDef(WorldGrid __instance, PlanetLayerDef def, ref PlanetLayer layer, ref bool __result)
	{
		if (inSiteFinder && !__result && SurfaceIdentity.IsSurface(def) && __instance.Surface != null)
		{
			layer = __instance.Surface;
			__result = true;
		}
	}
}
