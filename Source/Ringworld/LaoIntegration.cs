using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Layered Atmosphere and Orbit (LAO) is a soft dependency (ADR 0003). Nothing compiles against it: its types are looked
// up by name, and its extension reaches the ring's layer def through Patches/LayeredAtmosphereOrbit.xml, which only
// applies when LAO is loaded. Planet worlds are left to LAO. On a ring world the integration is active (ActiveOn)
// whenever LAO is loaded and the integration checked out at startup: the world has Odyssey's orbit (LAO's Orbit) and
// the layers ticked in LAO's own layer list that a ring supports (ADR 0006), chained by LAO's elevations. Ticked layers
// are added when the world is created or loaded, and unticked ones are never removed. Ringworld only reads LAO's list
// (the dev harness's @quicktest replaces it in memory, never on disk). Where the orbit layers sit comes from RingOrbitExtension, which the same XML patch adds to their
// layer defs; a ring world made without LAO has its single orbit moved there when it's loaded with LAO (RingOrbit).
// LAO's atmosphere layers are built as rings on a ring world (ADR 0005, RingAtmosphere), at the heights the same XML
// patch gives their defs (RingAtmosphereExtension). LAO's Luna assumes a sphere and is left out. Where LAO's code
// assumes a sphere, LaoCounterPatches corrects it on ring worlds. LaoLayerTabText gives LAO's Layer tab the ring's own
// texts on ring worlds; it's only text, so if it fails it logs its own warning and the orbit stack is left alone.
// If any part of the integration fails, one warning is logged and ring worlds keep the single orbit; if it's the ring's
// own extension that's missing, the exceptions LAO then throws while finishing a ring map are dropped too, so a ring
// world always loads. Without LAO none of this runs or logs.
//   RW_DEV_LAO_FAIL=1  takes LAO's extension off the ring's layer def at startup, as if the XML patch had failed
[StaticConstructorOnStartup]
public static class LaoIntegration
{
	private const string PackageId = "MrHydralisk.LayeredAtmosphereOrbit";

	private const string ExtensionTypeName = "LayeredAtmosphereOrbit.LayeredAtmosphereOrbitDefModExtension";

	private const string ModTypeName = "LayeredAtmosphereOrbit.LAOMod";

	internal const string SurfaceGroup = "LAO_Surface";

	private const string Namespace = "LayeredAtmosphereOrbit";

	// LAO's High Orbit: its layer, settings and scenario part defs, and the scenario tag LAO gives it, all have this name.
	public const string HighOrbitName = "LAO_HighOrbit";

	public static readonly bool Active = ModsConfig.IsActive(PackageId);

	// LAO is active and the whole integration checked out at startup, so ring worlds get the orbit stack.
	public static bool OrbitStackAvailable { get; private set; }

	// LAO's atmosphere layers, built as rings on a ring world (RingAtmosphere), lowest first.
	public static readonly string[] AtmosphereLayerNames = { "LAO_Troposphere", "LAO_Stratosphere", "LAO_Mesosphere" };

	// LAO's optional layers a ring world can have, when they're ticked in LAO's layer list: its atmosphere layers and High
	// Orbit.
	private static readonly string[] RingOptionalLayerNames = AtmosphereLayerNames.Append(HighOrbitName).ToArray();

	// LAO's layer list in its settings: the layer defNames it adds to every game. Only read, but for the dev harness's
	// in-memory override (OverrideLayerListInMemory).
	private const string LayerListField = "AutoAddLayersDefNames";

	private static FieldInfo elevationField;

	// Each of RingOptionalLayerNames' layer, settings and scenario part defs.
	// Filled only once the whole check passes.
	private static List<OptionalLayerDefs> optionalLayerDefs = new List<OptionalLayerDefs>();

	// One of LAO's optional layers: its layer, settings and scenario part defs, which share its defName.
	private sealed record OptionalLayerDefs(PlanetLayerDef Layer, PlanetLayerSettingsDef Settings, ScenPartDef Part);

	private static PropertyInfo settingsProperty;

	// The ring's layer def carries LAO's Surface-group extension, which keeps LAO's code from crashing on ring maps.
	private static bool ringExtensionArrived;

