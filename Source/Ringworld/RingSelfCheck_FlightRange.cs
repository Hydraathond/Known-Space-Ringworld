using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Flight cost and range per tile (RingFlightRange): vanilla's readers of a layer def's rangeDistanceFactor go through
// RangeFactor, an atmosphere ring's factor is the def's scaled by its tile size over the planet's, and every other
// layer uses the def's unchanged.
public static partial class RingSelfCheck
{
	// The ground's subdivisions and radius, from vanilla's surface settings (the reference the formula measures against).
	private const int GroundSubdivisions = 10;
	private const float GroundRadius = 100f;

	// How far an effective factor may be from the formula's, as a fraction of it.
	private const float FactorTolerance = 0.001f;

	// Ticket 13's pinned case, measured on a ring world: a mesosphere ring's factor is about 17 / 35.8 of its def's
	// 17 per tile, whichever the ring size (its hexes are the ground's).
	private const float MesosphereRatio = 0.0279f;
	private const float MesosphereRatioTolerance = 0.003f;

	private static void CheckFlightRange(RingLayer ring, RingCheckResult result)
	{
		foreach ((MethodInfo method, int reads) in RingFlightRange.Targets)
		{
			List<CodeInstruction> code = PatchProcessor.GetCurrentInstructions(method);
			string name = $"{method.DeclaringType?.Name}.{method.Name}";
			if (code.Count(instruction => instruction.Calls(RingFlightRange.RangeFactorMethod)) != reads)
			{
				result.Fail($"{name} doesn't read the range factor through RingFlightRange.RangeFactor {reads} time(s)");
			}
			if (code.Any(instruction => instruction.LoadsField(RingFlightRange.FactorField)))
			{
				result.Fail($"{name} still reads a layer def's rangeDistanceFactor directly");
			}
		}
		foreach (PlanetLayer layer in Find.WorldGrid.PlanetLayers.Values)
		{
			string name = $"layer {layer.LayerID} ({layer.Def?.defName})";
			float defFactor = layer.Def.rangeDistanceFactor;
			float actual = RingFlightRange.RangeFactor(layer);
			float expected = defFactor;
			if (layer is RingLayer shaped && shaped != ring)
			{
				PlanetLayerSettings settings = DefDatabase<PlanetLayerSettingsDef>.GetNamedSilentFail(layer.Def.defName)?.settings;
				if (settings == null)
				{
					result.Fail($"{name} has no settings def named after its layer def, to measure its planet tile size by");
					continue;
				}
				float planetTile = Mathf.Pow(2f, GroundSubdivisions - settings.subdivisions) * settings.radius / GroundRadius;
				expected = defFactor * (shaped.Geometry.tileSize / ring.Geometry.tileSize) / planetTile;
				if (expected >= defFactor)
				{
					result.Fail($"{name}'s expected factor {expected} isn't below its def's {defFactor}: a ring atmosphere's tiles are finer than a planet's");
				}
			}
			if (layer.Def.defName == "LAO_Mesosphere" && Mathf.Abs(actual / defFactor - MesosphereRatio) > MesosphereRatioTolerance)
			{
				result.Fail($"{name} has range factor {actual}, {actual / defFactor} of its def's; ticket 13's pinned case is about {MesosphereRatio} (17 / 35.8)");
			}
			if (Mathf.Abs(actual - expected) > expected * FactorTolerance)
			{
				result.Fail($"{name} has range factor {actual}, expected {expected} (def {defFactor})");
			}
		}
	}
}
