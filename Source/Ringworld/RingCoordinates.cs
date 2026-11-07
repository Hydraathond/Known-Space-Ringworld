using System;
using Verse;

namespace Ringworld;

// A ring tile's position as shown to the player (see RingGeometry.CoordinatesOf): degrees spinward (S) or
// anti-spinward (A) from 0°, and parallels (144 from the midline to each rim line) towards sinister (Sn) or dexter (Dx), or ∥0 on the midline.
// Display only: nothing reads it back.
public readonly struct RingCoordinates
{
	// Signed: positive spinward, in -180..180.
	public readonly double degreesAround;

	// Signed: positive towards dexter.
	public readonly double parallelsAcross;

	public RingCoordinates(double degreesAround, double parallelsAcross)
	{
		this.degreesAround = degreesAround;
		this.parallelsAcross = parallelsAcross;
	}

	// Rounded as shown, so the letter goes by the shown value: anything that rounds to 0.00° or 180.00° has none.
	private double DegreesRounded => Math.Round(Math.Abs(degreesAround), 2, MidpointRounding.AwayFromZero);

	private string DegreesShown => DegreesRounded.ToString("F2");

	private bool NoAroundDirection => DegreesRounded == 0.0 || DegreesRounded == 180.0;

	// Rounded as shown, so anything that rounds to 0.00 is on the midline, as for degrees.
	private double ParallelsRounded => Math.Round(Math.Abs(parallelsAcross), 2, MidpointRounding.AwayFromZero);

	private bool OnMidline => ParallelsRounded == 0.0;

	private string ParallelsShown => ParallelsRounded.ToString("F2");

	// e.g. "134.23°S, 43.10∥Dx", for the world inspect pane.
	public string ToShortString()
	{
		return Format(AroundKey("RW_RingAround_Spinward", "RW_RingAround_AntiSpinward", "RW_RingAround_NoDirection"), AcrossKey("RW_RingAcross_Dexter", "RW_RingAcross_Sinister", "RW_RingAcross_Midline"));
	}

	// e.g. "134.23° spinward, 43.10 parallels dexter", for messages.
	public string ToSpelledOutString()
	{
		return Format(AroundKey("RW_RingAround_Spinward_SpelledOut", "RW_RingAround_AntiSpinward_SpelledOut", "RW_RingAround_NoDirection_SpelledOut"), AcrossKey("RW_RingAcross_Dexter_SpelledOut", "RW_RingAcross_Sinister_SpelledOut", "RW_RingAcross_Midline_SpelledOut"));
	}

	private string AroundKey(string spinward, string antiSpinward, string noDirection)
	{
		return NoAroundDirection ? noDirection : degreesAround > 0.0 ? spinward : antiSpinward;
	}

	private string AcrossKey(string dexter, string sinister, string midline)
	{
		return OnMidline ? midline : parallelsAcross > 0.0 ? dexter : sinister;
	}

	private string Format(string aroundKey, string acrossKey)
	{
		return "RW_RingCoordinates".Translate(aroundKey.Translate(DegreesShown), acrossKey.Translate(ParallelsShown, "RW_RingParallelSymbol".Translate())).Resolve();
	}
}
