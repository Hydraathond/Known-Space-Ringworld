using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ringworld;

// With Worldbuilder's planet generation overhaul on, its DoWindowContents prefix draws the Create World page in three
// columns instead of vanilla's, so vanilla's coverage row, where Ringworld's shape rows sit, never draws. Its centre
// column (world preview, then rows of name, seed, Scale, Coverage and population) gets them instead:
//   - the world shape row where Scale is, and with Ring the ring size row in Scale's place, so Coverage stays put;
//   - with Ring, Coverage sets the ring's land coverage over its full range and leaves the page's planet coverage
//     alone (a ring forces that to 100% anyway, Patch_WorldGenerator_GenerateWorld);
//   - with Ring, the world preview is a "No preview for ring worlds" line, and the background preview generation
//     stops, cancelling one already running.
// Patched by name from WorldbuilderCompatibility when Worldbuilder is active. With the overhaul off, Worldbuilder's
// prefix lets vanilla's page draw, with Ringworld's own rows.
public static class WorldbuilderPageRows
{
	private const string PatchType = "Worldbuilder.Page_CreateWorldParams_DoWindowContents_Patch";

	public const string CenterColumn = "DrawCenterColumn";

	public const string PreviewArea = "doWorldPreviewArea";

	public const string PreviewAutoGeneration = "HandlePreviewAutoGeneration";

	// Worldbuilder's centre-column layout: labels 120 wide with controls 5 to their right (so the rows' label width is
	// 125), rows 30 high and 40 apart, the last one at least 10 above the column's bottom.
	private const float ControlOffset = 125f;

	private const float RowHeight = 30f;

	private const float RowStep = 40f;

	private const float BottomMargin = 10f;

	// Its rows: name, seed, Scale, Coverage and population, plus one for each of these mods.
	private const int WorldbuilderRows = 5;

	private static readonly string[] ModsWithARow = { "m00nl1ght.WorldTechLevel", "OskarPotocki.VFE.Insectoid2" };

	// The ring's land coverage slider moves in whole percents, so dragging it saves the last choice once per step.
	private const float LandCoverageStep = 0.01f;

	private static readonly ConstructorInfo RectCtor = AccessTools.Constructor(typeof(Rect), new[] { typeof(float), typeof(float), typeof(float), typeof(float) });

	private static FieldInfo previewThread;

	private static FieldInfo cancelPreview;

	private static Action<Page_CreateWorldParams, Rect> previewArea;

	private static string transpilerProblem;

	public static MethodInfo Method(string name)
	{
		return AccessTools.Method(AccessTools.TypeByName(PatchType), name);
	}

	internal static void Apply(Harmony harmony, List<string> problems)
	{
		Type type = AccessTools.TypeByName(PatchType);
		MethodInfo column = Method(CenterColumn);
		MethodInfo autoGeneration = Method(PreviewAutoGeneration);
		previewThread = AccessTools.Field(type, "thread");
		cancelPreview = AccessTools.Field(type, "shouldCancelGeneration");
		if (column == null || Method(PreviewArea) == null || autoGeneration == null || previewThread == null || cancelPreview == null)
		{
			problems.Add($"{PatchType}'s centre column or preview not found");
			return;
		}
		harmony.Patch(column, transpiler: new HarmonyMethod(typeof(WorldbuilderPageRows), nameof(Transpiler)));
		if (transpilerProblem != null)
		{
			problems.Add(transpilerProblem);
		}
		harmony.Patch(autoGeneration, prefix: new HarmonyMethod(typeof(WorldbuilderPageRows), nameof(StopPreviewOnRing)));
	}

