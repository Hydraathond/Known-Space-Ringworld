using UnityEngine;

namespace Ringworld;

// A ring's size in tiles, chosen per world and saved with it. The radius always follows from it, so hexes stay vanilla-sized.
public readonly struct RingSize : System.IEquatable<RingSize>
{
	// Distance between neighbouring hex centres on a vanilla planet.
	public const float VanillaHexSize = 0.8f;

	// About a vanilla planet at 100% coverage. Rings with more live tiles get a warning and a confirmation before
	// generating.
	public const int LargeTileCount = 225000;

	// The land coverages the Create World page offers, as vanilla's planet coverage row does.
	public static readonly float[] LandCoverages = { 0.3f, 0.5f, 1f };

	// Saves from before land coverage was saved load as this.
	public const float FullLandCoverage = 1f;

	// The lowest land coverage any page offers (Worldbuilder's Coverage slider).
	public const float MinLandCoverage = 0.05f;

	public static readonly RingSize Default = new RingSize(2000, 20);

	public readonly int tilesAround;

	public readonly int tilesAcross;

	public int TileCount => tilesAround * tilesAcross;

	// The limits the Create World page offers (the Advanced dialog's sliders). Odyssey's orbit layer scales with the
	// ring (RingOrbit), so it sits inside the ring at every size.
	public const int MinTilesAround = 1500;

	public const int MaxTilesAround = 20000;

	public const int MinTilesAcross = 10;

	public const int MaxTilesAcross = 100;

	// Columns on the live arc at this land coverage: all of them at 100%, otherwise that share of the full loop.
	public int LiveTilesAround(float landCoverage)
	{
		if (landCoverage >= FullLandCoverage)
		{
			return tilesAround;
		}
		return Mathf.Clamp(Mathf.RoundToInt(tilesAround * landCoverage), 1, tilesAround);
	}

	public int LiveTileCount(float landCoverage) => LiveTilesAround(landCoverage) * tilesAcross;

	// Counts live tiles only: the rest of the loop costs nothing.
	public bool IsLargeAt(float landCoverage) => LiveTileCount(landCoverage) > LargeTileCount;

	// The shadow-square count runs from v0.1.0's 8 up to Niven's 20, one per this many tiles around the full loop, whatever
	// the land coverage.
	public const int MinShadowSquares = 8;

	public const int MaxShadowSquares = 20;

	public const int TilesAroundPerShadowSquare = 800;

	// v0.1.0's fixed count, which saves from before the count was saved load as.
	public const int V1ShadowSquares = 8;

	// The shadow-square count a new world of this size gets: Small 8, Medium 8, Large 10, Lore-accurate ratio 20. A world's
	// own count is saved with it (RingLayer.ShadowSquareCount) and never recomputed, so this formula may change freely.
	public int NewWorldShadowSquareCount => Mathf.Clamp((int)System.Math.Round((double)tilesAround / TilesAroundPerShadowSquare, System.MidpointRounding.AwayFromZero), MinShadowSquares, MaxShadowSquares);

	public RingSize ClampToLimits()
	{
		return new RingSize(Mathf.Clamp(tilesAround, MinTilesAround, MaxTilesAround), Mathf.Clamp(tilesAcross, MinTilesAcross, MaxTilesAcross));
	}

	// Follows from the full loop, whatever the land coverage, so coverage never changes how big the ring looks.
	public float Radius => tilesAround * VanillaHexSize / (2f * Mathf.PI);

	// The smallest closed ring is 3 tiles around and 1 across.
	public RingSize(int tilesAround, int tilesAcross)
	{
		this.tilesAround = Mathf.Max(3, tilesAround);
		this.tilesAcross = Mathf.Max(1, tilesAcross);
	}

	public bool Equals(RingSize other)
	{
		return tilesAround == other.tilesAround && tilesAcross == other.tilesAcross;
	}

	public override bool Equals(object obj)
	{
		return obj is RingSize other && Equals(other);
	}

	public override int GetHashCode()
	{
		return tilesAround * 397 ^ tilesAcross;
	}

	public override string ToString()
	{
		return $"{tilesAround} x {tilesAcross} = {TileCount} tiles";
	}
}
