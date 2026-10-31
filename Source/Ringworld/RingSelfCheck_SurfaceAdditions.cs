using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// What other mods added to vanilla's Surface def reaches the ring (SurfaceAdditions). Whether a mod's features then
// work and look right on the ring is checked in game with that mod.
public static partial class RingSelfCheck
{
	// Vanilla Surface's draw layers that build their own sphere, from its def: the ring has its own stand-in and clouds.
	private static readonly Type[] SphereOnlySurfaceDrawLayers =
	{
		typeof(WorldDrawLayer_Glow),
		typeof(WorldDrawLayer_Clouds),
		typeof(WorldDrawLayer_UngeneratedPlanetParts),
		typeof(WorldDrawLayer_DebugNoise),
	};

	// Every world-gen step on Surface is on the ring's def, and every draw layer on it is on the ring's def and built on
	// the ring layer, except vanilla's sphere-only draw layers, which never are.
	private static void CheckSurfaceAdditions(RingLayer ring, RingCheckResult result)
	{
		PlanetLayerDef surface = PlanetLayerDefOf.Surface;
		foreach (WorldGenStepDef step in surface.GenStepsInOrder)
		{
			if (!ring.Def.GenStepsInOrder.Contains(step))
			{
				result.Fail($"Surface's world-gen step {step.defName} isn't on {ring.Def.defName}");
			}
		}
		foreach (Type type in surface.worldDrawLayers)
		{
			if (SphereOnlySurfaceDrawLayers.Contains(type))
			{
				continue;
			}
			if (!ring.Def.worldDrawLayers.Contains(type))
			{
				result.Fail($"Surface's draw layer {type.FullName} isn't on {ring.Def.defName}");
			}
			else if (!ring.WorldDrawLayers.Any(layer => layer.GetType() == type))
			{
				result.Fail($"draw layer {type.FullName} is on {ring.Def.defName} but wasn't built on the ring layer");
			}
		}
		foreach (Type type in ring.Def.worldDrawLayers)
		{
			if (SphereOnlySurfaceDrawLayers.Contains(type))
			{
				result.Fail($"{ring.Def.defName} has vanilla's sphere-only draw layer {type.Name}");
			}
		}
	}
}
