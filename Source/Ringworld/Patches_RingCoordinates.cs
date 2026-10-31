using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Ring coordinates (RingCoordinates) in place of vanilla's latitude/longitude, in the only two places a player sees them.
// Display only: LongLatOf keeps returning the climate gradient and local time, and vanilla's GenText helpers aren't
// patched (they only get a float, already the patched one). Orbit-layer tiles and dev readouts keep vanilla's text.
public static class RingCoordinatesDisplay
{
	public static bool TryGetCoordinates(PlanetTile tile, out RingCoordinates coordinates)
	{
		if (tile.Valid && tile.Layer is RingLayer { Geometry: not null } ring)
		{
			coordinates = ring.Geometry.CoordinatesOf(tile.tileId);
			return true;
		}
		coordinates = default;
		return false;
	}

	// Called from the pollution message transpiler with vanilla's "lat / long" text.
	public static string PollutionLocation(string vanillaText, PlanetTile tile)
	{
		return TryGetCoordinates(tile, out RingCoordinates coordinates) ? coordinates.ToSpelledOutString() : vanillaText;
	}
}

// The world inspect pane's tile readout starts with a "lat long" line; on ring tiles it's swapped for the short form.
[HarmonyPatch(typeof(WorldInspectPane), "TileInspectString", MethodType.Getter)]
public static class Patch_WorldInspectPane_TileInspectString
{
	public static void Postfix(ref string __result)
	{
		if (__result == null || !RingCoordinatesDisplay.TryGetCoordinates(Find.WorldSelector.SelectedTile, out RingCoordinates coordinates))
		{
			return;
		}
		int lineEnd = __result.IndexOf('\n');
		__result = coordinates.ToShortString() + (lineEnd < 0 ? "" : __result.Substring(lineEnd));
	}
}

// Biotech's "Pollution has increased by {0} at {1}." builds {1} inline as lat + " / " + long. The transpiler passes
// that string, with the polluted tile, through RingCoordinatesDisplay.PollutionLocation.
[HarmonyPatch(typeof(WorldPollutionUtility), nameof(WorldPollutionUtility.PolluteWorldAtTile))]
public static class Patch_WorldPollutionUtility_PolluteWorldAtTile
{
	private static readonly MethodInfo FindBestTileToPollute = AccessTools.Method(typeof(WorldPollutionUtility), nameof(WorldPollutionUtility.FindBestTileToPollute));

	private static readonly MethodInfo Concat3 = AccessTools.Method(typeof(string), nameof(string.Concat), new[] { typeof(string), typeof(string), typeof(string) });

	private static readonly MethodInfo PollutionLocation = AccessTools.Method(typeof(RingCoordinatesDisplay), nameof(RingCoordinatesDisplay.PollutionLocation));

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
		// The polluted tile is the local FindBestTileToPollute's result is stored in.
		int found = codes.FindIndex(code => code.Calls(FindBestTileToPollute));
		CodeInstruction storeTile = found >= 0 && found + 1 < codes.Count && codes[found + 1].IsStloc() ? codes[found + 1] : null;
		int concat = codes.FindIndex(code => code.Calls(Concat3));
		if (storeTile == null || concat < 0 || codes.FindLastIndex(code => code.Calls(Concat3)) != concat)
		{
			Log.Error("[Ringworld] Couldn't find the location text in WorldPollutionUtility.PolluteWorldAtTile; pollution messages on the ring will show vanilla's latitude/longitude.");
			return codes;
		}
		codes.InsertRange(concat + 1, new[]
		{
			CodeInstruction.LoadLocal(storeTile.LocalIndex()),
			new CodeInstruction(OpCodes.Call, PollutionLocation),
		});
		return codes;
	}
}
