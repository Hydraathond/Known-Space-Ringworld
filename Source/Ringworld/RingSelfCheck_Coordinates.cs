using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Ring-coordinates checks: sample tiles on fixed ring sizes format to known strings. That the inspect pane and the
// pollution message show them is checked by eye.
public static partial class RingSelfCheck
{
	// 0.2 degrees per column; 21 across has a midline row (10), 20 across doesn't. Parallels: row distance / rim line * 144.
	private static readonly RingGeometry OddCoordinatesRing = new RingGeometry(new RingSize(1800, 21), RingSize.FullLandCoverage, RingSize.V1ShadowSquares);

	private static readonly RingGeometry EvenCoordinatesRing = new RingGeometry(new RingSize(1800, 20), RingSize.FullLandCoverage, RingSize.V1ShadowSquares);

	// Rows 0 and the last are the wall foot; odd rows are shifted half a column spinward.
	private static readonly (RingGeometry geometry, int row, int column, string shortForm, string spelledOut)[] CoordinateSamples =
	{
		(OddCoordinatesRing, 10, 0, "0.00°, ∥0", "0.00°, on the midline"),
		(OddCoordinatesRing, 10, 900, "180.00°, ∥0", "180.00°, on the midline"),
		(OddCoordinatesRing, 0, 1, "0.20°S, 139.35∥Sn", "0.20° spinward, 139.35 parallels sinister"),
		(OddCoordinatesRing, 20, 1799, "0.20°A, 139.35∥Dx", "0.20° anti-spinward, 139.35 parallels dexter"),
		(OddCoordinatesRing, 11, 0, "0.10°S, 13.94∥Dx", "0.10° spinward, 13.94 parallels dexter"),
		(OddCoordinatesRing, 9, 1349, "90.10°A, 13.94∥Sn", "90.10° anti-spinward, 13.94 parallels sinister"),
		(OddCoordinatesRing, 0, 449, "89.80°S, 139.35∥Sn", "89.80° spinward, 139.35 parallels sinister"),
		(EvenCoordinatesRing, 9, 0, "0.10°S, 7.32∥Sn", "0.10° spinward, 7.32 parallels sinister"),
		(EvenCoordinatesRing, 10, 0, "0.00°, 7.32∥Dx", "0.00°, 7.32 parallels dexter"),
		(EvenCoordinatesRing, 19, 900, "179.90°A, 139.12∥Dx", "179.90° anti-spinward, 139.12 parallels dexter"),
		(EvenCoordinatesRing, 0, 0, "0.00°, 139.12∥Sn", "0.00°, 139.12 parallels sinister"),
	};

	// The expected strings are English, so they're only compared when the game is in English.
	private static void CheckCoordinateSamples(RingLayer ring, RingCheckResult result)
	{
		if (LanguageDatabase.activeLanguage != LanguageDatabase.defaultLanguage)
		{
			return;
		}
		foreach ((RingGeometry geometry, int row, int column, string shortForm, string spelledOut) in CoordinateSamples)
		{
			RingCoordinates coordinates = geometry.CoordinatesOf(geometry.TileId(row, column));
			string shown = coordinates.ToShortString();
			string written = coordinates.ToSpelledOutString();
			if (shown != shortForm || written != spelledOut)
			{
				result.Fail($"row {row}, column {column} of {geometry.tilesAround} x {geometry.tilesAcross} shows \"{shown}\" / \"{written}\", expected \"{shortForm}\" / \"{spelledOut}\"");
			}
		}
	}

	// A ring wide enough that its rim rows should read between 140 and 144 (the rim line is 144; rim rows sit half a
	// corner radius inside it).
	private static readonly RingGeometry WideCoordinatesRing = new RingGeometry(new RingSize(1800, 31), RingSize.FullLandCoverage, RingSize.V1ShadowSquares);

	// What a wide ring's rim rows must at least read.
	private const double WideRimRowMinimum = 140.0;

	// Language-independent: the rim rows read between 140 and 144, values mirror about the midline, and the dexter rim row
	// (positive) is the one the heading checks call North.
	private static void CheckCoordinateParallels(RingLayer ring, RingCheckResult result)
	{
		foreach (RingGeometry geometry in new[] { WideCoordinatesRing, ring.Geometry })
		{
			int last = geometry.tilesAcross - 1;
			double dexterRim = geometry.CoordinatesOf(geometry.TileId(last, 0)).parallelsAcross;
			double sinisterRim = geometry.CoordinatesOf(geometry.TileId(0, 0)).parallelsAcross;
			if (dexterRim >= RingGeometry.ParallelsToRimLine || sinisterRim <= -RingGeometry.ParallelsToRimLine
				|| (geometry == WideCoordinatesRing && (dexterRim <= WideRimRowMinimum || sinisterRim >= -WideRimRowMinimum)))
			{
				result.Fail($"{geometry.tilesAround} x {geometry.tilesAcross}: rim rows read {sinisterRim:F2} and {dexterRim:F2}, expected within 140..144 (wide ring) and strictly inside 144");
			}
			for (int row = 0; row <= last; row++)
			{
				double here = geometry.CoordinatesOf(geometry.TileId(row, 0)).parallelsAcross;
				double mirror = geometry.CoordinatesOf(geometry.TileId(last - row, 0)).parallelsAcross;
				if (System.Math.Abs(here + mirror) > 1E-06)
				{
					result.Fail($"{geometry.tilesAround} x {geometry.tilesAcross}: row {row} reads {here:F4} but row {last - row} reads {mirror:F4}, expected opposites");
					break;
				}
			}
			if (dexterRim <= 0.0)
			{
				result.Fail($"{geometry.tilesAround} x {geometry.tilesAcross}: the dexter rim row reads {dexterRim:F2}, expected positive");
			}
		}
		CheckDexterRimIsNorth(ring, result);
	}

	// The row the coordinates call dexter is the one the live headings call North: from a tile in the row below the dexter
	// rim, a neighbour in the rim row lies North.
	private static void CheckDexterRimIsNorth(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		int last = geometry.tilesAcross - 1;
		int column = geometry.firstColumn + geometry.liveTilesAround / 2;
		PlanetTile tile = new PlanetTile(geometry.TileId(last - 1, column), ring);
		List<PlanetTile> neighbors = new List<PlanetTile>();
		ring.GetTileNeighbors(tile.tileId, neighbors);
		foreach (PlanetTile neighbor in neighbors)
		{
			if (geometry.RowOf(neighbor.tileId) == last && Find.WorldGrid.GetRotFromTo(tile, neighbor) != Rot4.North)
			{
				result.Fail($"the dexter rim row (row {last}) is not North of row {last - 1} in the live headings");
				return;
			}
		}
	}
}
