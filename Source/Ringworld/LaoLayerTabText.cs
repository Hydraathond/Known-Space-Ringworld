using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Ringworld;

// LAO's Layer tab in the world inspect pane describes the world as a planet: its Surface group's description ("the
// outermost shell of the planet") and the group's planet's (LAO's Rimworld). On a ring world, while the tab draws, those two defs carry
// the ring's own texts instead, and LAO's are put back as soon as it's done, so nothing else ever sees the ring texts
// and the defs' group membership (the orbit view's visibility, LAO's site finder) is untouched. Only the first paragraph
// of LAO's planet text, its description of a planet, is replaced: the rest (its gravity-well line, and whatever other
// mods append, such as a mod-name tag) stays under the ring's text. Planet worlds keep LAO's texts.
// Only applied when LAO is loaded; if its tab or defs aren't found, nothing is patched (LaoIntegration logs one warning).
internal static class LaoLayerTabText
{
	private const string TabTypeName = "LayeredAtmosphereOrbit.WITab_PlanetLayer";

	private const string GroupTypeName = "LayeredAtmosphereOrbit.PlanetLayerGroupDef";

	private const string ParagraphBreak = "\n\n";

	internal const string SurfaceTextKey = "RW_LaoLayerTab_SurfaceDescription";

	internal const string WorldTextKey = "RW_LaoLayerTab_WorldDescription";

	// LAO's Surface group, and the planet def it belongs to (LAO's Rimworld, unless another mod moved it), as the tab
	// reads them; null until Apply has found both.
	internal static Def SurfaceGroup { get; private set; }

	internal static Def Planet { get; private set; }

	// LAO's tab method the swap is patched on; null until Apply has found it.
	internal static MethodInfo FillTab { get; private set; }

	// LAO's own descriptions of the two defs while the ring's are in them; null otherwise.
	private static string laoSurfaceText;

	private static string laoPlanetText;

	private static bool swapped;

	// Null if the tab and both defs were found and the swap patched on, otherwise what's missing.
	public static string Apply(Harmony harmony)
	{
		Type tab = AccessTools.TypeByName(TabTypeName);
		MethodInfo fillTab = tab == null ? null : AccessTools.Method(tab, "FillTab");
		if (fillTab == null)
		{
			return $"{TabTypeName}.FillTab not found";
		}
		Type groupType = AccessTools.TypeByName(GroupTypeName);
		Def group = groupType == null ? null : GenDefDatabase.GetDefSilentFail(groupType, LaoIntegration.SurfaceGroup, specialCaseForSoundDefs: false);
		if (group == null)
		{
			return $"{LaoIntegration.SurfaceGroup} not found";
		}
		Def planet = AccessTools.Field(groupType, "planet")?.GetValue(group) as Def;
		if (planet == null)
		{
			return $"{LaoIntegration.SurfaceGroup} has no planet";
		}
		SurfaceGroup = group;
		Planet = planet;
		FillTab = fillTab;
		harmony.Patch(fillTab, prefix: new HarmonyMethod(typeof(LaoLayerTabText), nameof(ShowRingTexts)), finalizer: new HarmonyMethod(typeof(LaoLayerTabText), nameof(RestoreLaoTexts)));
		return null;
	}

	// Returns void, not bool: a prefix returning false would skip LAO's FillTab.
	private static void ShowRingTexts()
	{
		SwapIn();
	}

	private static Exception RestoreLaoTexts(Exception __exception)
	{
		Restore();
		return __exception;
	}

	// Puts the ring's texts in the two defs, on a ring world only. Whether it did.
	internal static bool SwapIn()
	{
		if (swapped || SurfaceGroup == null || !SurfaceRing.InRingWorld)
		{
			return false;
		}
		laoSurfaceText = SurfaceGroup.description;
		laoPlanetText = Planet.description;
		SurfaceGroup.description = SurfaceTextKey.Translate();
		Planet.description = RingPlanetText(laoPlanetText);
		swapped = true;
		return true;
	}

	// Puts LAO's own texts back, if the ring's are in.
	internal static void Restore()
	{
		if (!swapped)
		{
			return;
		}
		SurfaceGroup.description = laoSurfaceText;
		Planet.description = laoPlanetText;
		laoSurfaceText = laoPlanetText = null;
		swapped = false;
	}

	// The ring's world text, followed by every paragraph of LAO's planet text after its first.
	internal static string RingPlanetText(string laoText)
	{
		string ringText = WorldTextKey.Translate();
		int firstBreak = laoText?.IndexOf(ParagraphBreak, StringComparison.Ordinal) ?? -1;
		return firstBreak < 0 ? ringText : ringText + laoText.Substring(firstBreak);
	}
}
