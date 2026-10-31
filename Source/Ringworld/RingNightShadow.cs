using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ringworld;

// The night shadow: one per night zone, darkening the inner surface with vanilla's night-side tint (ADR 0002). Cosmetic,
// like the shadow squares: local time (RingGeometry.LocalTimeLongitudeOf) makes the night zones and this follows it.
//
// The ring's copies of vanilla's lit world shaders draw it from one global, _RingShadow (Shaders/World/RW_RingShadow.glsl),
// which UniformAt computes and Patch_WorldRendererUtility_UpdateGlobalShadersParams sets every frame. Each shadow is
// centred on local midnight, turning with the shadow squares (GlobalDrawLayer_RingShadowSquares.TurnAt), and is as wide
// as vanilla's night on the midline that day, with straight edges from rim to rim and a one-hour soft edge.
public static class RingNightShadow
{
	public static readonly int PropertyID = Shader.PropertyToID("_RingShadow");

	// The glow below which vanilla's colony maps have night (GenCelestial.IsDaytime).
	private const float DaytimeGlow = 0.6f;

	// Vanilla's latitude for the midline (RingGeometry.VanillaLatitudeOf).
	private const float MidlineLatitude = 0f;

	private const int BisectionSteps = 24;

	private static readonly Func<float, int, float, float> CelestialSunGlowPercent = AccessTools.MethodDelegate<Func<float, int, float, float>>(AccessTools.Method(typeof(GenCelestial), "CelestialSunGlowPercent"));

	// Per day of the year, NightHalfShare's answer, or NaN until asked.
	private static readonly float[] NightHalfShares = NewNightHalfShares();

	// The global for the moment ticksAbs on a ring with this many night zones; see RW_RingShadow.glsl.
	public static Vector4 UniformAt(int ticksAbs, int nightZones)
	{
		float zone = 2f * Mathf.PI / nightZones;
		float centre = -GlobalDrawLayer_RingShadowSquares.TurnAt(ticksAbs, nightZones) * Mathf.Deg2Rad;
		float halfWidth = NightHalfShare(GenDate.DayOfYear(ticksAbs, 0f)) * zone;
		float softEdge = zone / GenDate.HoursPerDay;
		return new Vector4(centre, halfWidth, nightZones, softEdge);
	}

	// How dark the night shadow makes the inner surface at an angle round the ring (radians): 0 in daylight, 1 inside
	// a night shadow. The C# mirror of ringNightShadow.
	public static float Darkness(float angle, Vector4 shadow)
	{
		if (shadow.z < 0.5f)
		{
			return 0f;
		}
		float day = Mathf.Clamp01((AngleFromCentre(angle, shadow) - shadow.y) / Mathf.Max(shadow.w, 1e-6f) + 0.5f);
		return 1f - day;
	}

	// Radians from an angle round the ring to the nearest night shadow's centre.
	public static float AngleFromCentre(float angle, Vector4 shadow)
	{
		float zone = 2f * Mathf.PI / shadow.z;
		float t = (angle - shadow.x) / zone + 0.5f;
		return Mathf.Abs(t - Mathf.Floor(t) - 0.5f) * zone;
	}

	// Half of the share of a day that's night on the midline on this day of the year, from vanilla's sun glow: 0 with
	// no night, 0.5 with no day. Vanilla's glow is symmetric about noon and falls towards midnight, so night runs from
	// midnight to the same distance either side of it.
	public static float NightHalfShare(int dayOfYear)
	{
		dayOfYear = GenMath.PositiveMod(dayOfYear, GenDate.DaysPerYear);
		if (float.IsNaN(NightHalfShares[dayOfYear]))
		{
			NightHalfShares[dayOfYear] = ComputeNightHalfShare(dayOfYear);
		}
		return NightHalfShares[dayOfYear];
	}

	private static float ComputeNightHalfShare(int dayOfYear)
	{
		if (IsDaytimeAt(dayOfYear, 0f))
		{
			return 0f;
		}
		if (!IsDaytimeAt(dayOfYear, 0.5f))
		{
			return 0.5f;
		}
		// Bisect the day percent (0 midnight, 0.5 noon) between a moment of night and one of day.
		float night = 0f;
		float day = 0.5f;
		for (int step = 0; step < BisectionSteps; step++)
		{
			float middle = 0.5f * (night + day);
			if (IsDaytimeAt(dayOfYear, middle))
			{
				day = middle;
			}
			else
			{
				night = middle;
			}
		}
		return 0.5f * (night + day);
	}

	private static bool IsDaytimeAt(int dayOfYear, float dayPercent)
	{
		return CelestialSunGlowPercent(MidlineLatitude, dayOfYear, dayPercent) > DaytimeGlow;
	}

	private static float[] NewNightHalfShares()
	{
		float[] shares = new float[GenDate.DaysPerYear];
		for (int i = 0; i < shares.Length; i++)
		{
			shares[i] = float.NaN;
		}
		return shares;
	}
}
