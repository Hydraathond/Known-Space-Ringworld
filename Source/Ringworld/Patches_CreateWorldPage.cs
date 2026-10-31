using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Ringworld;

// The Create World page's first opening, and "Reset all", set the world shape rows to the last choice. Every change on
// them is saved as the last choice at once, so "Reset all" leaves them as they are.
[HarmonyPatch(typeof(Page_CreateWorldParams), nameof(Page_CreateWorldParams.Reset))]
public static class Patch_Page_CreateWorldParams_Reset
{
	public static void Postfix()
	{
		WorldShape.Reset();
	}
}

// Closing the Create World page (going back, or once its world is generated) ends this setup's shape rows. The page
// doesn't override PostClose, so Window's own is patched.
[HarmonyPatch(typeof(Window), nameof(Window.PostClose))]
public static class Patch_Window_PostClose
{
	public static void Postfix(Window __instance)
	{
		if (__instance is Page_CreateWorldParams)
		{
			WorldShape.ShapeRowsShown = false;
		}
	}
}

// Adds a World shape row above the planet-coverage row. With Ring picked, the ring size row follows it and vanilla's
// planet coverage row is swapped for the ring's land coverage row, below the shape rows; with Planet, the coverage
// row draws as in vanilla, one row lower.
//
// Vanilla's coverage row runs from the Label(new Rect(0, y, 200, 30), "PlanetCoverage".Translate()) call up to and
// including TooltipHandler.TipRegionByKey(..., "PlanetCoverageTip"), followed by "y += 40". It is wrapped as:
//   if (!CreateWorldPageRows.DoShapeRows(ref y, width)) { vanilla coverage row }
//
// My Little Planet inserts its planet size slider right after the coverage tooltip, where the ring rows' branch lands.
// Running first, whatever the load order, keeps the branch's target on vanilla's "y += 40", so the slider is drawn with
// a planet's coverage row and skipped under the ring rows, where the ring's size row sets its size.
[HarmonyPatch(typeof(Page_CreateWorldParams), nameof(Page_CreateWorldParams.DoWindowContents))]
[HarmonyBefore(MyLittlePlanetHarmonyId)]
public static class Patch_Page_CreateWorldParams_DoWindowContents
{
	private const string MyLittlePlanetHarmonyId = "net.quicksilverfox.rimworld.mod.worldgenrules";

	private static readonly ConstructorInfo RectCtor = AccessTools.Constructor(typeof(Rect), new[] { typeof(float), typeof(float), typeof(float), typeof(float) });

	private static readonly MethodInfo TipRegionByKey = AccessTools.Method(typeof(TooltipHandler), nameof(TooltipHandler.TipRegionByKey), new[] { typeof(Rect), typeof(string) });

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
		if (!TryFindCoverageRow(codes, out int start, out int resume, out object yLocal, out object widthLocal))
		{
			Log.Error("[Ringworld] Couldn't find the planet-coverage row on the Create World page; the world shape toggle is missing, so new worlds are vanilla planets.");
			WorldShape.ringByDefault = false;
			WorldShape.Ring = false;
			return codes;
		}
		Label skip = generator.DefineLabel();
		codes[resume].labels.Add(skip);
		CodeInstruction first = new CodeInstruction(OpCodes.Ldloca, yLocal);
		first.labels.AddRange(codes[start].labels);
		codes[start].labels.Clear();
		codes.InsertRange(start, new[]
		{
			first,
			new CodeInstruction(OpCodes.Ldloc, widthLocal),
			new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CreateWorldPageRows), nameof(CreateWorldPageRows.DoShapeRows))),
			new CodeInstruction(OpCodes.Brtrue, skip),
		});
		return codes;
	}

	// start: the first instruction of the row's label (ldc.r4 0, ldloc y, ldc.r4 200, ldc.r4 30, newobj Rect).
	// resume: the first instruction after the tooltip call. widthLocal: read from the button's
	// new Rect(200, y, width, 30), the first "ldloc y" after the label.
	private static bool TryFindCoverageRow(List<CodeInstruction> codes, out int start, out int resume, out object yLocal, out object widthLocal)
	{
		start = resume = -1;
		yLocal = widthLocal = null;
		int labelText = codes.FindIndex(c => c.Is(OpCodes.Ldstr, "PlanetCoverage"));
		if (labelText < 5 || !codes[labelText - 1].Is(OpCodes.Newobj, RectCtor) || !codes[labelText - 4].IsLdloc())
		{
			return false;
		}
		start = labelText - 5;
		yLocal = codes[labelText - 4].operand;
		int tip = codes.FindIndex(labelText, c => c.Is(OpCodes.Ldstr, "PlanetCoverageTip"));
		if (tip < 0 || tip + 2 >= codes.Count || !codes[tip + 1].Calls(TipRegionByKey))
		{
			return false;
		}
		resume = tip + 2;
		for (int i = labelText; i < tip; i++)
		{
			if (codes[i].IsLdloc() && Equals(codes[i].operand, yLocal) && codes[i + 1].IsLdloc())
			{
				widthLocal = codes[i + 1].operand;
				break;
			}
		}
		return yLocal != null && widthLocal != null;
	}
}

