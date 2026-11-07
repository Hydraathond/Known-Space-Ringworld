using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Night-shadow checks: where each shadow is and how wide, when the world shaders get it, and that ring worlds draw with
// the shaders that read it. How it looks, which features it darkens and that a Planet world loaded after a ring world
// looks vanilla again are checked by eye.
public static partial class RingSelfCheck
{
	// Steps per day when measuring vanilla's night on the midline.
	private const int NightLengthSteps = 2400;

	// Hours the night shadow's width may differ from vanilla's night on the midline: the measuring step.
	private const float NightLengthToleranceHours = 0.25f;

	private const int MaxListedMaterials = 10;

	// At each sampled moment there is one night shadow per night zone, and each one's centre is local midnight on the
	// midline below it, fully dark, with full daylight half a night zone away (by the shaders' formula, mirrored in
	// RingNightShadow.Darkness). Vanilla's local time steps by whole-hour time zones, so a tile's midnight can be half an
	// hour off the smooth one the shadow follows, plus half a tile where the centre falls between tile centres. On a live
	// arc, centres over the stand-in span have no tile to check.
	private static void CheckNightShadowCentredOnMidnight(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		int row = geometry.tilesAcross / 2;
		float tileAngle = 360f / geometry.tilesAround;
		float firstColumnAngle = geometry.CenterUV(row, 0).x / geometry.radius * Mathf.Rad2Deg;
		float hoursPerTile = tileAngle * geometry.nightZones / GenDate.TimeZoneWidth;
		float tolerance = 0.5f + 0.5f * hoursPerTile + 0.01f;
		for (int sample = 0; sample < NightZoneSamples; sample++)
		{
			int ticks = SampleTicks(sample);
			Vector4 shadow = RingNightShadow.UniformAt(ticks, geometry.nightZones);
			if (Mathf.RoundToInt(shadow.z) != geometry.nightZones)
			{
				result.Fail($"at tick {ticks} the night shadow has {shadow.z} night zones, the ring {geometry.nightZones}");
				continue;
			}
			for (int k = 0; k < geometry.nightZones; k++)
			{
				float centreRadians = shadow.x + k * 2f * Mathf.PI / geometry.nightZones;
				float noonRadians = centreRadians + Mathf.PI / geometry.nightZones;
				if (RingNightShadow.Darkness(centreRadians, shadow) < 1f || RingNightShadow.Darkness(noonRadians, shadow) > 0f)
				{
					result.Fail($"at tick {ticks} the night shadow's darkness is {RingNightShadow.Darkness(centreRadians, shadow):F2} at its centre and {RingNightShadow.Darkness(noonRadians, shadow):F2} half a night zone away, expected 1 and 0");
				}
				float centre = centreRadians * Mathf.Rad2Deg;
				int column = Mathf.RoundToInt((centre - firstColumnAngle) / tileAngle);
				int tile = geometry.TileId(row, column);
				if (tile < 0)
				{
					continue;
				}
				float hour = GenDate.HourFloat(ticks, ring.LongLatOf(tile).x);
				float fromMidnight = Mathf.Min(hour, GenDate.HoursPerDay - hour);
				if (fromMidnight > tolerance)
				{
					result.Fail($"at tick {ticks} the night shadow centred at {Mathf.Repeat(centre, 360f):F2} degrees is over local hour {hour:F2}, {fromMidnight:F2} hours from midnight (tolerance {tolerance:F2})");
				}
			}
		}
	}

