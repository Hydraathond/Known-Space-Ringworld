using System.Linq;
using HarmonyLib;
using Verse;

namespace Ringworld;

// LAO's Layer tab, which describes the world in the world inspect pane (LaoLayerTabText).
public static partial class RingSelfCheck
{
	// The paragraph after LAO's planet description as it ships, in English. Other mods may append more after it.
	private const string LaoGravityWellLine = "Gravity well elevation: 1000 km.";

	// With LAO, its tab carries the swap's prefix and finalizer, the swap puts the ring's Surface text and world text
	// (followed by LAO's planet text after its first paragraph: its gravity-well line, and what other mods append) in LAO's two defs, then LAO's own back. Without LAO, nothing is
	// patched. The tab itself only draws in the GUI, so the swap is run directly.
	private static void CheckLaoLayerTabText(RingLayer ring, RingCheckResult result)
	{
		if (!LaoIntegration.Active)
		{
			if (LaoLayerTabText.FillTab != null)
			{
				result.Fail("without LAO, the Layer tab swap was applied");
			}
			return;
		}
		if (LaoLayerTabText.FillTab == null)
		{
			result.Fail("LAO's Layer tab, its Surface group or the group's planet wasn't found");
			return;
		}
		Patches patches = Harmony.GetPatchInfo(LaoLayerTabText.FillTab);
		bool prefixed = patches?.Prefixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(LaoLayerTabText)) == true;
		bool finalized = patches?.Finalizers.Any(patch => patch.PatchMethod.DeclaringType == typeof(LaoLayerTabText)) == true;
		if (!prefixed || !finalized)
		{
			result.Fail("LAO's Layer tab doesn't carry the swap's prefix and finalizer");
		}
		string ringWorldText = LaoLayerTabText.WorldTextKey.Translate();
		foreach ((string lao, string expected) in new[]
		{
			("A planet.\n\n" + LaoGravityWellLine, ringWorldText + "\n\n" + LaoGravityWellLine),
			("A planet. More.\n\n" + LaoGravityWellLine + "\n\nA mod-name tag.", ringWorldText + "\n\n" + LaoGravityWellLine + "\n\nA mod-name tag."),
			("A planet with no gravity-well line.", ringWorldText),
		})
		{
			string got = LaoLayerTabText.RingPlanetText(lao);
			if (got != expected)
			{
				result.Fail($"from LAO's planet text \"{lao}\", the ring's is \"{got}\", not \"{expected}\"");
			}
		}
		Def group = LaoLayerTabText.SurfaceGroup;
		Def planet = LaoLayerTabText.Planet;
		string laoSurfaceText = group.description;
		string laoPlanetText = planet.description;
		if (LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage && laoPlanetText?.Contains("\n\n" + LaoGravityWellLine) != true)
		{
			result.Fail($"LAO's planet text \"{laoPlanetText}\" no longer has its gravity-well line as a paragraph of its own");
		}
		if (!LaoLayerTabText.SwapIn())
		{
			result.Fail("on a ring world the swap didn't run");
			return;
		}
		try
		{
			if (group.description != LaoLayerTabText.SurfaceTextKey.Translate())
			{
				result.Fail($"while swapped, the Surface group's text is \"{group.description}\", not the ring's");
			}
			if (planet.description != LaoLayerTabText.RingPlanetText(laoPlanetText))
			{
				result.Fail($"while swapped, the planet's text is \"{planet.description}\", not the ring's");
			}
		}
		finally
		{
			LaoLayerTabText.Restore();
		}
		if (group.description != laoSurfaceText || planet.description != laoPlanetText)
		{
			result.Fail("after the swap, LAO's own texts aren't back");
		}
	}
}
