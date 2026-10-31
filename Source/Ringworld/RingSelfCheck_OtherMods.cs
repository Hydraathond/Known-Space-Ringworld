using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Ringworld;

// Patches for My Little Planet's and Worldbuilder's world creation, read from the patched code. Whether each mod's
// page and presets then work with a ring is checked in game with that mod.
public static partial class RingSelfCheck
{
	// My Little Planet's planet size slider, from its decompiled source: a call its transpiler inserts after vanilla's
	// planet-coverage row.
	private const string MyLittlePlanetRowType = "WorldGenRules.WorldGenRules+Page_CreateWorldParams_DoWindowContents_Patch";

	// With My Little Planet, its slider is inside the stretch the ring rows skip (from the shape rows' branch to where it
	// lands), so it's drawn with a planet's coverage row and never under the ring rows, whatever the load order.
	private static void CheckMyLittlePlanetSlider(RingLayer ring, RingCheckResult result)
	{
		System.Type rowType = AccessTools.TypeByName(MyLittlePlanetRowType);
		if (rowType == null)
		{
			return;
		}
		MethodInfo slider = AccessTools.Method(rowType, "DrawPlanetSizeSlider");
		MethodInfo shapeRows = AccessTools.Method(typeof(CreateWorldPageRows), nameof(CreateWorldPageRows.DoShapeRows));
		List<CodeInstruction> code = PatchProcessor.GetCurrentInstructions(AccessTools.Method(typeof(Page_CreateWorldParams), nameof(Page_CreateWorldParams.DoWindowContents)));
		int rows = code.FindIndex(instruction => instruction.Calls(shapeRows));
		int sliderAt = slider == null ? -1 : code.FindIndex(instruction => instruction.Calls(slider));
		if (rows < 0 || rows + 1 >= code.Count || code[rows + 1].opcode != OpCodes.Brtrue)
		{
			result.Fail(rows < 0 ? "the Create World page has no shape rows" : "the shape rows have no branch past the coverage row");
			return;
		}
		if (sliderAt < 0)
		{
			result.Fail("the Create World page has no My Little Planet slider");
			return;
		}
		Label skip = (Label)code[rows + 1].operand;
		int resume = code.FindIndex(instruction => instruction.labels.Contains(skip));
		if (resume < 0)
		{
			result.Fail("the shape rows' branch lands nowhere");
			return;
		}
		if (sliderAt < rows || sliderAt > resume)
		{
			result.Fail($"My Little Planet's slider is at instruction {sliderAt}, outside the planet-only stretch {rows}-{resume}, so it's drawn under the ring rows");
		}
	}

	// With Worldbuilder, its preset terrain, settlement and map-marker restores carry Ringworld's guard. Without it,
	// nothing carries Ringworld's Worldbuilder patch.
	private static void CheckWorldbuilderPresetGuard(RingLayer ring, RingCheckResult result)
	{
		if (!WorldbuilderCompatibility.Active)
		{
			foreach (MethodBase method in Harmony.GetAllPatchedMethods())
			{
				if (Harmony.GetPatchInfo(method).Owners.Contains(WorldbuilderCompatibility.HarmonyId))
				{
					result.Fail($"{method.DeclaringType?.FullName}.{method.Name} carries {WorldbuilderCompatibility.HarmonyId}'s patch without Worldbuilder");
				}
			}
			return;
		}
		foreach (string name in WorldbuilderCompatibility.Restores)
		{
			MethodInfo restore = WorldbuilderCompatibility.Restore(name);
			if (restore == null)
			{
				result.Fail($"Worldbuilder's World_FinalizeInit_Patch.{name} not found");
				continue;
			}
			Patches patches = Harmony.GetPatchInfo(restore);
			if (patches == null || !patches.Prefixes.Any(patch => patch.owner == WorldbuilderCompatibility.HarmonyId))
			{
				result.Fail($"Worldbuilder's {name} has no Ringworld guard");
			}
		}
	}

