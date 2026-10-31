using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Which layer defs count as a world's surface for other mods' Surface checks (SurfaceIdentity), and whether those
// checks were rerouted. Whether the mods' features then work on the ring is checked in game with that mod.
public static partial class RingSelfCheck
{
	// True for vanilla's Surface and the ring's def, false for every other layer of the world.
	private static void CheckIsSurface(RingLayer ring, RingCheckResult result)
	{
		if (!SurfaceIdentity.IsSurface(PlanetLayerDefOf.Surface))
		{
			result.Fail("IsSurface(Surface) is false");
		}
		if (!SurfaceIdentity.IsSurface(ring.Def))
		{
			result.Fail($"IsSurface({ring.Def.defName}) is false");
		}
		if (SurfaceIdentity.IsSurface(null))
		{
			result.Fail("IsSurface(null) is true");
		}
		foreach (PlanetLayer layer in Find.WorldGrid.PlanetLayers.Values)
		{
			if (layer != ring && SurfaceIdentity.IsSurface(layer.Def))
			{
				result.Fail($"IsSurface({layer.Def.defName}) is true for layer {layer.LayerID}");
			}
		}
	}

	// Vanilla's Surface comparisons that go through IsSurface (Patch_VanillaSurfaceComparisons) call it and no longer
	// read Surface, and a layer looked up by Surface is the ring, from the grid and from each orbit layer connected to it.
	private static void CheckVanillaSurfaceChecks(RingLayer ring, RingCheckResult result)
	{
		MethodInfo isSurface = AccessTools.Method(typeof(SurfaceIdentity), nameof(SurfaceIdentity.IsSurface));
		FieldInfo surface = AccessTools.Field(typeof(PlanetLayerDefOf), nameof(PlanetLayerDefOf.Surface));
		foreach (MethodInfo target in Patch_VanillaSurfaceComparisons.Targets)
		{
			List<CodeInstruction> code = PatchProcessor.GetCurrentInstructions(target);
			string name = $"{target.DeclaringType?.Name}.{target.Name}";
			if (!code.Any(instruction => instruction.Calls(isSurface)))
			{
				result.Fail($"{name} doesn't call IsSurface");
			}
			if (code.Any(instruction => instruction.LoadsField(surface)))
			{
				result.Fail($"{name} still reads vanilla's Surface def");
			}
		}
		WorldGrid grid = Find.WorldGrid;
		if (grid.FirstLayerOfDef(PlanetLayerDefOf.Surface) != ring)
		{
			result.Fail("FirstLayerOfDef(Surface) isn't the ring");
		}
		foreach (PlanetLayer orbit in RingOrbit.Layers(grid))
		{
			// Vanilla's lookup finds a layer that connects directly to the one it's asked from.
			if (ring.DirectConnectionTo(orbit) && (!grid.TryGetFirstAdjacentLayerOfDef(new PlanetTile(0, orbit), PlanetLayerDefOf.Surface, out PlanetLayer found) || found != ring))
			{
				result.Fail($"from {RingOrbit.Describe(orbit)}, TryGetFirstAdjacentLayerOfDef(Surface) doesn't find the ring");
			}
		}
	}

	// VEF's two Surface checks, from its decompiled source: the object-spawn check (its map-wide overload) and the
	// moving-base spawner.
	private static readonly (string type, string method, string[] parameters)[] VefSurfaceChecks =
	{
		("VEF.Maps.VanillaExpandedFramework_MapGenerator_GenerateMap_Patch", "CanSpawnAt", new[] { "Verse.Map", "VEF.Maps.ObjectSpawnsDef" }),
		("VEF.Planet.VanillaExpandedFramework_FactionGenerator_NewGeneratedFaction_Patch", "Postfix", null),
	};

	// With Vanilla Expanded Framework, each of its Surface checks calls IsSurface instead of reading vanilla's Surface
	// def. Without it, nothing carries Ringworld's VEF patches.
	private static void CheckVefSurfaceChecks(RingLayer ring, RingCheckResult result)
	{
		MethodInfo isSurface = AccessTools.Method(typeof(SurfaceIdentity), nameof(SurfaceIdentity.IsSurface));
		FieldInfo surface = AccessTools.Field(typeof(PlanetLayerDefOf), nameof(PlanetLayerDefOf.Surface));
		if (!VefCompatibility.Active)
		{
			foreach (MethodBase method in Harmony.GetAllPatchedMethods())
			{
				if (Harmony.GetPatchInfo(method).Owners.Contains(VefCompatibility.HarmonyId))
				{
					result.Fail($"{method.DeclaringType?.FullName}.{method.Name} carries {VefCompatibility.HarmonyId}'s patch without VEF");
				}
			}
			return;
		}
		foreach ((string type, string name, string[] parameters) in VefSurfaceChecks)
		{
			MethodInfo target = AccessTools.Method(AccessTools.TypeByName(type), name, parameters?.Select(AccessTools.TypeByName).ToArray());
			if (target == null)
			{
				result.Fail($"{type}.{name} not found");
				continue;
			}
			List<CodeInstruction> code = PatchProcessor.GetCurrentInstructions(target);
			if (!code.Any(instruction => instruction.Calls(isSurface)))
			{
				result.Fail($"{type}.{name} doesn't call IsSurface");
			}
			if (code.Any(instruction => instruction.LoadsField(surface)))
			{
				result.Fail($"{type}.{name} still reads vanilla's Surface def");
			}
		}
	}
}
