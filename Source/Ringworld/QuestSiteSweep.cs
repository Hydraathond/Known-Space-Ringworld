using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace Ringworld;

// The quest-site sweep: a heavier Dev-mode check than the self-check, run only from its debug action or the dev
// harness (RW_DEV_QUEST_SWEEP=1). Every quest script that can start a quest, from Core and the five DLCs, is generated
// many times on the current world, as the dev quest tools generate them, and each new site or other world object it
// would spawn must be:
// - on the kind of layer vanilla means: one not in space on the world's surface (the ring), one in space on an orbit
//   layer (on a ring world, one centred on the sun). Which layer each quest's objects land on is logged, to set
//   against a planet run;
// - on a live tile (the layer has the tile), never a wall-foot tile;
// - legal by vanilla's own rules: a tile vanilla allows a new settlement on, and on the ground, reachable by caravan
//   from a player colony on the same layer, if there is one;
// and generating it must log no error. World objects the quest only points at (an existing settlement, say) are checked
// for layer, live tile and wall foot. Then every site part from those mods is checked: one no quest placed is searched
// for with vanilla's site tile finder, as QuestNode_GetSiteTile does, held to the same rules.
// It logs PASS/FAIL per quest script and site part, with failing tile IDs. It runs on planet worlds too, as a baseline:
// a quest that fails there as well isn't the ring's doing. The generated quests are never added to the game, but
// generating them can leave pawns and factions behind, so don't save the game after a sweep.
public static class QuestSiteSweep
{
	private static readonly float[] Points = { 200f, 800f, 2500f };

	private const int TriesPerPoints = 4;

	private const int SearchesPerSitePart = 12;

	private const int MaxListedTiles = 20;

	private static readonly AccessTools.FieldRef<TickManager, int> TicksGame = AccessTools.FieldRefAccess<TickManager, int>("ticksGameInt");

	// What one quest script or site part produced: problems by kind, each with the tiles it was found on.
	private sealed class Findings
	{
		public readonly Dictionary<string, List<int>> problems = new Dictionary<string, List<int>>();

		public readonly Dictionary<string, int> layers = new Dictionary<string, int>();

		public int generated;

		public int notRun;

		public void Problem(string kind, PlanetTile? tile = null)
		{
			if (!problems.TryGetValue(kind, out List<int> tiles))
			{
				problems[kind] = tiles = new List<int>();
			}
			if (tile is PlanetTile found && !tiles.Contains(found.tileId))
			{
				tiles.Add(found.tileId);
			}
		}

		public void Placed(PlanetLayer layer)
		{
			string name = Describe(layer);
			layers[name] = layers.TryGetValue(name, out int count) ? count + 1 : 1;
		}

		public string DescribeProblems()
		{
			return string.Join("; ", problems.Select(problem =>
			{
				if (problem.Value.Count == 0)
				{
					return problem.Key;
				}
				string more = problem.Value.Count > MaxListedTiles ? $", ... ({problem.Value.Count - MaxListedTiles} more)" : "";
				return $"{problem.Key} (tiles {string.Join(", ", problem.Value.Take(MaxListedTiles))}{more})";
			}));
		}

		public string DescribeLayers()
		{
			return layers.Count == 0 ? "no world objects" : string.Join(", ", layers.OrderBy(layer => layer.Key).Select(layer => $"{layer.Value} on {layer.Key}"));
		}
	}

	public static void Run()
	{
		// With verbose logging on, the deep profiler logs every quest generated, and the log stops at 10,000 messages.
		bool profiling = DeepProfiler.enabled;
		DeepProfiler.enabled = false;
		try
		{
			Sweep();
		}
		finally
		{
			DeepProfiler.enabled = profiling;
		}
	}

