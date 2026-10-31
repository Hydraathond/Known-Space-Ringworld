using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Ringworld;

// Vanilla Expanded Framework (VEF) is a soft dependency: its methods are looked up by name and patched only when it's
// active. Two of its checks compare a layer's def to vanilla's Surface, so on a ring world they never pass:
// - object spawns (ObjectSpawnsDef) with no layer list spawn only on Surface maps, so none spawn on ring maps;
// - moving bases (MovingBaseDef) are only spawned for factions generated on Surface, so a ring world gets none.
// Each comparison goes through SurfaceIdentity.IsSurface instead, which is the same on planet worlds. If a target or its
// comparison isn't found, one warning is logged and that check keeps VEF's behaviour. Without VEF none of this runs or
// logs.
//   RW_DEV_VEF_UNPATCHED=1  applies none of it, to see VEF's own behaviour on a ring world
[StaticConstructorOnStartup]
public static class VefCompatibility
{
	private const string PackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

	public const string HarmonyId = "ringworld.vef";

	public static readonly bool Active = ModsConfig.IsActive(PackageId);

	// VEF's methods that compare a layer's def to Surface once each: the object-spawn check (the map-wide overload, not
	// the per-cell one), the moving-base spawner. Parameter type names pick an overload; null means there's only one.
	private static readonly (string type, string method, string[] parameters)[] Targets =
	{
		("VEF.Maps.VanillaExpandedFramework_MapGenerator_GenerateMap_Patch", "CanSpawnAt", new[] { "Verse.Map", "VEF.Maps.ObjectSpawnsDef" }),
		("VEF.Planet.VanillaExpandedFramework_FactionGenerator_NewGeneratedFaction_Patch", "Postfix", null),
	};

	// Comparisons the transpiler swapped in the target being patched. Harmony runs the transpiler inside Patch, so the
	// static constructor resets it before each target and reads it after.
	private static int swapped;

	static VefCompatibility()
	{
		if (!Active || Environment.GetEnvironmentVariable("RW_DEV_VEF_UNPATCHED") == "1")
		{
			return;
		}
		Harmony harmony = new Harmony(HarmonyId);
		List<string> problems = new List<string>();
		foreach ((string type, string method, string[] parameters) in Targets)
		{
			swapped = 0;
			try
			{
				MethodInfo target = FindTarget(type, method, parameters);
				if (target == null)
				{
					problems.Add($"{type}.{method} not found");
					continue;
				}
				harmony.Patch(target, transpiler: new HarmonyMethod(typeof(VefCompatibility), nameof(Transpiler)));
			}
			catch (Exception e)
			{
				problems.Add($"{type}.{method}: {e.Message}");
				continue;
			}
			if (swapped != 1)
			{
				problems.Add($"{type}.{method} has {swapped} Surface comparisons, not 1");
			}
		}
		if (problems.Count > 0)
		{
			Log.Warning($"[Ringworld] Vanilla Expanded Framework patch incomplete ({string.Join("; ", problems)}); VEF's object spawns or moving bases may be missing on ring worlds.");
		}
	}

	// Null if the type, the method or a parameter type isn't found.
	private static MethodInfo FindTarget(string type, string method, string[] parameters)
	{
		Type[] types = parameters == null ? null : Array.ConvertAll(parameters, AccessTools.TypeByName);
		if (types != null && Array.IndexOf(types, null) >= 0)
		{
			return null;
		}
		Type declaring = AccessTools.TypeByName(type);
		return declaring == null ? null : AccessTools.Method(declaring, method, types);
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> code = SurfaceIdentity.RouteThroughIsSurface(instructions, out int count);
		swapped += count;
		return code;
	}
}
