using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Below 100% land coverage, a ring world's orbit layers only have tiles over the live arc's angular span: land coverage
// x 360 degrees round the ring, centred on 0 (a 30% span runs 54A to 54S, give or take the live arc's rounding to whole
// columns). Tiles whose centre is outside it are dropped while the layer is built, as vanilla drops tiles outside a
// partial-coverage planet's view. The span is saved with each orbit layer and never recomputed, so the same tiles are
// built on every load and tile ids stay stable; a layer saved without one (a planet world, a full loop, or a save from
// before orbit spans) loads as a full sphere.
// With LAO's orbit stack, each orbit layer made on a ring world also has an orbit band: tiles further than its def's
// band angle from the ring plane are dropped the same way (RingOrbitExtension.bandHalfAngle), and the angle is saved
// beside the span. Every other layer, and a save from before orbit bands, keeps tiles pole to pole.
public static class RingOrbitSpan
{
	// Half the span either side of 0 degrees; this or more is the full loop.
	public const float FullSpan = 180f;

	// The orbit band's angle either side of the ring plane; this or more is pole to pole.
	public const float FullBand = 90f;

	private sealed class Span
	{
		public float halfAngle = FullSpan;

		public float bandHalfAngle = FullBand;

		public bool movedIntoStack;
	}

	private static readonly ConditionalWeakTable<PlanetLayer, Span> Spans = new ConditionalWeakTable<PlanetLayer, Span>();

	public static float HalfAngleForCoverage(float landCoverage)
	{
		return Mathf.Min(FullSpan, landCoverage * FullSpan);
	}

	// The layer's span, in degrees either side of 0; FullSpan if it has none.
	public static float HalfAngleOf(PlanetLayer layer)
	{
		return Spans.TryGetValue(layer, out Span span) ? span.halfAngle : FullSpan;
	}

	// The layer's orbit band, in degrees either side of the ring plane; FullBand if it has none.
	public static float BandHalfAngleOf(PlanetLayer layer)
	{
		return Spans.TryGetValue(layer, out Span span) ? span.bandHalfAngle : FullBand;
	}

	public static bool IsFullSpan(PlanetLayer layer)
	{
		return HalfAngleOf(layer) >= FullSpan;
	}

	public static bool IsFullBand(PlanetLayer layer)
	{
		return BandHalfAngleOf(layer) >= FullBand;
	}

	// Whether the layer was moved into LAO's orbit stack from a ring world made without it (RingOrbit.UpgradeLoadedLayer).
	public static bool MovedIntoStack(PlanetLayer layer)
	{
		return Spans.TryGetValue(layer, out Span span) && span.movedIntoStack;
	}

	internal static void MarkMovedIntoStack(PlanetLayer layer)
	{
		Spans.GetValue(layer, _ => new Span()).movedIntoStack = true;
	}

	// The full loop, pole to pole: a whole sphere.
	public static bool IsFullSphere(PlanetLayer layer)
	{
		return IsFullSpan(layer) && IsFullBand(layer);
	}

	// Outside the span, by the angle round the ring of a layer-local point; slack widens the span by that many degrees.
	public static bool OutsideSpan(PlanetLayer layer, Vector3 point, float slack = 0f)
	{
		return !IsFullSpan(layer) && Mathf.Abs(RingGeometry.AngleOf(point) * Mathf.Rad2Deg) > HalfAngleOf(layer) + slack;
	}

	// Outside the orbit band, by the angle of a layer-local point from the ring plane; slack widens it by that many
	// degrees.
	public static bool OutsideBand(PlanetLayer layer, Vector3 point, float slack = 0f)
	{
		return !IsFullBand(layer) && Mathf.Abs(AngleFromRingPlane(point)) > BandHalfAngleOf(layer) + slack;
	}

	// In degrees, towards +Y positive.
	public static float AngleFromRingPlane(Vector3 point)
	{
		return Mathf.Atan2(point.y, RingGeometry.DistanceFromAxis(point)) * Mathf.Rad2Deg;
	}

	// A layer that isn't loaded is new: on a new world (whose ring may not be registered yet, so the coverage picked on
	// the Create World page is used), or added to an existing one (Odyssey added to a ring world, or a dev-added layer).
	internal static void SetForNewLayer(PlanetLayer layer)
	{
		if (Spans.TryGetValue(layer, out _))
		{
			return;
		}
		float halfAngle = FullSpan;
		if (Current.CreatingWorld != null)
		{
			if (WorldShape.Ring)
			{
				halfAngle = HalfAngleForCoverage(WorldShape.LandCoverage);
			}
		}
		else if (SurfaceRing.Current is { } ring)
		{
			halfAngle = HalfAngleForCoverage(ring.LandCoverage);
		}
		float bandHalfAngle = Mathf.Min(FullBand, RingOrbit.ExtensionForNewLayer(layer.Def).bandHalfAngle);
		Spans.Add(layer, new Span { halfAngle = halfAngle, bandHalfAngle = bandHalfAngle });
	}

	// Written only below the full loop and pole to pole, and for a layer moved into the stack, so planet worlds' saves
	// don't change.
	internal static void Expose(PlanetLayer layer)
	{
		Span span = Spans.GetValue(layer, _ => new Span());
		Scribe_Values.Look(ref span.halfAngle, "ringOrbitSpanHalfAngle", FullSpan);
		Scribe_Values.Look(ref span.bandHalfAngle, "ringOrbitBandHalfAngle", FullBand);
		Scribe_Values.Look(ref span.movedIntoStack, "ringOrbitMovedIntoStack", false);
	}
}

// Every load builds the layer from ExposeData, after this has read the saved span; a new layer is built straight after
// it's made, so it gets its span here first.
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.InitializeLayer))]
public static class Patch_PlanetLayer_InitializeLayer_OrbitSpan
{
	public static void Prefix(PlanetLayer __instance)
	{
		if (__instance is OrbitLayer)
		{
			RingOrbitSpan.SetForNewLayer(__instance);
		}
	}
}

// A prefix, so the span is read before vanilla's ExposeData builds the layer.
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.ExposeData))]
public static class Patch_PlanetLayer_ExposeData_OrbitSpan
{
	public static void Prefix(PlanetLayer __instance)
	{
		if (__instance is OrbitLayer)
		{
			RingOrbitSpan.Expose(__instance);
		}
	}
}

// Vanilla's tile filter, given the generated tile's corners (indices into its working vertex list): a tile it keeps is
// still dropped if its centre, the corners' average as GetTileCenter takes it, is outside the span or the orbit band.
[HarmonyPatch(typeof(PlanetLayer), "ShouldDiscardGeneratedTile")]
public static class Patch_PlanetLayer_ShouldDiscardGeneratedTile
{
	private static readonly AccessTools.FieldRef<List<Vector3>> TmpVerts = AccessTools.StaticFieldRefAccess<List<Vector3>>(AccessTools.Field(typeof(PlanetLayer), "tmp_verts"));

	public static void Postfix(PlanetLayer __instance, List<int> generatedTileVertsList, ref bool __result)
	{
		if (__result || __instance is not OrbitLayer)
		{
			return;
		}
		List<Vector3> verts = TmpVerts();
		Vector3 sum = Vector3.zero;
		foreach (int index in generatedTileVertsList)
		{
			sum += verts[index];
		}
		Vector3 center = sum / generatedTileVertsList.Count;
		__result = RingOrbitSpan.OutsideSpan(__instance, center) || RingOrbitSpan.OutsideBand(__instance, center);
	}
}
