using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Other mods add world-gen steps and draw layers to vanilla's Surface def, from code (Geological Landforms and Biome
// Transitions) or by XML patch on its defName, and the ring's def never gets them. Just before a ring layer is built,
// on a new world or a loaded one, whatever Surface has and the ring's def lacks is copied over and logged, apart from
// vanilla's sphere-only draw layers. Done then rather than at startup because some mods register late (Geological
// Landforms from its Lunar init). Only the ring's def changes, so planet worlds keep vanilla's Surface as it is.
public static class SurfaceAdditions
{
	// Surface's draw layers that build their own sphere. The ring draws its own stand-in and clouds instead, and no glow
	// or debug noise.
	private static readonly Type[] SphereOnlyDrawLayers =
	{
		typeof(WorldDrawLayer_Glow),
		typeof(WorldDrawLayer_Clouds),
		typeof(WorldDrawLayer_UngeneratedPlanetParts),
		typeof(WorldDrawLayer_DebugNoise),
	};

	private static readonly AccessTools.FieldRef<PlanetLayerDef, List<WorldGenStepDef>> WorldGenSteps = AccessTools.FieldRefAccess<PlanetLayerDef, List<WorldGenStepDef>>("worldGenSteps");

	private static readonly AccessTools.FieldRef<PlanetLayerDef, List<WorldGenStepDef>> CachedGenSteps = AccessTools.FieldRefAccess<PlanetLayerDef, List<WorldGenStepDef>>("cachedGenSteps");

	public static void CopyOnto(PlanetLayerDef ringDef)
	{
		PlanetLayerDef surface = PlanetLayerDefOf.Surface;
		List<WorldGenStepDef> ringSteps = WorldGenSteps(ringDef);
		List<WorldGenStepDef> steps = WorldGenSteps(surface).Where(step => step != null && !ringSteps.Contains(step)).ToList();
		List<Type> drawLayers = surface.worldDrawLayers.Where(type => type != null && !SphereOnlyDrawLayers.Contains(type) && !ringDef.worldDrawLayers.Contains(type)).ToList();
		if (steps.Count == 0 && drawLayers.Count == 0)
		{
			return;
		}
		ringSteps.AddRange(steps);
		CachedGenSteps(ringDef) = null;
		ringDef.worldDrawLayers.AddRange(drawLayers);
		Log.Message($"[Ringworld] Copied other mods' additions to the Surface def onto {ringDef.defName}: world-gen steps [{string.Join(", ", steps.Select(step => step.defName))}], draw layers [{string.Join(", ", drawLayers.Select(type => type.FullName))}].");
	}
}
