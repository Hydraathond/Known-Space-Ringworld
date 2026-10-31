using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace Ringworld;

// Below 100% land coverage, land fades to ocean over the last few columns before each cut of the live arc, as vanilla's
// ConvertToIsland fades a partial-coverage planet's land into ocean at the edge of its view (which never runs on a
// ring: its planet coverage is always 100%). The terrain step's elevation noise is wrapped as soon as it's set up, so
// rainfall, swampiness, hilliness, temperature and biome all follow the faded elevation, as they do vanilla's.
// Full loops and planet layers are untouched.
[HarmonyPatch(typeof(WorldGenStep_Terrain), "SetupElevationNoise")]
public static class Patch_WorldGenStep_Terrain_SetupElevationNoise
{
	private static readonly AccessTools.FieldRef<WorldGenStep_Terrain, ModuleBase> NoiseElevation = AccessTools.FieldRefAccess<WorldGenStep_Terrain, ModuleBase>("noiseElevation");

	public static void Postfix(WorldGenStep_Terrain __instance, PlanetLayer layer)
	{
		if (layer is RingLayer { IsSurfaceRing: true, Geometry: { IsFullLoop: false } geometry })
		{
			ref ModuleBase noise = ref NoiseElevation(__instance);
			noise = new CutFade(geometry, Gen.HashCombineInt(Find.World.info.Seed, CutFade.SeedPart), noise);
		}
	}
}

// Elevation in metres, lowered towards ocean by RingGeometry.CutFadeAt: land sinks to OceanDepth below sea level and
// water deepens by as much, so the coast falls somewhere in the fade (nearer the cut where the land is high) and every
// tile within CutOceanColumns of a cut is ocean.
// The fade is pushed into the arc by low-frequency noise (up to RingGeometry.maxCutWanderColumns), so the coast at a
// cut wanders in bays and headlands rather than running straight across the ring. The noise is sampled where the point
// is, so the two cuts get different coasts, and it's seeded from the world seed, so the same seed gives the same coast.
internal class CutFade : ModuleBase
{
	public const int SeedPart = 741902385;

	private const double OceanDepth = 300.0;

	// Wander noise cycles from rim to rim (each a bay and a headland), so every ring size looks the same.
	private const double WanderCyclesAcross = 1.25;

	private readonly RingGeometry geometry;

	private readonly ModuleBase wander;

	public CutFade(RingGeometry geometry, int seed, ModuleBase input)
		: base(1)
	{
		this.geometry = geometry;
		modules[0] = input;
		wander = new Perlin(WanderCyclesAcross / (2.0 * geometry.HalfWidth), 2.0, 0.5, 2, seed, QualityMode.High);
	}

	public override double GetValue(double x, double y, double z)
	{
		double elevation = modules[0].GetValue(x, y, z);
		float columnsFromCut = geometry.ColumnsFromCut(new Vector3((float)x, (float)y, (float)z));
		if (columnsFromCut >= geometry.cutFadeColumns + geometry.maxCutWanderColumns)
		{
			return elevation;
		}
		// Two-octave Perlin noise seldom leaves -1..1, so this mapping rarely clamps (a clamped stretch would wander
		// nowhere and run straight again). The wander only ever pushes the fade inward.
		float inward = geometry.maxCutWanderColumns * Mathf.Clamp01(0.5f + 0.5f * (float)wander.GetValue(x, y, z));
		float fade = geometry.CutFadeAt(columnsFromCut - inward);
		if (fade <= 0f)
		{
			return elevation;
		}
		if (elevation > 0.0)
		{
			elevation *= 1.0 - fade;
		}
		return elevation - fade * OceanDepth;
	}
}