	// Each of Worldbuilder's rows starts "Text.Anchor = MiddleLeft; Widgets.Label(new Rect(rect.x, y, 120, 30),
	// key.Translate())". Scale's row runs to Coverage's, and is wrapped as:
	//   if (!WorldbuilderPageRows.DoShapeRows(ref y, rect.x, controlWidth)) { Scale row }
	// The rows' top, its first store to y, is passed through FitRowsTop, and Coverage's slider and the preview call are
	// swapped for CoverageSlider and DoPreviewArea, which take the same arguments.
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
		int scaleLabel = codes.FindIndex(c => c.Is(OpCodes.Ldstr, "WB_Scale"));
		int coverageLabel = codes.FindIndex(c => c.Is(OpCodes.Ldstr, "WB_Coverage"));
		if (!TryFindRowStart(codes, scaleLabel, null, out int scale, out object yLocal) || !TryFindRowStart(codes, coverageLabel, yLocal, out int coverage, out _))
		{
			return Fail(codes, "Scale or Coverage row not found");
		}
		CodeInstruction rectArg = codes[scale + 2];
		object widthLocal = null;
		for (int i = scaleLabel; i < coverage - 1; i++)
		{
			if (codes[i].IsLdloc() && Equals(codes[i].operand, yLocal) && codes[i + 1].IsLdloc())
			{
				widthLocal = codes[i + 1].operand;
				break;
			}
		}
		int top = codes.FindIndex(c => c.IsStloc() && Equals(c.operand, yLocal));
		int slider = codes.FindIndex(coverageLabel, c => c.Calls(AccessTools.Method(typeof(Widgets), nameof(Widgets.HorizontalSlider), new[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) })));
		int preview = codes.FindIndex(c => c.Calls(Method(PreviewArea)));
		if (widthLocal == null || top < 0 || top > scale || slider < 0 || preview < 0)
		{
			return Fail(codes, "Scale's slider width, the rows' top, Coverage's slider or the preview call not found");
		}
		codes[slider].operand = AccessTools.Method(typeof(WorldbuilderPageRows), nameof(CoverageSlider));
		codes[preview].operand = AccessTools.Method(typeof(WorldbuilderPageRows), nameof(DoPreviewArea));
		Label skip = generator.DefineLabel();
		codes[coverage].labels.Add(skip);
		CodeInstruction first = new CodeInstruction(OpCodes.Ldloca, yLocal);
		first.labels.AddRange(codes[scale].labels);
		codes[scale].labels.Clear();
		codes.InsertRange(scale, new[]
		{
			first,
			new CodeInstruction(rectArg.opcode, rectArg.operand),
			new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Rect), nameof(Rect.x))),
			new CodeInstruction(OpCodes.Ldloc, widthLocal),
			new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WorldbuilderPageRows), nameof(DoShapeRows))),
			new CodeInstruction(OpCodes.Brtrue, skip),
		});
		codes.InsertRange(top + 1, new[]
		{
			new CodeInstruction(OpCodes.Ldloc, yLocal),
			new CodeInstruction(rectArg.opcode, rectArg.operand),
			new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Rect), nameof(Rect.yMax))),
			new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WorldbuilderPageRows), nameof(FitRowsTop))),
			new CodeInstruction(OpCodes.Stloc, yLocal),
		});
		return codes;
	}

	private static List<CodeInstruction> Fail(List<CodeInstruction> codes, string problem)
	{
		transpilerProblem = $"{PatchType}.{CenterColumn}: {problem}, so its page has no world shape rows";
		return codes;
	}

	// start: the row's "ldc.i4.3, call set_Anchor, ldarga rect, call get_x, ldloc y, ldc.r4 120, ldc.r4 30, newobj Rect"
	// before its label key at labelText. yLocal: the row's y, which must be expectedY when that's given.
	private static bool TryFindRowStart(List<CodeInstruction> codes, int labelText, object expectedY, out int start, out object yLocal)
	{
		start = labelText - 8;
		yLocal = null;
		if (start < 0 || !codes[labelText - 1].Is(OpCodes.Newobj, RectCtor) || !codes[labelText - 4].IsLdloc() || !codes[start + 2].IsLdarga() || !codes[start + 1].Calls(AccessTools.PropertySetter(typeof(Text), nameof(Text.Anchor))))
		{
			return false;
		}
		yLocal = codes[labelText - 4].operand;
		return expectedY == null || Equals(yLocal, expectedY);
	}

	// Draws the World shape row and moves y past it. With Ring, also draws the ring size row, moves y past it too (the
	// skipped Scale row's own step with it) and returns true, so the caller skips Scale.
	public static bool DoShapeRows(ref float y, float x, float width)
	{
		WorldShape.ShapeRowsShown = true;
		Text.Anchor = TextAnchor.MiddleLeft;
		CreateWorldPageRows.DoShapeRow(x, y, ControlOffset, width);
		y += RowStep;
		if (WorldShape.Ring)
		{
			// Worldbuilder's 120-wide label has no room for the live tile count, so Coverage's slider shows it.
			CreateWorldPageRows.DoRingSizeRow(x, y, ControlOffset, width, "RW_RingSize".Translate());
			y += RowStep;
		}
		Text.Anchor = TextAnchor.UpperLeft;
		return WorldShape.Ring;
	}

	// Worldbuilder puts its rows' top where they'd end above the column's bottom if each were 30 high, counting a row
	// each for Biotech and Odyssey too, though those draw none here. With both active that leaves room for the extra
	// row; otherwise the rows move up just enough for the last one to end above the bottom.
	public static float FitRowsTop(float top, float columnBottom)
	{
		int rows = WorldbuilderRows + 1;
		foreach (string packageId in ModsWithARow)
		{
			if (ModsConfig.IsActive(packageId))
			{
				rows++;
			}
		}
		float overflow = top + (rows - 1) * RowStep + RowHeight - (columnBottom - BottomMargin);
		return overflow > 0f ? top - overflow : top;
	}

	// Worldbuilder's Coverage slider, taking Widgets.HorizontalSlider's arguments. With Ring, it shows and sets the
	// ring's land coverage instead, and returns the planet coverage unchanged.
	public static float CoverageSlider(Rect rect, float value, float min, float max, bool middleAlignment, string label, string leftAlignedLabel, string rightAlignedLabel, float roundTo)
	{
		if (!WorldShape.Ring)
		{
			return Widgets.HorizontalSlider(rect, value, min, max, middleAlignment, label, leftAlignedLabel, rightAlignedLabel, roundTo);
		}
		string ringLabel = "RW_LandCoverageWithLiveTiles".Translate(WorldShape.LandCoverage.ToStringPercent(), WorldShape.LiveTileCount.ToString("N0"));
		// The slider plays its drag sound whenever its rounded result differs from the value passed in, so it's passed a
		// value already on its step (a stored 0.3 needn't be exactly 30 steps), which rounds back to itself.
		float shown = Mathf.RoundToInt(WorldShape.LandCoverage / LandCoverageStep) * LandCoverageStep;
		float coverage = Widgets.HorizontalSlider(rect, shown, RingSize.MinLandCoverage, RingSize.FullLandCoverage, middleAlignment, ringLabel, leftAlignedLabel, rightAlignedLabel, LandCoverageStep);
		if (coverage != shown)
		{
			WorldShape.LandCoverage = coverage;
			WorldShape.SaveLastChoice();
		}
		TooltipHandler.TipRegionByKey(rect, "RW_LandCoverageTip");
		return value;
	}

	// Worldbuilder's world preview, with its show and regenerate buttons, or with Ring a line of text in its place.
	public static void DoPreviewArea(Page_CreateWorldParams window, Rect previewRect)
	{
		if (!WorldShape.Ring)
		{
			previewArea ??= AccessTools.MethodDelegate<Action<Page_CreateWorldParams, Rect>>(Method(PreviewArea));
			previewArea(window, previewRect);
			return;
		}
		Text.Font = GameFont.Medium;
		Text.Anchor = TextAnchor.MiddleCenter;
		GUI.color = Color.gray;
		Widgets.Label(previewRect, "RW_NoRingPreview".Translate());
		GUI.color = Color.white;
		Text.Anchor = TextAnchor.UpperLeft;
		Text.Font = GameFont.Small;
	}

	// With Ring, Worldbuilder's per-frame preview upkeep is skipped, so no preview starts, and one already running is
	// cancelled. Back on Planet, its upkeep sees the cancelled run and starts a fresh one.
	private static bool StopPreviewOnRing()
	{
		if (!WorldShape.Ring)
		{
			return true;
		}
		if (previewThread.GetValue(null) is Thread { IsAlive: true })
		{
			cancelPreview.SetValue(null, true);
		}
		return false;
	}
}
