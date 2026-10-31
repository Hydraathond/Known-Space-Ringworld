using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Picks the wall-foot tiles that are spill mountains: about one every SpacingHexes along each rim, jittered by
// JitterPercent, port and starboard drawn independently. A spot that falls on water (no wall foot there) is skipped,
// not moved. Runs right after RW_WallFoot, with vanilla's world-seeded random for this step.
// The tiles are saved with the ring (RingLayer.SpillMountainTiles), so this only runs on generation.
public class WorldGenStep_SpillMountains : WorldGenStep
{
	public const int SpacingHexes = 150;

	public const int JitterPercent = 30;

	public const int MinGap = SpacingHexes * (100 - JitterPercent) / 100;

	public const int MaxGap = SpacingHexes * (100 + JitterPercent) / 100;

	public override int SeedPart => 730561942;

	public override void GenerateFresh(string seed, PlanetLayer layer)
	{
		if (layer is not RingLayer { IsSurfaceRing: true } ring || ring.Geometry == null)
		{
			return;
		}
		if (!ring.SpillMountainsOn)
		{
			ring.SetSpillMountains(new List<int>());
			return;
		}
		RingGeometry geometry = ring.Geometry;
		List<int> tiles = new List<int>();
		foreach (int row in geometry.RimRows)
		{
			foreach (int column in geometry.IsFullLoop ? SpotsRoundLoop(geometry.tilesAround) : SpotsAlongArc(geometry))
			{
				int id = geometry.TileId(row, column);
				if (id >= 0 && ring.IsWallFootTile(id))
				{
					tiles.Add(id);
				}
			}
		}
		ring.SetSpillMountains(tiles);
	}

	// From a random start within the first spacing, each next spot a jittered gap on, to the end of the arc.
	private static IEnumerable<int> SpotsAlongArc(RingGeometry geometry)
	{
		for (int column = geometry.firstColumn + Rand.Range(0, SpacingHexes); column <= geometry.LastColumn; column += Rand.RangeInclusive(MinGap, MaxGap))
		{
			yield return column;
		}
	}

	// A walk like the arc's wouldn't close: its last gap, back round to the first spot, could be any length. Instead the
	// loop is split into as many equal gaps as are nearest the spacing, and each spot is jittered about its share by
	// at most what keeps every gap, rounding included, within MinGap..MaxGap. Rings are at least
	// RingSize.MinTilesAround round, so the equal gap is always well inside that range.
	private static IEnumerable<int> SpotsRoundLoop(int tilesAround)
	{
		int count = Mathf.Max(1, Mathf.RoundToInt((float)tilesAround / SpacingHexes));
		float gap = (float)tilesAround / count;
		float jitter = Mathf.Max(0f, Mathf.Min(SpacingHexes * JitterPercent / 200f, (gap - MinGap) / 2f, (MaxGap - gap) / 2f) - 0.5f);
		float start = Rand.Range(0f, gap);
		for (int i = 0; i < count; i++)
		{
			yield return Mathf.RoundToInt(start + i * gap + Rand.Range(-jitter, jitter)) % tilesAround;
		}
	}
}
