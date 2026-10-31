using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// A world set up without the Create World page's shape rows (dev quick test, HugsLib quickstart, SOS2's ship start,
// Worldbuilder's Select World, any page-replacing mod) is the last choice, and the player is told so once its game is
// running. Not shown when the page's transpiler failed: then there's no choice and every world is a planet.
//
// Some paths generate the world before the Play scene, whose UI clears every live message as it starts, so the
// message waits for Game.FinalizeInit. It's tied to the world it's about, so a game that loads another world drops it.
[HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld))]
public static class LastChoiceNotice
{
	private static World pendingWorld;

	private static string pendingText;

	public static void Postfix(World __result)
	{
		pendingWorld = null;
		if (WorldShape.ShapeRowsShown || !WorldShape.ringByDefault)
		{
			return;
		}
		pendingWorld = __result;
		pendingText = WorldShape.LastChoiceMessage();
		Log.Message("[Ringworld] Generated without the Create World page's shape rows: " + pendingText);
	}

	[HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
	public static class Patch_Game_FinalizeInit
	{
		public static void Postfix()
		{
			if (pendingWorld != null && pendingWorld == Find.World)
			{
				Messages.Message(pendingText, MessageTypeDefOf.NeutralEvent);
			}
			pendingWorld = null;
		}
	}
}