	// With Worldbuilder, its page's centre column, read as patched: the shape rows come before Scale and branch past it
	// to Coverage, Coverage's slider and the world preview go through Ringworld's ring-aware stand-ins, the rows' top
	// is fitted for the extra row, and its background preview generation carries Ringworld's stop.
	private static void CheckWorldbuilderPageRows(RingLayer ring, RingCheckResult result)
	{
		if (!WorldbuilderCompatibility.Active)
		{
			return;
		}
		MethodInfo column = WorldbuilderPageRows.Method(WorldbuilderPageRows.CenterColumn);
		MethodInfo autoGeneration = WorldbuilderPageRows.Method(WorldbuilderPageRows.PreviewAutoGeneration);
		if (column == null || autoGeneration == null)
		{
			result.Fail($"Worldbuilder's {(column == null ? WorldbuilderPageRows.CenterColumn : WorldbuilderPageRows.PreviewAutoGeneration)} not found");
			return;
		}
		List<CodeInstruction> code = PatchProcessor.GetCurrentInstructions(column);
		int rows = code.FindIndex(instruction => instruction.Calls(AccessTools.Method(typeof(WorldbuilderPageRows), nameof(WorldbuilderPageRows.DoShapeRows))));
		int scale = code.FindIndex(instruction => instruction.Is(OpCodes.Ldstr, "WB_Scale"));
		int coverage = code.FindIndex(instruction => instruction.Is(OpCodes.Ldstr, "WB_Coverage"));
		if (rows < 0 || rows + 1 >= code.Count || code[rows + 1].opcode != OpCodes.Brtrue)
		{
			result.Fail(rows < 0 ? "Worldbuilder's centre column has no shape rows" : "Worldbuilder's shape rows have no branch past Scale");
			return;
		}
		Label skip = (Label)code[rows + 1].operand;
		int resume = code.FindIndex(instruction => instruction.labels.Contains(skip));
		if (scale < 0 || coverage < 0 || !(rows < scale && scale < resume && resume < coverage))
		{
			result.Fail($"Worldbuilder's shape rows (instruction {rows}) and their branch (to {resume}) don't skip Scale ({scale}) and land before Coverage ({coverage})");
		}
		// The first slider after Coverage's label: Worldbuilder's own, or Ringworld's stand-in for it.
		int slider = coverage < 0 ? -1 : code.FindIndex(coverage, instruction => instruction.operand is MethodInfo method && (method.Name == nameof(Verse.Widgets.HorizontalSlider) || method.Name == nameof(WorldbuilderPageRows.CoverageSlider)));
		if (slider < 0 || !code[slider].Calls(AccessTools.Method(typeof(WorldbuilderPageRows), nameof(WorldbuilderPageRows.CoverageSlider))))
		{
			result.Fail("Worldbuilder's Coverage slider doesn't set the ring's land coverage");
		}
		if (!code.Any(instruction => instruction.Calls(AccessTools.Method(typeof(WorldbuilderPageRows), nameof(WorldbuilderPageRows.DoPreviewArea)))) || code.Any(instruction => instruction.Calls(WorldbuilderPageRows.Method(WorldbuilderPageRows.PreviewArea))))
		{
			result.Fail("Worldbuilder's world preview isn't replaced on a ring");
		}
		if (!code.Any(instruction => instruction.Calls(AccessTools.Method(typeof(WorldbuilderPageRows), nameof(WorldbuilderPageRows.FitRowsTop)))))
		{
			result.Fail("Worldbuilder's centre-column rows aren't fitted for the shape rows");
		}
		Patches patches = Harmony.GetPatchInfo(autoGeneration);
		if (patches == null || !patches.Prefixes.Any(patch => patch.owner == WorldbuilderCompatibility.HarmonyId))
		{
			result.Fail("Worldbuilder's background preview generation has no Ringworld stop");
		}
	}
}
