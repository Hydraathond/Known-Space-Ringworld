using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// In-game test run for development, driven by tools/run-in-game.sh: loads a save, runs the self-check, shows the ring
// view and the orbit view so their draw layers render and any exception lands in Player.log, and quits. Inert unless
// RW_DEV_SAVE names a save: its patches are applied by hand here, never by PatchAll, so players' games run none of it.
//   RW_DEV_SAVE      save name to load, or @quicktest for a new game as the dev quick test makes it, on a fixed world
//                    whatever Ringworld's and LAO's settings files say: a Small ring at full coverage, on TheGravship,
//                    with every LAO layer ticked (SetUpQuickTestWorld). The RW_DEV_RING, RW_DEV_COVERAGE,
//                    RW_DEV_SCENARIO and RW_DEV_LAO_LAYERS overrides apply to @quicktest only.
//   RW_DEV_RING      optional planet|<around>x<across>: a planet world, or a ring of that size, instead of a Small ring
//   RW_DEV_COVERAGE  optional fraction: the ring's land coverage instead of 100%, or a planet's coverage instead of
//                    the quick test's 30%
//   RW_DEV_SCENARIO  optional scenario defName instead of TheGravship
//   RW_DEV_LAO_LAYERS  optional comma-separated layer defNames, or none: LAO's layer list instead of every LAO layer
//   RW_DEV_DAYNIGHT  optional on|off, sets the planet day/night setting after loading
//   RW_DEV_SAVEAS    optional save name: saves the game there after the self-check, to load it in a later run
//   RW_DEV_VEF_SPAWNS  optional 1: after the self-check, generates a map on a free surface tile and logs how many of
//                    VEF's object-spawn defs may spawn there and how many of their things it has (DevVefSpawnProbe)
//   RW_DEV_WB_PRESET  optional 1: after the self-check, runs Worldbuilder's terrain restore on the ring with a
//                    planet-sized stand-in preset and logs what it did (DevWorldbuilderPresetProbe)
//   RW_DEV_FUEL_PROBE  optional 1: after the self-check, logs the fuel shown between each pair of layers in both
//                    directions, cold and right after the opposite flight (DevFuelProbe)
//   RW_DEV_QUEST_SWEEP  optional 1: after the self-check (and after any RW_DEV_SAVEAS save, as the sweep leaves the
//                    game unfit to save), runs the quest-site sweep (QuestSiteSweep), on planet worlds too
//   RW_DEV_OFF_MAIN_THREAD  optional 1: when a save's world loads, on the loading thread and before the world has
//                    rendered, calls the self-check (its guard should skip it) and builds the ring's thickness and cloud
//                    band there, as the on-load self-check did before v0.1.3 ticket 03, and logs an "Off-main-thread
//                    probe" line per layer. The harness's own self-check then shows whether they recovered.
//   RW_DEV_HIDE_HILLS  optional 1: empties vanilla's hills layer on the ring, as a mod that draws the hill icons itself
//                    does (ReGrowth 2's World Beautification), so the spill-mountain icon check meets an unknown mod
//   RW_DEV_HEX_SIZE  optional 1: logs a "Hex size probe" line for every planet layer built (tile count, radius,
//                    AverageTileSize) and, after the self-check, one hex's on-screen size at the nearest and furthest
//                    zoom (DevHexSizeProbe)
//   RW_DEV_QUEST_REACH  optional 1: after the self-check, logs "Quest reach probe" lines: the surface's landmasses, and
//                    for sample colony tiles how many site-valid tiles each quest distance band has on and off the
//                    colony's landmass (DevQuestReachProbe)
[StaticConstructorOnStartup]
public static class DevHarness
{
	private static readonly string Save = Environment.GetEnvironmentVariable("RW_DEV_SAVE");
	private static readonly string DayNight = Environment.GetEnvironmentVariable("RW_DEV_DAYNIGHT");
	private static readonly string SaveAs = Environment.GetEnvironmentVariable("RW_DEV_SAVEAS");
	private static readonly string Scenario = Environment.GetEnvironmentVariable("RW_DEV_SCENARIO") is { Length: > 0 } scenario ? scenario : "TheGravship";
	private static readonly string WorldOverride = Environment.GetEnvironmentVariable("RW_DEV_RING");
	private static readonly string CoverageOverride = Environment.GetEnvironmentVariable("RW_DEV_COVERAGE");
	private static readonly string LaoLayersOverride = Environment.GetEnvironmentVariable("RW_DEV_LAO_LAYERS");
	private static readonly bool VefSpawns = Environment.GetEnvironmentVariable("RW_DEV_VEF_SPAWNS") == "1";
	private static readonly bool WorldbuilderPreset = Environment.GetEnvironmentVariable("RW_DEV_WB_PRESET") == "1";
	private static readonly bool QuestSweep = Environment.GetEnvironmentVariable("RW_DEV_QUEST_SWEEP") == "1";
	private static readonly bool FuelProbe = Environment.GetEnvironmentVariable("RW_DEV_FUEL_PROBE") == "1";
	private static readonly bool OffMainThread = Environment.GetEnvironmentVariable("RW_DEV_OFF_MAIN_THREAD") == "1";
	private static readonly bool HideHills = Environment.GetEnvironmentVariable("RW_DEV_HIDE_HILLS") == "1";
	private static readonly bool HexSize = Environment.GetEnvironmentVariable("RW_DEV_HEX_SIZE") == "1";
	private static readonly bool QuestReach = Environment.GetEnvironmentVariable("RW_DEV_QUEST_REACH") == "1";
	private const string QuickTest = "@quicktest";
	// At 0 the ring is edge-on and only the outer face draws.
	private const float OrbitTilt = 35f;
	private static int step;
	private static int wait;

