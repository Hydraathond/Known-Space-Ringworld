using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Code that asks whether a layer is the world's surface by comparing its def to vanilla's Surface is wrong on a ring
// world, whose surface has the ring's own def. Targeted transpilers swap such comparisons for IsSurface. Making the
// ring's def be Surface instead was rejected: it leaks into planet worlds made later in the same session.
public static class SurfaceIdentity
{
	private static readonly FieldInfo SurfaceField = AccessTools.Field(typeof(PlanetLayerDefOf), nameof(PlanetLayerDefOf.Surface));

	private static readonly MethodInfo IsSurfaceMethod = AccessTools.Method(typeof(SurfaceIdentity), nameof(IsSurface));

	private static PlanetLayerDef ringDef;

	// The surface ring's def, RW_Ring.
	public static PlanetLayerDef RingDef => ringDef ??= DefDatabase<PlanetLayerDef>.GetNamed("RW_Ring");

	// RW_Ring is the surface ring's def only: another ring-shaped layer (an atmosphere layer) has a def of its own, so
	// this stays false for it.
	public static bool IsSurface(PlanetLayerDef def)
	{
		return def != null && (def == PlanetLayerDefOf.Surface || def == RingDef);
	}

	// For a transpiler: each "def == Surface" or "def != Surface" branch becomes a branch on IsSurface(def). Other reads
	// of Surface (a lookup by def, say) are left alone. swapped is how many comparisons were changed.
	public static List<CodeInstruction> RouteThroughIsSurface(IEnumerable<CodeInstruction> instructions, out int swapped)
	{
		List<CodeInstruction> code = new List<CodeInstruction>(instructions);
		swapped = 0;
		for (int i = 0; i < code.Count - 1; i++)
		{
			if (!code[i].LoadsField(SurfaceField))
			{
				continue;
			}
			OpCode branch = code[i + 1].opcode;
			OpCode replacement;
			if (branch == OpCodes.Beq || branch == OpCodes.Beq_S)
			{
				replacement = OpCodes.Brtrue;
			}
			else if (branch == OpCodes.Bne_Un || branch == OpCodes.Bne_Un_S)
			{
				replacement = OpCodes.Brfalse;
			}
			else
			{
				continue;
			}
			code[i].opcode = OpCodes.Call;
			code[i].operand = IsSurfaceMethod;
			code[i + 1].opcode = replacement;
			swapped++;
		}
		return code;
	}
}