	private static void Sweep()
	{
		World world = Find.World;
		bool ring = SurfaceRing.Of(world.grid) != null;
		Log.Message($"[Ringworld] Quest sweep of the {(ring ? "ring" : "planet")} world: each quest {Points.Length * TriesPerPoints} times at {string.Join("/", Points)} points. Don't save the game afterwards.");
		string factionCounts = string.Join(", ", Find.FactionManager.AllFactionsListForReading.GroupBy(faction => faction.def).Where(group => group.Key.hidden || group.Count() > 1).Select(group => $"{group.Key.defName} x{group.Count()}"));
		Log.Message($"[Ringworld] Hidden factions, and any def with several: {factionCounts}.");
		int passed = 0;
		int failed = 0;
		List<string> notRun = new List<string>();
		CheckLayerLists(ref passed, ref failed);
		HashSet<SitePartDef> placedParts = new HashSet<SitePartDef>();
		List<QuestScriptDef> scripts = StartingScripts().ToList();
		foreach (QuestScriptDef script in scripts)
		{
			// Quests log errors of their own, which mustn't stop the log before the sweep's own lines.
			Log.ResetMessageCount();
			Findings findings = SweepScript(script, placedParts);
			string name = $"quest {script.defName} ({script.modContentPack.Name})";
			if (findings.generated == 0 && findings.problems.Count == 0)
			{
				notRun.Add(script.defName);
				continue;
			}
			string summary = $"{findings.generated} of {Points.Length * TriesPerPoints} generated, {findings.DescribeLayers()}";
			Report(name, summary, findings, ref passed, ref failed);
		}
		foreach (SitePartDef part in DefDatabase<SitePartDef>.AllDefs.Where(def => def.modContentPack?.IsOfficialMod == true).OrderBy(def => def.defName))
		{
			Log.ResetMessageCount();
			string name = $"site part {part.defName} ({part.modContentPack.Name})";
			if (placedParts.Contains(part))
			{
				Log.Message($"[Ringworld]   PASS {name}: placed by the quests above");
				passed++;
				continue;
			}
			Findings findings = SearchForSitePart(part);
			Report(name, $"no quest above placed it; {findings.generated} of {SearchesPerSitePart} site searches found a tile, {findings.DescribeLayers()}", findings, ref passed, ref failed);
		}
		List<string> subScripts = DefDatabase<QuestScriptDef>.AllDefs.Where(def => def.modContentPack?.IsOfficialMod == true && !scripts.Contains(def)).Select(def => def.defName).OrderBy(name => name).ToList();
		Log.Message($"[Ringworld]   Not swept on their own, as nothing starts a quest with them (run through the quests that use them): {string.Join(", ", subScripts)}");
		string notRunText = notRun.Count == 0 ? "" : $"; {notRun.Count} quests can't start on this world now and weren't generated: {string.Join(", ", notRun)}";
		Log.Message($"[Ringworld] Quest sweep done: {passed} passed, {failed} failed{notRunText}.");
	}

	private static void Report(string name, string summary, Findings findings, ref int passed, ref int failed)
	{
		if (findings.problems.Count == 0)
		{
			Log.Message($"[Ringworld]   PASS {name}: {summary}");
			passed++;
		}
		else
		{
			Log.Error($"[Ringworld]   FAIL {name}: {summary}: {findings.DescribeProblems()}");
			failed++;
		}
	}

	// Quest scripts a quest can start from: picked by the storyteller or as a decree, marked as a special root, given
	// by an incident, or with a root node of their own (QuestNode_Root_...), as Odyssey's gravcore quests have, which its
	// grav engine quest starts later. The rest only run inside those.
	private static IEnumerable<QuestScriptDef> StartingScripts()
	{
		HashSet<QuestScriptDef> byIncident = new HashSet<QuestScriptDef>(DefDatabase<IncidentDef>.AllDefs.Select(def => def.questScriptDef).Where(def => def != null));
		return DefDatabase<QuestScriptDef>.AllDefs
			.Where(def => def.modContentPack?.IsOfficialMod == true && def.root != null && (def.IsRootAny || byIncident.Contains(def) || def.root.GetType().Name.StartsWith("QuestNode_Root")))
			.OrderBy(def => def.modContentPack.loadOrder)
			.ThenBy(def => def.defName);
	}