	static DevHarness()
	{
		if (string.IsNullOrEmpty(Save))
		{
			return;
		}
		Harmony harmony = new Harmony("ringworld.dev");
		harmony.Patch(AccessTools.Method(typeof(Root), nameof(Root.Update)), postfix: new HarmonyMethod(typeof(DevHarness), nameof(Tick)));
		harmony.Patch(AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld)), prefix: new HarmonyMethod(typeof(DevHarness), nameof(GiveQuickTestWorldItsFactions)));
		if (OffMainThread)
		{
			harmony.Patch(AccessTools.Method(typeof(World), nameof(World.FinalizeInit)), postfix: new HarmonyMethod(typeof(DevHarness), nameof(ProbeOffMainThread)));
		}
		if (HideHills)
		{
			harmony.Patch(AccessTools.Method(typeof(WorldDrawLayer_Hills), nameof(WorldDrawLayer_Hills.Regenerate)), prefix: new HarmonyMethod(typeof(DevHarness), nameof(EmptyRingHills)));
		}
		if (HexSize)
		{
			harmony.Patch(AccessTools.Method(typeof(PlanetLayer), nameof(PlanetLayer.InitializeLayer)), postfix: new HarmonyMethod(typeof(DevHexSizeProbe), nameof(DevHexSizeProbe.LogLayer)));
		}
		if (Save == QuickTest && !string.IsNullOrEmpty(CoverageOverride))
		{
			harmony.Patch(AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld)), prefix: new HarmonyMethod(typeof(DevHarness), nameof(SetPlanetCoverage)));
		}
		Log.Message($"[Ringworld] Dev harness on: save={Save} daynight={DayNight}");
	}

	// @quicktest's fixed world, set in memory before it's made: a Small ring at full coverage with every LAO layer ticked,
	// or what RW_DEV_RING, RW_DEV_COVERAGE and RW_DEV_LAO_LAYERS say. WorldShape's fields are set directly: its
	// SaveLastChoice and LAO's settings Write are never called, so neither settings file changes on disk.
	private static void SetUpQuickTestWorld()
	{
		WorldShape.Ring = true;
		WorldShape.RingSize = WorldShape.Presets[0].size;
		WorldShape.LandCoverage = RingSize.FullLandCoverage;
		if (WorldOverride == "planet")
		{
			WorldShape.Ring = false;
		}
		else if (!string.IsNullOrEmpty(WorldOverride))
		{
			string[] parts = WorldOverride.Split('x');
			if (parts.Length == 2 && int.TryParse(parts[0], out int around) && int.TryParse(parts[1], out int across))
			{
				WorldShape.RingSize = new RingSize(around, across);
			}
			else
			{
				Log.Error($"[Ringworld] FAIL dev harness: RW_DEV_RING={WorldOverride} is neither planet nor <around>x<across>; making a Small ring");
			}
		}
		if (WorldShape.Ring && TryParseCoverage(out float coverage))
		{
			WorldShape.LandCoverage = Mathf.Clamp(coverage, RingSize.MinLandCoverage, RingSize.FullLandCoverage);
		}
		if (LaoIntegration.Active)
		{
			SetLaoLayerList();
		}
		Log.Message($"[Ringworld] Dev harness quick test: {(WorldShape.Ring ? $"ring {WorldShape.RingSize.tilesAround}x{WorldShape.RingSize.tilesAcross}, land coverage {WorldShape.LandCoverage:0.###}" : "planet")}, scenario {Scenario}");
	}

	// LAO's layer list (AutoAddLayersDefNames, as LaoIntegration reads it) replaced in memory: every layer LAO's settings
	// offer, or RW_DEV_LAO_LAYERS's.
	private static void SetLaoLayerList()
	{
		IEnumerable<string> names = null;
		if (LaoLayersOverride == "none")
		{
			names = Enumerable.Empty<string>();
		}
		else if (!string.IsNullOrEmpty(LaoLayersOverride))
		{
			names = LaoLayersOverride.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0);
		}
		List<string> layers = LaoIntegration.OverrideLayerListInMemory(names);
		Log.Message($"[Ringworld] Dev harness quick test: LAO layers {string.Join(", ", layers)}");
	}

	private static bool TryParseCoverage(out float coverage)
	{
		return float.TryParse(CoverageOverride, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out coverage);
	}

	// WorldGenerator.GenerateWorld prefix, set only on @quicktest with RW_DEV_COVERAGE: a planet's coverage (a ring's
	// is WorldShape.LandCoverage).
	private static void SetPlanetCoverage(ref float planetCoverage)
	{
		if (!WorldShape.Ring && TryParseCoverage(out float coverage))
		{
			planetCoverage = coverage;
		}
	}

	// As ReGrowth's prefix does: clears the layer and draws nothing, on the ring only.
	private static bool EmptyRingHills(WorldDrawLayer_Hills __instance, ref IEnumerable __result)
	{
		if (__instance.planetLayer is not RingLayer)
		{
			return true;
		}
		foreach (LayerSubMesh subMesh in Patch_WorldDrawLayerBase_FinalizeMesh.SubMeshes(__instance))
		{
			subMesh.Clear(MeshParts.All);
		}
		__result = Enumerable.Empty<object>();
		return false;
	}

	private static void ProbeOffMainThread(World __instance)
	{
		if (SurfaceRing.Of(__instance.grid) is not { } ring)
		{
			return;
		}
		if (UnityData.IsInMainThread)
		{
			Log.Warning("[Ringworld] Off-main-thread probe: World.FinalizeInit ran on the main thread; nothing probed");
			return;
		}
		RingSelfCheck.Run(__instance, "off-main-thread probe");
		foreach (WorldDrawLayer layer in ring.WorldDrawLayers.Where(layer => layer is WorldDrawLayer_RingThickness or WorldDrawLayer_RingClouds))
		{
			layer.Regenerate().ExecuteEnumerable();
			Log.Message($"[Ringworld] Off-main-thread probe: built {layer.GetType().Name} on the loading thread; dirty afterwards (rebuilt on a later frame): {layer.Dirty}");
		}
	}

	// The dev quick test makes its world with no faction list, which vanilla reads as "every required faction", but
	// LAO's duplicate guard reads as "none" on its layers, so a stacked quick-test world gets no Salvagers. The Create
	// World page always passes the list, as Page_CreateWorldParams does: so does this.
	private static void GiveQuickTestWorldItsFactions(ref List<FactionDef> factions)
	{
		if (factions != null)
		{
			return;
		}
		factions = new List<FactionDef>();
		foreach (FactionDef def in FactionGenerator.ConfigurableFactions)
		{
			for (int i = 0; i < def.startingCountAtWorldCreation; i++)
			{
				factions.Add(def);
			}
		}
		foreach (FactionDef def in FactionGenerator.ConfigurableFactions)
		{
			if (def.replacesFaction != null)
			{
				factions.RemoveAll(other => other == def.replacesFaction);
			}
		}
	}

	// The self-check skips planet worlds, so the harness checks there that no layer's view has a ghost icon.
	private static void LogPlanetGhostIcons()
	{
		int layers = Find.WorldGrid.PlanetLayers.Count;
		int icons = Find.WorldGrid.PlanetLayers.Values.Sum(layer => GhostIcons.For(Find.World, layer).Count);
		if (icons > 0)
		{
			Log.Error($"[Ringworld] FAIL ghost icons on a planet world: {icons} from its {layers} layers");
		}
		else
		{
			Log.Message($"[Ringworld] Ghost icons on a planet world: none from its {layers} layers.");
		}
	}

	// The self-check skips planet worlds, so the harness checks there that Odyssey's orbit keeps vanilla's icon size.
	private static void LogPlanetOrbitIcons()
	{
		PlanetLayer orbit = Find.WorldGrid.Orbit;
		if (orbit == null)
		{
			Log.Message("[Ringworld] Orbit icons on a planet world: no orbit layer.");
			return;
		}
		string fault = RingSelfCheck.IconSizeFault(RingSelfCheck.IconProbe(orbit), 1f);
		if (fault != null)
		{
			Log.Error($"[Ringworld] FAIL orbit icons on a planet world: {fault}");
		}
		else
		{
			Log.Message("[Ringworld] Orbit icons on a planet world: vanilla's size.");
		}
	}

	private static void Tick()
	{
		if (LongEventHandler.ShouldWaitForEvent)
		{
			return;
		}
		if (wait > 0)
		{
			wait--;
			return;
		}
		switch (step)
		{
			case 0:
				if (Current.ProgramState == ProgramState.Entry)
				{
					if (Save == QuickTest)
					{
						// As the main menu's dev quick test button, which always plays Crashlanded. The harness quits
						// after the run, so the swapped DefOf is never seen by a real game.
						ScenarioDefOf.Crashlanded = DefDatabase<ScenarioDef>.GetNamed(Scenario);
						SetUpQuickTestWorld();
						LongEventHandler.QueueLongEvent(delegate
						{
							Root_Play.SetupForQuickTestPlay();
							PageUtility.InitGameStart();
						}, "GeneratingMap", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
					}
					else
					{
						GameDataSaveLoader.LoadGame(Save);
					}
					step = 1;
				}
				break;
			case 1:
				if (Current.ProgramState == ProgramState.Playing)
				{
					if (DayNight == "on" || DayNight == "off")
					{
						Find.PlaySettings.usePlanetDayNightSystem = DayNight == "on";
					}
					CameraJumper.TryShowWorld();
					PlanetLayer.Selected = Find.WorldGrid.Surface;
					step = 2;
					wait = 5;
				}
				break;
			case 2:
				// Selected again: loading a save can jump the camera to a layer of its own (a gravship's orbit) after the
				// first frames. Then the whole ring in view, as far out as the ground goes.
				PlanetLayer.Selected = Find.WorldGrid.Surface;
				if (SurfaceRing.Current is { } ring)
				{
					float far = RingCamera.MaxDistance(ring);
					Traverse.Create(typeof(RingCamera)).Field("desiredDistance").SetValue(far);
					Traverse.Create(typeof(RingCamera)).Field("distance").SetValue(far);
				}
				step = 3;
				wait = 120;
				break;
			case 3:
				RingSelfCheck.Run(Find.World, "dev harness");
				if (!SurfaceRing.InRingWorld)
				{
					LogPlanetGhostIcons();
					LogPlanetOrbitIcons();
				}
				if (VefSpawns)
				{
					DevVefSpawnProbe.Run();
				}
				if (FuelProbe)
				{
					DevFuelProbe.Run();
				}
				if (HexSize)
				{
					DevHexSizeProbe.LogScreenSizes();
				}
				if (QuestReach)
				{
					DevQuestReachProbe.Run();
				}
				if (!string.IsNullOrEmpty(SaveAs))
				{
					GameDataSaveLoader.SaveGame(SaveAs);
				}
				// After the save: without the guard the probe leaves the ring broken.
				if (WorldbuilderPreset)
				{
					DevWorldbuilderPresetProbe.Run();
				}
				if (QuestSweep)
				{
					QuestSiteSweep.Run();
				}
				step = 4;
				wait = 30;
				break;
			case 4:
				// The first sun-centred orbit layer: only those get the orbit view, and an atmosphere layer may be listed first.
				PlanetLayer orbit = RingOrbit.Layers(Find.WorldGrid).FirstOrDefault();
				if (orbit == null)
				{
					step = 6;
					break;
				}
				PlanetLayer.Selected = orbit;
				step = 5;
				wait = 60;
				break;
			case 5:
				Traverse.Create(Find.WorldCameraDriver).Field("desiredAltitude").SetValue(1100f);
				Find.WorldCameraDriver.sphereRotation = Quaternion.AngleAxis(OrbitTilt, Vector3.right);
				step = 6;
				wait = 240;
				break;
			case 6:
				Root.Shutdown();
				step = 7;
				break;
		}
	}
}
