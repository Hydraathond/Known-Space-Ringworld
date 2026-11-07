using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;

namespace Ringworld;

// Smaller orbit icons: on a ring world, a world object on a sun-centred orbit layer (RingOrbit: Odyssey's orbit, High
// Orbit) has its expanding icon drawn at SizeFactor of vanilla's size, so the orbit view isn't crowded. Only the icon's
// size changes, about its centre; vanilla's click and hover area shrinks with it, as both read the same rect. Surface
// and atmosphere-layer objects (their ghost icons included) and planet worlds keep vanilla's size.
public static class OrbitIcons
{
	// Of vanilla's icon size.
	public const float SizeFactor = 0.8f;

	// The factor the object's icon is drawn at: SizeFactor on a ring world's sun-centred orbit layer, 1 otherwise.
	public static float FactorFor(WorldObject worldObject)
	{
		if (!SurfaceRing.InRingWorld || worldObject == null || !worldObject.Tile.Valid)
		{
			return 1f;
		}
		return RingOrbit.IsSunCentred(worldObject.Tile.Layer) ? SizeFactor : 1f;
	}
}

// Every expanding icon's on-screen rect goes through ExpandedIconScreenRect: vanilla's drawing, its hover and click
// picking, its drag-box selection and the ghost icons. Shrinking it about its centre resizes the orbit icons everywhere.
[HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandedIconScreenRect))]
public static class Patch_ExpandableWorldObjectsUtility_OrbitIconSize
{
	public static void Postfix(WorldObject o, ref Rect __result)
	{
		float factor = OrbitIcons.FactorFor(o);
		if (factor == 1f)
		{
			return;
		}
		Vector2 center = __result.center;
		Vector2 size = __result.size * factor;
		__result = new Rect(center - size / 2f, size);
	}
}
