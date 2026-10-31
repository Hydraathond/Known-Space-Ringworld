using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Orbit layers on a ring world: every layer of an OrbitLayer type centred on the sun, Odyssey's orbit or any other. Each
// is a sphere (or the part of one spanning the live arc and its orbit band, RingOrbitSpan) at a fraction of the ring radius
// at every ring size: half, or with LAO's orbit stack its def's (RingOrbitExtension). Its
// subdivisions stay as its settings say, so Odyssey's orbit keeps vanilla's tile count and orbit travel costs in tiles
// stay vanilla; its tiles just grow with the ring. The orbit view's camera scales with it (OrbitViewCamera). An
// orbit-type layer centred anywhere else, round a moon say, is left alone.
public static class RingOrbit
{
	public const float DefaultRadiusFactor = 0.5f;

	// How far a layer's origin may be from the sun and still count as centred on it.
	private const float SunCentredTolerance = 0.01f;

	// The sun is at the ring's centre, the surface layer's origin; before the ring is registered, vanilla's surface
	// origin.
	public static Vector3 SunPosition(WorldGrid grid)
	{
		return grid?.Surface?.Origin ?? Vector3.zero;
	}

	public static bool IsSunCentred(PlanetLayerDef def, Vector3 origin, WorldGrid grid)
	{
		return IsSunCentred(def, origin, SunPosition(grid));
	}

	private static bool IsSunCentred(PlanetLayerDef def, Vector3 origin, Vector3 sun)
	{
		return def?.layerType != null && typeof(OrbitLayer).IsAssignableFrom(def.layerType) && (origin - sun).magnitude <= SunCentredTolerance;
	}

	public static bool IsSunCentred(PlanetLayer layer)
	{
		return layer is OrbitLayer && IsSunCentred(layer.Def, layer.Origin, Find.WorldGrid);
	}

	// Every sun-centred orbit layer of the world, in layer id order.
	public static IEnumerable<PlanetLayer> Layers(WorldGrid grid)
	{
		if (grid == null)
		{
			return Enumerable.Empty<PlanetLayer>();
		}
		return grid.PlanetLayers.Values.Where(IsSunCentred).OrderBy(layer => layer.LayerID);
	}

	// Where a layer of the def made now goes, on a ring world being created or one it's added to. The def's extension is
	// only read while LAO's orbit stack works (LaoIntegration.OrbitStackAvailable); otherwise it's the defaults, so
	// without LAO a ring world keeps v0.1.0's single full-sphere orbit at half the ring radius.
	public static RingOrbitExtension ExtensionForNewLayer(PlanetLayerDef def)
	{
		bool ringWorld = Current.CreatingWorld != null ? WorldShape.Ring : SurfaceRing.InRingWorld;
		if (!ringWorld || !LaoIntegration.OrbitStackAvailable)
		{
			return RingOrbitExtension.Default;
		}
		return Extension(def);
	}

	private static readonly AccessTools.FieldRef<PlanetLayer, float> LayerRadius = AccessTools.FieldRefAccess<PlanetLayer, float>("radius");

	// How close a saved orbit's radius must be to the default fraction of the ring radius to count as made without the
	// orbit stack.
	private const float UpgradeTolerance = 0.01f;

	// The surface ring of the world being loaded, once its layer has loaded (it's layer 0, loaded before the orbit
	// layers); null while loading a planet world.
	private static RingLayer loadingSurfaceRing;

	internal static void NoteLoadingWorld()
	{
		loadingSurfaceRing = null;
	}

	internal static void NoteLoadedLayer(PlanetLayer layer)
	{
		if (layer is RingLayer ring && ring.IsSurfaceRing)
		{
			loadingSurfaceRing = ring;
		}
	}

