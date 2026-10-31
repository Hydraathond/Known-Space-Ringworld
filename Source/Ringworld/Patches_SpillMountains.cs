using System.Collections.Generic;
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
}

// Vanilla's hills layer picks each tile's icon by its hilliness; the transpiler has it read Mountainous on a spill
// mountain, so it shows vanilla's ordinary mountain icon instead of the impassable-mountain icon the rest of the wall
// foot has. Only the icon changes: the tile's hilliness stays impassable.
[HarmonyPatch]
public static class Patch_WorldDrawLayer_Hills_Regenerate
{
	private static readonly FieldInfo HillinessField = AccessTools.Field(typeof(Tile), nameof(Tile.hilliness));

	private static readonly MethodInfo IconHilliness = AccessTools.Method(typeof(SpillMountains), nameof(SpillMountains.IconHilliness));

	public static MethodBase TargetMethod()
	{
		return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(WorldDrawLayer_Hills), nameof(WorldDrawLayer_Hills.Regenerate)));
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(HillinessField))
			{
				replaced++;
				yield return new CodeInstruction(OpCodes.Call, IconHilliness).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				continue;
			}
			yield return instruction;
		}
		if (replaced != 1)
		{
			Log.Error($"[Ringworld] Expected one hilliness read in WorldDrawLayer_Hills.Regenerate, patched {replaced}; spill mountains may show the impassable-mountain icon.");
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
