using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Spill mountains are cosmetic: they keep the wall foot's impassable hilliness and climate biome, so settling and caravan
// routing refuse them as they do any wall-foot tile. Only their icon and inspect-pane name differ.
public static class SpillMountains
{
	public static bool IsSpillMountain(PlanetTile tile)
	{
		return tile.Valid && tile.Layer is RingLayer { IsSurfaceRing: true } ring && ring.IsSpillMountain(tile.tileId);
	}

	// Called from the hills-layer transpiler in place of reading Tile.hilliness: a spill mountain is drawn as a mountain.
	public static Hilliness IconHilliness(Tile tile)
	{
		return IsSpillMountain(tile.tile) ? Hilliness.Mountainous : tile.hilliness;
	}

	private static readonly FieldInfo HillinessField = AccessTools.Field(typeof(Tile), nameof(Tile.hilliness));

	private static readonly MethodInfo IconHillinessMethod = AccessTools.Method(typeof(SpillMountains), nameof(IconHilliness));

	// For the hill-icon transpilers: every Tile.hilliness read becomes a call to IconHilliness. replaced counts them.
	public static List<CodeInstruction> ReadIconHilliness(IEnumerable<CodeInstruction> instructions, out int replaced)
	{
		List<CodeInstruction> result = new List<CodeInstruction>();
		replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(HillinessField))
			{
				replaced++;
				result.Add(new CodeInstruction(OpCodes.Call, IconHillinessMethod).WithLabels(instruction.labels).WithBlocks(instruction.blocks));
				continue;
			}
			result.Add(instruction);
		}
		return result;
	}
}

// Vanilla's hills layer picks each tile's icon by its hilliness; the transpiler has it read Mountainous on a spill
// mountain, so it shows vanilla's ordinary mountain icon instead of the impassable-mountain icon the rest of the wall
// foot has. Only the icon changes: the tile's hilliness stays impassable.
[HarmonyPatch]
public static class Patch_WorldDrawLayer_Hills_Regenerate
{
	public static MethodBase TargetMethod()
	{
		return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(WorldDrawLayer_Hills), nameof(WorldDrawLayer_Hills.Regenerate)));
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> patched = SpillMountains.ReadIconHilliness(instructions, out int replaced);
		if (replaced != 1)
		{
			Log.Error($"[Ringworld] Expected one hilliness read in WorldDrawLayer_Hills.Regenerate, patched {replaced}; spill mountains may show the impassable-mountain icon.");
		}
		return patched;
	}
}

// ReGrowth 2's World Beautification replaces vanilla's hills layer: its prefix empties WorldDrawLayer_Hills, and its own
// WorldDrawLayer_Beautification (copied from Surface onto the ring, as every Surface draw layer is) draws every hill icon
// from the tile's hilliness, in its Regenerate iterator and in DrawVanillaHills, its fallback to vanilla's icons. So
// every hilliness read there goes through IconHilliness too, and a spill mountain gets ReGrowth's ordinary-mountain icon
// (its Mountains texture for the biome and snow level, or vanilla's) instead of its impassable one. Every other tile reads
// its real hilliness, so draws as before. Resolved by type name: without ReGrowth, Prepare skips the patch quietly.
[HarmonyPatch]
public static class Patch_ReGrowth_WorldDrawLayer_Beautification
{
	public const string LayerTypeName = "ReGrowthCore.WorldDrawLayer_Beautification";

	private static readonly string[] TargetNames = { "Regenerate", "DrawVanillaHills" };

	// Hilliness reads swapped across both targets. Harmony runs the transpiler inside each target's patching, and calls
	// Cleanup with no original once all are done.
	private static int replaced;

	// Whether Prepare let the patch go ahead; Harmony calls Cleanup either way.
	private static bool patching;

	// Without ReGrowth, skipped quietly; with a ReGrowth that has none of the targets, skipped with the error below.
	public static bool Prepare(MethodBase original)
	{
		if (original != null)
		{
			return true;
		}
		if (AccessTools.TypeByName(LayerTypeName) == null)
		{
			return false;
		}
		if (Targets().Any())
		{
			patching = true;
			return true;
		}
		Log.Error($"[Ringworld] Found none of {string.Join(", ", TargetNames)} in ReGrowth's {LayerTypeName}; with its World Beautification on, spill mountains may show the impassable-mountain icon.");
		return false;
	}

	public static IEnumerable<MethodBase> TargetMethods()
	{
		return Targets();
	}

	// Regenerate is an iterator, so its body is the enumerator's MoveNext. A target ReGrowth no longer has is left out.
	private static IEnumerable<MethodBase> Targets()
	{
		System.Type layer = AccessTools.TypeByName(LayerTypeName);
		foreach (string name in TargetNames)
		{
			MethodInfo method = AccessTools.Method(layer, name);
			MethodBase target = method == null ? null : name == "Regenerate" ? AccessTools.EnumeratorMoveNext(method) : method;
			if (target != null)
			{
				yield return target;
			}
		}
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> patched = SpillMountains.ReadIconHilliness(instructions, out int count);
		replaced += count;
		return patched;
	}

	public static void Cleanup(MethodBase original)
	{
		if (original == null && patching && replaced == 0)
		{
			Log.Error($"[Ringworld] Found no hilliness read in ReGrowth's {LayerTypeName}; with its World Beautification on, spill mountains may show the impassable-mountain icon.");
		}
	}
}

// The inspect pane's title for a selected tile is its landmark's name or its biome; a spill mountain without a landmark
// is named instead of its biome.
[HarmonyPatch(typeof(WorldInspectPane), nameof(WorldInspectPane.GetLabel))]
public static class Patch_WorldInspectPane_GetLabel
{
	public static void Postfix(ref string __result)
	{
		if (Find.WorldSelector.NumSelectedObjects == 0 && SpillMountains.IsSpillMountain(Find.WorldSelector.SelectedTile) && Find.World.landmarks?[Find.WorldSelector.SelectedTile] == null)
		{
			__result = "RW_SpillMountain".Translate();
		}
	}
}