	// At each sampled moment each night shadow's centre is under its own shadow square's centre, so they move together.
	private static void CheckNightShadowUnderSquares(RingLayer ring, RingCheckResult result)
	{
		if (!TryGetShadowSquares(ring, result, out GlobalDrawLayer_RingShadowSquares squares))
		{
			return;
		}
		int nightZones = ring.Geometry.nightZones;
		for (int sample = 0; sample < NightZoneSamples; sample++)
		{
			int ticks = SampleTicks(sample);
			Vector4 shadow = RingNightShadow.UniformAt(ticks, nightZones);
			Quaternion rotation = GlobalDrawLayer_RingShadowSquares.RotationAt(ticks, nightZones);
			bool[] used = new bool[nightZones];
			foreach (Vector3 centre in squares.Centres)
			{
				float angle = RingGeometry.AngleOf(rotation * centre);
				float off = RingNightShadow.AngleFromCentre(angle, shadow) * Mathf.Rad2Deg;
				int k = GenMath.PositiveMod(Mathf.RoundToInt((angle - shadow.x) * nightZones / (2f * Mathf.PI)), nightZones);
				if (off > 0.01f)
				{
					result.Fail($"at tick {ticks} the shadow square at {angle * Mathf.Rad2Deg:F2} degrees is {off:F2} degrees from the nearest night shadow's centre");
				}
				else if (used[k])
				{
					result.Fail($"at tick {ticks} two shadow squares are over the same night shadow");
				}
				used[k] = true;
			}
		}
	}

	// At each sampled moment the night shadow is as wide as vanilla's night on the midline that day: measured over the
	// next day from vanilla's sun glow (the formula GenCelestial.CelestialSunGlow reads for a tile) at the midline's latitude, with the
	// local time of the first live column's midline-row tile, as a colony map on the midline there would have it. No row
	// lies on the midline when the ring has an even number of rows, so its latitude is the middle of the two middle rows'.
	private static void CheckNightShadowWidth(RingLayer ring, RingCheckResult result)
	{
		Func<float, int, float, float> celestialSunGlowPercent = AccessTools.MethodDelegate<Func<float, int, float, float>>(AccessTools.Method(typeof(GenCelestial), "CelestialSunGlowPercent"));
		RingGeometry geometry = ring.Geometry;
		Vector2 upperMidline = ring.LongLatOf(geometry.TileId(geometry.tilesAcross / 2, geometry.firstColumn));
		Vector2 lowerMidline = ring.LongLatOf(geometry.TileId((geometry.tilesAcross - 1) / 2, geometry.firstColumn));
		float longitude = upperMidline.x;
		float latitude = 0.5f * (upperMidline.y + lowerMidline.y);
		float zone = 2f * Mathf.PI / geometry.nightZones;
		for (int sample = 0; sample < NightZoneSamples; sample++)
		{
			int ticks = SampleTicks(sample);
			int nightSteps = 0;
			for (int step = 0; step < NightLengthSteps; step++)
			{
				int stepTicks = ticks + step * GenDate.TicksPerDay / NightLengthSteps;
				float glow = celestialSunGlowPercent(latitude, GenDate.DayOfYear(stepTicks, longitude), GenDate.DayPercent(stepTicks, longitude));
				if (!GenCelestial.IsDaytime(glow))
				{
					nightSteps++;
				}
			}
			float vanillaHours = (float)nightSteps / NightLengthSteps * GenDate.HoursPerDay;
			float shadowHours = 2f * RingNightShadow.UniformAt(ticks, geometry.nightZones).y / zone * GenDate.HoursPerDay;
			if (Mathf.Abs(shadowHours - vanillaHours) > NightLengthToleranceHours)
			{
				result.Fail($"at tick {ticks} the night shadow is {shadowHours:F2} hours wide, vanilla's night on the midline {vanillaHours:F2} hours");
			}
		}
	}

