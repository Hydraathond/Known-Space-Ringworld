using System;
using Verse;

namespace Ringworld;

// A ring tile's position as shown to the player (see RingGeometry.CoordinatesOf): degrees spinward (S) or
// anti-spinward (A) from 0°, and whole kilometres from the midline towards port (P) or starboard (Sb), or M on it.
// Display only: nothing reads it back.
public readonly struct RingCoordinates
{
	// Signed: positive spinward, in -180..180.
	public readonly double degreesAround;

	// Signed: positive towards starboard.
	public readonly double kmAcross;

	public RingCoordinates(double degreesAround, double kmAcross)
	{
		this.degreesAround = degreesAround;
		this.kmAcross = kmAcross;
	}

	// Rounded as shown, so the letter goes by the shown value: anything that rounds to 0.00° or 180.00° has none.
	private double DegreesRounded => Math.Round(Math.Abs(degreesAround), 2, MidpointRounding.AwayFromZero);

	private string DegreesShown => DegreesRounded.ToString("F2");

	private bool NoAroundDirection => DegreesRounded == 0.0 || DegreesRounded == 180.0;

	// Only a row exactly on the midline (odd tiles across) is M; rows half a row off it show their 11 km.
	private bool OnMidline => Math.Abs(kmAcross) < 1E-06;

	private string KmShown => Math.Round(Math.Abs(kmAcross), MidpointRounding.AwayFromZero).ToString("F0");

	// e.g. "134.23°S, 43Sb", for the world inspect pane.
	public string ToShortString()
	{
		return Format(AroundKey("RW_RingAround_Spinward", "RW_RingAround_AntiSpinward", "RW_RingAround_NoDirection"), AcrossKey("RW_RingAcross_Starboard", "RW_RingAcross_Port", "RW_RingAcross_Midline"));
	}

	// e.g. "134.23° spinward, 43 km starboard", for messages.
	public string ToSpelledOutString()
	{
		return Format(AroundKey("RW_RingAround_Spinward_SpelledOut", "RW_RingAround_AntiSpinward_SpelledOut", "RW_RingAround_NoDirection_SpelledOut"), AcrossKey("RW_RingAcross_Starboard_SpelledOut", "RW_RingAcross_Port_SpelledOut", "RW_RingAcross_Midline_SpelledOut"));
	}

	private string AroundKey(string spinward, string antiSpinward, string noDirection)
	{
		return NoAroundDirection ? noDirection : degreesAround > 0.0 ? spinward : antiSpinward;
	}

	private string AcrossKey(string starboard, string port, string midline)
	{
		return OnMidline ? midline : kmAcross > 0.0 ? starboard : port;
	}

	private string Format(string aroundKey, string acrossKey)
	{
		return "RW_RingCoordinates".Translate(aroundKey.Translate(DegreesShown), acrossKey.Translate(KmShown)).Resolve();
	}
}