// The rows the Create World page gets for the world shape, laid out like vanilla's: a 200-wide label, then the
// control in the rest of the column, 40 apart.
public static class CreateWorldPageRows
{
	private const float LabelWidth = 200f;

	private const float RowHeight = 30f;

	private const float RowStep = 40f;

	private const float AdvancedButtonWidth = 110f;

	// The narrowest control width the ring size row gets on either page (vanilla's, about 283 at its widest window;
	// Worldbuilder's is about 330), which the short labels must fit (RingSelfCheck_Labels).
	internal const float RingSizeMinControlWidth = 280f;

	// The ring size button's width in a row whose control is width wide: the Advanced button and a gap sit beside it.
	internal static float RingSizeButtonWidth(float width)
	{
		return width - AdvancedButtonWidth - 10f;
	}

	// Draws the World shape row and moves y past it. With Ring picked, also draws the ring size row and the land
	// coverage row below it and returns true, so the caller skips vanilla's coverage row (the caller still moves y past
	// the last row drawn).
	public static bool DoShapeRows(ref float y, float width)
	{
		WorldShape.ShapeRowsShown = true;
		DoShapeRow(0f, y, LabelWidth, width);
		y += RowStep;
		if (!WorldShape.Ring)
		{
			return false;
		}
		DoRingSizeRow(0f, y, LabelWidth, width, "RW_RingSizeWithLiveTiles".Translate(WorldShape.LiveTileCount.ToString("N0")));
		y += RowStep;
		DoLandCoverageRow(y, width);
		return true;
	}

	// The shape and ring size rows draw from x: a labelWidth-wide label, then the control, width wide. Worldbuilder's
	// page draws them too (WorldbuilderPageRows).
	internal static void DoShapeRow(float x, float y, float labelWidth, float width)
	{
		Widgets.Label(new Rect(x, y, labelWidth, RowHeight), "RW_WorldShape".Translate());
		float half = width / 2f;
		if (Widgets.RadioButtonLabeled(new Rect(x + labelWidth, y, half - 10f, RowHeight), "RW_WorldShape_Ring".Translate(), WorldShape.Ring) && !WorldShape.Ring)
		{
			WorldShape.Ring = true;
			WorldShape.SaveLastChoice();
			SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
		}
		if (Widgets.RadioButtonLabeled(new Rect(x + labelWidth + half + 10f, y, half - 10f, RowHeight), "RW_WorldShape_Planet".Translate(), !WorldShape.Ring) && WorldShape.Ring)
		{
			WorldShape.Ring = false;
			WorldShape.SaveLastChoice();
			SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
		}
		TooltipHandler.TipRegionByKey(new Rect(x, y, labelWidth + width, RowHeight), "RW_WorldShapeTip");
	}