	// Upgrading a ring world made without LAO's orbit stack, loaded while the stack works: Odyssey's orbit, saved
	// centred on the sun at v0.1.0's half the ring radius with no orbit band, is moved to its place in the stack (0.7 x)
	// before its tiles are built, and marked as moved (RingOrbitSpan.MovedIntoStack). Its span and orbit band stay as
	// saved, so it stays a full sphere at 100% land coverage, and its tile ids, and so its world objects and maps, are
	// kept. Only a newly made layer gets an orbit band (ExtensionForNewLayer).
	internal static void UpgradeLoadedLayer(PlanetLayer layer)
	{
		RingLayer ring = loadingSurfaceRing;
		if (Scribe.mode != LoadSaveMode.LoadingVars || ring == null || !LaoIntegration.OrbitStackAvailable || layer.Def != PlanetLayerDefOf.Orbit || !IsSunCentred(layer.Def, layer.Origin, ring.Origin) || !RingOrbitSpan.IsFullBand(layer))
		{
			return;
		}
		float factor = Extension(layer.Def).radiusFactor;
		float savedFactor = layer.Radius / ring.Radius;
		if (Mathf.Abs(savedFactor - DefaultRadiusFactor) > UpgradeTolerance)
		{
			return;
		}
		float radius = ring.Radius * factor;
		Log.Message($"[Ringworld] Moved {Describe(layer)} from {savedFactor:F2} to {factor:F2} x the ring radius ({layer.Radius:F1} to {radius:F1}), into Layered Atmosphere and Orbit's orbit stack.");
		LayerRadius(layer) = radius;
		RingOrbitSpan.MarkMovedIntoStack(layer);
	}

	// A new layer's radius on a ring of this size.
	public static float RadiusForNewLayer(RingSize size, PlanetLayerDef def)
	{
		return size.Radius * ExtensionForNewLayer(def).radiusFactor;
	}

	// The radius the layer's vanilla camera settings (its extra camera altitude) are made for.
	public static float BaselineRadius(PlanetLayerDef def)
	{
		float baseline = Extension(def).baselineRadius;
		return baseline > 0f ? baseline : PlanetLayerSettingsDefOf.Orbit.settings.radius;
	}

	// For log lines.
	public static string Describe(PlanetLayer layer)
	{
		return $"orbit layer {layer.LayerID} ({layer.Def.label})";
	}

	private static RingOrbitExtension Extension(PlanetLayerDef def)
	{
		return def.GetModExtension<RingOrbitExtension>() ?? RingOrbitExtension.Default;
	}
}

// Where LAO's orbit stack puts the sun-centred orbit layers of a def made on a ring world
// (RingOrbit.ExtensionForNewLayer). Defs without it get the defaults, which are v0.1.0's orbit's.
public class RingOrbitExtension : DefModExtension
{
	public static readonly RingOrbitExtension Default = new RingOrbitExtension();

	// Of the ring's radius.
	public float radiusFactor = RingOrbit.DefaultRadiusFactor;

	// The radius the def's layers' vanilla camera settings are made for; 0 means vanilla's orbit radius (130).
	public float baselineRadius;

	// Degrees either side of the ring plane its layers keep tiles in (RingOrbitSpan); 90 is the full sphere.
	public float bandHalfAngle = RingOrbitSpan.FullBand;
}

// While a world loads, which surface ring it has, for RingOrbit.UpgradeLoadedLayer.
[HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.ExposeData))]
public static class Patch_WorldGrid_ExposeData_OrbitUpgrade
{
	public static void Prefix()
	{
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			RingOrbit.NoteLoadingWorld();
		}
	}
}

// Notes the world's surface ring once its layer has loaded, for RingOrbit.UpgradeLoadedLayer.
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.ExposeData))]
public static class Patch_PlanetLayer_ExposeData_OrbitUpgrade
{
	public static void Postfix(PlanetLayer __instance)
	{
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			RingOrbit.NoteLoadedLayer(__instance);
		}
	}
}

// While loading, vanilla's ExposeData builds the layer's tiles in InitializeLayer, after reading its radius.
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.InitializeLayer))]
public static class Patch_PlanetLayer_InitializeLayer_OrbitUpgrade
{
	public static void Prefix(PlanetLayer __instance)
	{
		if (__instance is OrbitLayer)
		{
			RingOrbit.UpgradeLoadedLayer(__instance);
		}
	}
}

