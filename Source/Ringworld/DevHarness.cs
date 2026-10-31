using System;
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
//   RW_DEV_SAVE      save name to load, or @quicktest for a new game as the dev quick test makes it (a page-less
//                    world, so the last world shape choice)
//   RW_DEV_SCENARIO  optional scenario defName for @quicktest instead of Crashlanded (TheGravship, for Odyssey's
//                    gravship quests)
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
[StaticConstructorOnStartup]
public static class DevHarness
{
	private static readonly string Save = Environment.GetEnvironmentVariable("RW_DEV_SAVE");
	private static readonly string DayNight = Environment.GetEnvironmentVariable("RW_DEV_DAYNIGHT");
	private static readonly string SaveAs = Environment.GetEnvironmentVariable("RW_DEV_SAVEAS");
	private static readonly string Scenario = Environment.GetEnvironmentVariable("RW_DEV_SCENARIO");
	private static readonly bool VefSpawns = Environment.GetEnvironmentVariable("RW_DEV_VEF_SPAWNS") == "1";
	private static readonly bool WorldbuilderPreset = Environment.GetEnvironmentVariable("RW_DEV_WB_PRESET") == "1";
	private static readonly bool QuestSweep = Environment.GetEnvironmentVariable("RW_DEV_QUEST_SWEEP") == "1";
	private static readonly bool FuelProbe = Environment.GetEnvironmentVariable("RW_DEV_FUEL_PROBE") == "1";
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
		Log.Message($"[Ringworld] Dev harness on: save={Save} daynight={DayNight}");
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
						if (!string.IsNullOrEmpty(Scenario))
						{
							ScenarioDefOf.Crashlanded = DefDatabase<ScenarioDef>.GetNamed(Scenario);
						}
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
				if (VefSpawns)
				{
					DevVefSpawnProbe.Run();
				}
				if (FuelProbe)
				{
					DevFuelProbe.Run();
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