	// The vanilla page's label carries the live tile count, which follows the land coverage row too.
	internal static void DoRingSizeRow(float x, float y, float labelWidth, float width, string label)
	{
		Widgets.Label(new Rect(x, y, labelWidth, RowHeight), label);
		Rect button = new Rect(x + labelWidth, y, RingSizeButtonWidth(width), RowHeight);
		if (Widgets.ButtonText(button, ButtonLabel()))
		{
			List<FloatMenuOption> options = new List<FloatMenuOption>();
			foreach (RingSizePreset preset in WorldShape.Presets)
			{
				options.Add(new FloatMenuOption(preset.LabelWithSize, delegate
				{
					WorldShape.RingSize = preset.size;
					WorldShape.SaveLastChoice();
				}));
			}
			Find.WindowStack.Add(new FloatMenu(options));
		}
		TooltipHandler.TipRegionByKey(new Rect(x, y, button.xMax - x, RowHeight), "RW_RingSizeTip");
		if (Widgets.ButtonText(new Rect(x + labelWidth + width - AdvancedButtonWidth, y, AdvancedButtonWidth, RowHeight), "RW_RingSizeAdvanced".Translate()))
		{
			Find.WindowStack.Add(new Dialog_RingSize());
		}
	}

	// The ring size button shows only the short label, so it fits; the dropdown has the full names with sizes.
	private static string ButtonLabel()
	{
		return WorldShape.CurrentPreset?.ShortLabel ?? "RW_RingSizeCustomShort".Translate();
	}

	// Laid out like vanilla's planet coverage row, offering RingSize.LandCoverages.
	private static void DoLandCoverageRow(float y, float width)
	{
		Widgets.Label(new Rect(0f, y, LabelWidth, RowHeight), "RW_LandCoverage".Translate());
		Rect button = new Rect(LabelWidth, y, width, RowHeight);
		if (Widgets.ButtonText(button, WorldShape.LandCoverage.ToStringPercent()))
		{
			List<FloatMenuOption> options = new List<FloatMenuOption>();
			foreach (float coverage in RingSize.LandCoverages)
			{
				options.Add(new FloatMenuOption(coverage.ToStringPercent(), delegate
				{
					WorldShape.LandCoverage = coverage;
					WorldShape.SaveLastChoice();
				}));
			}
			Find.WindowStack.Add(new FloatMenu(options));
		}
		TooltipHandler.TipRegionByKey(new Rect(0f, y, button.xMax, RowHeight), "RW_LandCoverageTip");
	}
}

// Generate on a ring with over RingSize.LargeTileCount live tiles first asks for confirmation. Confirming runs
// vanilla's CanDoNext,
// which queues the world generation; cancelling leaves the page as it was.
[HarmonyPatch(typeof(Page_CreateWorldParams), "CanDoNext")]
public static class Patch_Page_CreateWorldParams_CanDoNext
{
	private static readonly System.Func<Page_CreateWorldParams, bool> CanDoNext = AccessTools.MethodDelegate<System.Func<Page_CreateWorldParams, bool>>(AccessTools.Method(typeof(Page_CreateWorldParams), "CanDoNext"));

	// Set while re-running CanDoNext after the player confirmed, so it goes through.
	private static bool bypassConfirmation;

	public static bool Prefix(Page_CreateWorldParams __instance, ref bool __result)
	{
		if (bypassConfirmation || !WorldShape.Ring || !WorldShape.IsLarge)
		{
			return true;
		}
		Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RW_ConfirmLargeRing".Translate(WorldShape.LiveTileCount.ToString("N0"), RingSize.LargeTileCount.ToString("N0")), delegate
		{
			bypassConfirmation = true;
			try
			{
				CanDoNext(__instance);
			}
			finally
			{
				bypassConfirmation = false;
			}
		}));
		__result = false;
		return false;
	}
}