	static LaoIntegration()
	{
		if (!Active)
		{
			return;
		}
		Harmony harmony = new Harmony("ringworld.lao");
		string problem = Guarded(() => Check(Environment.GetEnvironmentVariable("RW_DEV_LAO_FAIL") == "1"));
		// Applied whether or not the orbit stack checks out: a ring world with a single orbit needs them too.
		string counterPatchProblem = Guarded(() => LaoCounterPatches.Apply(harmony));
		problem ??= counterPatchProblem;
		if (problem == null)
		{
			problem = Guarded(() => RingAtmosphere.Apply(harmony));
		}
		OrbitStackAvailable = problem == null;
		if (problem != null)
		{
			Log.Warning($"[Ringworld] Layered Atmosphere and Orbit integration failed ({problem}); ring worlds keep a single orbit.");
		}
		// Only text, so a failure here leaves the orbit stack alone.
		string tabTextProblem = Guarded(() => LaoLayerTabText.Apply(harmony));
		if (tabTextProblem != null)
		{
			Log.Warning($"[Ringworld] Layered Atmosphere and Orbit's Layer tab not patched ({tabTextProblem}); it describes ring worlds with LAO's planet texts.");
		}
		if (!ringExtensionArrived)
		{
			harmony.Patch(AccessTools.Method(typeof(Map), nameof(Map.FinalizeInit)), finalizer: new HarmonyMethod(typeof(LaoIntegration), nameof(DropLaoExceptionOnRing)));
		}
	}

	// What a step of the integration found wrong, or what it threw.
	private static string Guarded(Func<string> step)
	{
		try
		{
			return step();
		}
		catch (Exception e)
		{
			return e.Message;
		}
	}

	// Null if the ring's layer def carries LAO's extension in LAO's Surface group, and everything the orbit stack needs
	// is there: the optional layers' defs, every orbit layer def with LAO's extension and RingOrbitExtension, every
	// atmosphere layer def with LAO's extension and RingAtmosphereExtension, and LAO's fuel settings and layer list.
	// Otherwise what's wrong. What it finds by reflection or by name is kept for RingParts.
	private static string Check(bool simulateFailure)
	{
		Type extension = AccessTools.TypeByName(ExtensionTypeName);
		if (simulateFailure && extension != null)
		{
			DefDatabase<PlanetLayerDef>.GetNamed("RW_Ring").modExtensions?.RemoveAll(extension.IsInstanceOfType);
		}
		if (extension == null)
		{
			return $"type {ExtensionTypeName} not found";
		}
		FieldInfo group = AccessTools.Field(extension, "planetLayerGroup");
		if (group == null)
		{
			return $"{ExtensionTypeName} has no planetLayerGroup";
		}
		PlanetLayerDef ring = DefDatabase<PlanetLayerDef>.GetNamed("RW_Ring");
		DefModExtension onRing = ring.modExtensions?.FirstOrDefault(extension.IsInstanceOfType);
		if (onRing == null)
		{
			return $"{ring.defName} has no {extension.Name}";
		}
		if ((group.GetValue(onRing) as Def)?.defName != SurfaceGroup)
		{
			return $"{ring.defName} is not in {SurfaceGroup}";
		}
		ringExtensionArrived = true;
		elevationField = AccessTools.Field(extension, "elevation");
		if (elevationField == null)
		{
			return $"{ExtensionTypeName} has no elevation";
		}
		List<OptionalLayerDefs> optional = new List<OptionalLayerDefs>();
		foreach (string name in RingOptionalLayerNames)
		{
			OptionalLayerDefs defs = new OptionalLayerDefs(DefDatabase<PlanetLayerDef>.GetNamedSilentFail(name), DefDatabase<PlanetLayerSettingsDef>.GetNamedSilentFail(name), DefDatabase<ScenPartDef>.GetNamedSilentFail(name));
			if (defs.Layer == null || defs.Settings == null || defs.Part == null)
			{
				return $"{name}'s layer, settings or scenario part def not found";
			}
			optional.Add(defs);
		}
		foreach (PlanetLayerDef layer in optional.Select(defs => defs.Layer).Prepend(PlanetLayerDefOf.Orbit))
		{
			Type ringExtension = AtmosphereLayerNames.Contains(layer.defName) ? typeof(RingAtmosphereExtension) : typeof(RingOrbitExtension);
			if (layer.modExtensions?.Any(ringExtension.IsInstanceOfType) != true)
			{
				return $"{layer.defName} has no {ringExtension.Name}";
			}
			if (layer.modExtensions.FirstOrDefault(extension.IsInstanceOfType) == null)
			{
				return $"{layer.defName} has no {extension.Name}";
			}
		}
		settingsProperty = AccessTools.Property(AccessTools.TypeByName(ModTypeName), "Settings");
		if (settingsProperty == null)
		{
			return $"{ModTypeName}.Settings not found";
		}
		foreach (string name in new[] { "UseFuelCostBetweenLayers", "FuelPerKm", LayerListField })
		{
			if (AccessTools.Field(settingsProperty.PropertyType, name) == null)
			{
				return $"LAO's settings have no {name}";
			}
		}
		optionalLayerDefs = optional;
		return null;
	}

