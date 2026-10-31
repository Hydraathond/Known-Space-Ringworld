using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Vanilla 1.7 reads its Surface def in 9 places. On a ring world, whose surface has the ring's own def:
// - Three comparisons go through SurfaceIdentity.IsSurface, so the ring gets what a planet's surface gets: an abandoned
//   settlement where a settlement is abandoned (Settlement.Abandon) or a site that leaves one is destroyed
//   (SitePartWorker.PostDestroy), and the launch marker where a gravship takes off from a colony (MapParent.Abandon).
// - Looking a layer up by the Surface def finds the ring (Patch_WorldGrid_SurfaceLookups). That covers the quest site
//   finder's fallback to the surface from orbit (TileFinder.TryFindNewSiteTile) and Odyssey's gravcore quests, whose
//   sites are on the layer of def Surface.
// - The rest are left alone: the scenario's default surface part (Scenario, ScenarioMaker), which
//   Patch_WorldGrid_CreateRequiredLayers swaps for the ring's; a forced-map scenario part (ScenPart_ForcedMap), which
//   only skips a lookup the surface doesn't need; a fallback for saves with no root surface (WorldGrid.ExposeData), as
//   a ring's is saved; and the default layer list of generated locations (GeneratedLocationDef), which no vanilla def
//   uses.
// Def lists that name Surface (layer whitelists of quests, arrival modes, factions and so on) get the ring's def beside
// it by XML patch (Patches/RingInSurfaceLayerLists.xml). Planet worlds have no ring layer, so none of this changes them.
[HarmonyPatch]
public static class Patch_VanillaSurfaceComparisons
{
	public static readonly MethodInfo[] Targets =
	{
		AccessTools.Method(typeof(Settlement), nameof(Settlement.Abandon)),
		AccessTools.Method(typeof(MapParent), nameof(MapParent.Abandon)),
		AccessTools.Method(typeof(SitePartWorker), nameof(SitePartWorker.PostDestroy)),
	};

	public static IEnumerable<MethodBase> TargetMethods()
	{
		return Targets;
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		List<CodeInstruction> code = SurfaceIdentity.RouteThroughIsSurface(instructions, out int swapped);
		if (swapped != 1)
		{
			Log.Warning($"[Ringworld] {original.DeclaringType?.Name}.{original.Name} has {swapped} Surface comparisons, not 1; it may treat the ring as not the surface.");
		}
		return code;
	}
}

// On a ring world, a layer looked up by vanilla's Surface def is the ring. TryGetFirstLayerOfDef goes through
// FirstLayerOfDef.
[HarmonyPatch]
public static class Patch_WorldGrid_SurfaceLookups
{
	public static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(WorldGrid), nameof(WorldGrid.FirstLayerOfDef));
		yield return AccessTools.Method(typeof(WorldGrid), nameof(WorldGrid.TryGetFirstAdjacentLayerOfDef));
	}

	public static void Prefix(WorldGrid __instance, ref PlanetLayerDef def)
	{
		if (def == PlanetLayerDefOf.Surface && SurfaceRing.Of(__instance) is { } ring)
		{
			def = ring.Def;
		}
	}
}