	// The ring structure shader darkens a face by the night shadow when its material says so. Each face of the thickness
	// is told apart by the way it faces, independently of how the mesh was built: the wall tops face the axis, the inner
	// wall faces the midline, the outer face away from the axis and the walls' outer sides away from the midline. Only
	// the first two take the night shadow; no face of the shadow squares does.
	private static void CheckNightShadowOnStructure(RingLayer ring, RingCheckResult result)
	{
		HashSet<string> wrong = new HashSet<string>();
		foreach ((_, bool squares, LayerSubMesh subMesh) in StructureSubMeshes(ring, result))
		{
			bool actual = subMesh.material.GetFloat(RingStructure.NightShadowID) > 0.5f;
			for (int t = 0; t < subMesh.tris.Count; t += 3)
			{
				Vector3 a = subMesh.verts[subMesh.tris[t]];
				Vector3 b = subMesh.verts[subMesh.tris[t + 1]];
				Vector3 c = subMesh.verts[subMesh.tris[t + 2]];
				Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
				Vector3 centre = (a + b + c) / 3f;
				float outward = Vector3.Dot(normal, new Vector3(centre.x, 0f, centre.z).normalized);
				(string face, bool expected) = squares ? ("shadow squares", false)
					: outward < -0.5f ? ("rim walls' tops", true)
					: outward > 0.5f ? ("outer face", false)
					: normal.y * centre.y < 0f ? ("rim walls' inner faces", true)
					: ("rim walls' outer sides", false);
				if (actual != expected)
				{
					wrong.Add(expected ? $"the night shadow doesn't reach the {face}" : $"the night shadow reaches the {face}");
				}
			}
		}
		foreach (string failure in wrong)
		{
			result.Fail(failure);
		}
	}

	// Once vanilla has set the world's shader globals for this frame, the world shaders get the night shadow wherever
	// vanilla would show its night side (any layer but the ring, orbit-map backgrounds, or the planet day/night setting
	// on), for the current moment, and none otherwise.
	private static void CheckNightShadowShown(RingLayer ring, RingCheckResult result)
	{
		if (Find.WorldSelector?.SelectedLayer == null)
		{
			// Straight after world generation nothing is selected and vanilla can't set the globals yet; the check on
			// load and the debug action cover it.
			return;
		}
		WorldRendererUtility.UpdateGlobalShadersParams();
		bool shown = WorldRendererUtility.WorldBackgroundNow || !PlanetLayer.Selected.IsRootSurface || Find.PlaySettings.usePlanetDayNightSystem;
		Vector4 expected = shown ? RingNightShadow.UniformAt(RingLocalTime.NowTicksAbs(), ring.Geometry.nightZones) : Vector4.zero;
		if (!Shader.TryGetGlobalVector(RingNightShadow.PropertyID, out Vector4 actual))
		{
			result.Fail("the night shadow global isn't set");
		}
		else if ((actual - expected).sqrMagnitude > 1e-8f)
		{
			result.Fail($"the night shadow global is {actual}, expected {expected} ({(shown ? "vanilla would show its night side" : "vanilla would show no night side")})");
		}
	}

	// Every ring copy of a vanilla lit world shader loads, and once the world's materials are switched, no live material
	// still draws with a vanilla lit world shader. Materials made later are switched as they're drawn.
	private static void CheckLitMaterialsSwitched(RingLayer ring, RingCheckResult result)
	{
		foreach (string ringShaderName in RingMaterials.RingShaderNames.Values)
		{
			if (!RingMaterials.RingShaderLoaded(ringShaderName))
			{
				result.Fail($"the ring shader {ringShaderName} didn't load");
			}
		}
		RingMaterials.Sync(Find.World, force: true);
		List<string> unswitched = RingMaterials.LiveMaterials()
			.Where(material => RingMaterials.RingShaderNames.ContainsKey(RingMaterials.ShaderDefName(material)))
			.Select(material => $"{material.name} ({RingMaterials.ShaderDefName(material)})")
			.ToList();
		if (unswitched.Count > 0)
		{
			string more = unswitched.Count > MaxListedMaterials ? $" and {unswitched.Count - MaxListedMaterials} more" : "";
			result.Fail($"{unswitched.Count} materials still draw with a vanilla lit world shader: {string.Join(", ", unswitched.Take(MaxListedMaterials))}{more}");
		}
	}
}