	// Only when the ring's extension didn't arrive: without it, LAO's postfix on Map.FinalizeInit throws on every ring map (it finds no planet
	// for the ring), after vanilla has finished the map. Only exceptions thrown from LAO's code, on ring worlds, are
	// dropped; the warning at startup has already said why.
	private static Exception DropLaoExceptionOnRing(Exception __exception)
	{
		if (__exception != null && SurfaceRing.InRingWorld && __exception.TargetSite?.DeclaringType?.Namespace == Namespace)
		{
			return null;
		}
		return __exception;
	}

	// Whether LAO's integration is active on the grid's world: a ring world, with LAO loaded and the integration
	// working. Such a world has Odyssey's orbit in the orbit stack and every ticked optional layer (RingParts).
	public static bool ActiveOn(WorldGrid grid)
	{
		return OrbitStackAvailable && SurfaceRing.Of(grid) != null;
	}

	// The optional layer defs a ring world can have with LAO, ticked or not; none if the integration failed.
	public static IEnumerable<PlanetLayerDef> RingOptionalLayers => OrbitStackAvailable ? optionalLayerDefs.Select(defs => defs.Layer) : Enumerable.Empty<PlanetLayerDef>();

	// Whether a ring world can have a layer of the def as one of LAO's optional layers, even if the integration failed
	// (a world made while it worked keeps them).
	public static bool IsRingOptionalLayer(PlanetLayerDef def)
	{
		return RingOptionalLayerNames.Contains(def.defName);
	}

	// Dev harness only (@quicktest): replaces LAO's layer list in memory with these defNames, or with every layer LAO's
	// settings offer when null, and returns the list now in place. LAO's settings file isn't written, and players' games
	// never call this.
	internal static List<string> OverrideLayerListInMemory(IEnumerable<string> defNames)
	{
		Type mod = AccessTools.TypeByName(ModTypeName);
		List<string> layers = (defNames ?? ((IEnumerable<PlanetLayerDef>)AccessTools.Field(mod, "AutoAddLayerOptions").GetValue(null)).Select(def => def.defName)).ToList();
		Traverse.Create((settingsProperty ?? AccessTools.Property(mod, "Settings")).GetValue(null)).Field(LayerListField).SetValue(layers);
		return layers;
	}

	// Those of RingOptionalLayers ticked in LAO's layer list now.
	public static IEnumerable<PlanetLayerDef> TickedRingOptionalLayers => TickedOptionalLayerDefs().Select(defs => defs.Layer);

	private static IEnumerable<OptionalLayerDefs> TickedOptionalLayerDefs()
	{
		if (!OrbitStackAvailable)
		{
			return Enumerable.Empty<OptionalLayerDefs>();
		}
		List<string> ticked = Traverse.Create(settingsProperty.GetValue(null)).Field(LayerListField).GetValue<List<string>>();
		return optionalLayerDefs.Where(defs => ticked?.Contains(defs.Layer.defName) == true);
	}