// An orbit layer's radius is saved with it, so it only has to be set when the layer is made: on a new world, or when
// an orbit layer is added to an existing ring world (RingOrbit.ExtensionForNewLayer), or moved when a ring world made
// without LAO's orbit stack is loaded with it (RingOrbit.UpgradeLoadedLayer). A new world's ring may not be registered
// yet (layers are made in scenario order), so then the size picked on the Create World page is used. An atmosphere
// layer, made after the ring, gets its radius over the ring here too (RingAtmosphere).
[HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.RegisterPlanetLayer), typeof(PlanetLayerDef), typeof(PlanetLayerSettings), typeof(float?), typeof(Vector3?), typeof(Vector3?), typeof(float?))]
public static class Patch_WorldGrid_RegisterPlanetLayer
{
	public static void Prefix(WorldGrid __instance, PlanetLayerDef layer, PlanetLayerSettings settings, ref float? overrideRadius, Vector3? overrideOrigin)
	{
		if (overrideRadius.HasValue)
		{
			return;
		}
		// An atmosphere layer's def is an orbit-layer def, but on a ring world it's built as a ring (RingAtmosphere).
		if (RingAtmosphere.BuildsAsRing(layer, __instance))
		{
			overrideRadius = RingAtmosphere.RadiusFor(RingAtmosphere.SurfaceUnder(__instance), layer);
			return;
		}
		if (!RingOrbit.IsSunCentred(layer, overrideOrigin ?? settings.origin, __instance))
		{
			return;
		}
		if (SurfaceRing.Of(__instance) is { } ring)
		{
			overrideRadius = RingOrbit.RadiusForNewLayer(ring.Size, layer);
		}
		else if (Current.CreatingWorld != null && WorldShape.Ring)
		{
			overrideRadius = RingOrbit.RadiusForNewLayer(WorldShape.RingSize, layer);
		}
	}
}

// Vanilla's "add test planet layer" debug action makes a second orbit at a planet's radius 200, with 10 subdivisions
// and nothing on it, so on a ring world it would be outside smaller rings and couldn't be selected. There it makes one
// as the scenario makes the orbit instead: Odyssey's orbit settings and the radius rule, so it shares the orbit's
// sphere. It gets sites of the world's existing factions, as the Factions world-gen step would place them, so it can be
// viewed; that step itself reads the world being created, which only exists during world generation. Vanilla's
// "remove test planet layer" removes it as usual. Dev mode only, so it isn't the multi-layer orbit ADR 0003 keeps off
// the no-LAO path.
[HarmonyPatch(typeof(DebugActionsMisc), "AddTestPlanetLayer")]
public static class Patch_DebugActionsMisc_AddTestPlanetLayer
{
	private static readonly AccessTools.FieldRef<PlanetLayer> TestLayer = AccessTools.StaticFieldRefAccess<PlanetLayer>(AccessTools.Field(typeof(DebugActionsMisc), "TestLayer"));

	public static bool Prefix()
	{
		WorldGrid grid = Find.WorldGrid;
		if (SurfaceRing.Of(grid) == null || grid.Orbit == null)
		{
			return true;
		}
		if (TestLayer() != null)
		{
			grid.RemovePlanetLayer(TestLayer());
		}
		PlanetLayer orbit = grid.Orbit;
		PlanetLayer test = grid.RegisterPlanetLayer(PlanetLayerDefOf.Orbit, PlanetLayerSettingsDefOf.Orbit.settings);
		foreach (WorldGenStepDef step in test.Def.GenStepsInOrder)
		{
			if (step.worldGenStep is not WorldGenStep_Factions)
			{
				step.worldGenStep.GenerateFresh(Find.World.info.seedString, test);
			}
		}
		// An empty list adds no new factions.
		FactionGenerator.GenerateFactionsIntoWorldLayer(test, new List<FactionDef>());
		test.AddConnection(orbit, 0f);
		orbit.AddConnection(test, 0f);
		TestLayer() = test;
		return false;
	}
}