	private static Findings SweepScript(QuestScriptDef script, HashSet<SitePartDef> placedParts)
	{
		Findings findings = new Findings();
		IIncidentTarget target = (IIncidentTarget)SurfaceHomeMap() ?? Find.World;
		int startTick = Find.TickManager.TicksGame;
		int attempt = 0;
		foreach (float points in Points)
		{
			for (int i = 0; i < TriesPerPoints; i++)
			{
				attempt++;
				// A new tick per attempt: CanRun caches its answer per tick, and the site finder's landmark roll is
				// seeded by it.
				TicksGame(Find.TickManager) = startTick + attempt;
				Rand.PushState(Gen.HashCombineInt(script.shortHash, attempt));
				List<string> errors = new List<string>();
				Application.LogCallback onLog = (text, stack, type) =>
				{
					if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
					{
						errors.Add(FirstLine(text));
					}
				};
				Application.logMessageReceived += onLog;
				try
				{
					Slate slate = SlateFor(script, points);
					if (!script.CanRun(slate, target))
					{
						findings.notRun++;
						continue;
					}
					Quest quest = QuestGen.Generate(script, slate);
					if (quest == null)
					{
						findings.Problem("generating returned no quest");
						continue;
					}
					findings.generated++;
					CheckQuest(quest, findings, placedParts);
				}
				catch (Exception e)
				{
					findings.Problem($"threw {e.GetType().Name}: {FirstLine(e.Message)}");
				}
				finally
				{
					Application.logMessageReceived -= onLog;
					Rand.PopState();
					TicksGame(Find.TickManager) = startTick;
				}
				foreach (string error in errors)
				{
					findings.Problem($"logged an error: {error}");
				}
			}
		}
		return findings;
	}

	// As vanilla's dev quest tools set them up.
	private static Slate SlateFor(QuestScriptDef script, float points)
	{
		Slate slate = new Slate();
		slate.Set("points", points);
		slate.Set("debugDontGenerateRewardThings", true);
		if (script.IsRootDecree)
		{
			slate.Set("asker", PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.RandomElementWithFallback());
		}
		if (script == QuestScriptDefOf.LongRangeMineralScannerLump)
		{
			slate.Set("targetMineableThing", ThingDefOf.Gold);
			slate.Set("targetMineable", ThingDefOf.MineableGold);
			slate.Set("worker", PawnsFinder.AllMaps_FreeColonists.FirstOrDefault());
		}
		return slate;
	}

	private static Map SurfaceHomeMap()
	{
		return HomeMapsOn(Find.WorldGrid.Surface).FirstOrDefault() ?? Find.AnyPlayerHomeMap;
	}

	private static IEnumerable<Map> HomeMapsOn(PlanetLayer layer)
	{
		return Find.Maps.Where(map => map.IsPlayerHome && map.Tile.Valid && map.Tile.Layer == layer);
	}

	private static void CheckQuest(Quest quest, Findings findings, HashSet<SitePartDef> placedParts)
	{
		HashSet<WorldObject> spawned = new HashSet<WorldObject>();
		foreach (QuestPart_SpawnWorldObject part in quest.PartsListForReading.OfType<QuestPart_SpawnWorldObject>())
		{
			if (part.worldObject == null)
			{
				continue;
			}
			spawned.Add(part.worldObject);
			if (part.worldObject is Site site)
			{
				placedParts.UnionWith(site.parts.Select(sitePart => sitePart.def));
			}
			CheckTile(part.worldObject.Tile, newObject: true, findings);
		}
		foreach (GlobalTargetInfo target in quest.QuestLookTargets)
		{
			if (target.WorldObject is WorldObject worldObject && !spawned.Contains(worldObject) && worldObject.Spawned && worldObject.Faction != Faction.OfPlayer)
			{
				spawned.Add(worldObject);
				CheckTile(worldObject.Tile, newObject: false, findings);
			}
		}
	}

