using System;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Climate checks on the generated tile temperatures, averaged per row. Only tiles low enough that vanilla terrain
// doesn't cool them for elevation are counted, so mountains don't hide the gradient.
public static partial class RingSelfCheck
{
	// Vanilla WorldGenStep_Terrain.ElevationTempReductionStartAlt (private there).
	private const float NoElevationCoolingBelow = 250f;

	// How much warmer than a row nearer the midline a row may average. Vanilla's +-4 degree temperature noise doesn't
	// fully average out over one row, and within about 9 degrees of the midline the gradient drops only 1 degree.
	private const float RowWarmingTolerance = 1f;

	// Room for the noise around the range's ends, in temperature before the global setting's curve.
	private const float RimRangeMargin = 2f;

	// Going outward from the midline, no row averages more than RowWarmingTolerance above any row between it and the
	// midline; so the midline rows are the warmest and neither rim gets warmer again.
	private static void CheckClimateFallsTowardRims(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		float?[] averages = SeaLevelTemperatureByRow(ring);
		int upperMidline = geometry.tilesAcross / 2;
		int lowerMidline = (geometry.tilesAcross - 1) / 2;
		CheckRowsCooling(averages, upperMidline, 1, result);
		CheckRowsCooling(averages, lowerMidline, -1, result);
	}

	private static void CheckRowsCooling(float?[] averages, int midlineRow, int step, RingCheckResult result)
	{
		float coolest = float.MaxValue;
		int coolestRow = -1;
		for (int row = midlineRow; row >= 0 && row < averages.Length; row += step)
		{
			if (averages[row] is not float average)
			{
				continue;
			}
			if (average > coolest + RowWarmingTolerance)
			{
				result.Fail($"row {row} averages {average:F1}C, warmer than row {coolestRow} ({coolest:F1}C) nearer the midline");
			}
			if (average < coolest)
			{
				coolest = average;
				coolestRow = row;
			}
		}
	}

	// Each rim row averages what vanilla gives a low tile within RingGeometry.RimVanillaLatitudeRange under the world's
	// temperature setting.
	private static void CheckRimClimate(RingLayer ring, RingCheckResult result)
	{
		Func<float, float> baseTemperatureAtLatitude = AccessTools.MethodDelegate<Func<float, float>>(AccessTools.Method(typeof(WorldGenStep_Terrain), "BaseTemperatureAtLatitude"));
		SimpleCurve temperatureCurve = Find.World.info.overallTemperature.GetTemperatureCurve();
		FloatRange latitudes = RingGeometry.RimVanillaLatitudeRange;
		float min = baseTemperatureAtLatitude(latitudes.max) - RimRangeMargin;
		float max = baseTemperatureAtLatitude(latitudes.min) + RimRangeMargin;
		if (temperatureCurve != null)
		{
			min = temperatureCurve.Evaluate(min);
			max = temperatureCurve.Evaluate(max);
		}
		float?[] averages = SeaLevelTemperatureByRow(ring);
		foreach (int row in ring.Geometry.RimRows)
		{
			if (averages[row] is not float average)
			{
				result.Fail($"rim row {row} has no tiles below {NoElevationCoolingBelow}m to measure");
			}
			else if (average < min || average > max)
			{
				result.Fail($"rim row {row} averages {average:F1}C, expected {min:F1}C to {max:F1}C (vanilla at {latitudes.min}-{latitudes.max} degrees latitude)");
			}
		}
	}

	// Average temperature of each row's tiles that vanilla doesn't cool for elevation; null for a row with none.
	private static float?[] SeaLevelTemperatureByRow(RingLayer ring)
	{
		RingGeometry geometry = ring.Geometry;
		double[] sums = new double[geometry.tilesAcross];
		int[] counts = new int[geometry.tilesAcross];
		for (int id = 0; id < ring.TilesCount; id++)
		{
			SurfaceTile tile = ring[id];
			if (tile == null || tile.elevation > NoElevationCoolingBelow)
			{
				continue;
			}
			int row = geometry.RowOf(id);
			sums[row] += tile.temperature;
			counts[row]++;
		}
		float?[] averages = new float?[geometry.tilesAcross];
		for (int row = 0; row < geometry.tilesAcross; row++)
		{
			if (counts[row] > 0)
			{
				averages[row] = (float)(sums[row] / counts[row]);
			}
		}
		return averages;
	}
}