	// The scenario's parts as a ring world builds its layers from them while LAO is active, new or loaded: every planet
	// layer but the surface and Odyssey's orbit is left out (other mods' layers too: with LAO they're LAO's planets,
	// also built as spheres round the sun), and while the integration works each of RingOptionalLayers ticked in LAO's
	// layer list is put in, as LAO adds it to a planet's scenario. Vanilla makes the layers the world lacks and keeps
	// the rest as they were saved, so an unticked layer stays. Without Odyssey's orbit no optional layer is put in. The
	// layers are chained ring first, then by LAO's elevation, each zooming out to the next and in to the one before,
	// with LAO's climb fuel cost on each zoom-out connection. LAO adds its layers to every scenario and rewires their
	// connections; the parts and their connections are shared with the scenario def, so the kept layers are copies.
	public static List<ScenPart> RingParts(List<ScenPart> parts, ScenPart_PlanetLayer surface, out ScenPart_PlanetLayer ringSurface)
	{
		ScenPart_PlanetLayer orbit = parts.OfType<ScenPart_PlanetLayer>().FirstOrDefault(part => part.layer == PlanetLayerDefOf.Orbit);
		List<ScenPart_PlanetLayer> above = new List<ScenPart_PlanetLayer>();
		if (orbit != null)
		{
			above.Add(orbit);
			foreach (OptionalLayerDefs defs in TickedOptionalLayerDefs())
			{
				above.Add(new ScenPart_PlanetLayerFixed
				{
					def = defs.Part,
					layer = defs.Layer,
					settingsDef = defs.Settings,
					hide = true,
					tag = defs.Layer.defName,
				});
			}
			above.SortBy(part => Elevation(part.layer));
		}
		List<ScenPart_PlanetLayer> chain = above.Prepend(surface).ToList();
		List<ScenPart_PlanetLayer> copies = chain.Select((part, i) => LinkedCopy(part, i > 0 ? chain[i - 1] : null, i + 1 < chain.Count ? chain[i + 1] : null)).ToList();
		ringSurface = copies[0];
		List<ScenPart> kept = new List<ScenPart>();
		foreach (ScenPart part in parts)
		{
			if (part == orbit)
			{
				kept.Add(copies[chain.IndexOf(orbit)]);
			}
			else if (!(part is ScenPart_PlanetLayer))
			{
				kept.Add(part);
			}
		}
		kept.AddRange(copies.Where((copy, i) => i > 0 && chain[i] != orbit));
		return kept;
	}

	// A copy of part whose only connections zoom in to inner and out to outer (either may be null). LAO has usually
	// already removed the part's own connections to them; if not, their fuel costs are kept, unless LAO's climb fuel
	// cost is on, which sets the zoom-out one.
	private static ScenPart_PlanetLayer LinkedCopy(ScenPart_PlanetLayer part, ScenPart_PlanetLayer inner, ScenPart_PlanetLayer outer)
	{
		ScenPart_PlanetLayer copy = part is ScenPart_PlanetLayerFixed ? new ScenPart_PlanetLayerFixed() : new ScenPart_PlanetLayer();
		copy.def = part.def;
		copy.layer = part.layer;
		copy.settingsDef = part.settingsDef;
		copy.hide = part.hide;
		copy.tag = part.tag;
		if (inner != null)
		{
			copy.connections.Add(new LayerConnection { tag = inner.tag, fuelCost = ExistingFuelCost(part, inner), zoomMode = LayerConnection.ZoomMode.ZoomIn });
		}
		if (outer != null)
		{
			float fuelCost = ClimbFuelCost(part.layer, outer.layer) ?? ExistingFuelCost(part, outer);
			copy.connections.Add(new LayerConnection { tag = outer.tag, fuelCost = fuelCost, zoomMode = LayerConnection.ZoomMode.ZoomOut });
		}
		return copy;
	}

	// The fuel cost of part's own connection to other, if it has one.
	private static float ExistingFuelCost(ScenPart_PlanetLayer part, ScenPart_PlanetLayer other)
	{
		return part.connections.FirstOrDefault(connection => connection.tag == other.tag)?.fuelCost ?? 0f;
	}

	// LAO's climb fuel cost from one layer out to the next, as LAO sets it on a zoom-out connection when its fuel cost
	// between layers is on: the difference in their elevations times its fuel per km. Null when that's off, or the
	// integration failed.
	private static float? ClimbFuelCost(PlanetLayerDef from, PlanetLayerDef to)
	{
		if (!OrbitStackAvailable)
		{
			return null;
		}
		Traverse laoSettings = Traverse.Create(settingsProperty.GetValue(null));
		if (!laoSettings.Field("UseFuelCostBetweenLayers").GetValue<bool>())
		{
			return null;
		}
		return (Elevation(to) - Elevation(from)) * laoSettings.Field("FuelPerKm").GetValue<float>();
	}

	// LAO's elevation for a layer without its extension, in km.
	private const float DefaultElevation = 200f;

	// LAO's elevation of a layer, in km; the default if the integration couldn't find LAO's elevation field.
	internal static float Elevation(PlanetLayerDef def)
	{
		if (elevationField == null)
		{
			return DefaultElevation;
		}
		DefModExtension extension = def.modExtensions?.FirstOrDefault(elevationField.DeclaringType.IsInstanceOfType);
		return extension != null ? (float)elevationField.GetValue(extension) : DefaultElevation;
	}
}