	private static Findings SearchForSitePart(SitePartDef part)
	{
		Findings findings = new Findings();
		int maxDist = 27;
		if (part.conditionCauserDef?.GetCompProperties<CompProperties_CausesGameCondition>() is CompProperties_CausesGameCondition causer)
		{
			maxDist = Mathf.Min(maxDist, causer.worldRange);
		}
		PlanetTile near = SurfaceHomeMap()?.Tile ?? PlanetTile.Invalid;
		int startTick = Find.TickManager.TicksGame;
		for (int i = 0; i < SearchesPerSitePart; i++)
		{
			TicksGame(Find.TickManager) = startTick + i + 1;
			Rand.PushState(Gen.HashCombineInt(part.shortHash, i));
			try
			{
				if (TileFinder.TryFindNewSiteTile(out PlanetTile tile, near, 7, Mathf.Max(7, maxDist)))
				{
					findings.generated++;
					CheckTile(tile, newObject: true, findings);
				}
			}
			catch (Exception e)
			{
				findings.Problem($"the site search threw {e.GetType().Name}: {FirstLine(e.Message)}");
			}
			finally
			{
				Rand.PopState();
				TicksGame(Find.TickManager) = startTick;
			}
		}
		if (findings.generated == 0)
		{
			findings.Problem($"none of {SearchesPerSitePart} site searches found a tile");
		}
		return findings;
	}

	private static void CheckTile(PlanetTile tile, bool newObject, Findings findings)
	{
		if (!tile.Valid)
		{
			findings.Problem("a world object with no tile");
			return;
		}
		WorldGrid grid = Find.WorldGrid;
		PlanetLayer layer = tile.Layer;
		if (layer == null || !grid.PlanetLayers.Values.Contains(layer))
		{
			findings.Problem("on no layer of this world", tile);
			return;
		}
		findings.Placed(layer);
		bool space = layer.Def.isSpace;
		if (!space && layer != grid.Surface)
		{
			findings.Problem($"on {layer.Def.defName}, not the surface", tile);
		}
		if (space && SurfaceRing.Of(grid) != null && !RingOrbit.Layers(grid).Contains(layer))
		{
			findings.Problem($"on {layer.Def.defName}, not an orbit layer around the sun", tile);
		}
		if (space && SurfaceRing.Of(grid) != null && layer.Def != PlanetLayerDefOf.Orbit)
		{
			findings.Problem($"on {layer.Def.defName}, not Odyssey's orbit, which every vanilla orbit site names", tile);
		}
		if ((uint)tile.tileId >= (uint)layer.TilesCount)
		{
			findings.Problem("not a live tile", tile);
			return;
		}
		if (space && RingOrbitSpan.OutsideBand(layer, grid.GetTileCenter(tile)))
		{
			findings.Problem("outside the layer's orbit band", tile);
		}
		if (layer is RingLayer { IsSurfaceRing: true } ring && ring.IsWallFootTile(tile.tileId))
		{
			findings.Problem("on the wall foot", tile);
		}
		if (!newObject)
		{
			return;
		}
		StringBuilder reason = new StringBuilder();
		if (!TileFinder.IsValidTileForNewSettlement(tile, reason))
		{
			if (Find.WorldObjects.AnySettlementBaseAtOrAdjacent(tile, out WorldObject settlement))
			{
				reason.Append($" Next to {settlement.Label} of {settlement.Faction?.Name ?? "no faction"}.");
			}
			findings.Problem($"not a tile vanilla allows a site on ({reason})", tile);
		}
		if (!space)
		{
			List<PlanetTile> colonies = HomeMapsOn(layer).Select(map => map.Tile).ToList();
			if (colonies.Count > 0 && !colonies.Any(colony => Find.WorldReachability.CanReach(colony, tile)))
			{
				int landmass = layer.Tiles.Count(other => Find.WorldReachability.CanReach(colonies[0], other.tile));
				findings.Problem($"not reachable by caravan from a colony, whose landmass is {landmass} tiles", tile);
			}
		}
	}

	private static string Describe(PlanetLayer layer)
	{
		if (layer is RingLayer { IsSurfaceRing: true })
		{
			return "the ring";
		}
		if (layer == Find.WorldGrid.Surface)
		{
			return "the surface";
		}
		return RingOrbit.IsSunCentred(layer) ? RingOrbit.Describe(layer) : layer.Def.defName;
	}

