using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Night-zone checks on local time along the midline. That colony maps keep vanilla day length and night share, and that
// planet worlds are unaffected, follows from the patch touching only the longitude of ring tiles, and is checked in game.
public static partial class RingSelfCheck
{
	// The moments sampled, a quarter of a day apart from now, so a lucky time of day can't hide a wrong count.
	private const int NightZoneSamples = 4;

	// Vanilla rounds local time to whole-hour time zones, so tiles half a night zone apart can be an hour off 12 hours
	// (plus a little for float rounding).
	private const float OppositeHoursTolerance = 1.05f;

	// At each sampled moment the midline has exactly one night stretch per night zone, whose lengths differ by at
	// most one tile (where a stretch's ends fall between tile centres). On a live arc the midline only runs over land
	// coverage x the night zones: it has a night stretch for each zone it covers and at most one more than the zones it
	// touches, and its whole stretches (not cut short by a cut) are equally long.
	private static void CheckNightZoneCount(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		float zonesOnArc = (float)geometry.nightZones * geometry.liveTilesAround / geometry.tilesAround;
		int fewest = geometry.IsFullLoop ? geometry.nightZones : Mathf.FloorToInt(zonesOnArc);
		int most = geometry.IsFullLoop ? geometry.nightZones : Mathf.CeilToInt(zonesOnArc) + 1;
		for (int sample = 0; sample < NightZoneSamples; sample++)
		{
			int ticks = SampleTicks(sample);
			List<NightStretch> stretches = MidlineNightStretches(ring, ticks);
			if (stretches == null)
			{
				// An arc shorter than a night zone may lie wholly in one night.
				if (zonesOnArc >= 1f)
				{
					result.Fail($"at tick {ticks} the whole midline is night");
				}
				continue;
			}
			if (stretches.Count < fewest || stretches.Count > most)
			{
				string expected = fewest == most ? fewest.ToString() : $"{fewest}-{most}";
				result.Fail($"at tick {ticks} the midline has {stretches.Count} night stretches, expected {expected} over its {zonesOnArc:F2} night zones");
				continue;
			}
			List<int> lengths = stretches.Where(stretch => stretch.whole).Select(stretch => stretch.length).ToList();
			if (lengths.Count > 1 && lengths.Max() - lengths.Min() > 1)
			{
				result.Fail($"at tick {ticks} the midline's night stretches are {lengths.Min()} to {lengths.Max()} tiles long, not equal");
			}
		}
	}

	// A run of night tiles on the midline: its first column and its length in tiles. On a full loop it may run across
	// the seam. whole: it isn't cut short by one of the live arc's cuts.
	private readonly struct NightStretch
	{
		public readonly int start;

		public readonly int length;

		public readonly bool whole;

		public NightStretch(int start, int length, bool whole)
		{
			this.start = start;
			this.length = length;
			this.whole = whole;
		}
	}

	// The moment sampled for one of the NightZoneSamples samples.
	private static int SampleTicks(int sample)
	{
		return RingLocalTime.NowTicksAbs() + sample * GenDate.TicksPerDay / NightZoneSamples;
	}

	// The night stretches along the live arc's midline at a moment, from vanilla's sun glow, or null if the whole live
	// midline is night.
	private static List<NightStretch> MidlineNightStretches(RingLayer ring, int ticks)
	{
		RingGeometry geometry = ring.Geometry;
		int row = geometry.tilesAcross / 2;
		int count = geometry.liveTilesAround;
		bool[] night = new bool[count];
		int firstDay = -1;
		for (int index = 0; index < count; index++)
		{
			PlanetTile tile = new PlanetTile(geometry.TileId(row, geometry.firstColumn + index), ring);
			night[index] = !GenCelestial.IsDaytime(GenCelestial.CelestialSunGlow(tile, ticks));
			if (!night[index] && firstDay < 0)
			{
				firstDay = index;
			}
		}
		if (firstDay < 0)
		{
			return null;
		}
		List<NightStretch> stretches = new List<NightStretch>();
		int length = 0;
		if (geometry.IsFullLoop)
		{
			// Walk once round from a day tile, so a stretch across the seam is counted once.
			for (int step = 1; step <= count; step++)
			{
				int column = (firstDay + step) % count;
				if (night[column])
				{
					length++;
				}
				else if (length > 0)
				{
					stretches.Add(new NightStretch((column - length + count) % count, length, whole: true));
					length = 0;
				}
			}
			return stretches;
		}
		// Walk the arc from cut to cut; a stretch reaching either cut is cut short.
		for (int index = 0; index <= count; index++)
		{
			if (index < count && night[index])
			{
				length++;
			}
			else if (length > 0)
			{
				int start = index - length;
				stretches.Add(new NightStretch(geometry.firstColumn + start, length, whole: start > 0 && index < count));
				length = 0;
			}
		}
		return stretches;
	}

	// Two tiles half a night zone's share of the ring apart (1/16 with 8 night zones, 1/40 with 20) are 12 hours apart
	// in local time, so one has day while the other has night.
	private static void CheckNightZonesOpposite(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		int row = geometry.tilesAcross / 2;
		int apart = geometry.tilesAround / (2 * geometry.nightZones);
		int ticks = RingLocalTime.NowTicksAbs();
		foreach (int column in geometry.LiveColumns())
		{
			int tile = geometry.TileId(row, column);
			int other = geometry.TileId(row, column + apart);
			if (other < 0)
			{
				continue;
			}
			float hours = GenDate.HourFloat(ticks, ring.LongLatOf(other).x) - GenDate.HourFloat(ticks, ring.LongLatOf(tile).x);
			float difference = GenMath.PositiveMod(hours, 24f);
			if (Math.Abs(difference - 12f) > OppositeHoursTolerance)
			{
				result.FailTile(tile);
			}
		}
	}

	// Before play with a starting tile picked (the check does nothing in a running game, where the framework has no way
	// to report a skip), the time the night shadow and shadow squares show is 6 am local time at that tile, as vanilla's
	// globe shows it on the starting-site page, whatever the season (an unpicked one included). With no tile picked
	// vanilla's cache still holds its initial -1, which the mod shows as vanilla does, so that case isn't checked. The
	// expected hour is 6 from vanilla's own clock maths, not from the start tick the mod reads. It reads the starting tile
	// and season as they are and sets nothing, as this may run off the main thread while the page reads them; so it covers
	// whichever tile and season are picked when it runs.
	private static void CheckStartTimeBeforePlay(RingLayer ring, RingCheckResult result)
	{
		if (Current.ProgramState == ProgramState.Playing || Find.GameInitData is not { } initData || !initData.gameToLoad.NullOrEmpty() || !initData.startingTile.Valid)
		{
			return;
		}
		PlanetTile tile = initData.startingTile;
		float hour = GenDate.HourFloat(RingLocalTime.NowTicksAbs(), tile.Layer.LongLatOf(tile.tileId).x);
		if (Math.Abs(hour - 6f) > 0.01f)
		{
			result.Fail($"before play the starting tile's local hour is {hour:F2}, not 6 (tile {tile}, season {initData.startingSeason})");
		}
	}
}
