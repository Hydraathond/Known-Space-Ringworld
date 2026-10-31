using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// The world pollution overlay on the ring.

// Pollution picks its submeshes by material in its own method rather than WorldDrawLayerBase.GetSubMesh, so its
// materials are swapped for the ring's here (see RingMaterials).
[HarmonyPatch(typeof(WorldDrawLayer_Pollution), "GetMaterialForTilePollution")]
public static class Patch_WorldDrawLayer_Pollution_GetMaterialForTilePollution
{
	public static void Postfix(WorldDrawLayer_Pollution __instance, ref Material __result)
	{
		if (__instance.planetLayer is RingLayer ring)
		{
			__result = RingMaterials.For(__result, ring);
		}
	}
}

// When pollution changes in play, vanilla rebuilds one region's submeshes and uploads them itself, bypassing
// WorldDrawLayerBase.FinalizeMesh and so the ring mirroring. Mirror them and upload again.
[HarmonyPatch(typeof(WorldDrawLayer_Pollution), "RegenerateRegion")]
public static class Patch_WorldDrawLayer_Pollution_RegenerateRegion
{
	public static void Postfix(WorldDrawLayer_Pollution __instance, int regionId)
	{
		if (__instance.planetLayer is not RingLayer ring)
		{
			return;
		}
		foreach (LayerSubMesh subMesh in __instance.GetSubMeshesForRegion(regionId))
		{
			if (subMesh.verts.Count == 0)
			{
				continue;
			}
			Patch_WorldDrawLayerBase_FinalizeMesh.Mirror(subMesh, ring.Radius);
			subMesh.FinalizeMesh(MeshParts.All);
		}
	}
}