	private static string FirstLine(string text)
	{
		string line = (text ?? "").Split('\n')[0].Trim();
		return line.Length > 160 ? line.Substring(0, 160) + "..." : line;
	}

	// Every list of planet layers in a def that names vanilla's Surface also names the ring's def
	// (Patches/RingInSurfaceLayerLists.xml), in quest scripts' nodes too, where a list is kept as text until a quest reads it.
	private static void CheckLayerLists(ref int passed, ref int failed)
	{
		PlanetLayerDef ringDef = DefDatabase<PlanetLayerDef>.GetNamed("RW_Ring");
		LayerListSearch search = new LayerListSearch(ringDef);
		foreach (Type defType in GenDefDatabase.AllDefTypesWithDatabases())
		{
			foreach (Def def in GenDefDatabase.GetAllDefsInDatabaseForDef(defType))
			{
				search.Visit(def, def, 0);
			}
		}
		string name = "layer lists: every list of planet layers in a def that names vanilla's Surface also names the ring's def";
		if (search.lists == 0)
		{
			Log.Error($"[Ringworld]   FAIL {name}: no list naming Surface was found, so the search is broken");
			failed++;
		}
		else if (search.missing.Count > 0)
		{
			Log.Error($"[Ringworld]   FAIL {name}: {search.missing.Count} of {search.lists} lists don't: {string.Join(", ", search.missing.Take(MaxListedTiles))}");
			failed++;
		}
		else
		{
			Log.Message($"[Ringworld]   PASS {name} ({search.lists} lists)");
			passed++;
		}
	}

	// Walks each def's fields, and their fields in turn, for lists of planet layers; other defs it points at are walked
	// on their own.
	private sealed class LayerListSearch
	{
		private const int MaxDepth = 16;

		private readonly PlanetLayerDef ringDef;

		private readonly HashSet<object> seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

		private readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();

		public readonly List<string> missing = new List<string>();

		public int lists;

		public LayerListSearch(PlanetLayerDef ringDef)
		{
			this.ringDef = ringDef;
		}

		public void Visit(object value, Def owner, int depth)
		{
			switch (value)
			{
				case null:
				case string:
				case Type:
				case Delegate:
				case UnityEngine.Object:
				case MemberInfo:
					return;
				case List<PlanetLayerDef> layers:
					if (layers.Contains(PlanetLayerDefOf.Surface))
					{
						Found(owner, layers.Contains(ringDef));
					}
					return;
				case ISlateRef slateRef:
					if (slateRef.SlateRef is string text && text.Contains(">Surface<"))
					{
						Found(owner, text.Contains(">RW_Ring<"));
					}
					return;
				case Def when depth > 0:
					return;
			}
			Type type = value.GetType();
			if (type.IsPrimitive || type.IsEnum || depth > MaxDepth || !seen.Add(value))
			{
				return;
			}
			if (value is IEnumerable items)
			{
				foreach (object item in items)
				{
					Visit(item, owner, depth + 1);
				}
				return;
			}
			foreach (FieldInfo field in FieldsOf(type))
			{
				Visit(field.GetValue(value), owner, depth + 1);
			}
		}

		private void Found(Def owner, bool namesRing)
		{
			lists++;
			if (!namesRing)
			{
				missing.Add($"{owner.defName} ({owner.GetType().Name})");
			}
		}

		private FieldInfo[] FieldsOf(Type type)
		{
			if (!fields.TryGetValue(type, out FieldInfo[] found))
			{
				List<FieldInfo> all = new List<FieldInfo>();
				for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
				{
					all.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(field => !field.FieldType.IsPrimitive && !field.FieldType.IsEnum && field.FieldType != typeof(string)));
				}
				fields[type] = found = all.ToArray();
			}
			return found;
		}
	}

	[DebugAction("Ringworld", "Ringworld: quest-site sweep (heavy; don't save afterwards)", allowedGameStates = AllowedGameStates.Playing)]
	private static void SweepAction()
	{
		Run();
	}
}
