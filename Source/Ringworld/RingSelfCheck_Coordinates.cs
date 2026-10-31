using Verse;

namespace Ringworld;

// Ring-coordinates checks: sample tiles on fixed ring sizes format to known strings. That the inspect pane and the
// pollution message show them is checked by eye.
public static partial class RingSelfCheck
{
	// 0.2 degrees per column; 21 across has a midline row (10), 20 across doesn't.
	private static readonly RingGeometry OddCoordinatesRing = new RingGeometry(new RingSize(1800, 21), RingSize.FullLandCoverage, RingSize.V1ShadowSquares);

	private static readonly RingGeometry EvenCoordinatesRing = new RingGeometry(new RingSize(1800, 20), RingSize.FullLandCoverage, RingSize.V1ShadowSquares);

	// Rows 0 and the last are the wall foot; odd rows are shifted half a column spinward.
	private static readonly (RingGeometry geometry, int row, int column, string shortForm, string spelledOut)[] CoordinateSamples =
	{
		(OddCoordinatesRing, 10, 0, "0.00°, M", "0.00°, on the midline"),
		(OddCoordinatesRing, 10, 900, "180.00°, M", "180.00°, on the midline"),
		(OddCoordinatesRing, 0, 1, "0.20°S, 217P", "0.20° spinward, 217 km port"),
		(OddCoordinatesRing, 20, 1799, "0.20°A, 217Sb", "0.20° anti-spinward, 217 km starboard"),
		(OddCoordinatesRing, 11, 0, "0.10°S, 22Sb", "0.10° spinward, 22 km starboard"),
		(OddCoordinatesRing, 9, 1349, "90.10°A, 22P", "90.10° anti-spinward, 22 km port"),
		(OddCoordinatesRing, 0, 449, "89.80°S, 217P", "89.80° spinward, 217 km port"),
		(EvenCoordinatesRing, 9, 0, "0.10°S, 11P", "0.10° spinward, 11 km port"),
		(EvenCoordinatesRing, 10, 0, "0.00°, 11Sb", "0.00°, 11 km starboard"),
		(EvenCoordinatesRing, 19, 900, "179.90°A, 206Sb", "179.90° anti-spinward, 206 km starboard"),
		(EvenCoordinatesRing, 0, 0, "0.00°, 206P", "0.00°, 206 km port"),
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
}
