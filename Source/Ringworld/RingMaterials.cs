using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// On ring worlds every material that uses one of vanilla's lit world shaders, other mods' included, draws with the
// ring's copy of that shader instead (Defs/ShaderDefs/Shaders_Ring.xml), and is switched back when a Planet world loads
// (ADR 0002: don't drop the switch thinking it's a bug). The copies all draw the night shadow (RingNightShadow), and those
// for terrain, ice, ocean and pollution also map their textures onto the unrolled ring: vanilla's texture by the
// direction from the planet centre, which magnifies their textures by ring radius / 40.
//
// Materials are switched in place. Sync sweeps every live material when the world changes; materials made later are
// switched as they're drawn (For). Other mods' own world shaders aren't copied, so they stay lit.
public static class RingMaterials
{
	// Vanilla shader def name -> the ring's copy.
	public static readonly IReadOnlyDictionary<string, string> RingShaderNames = new Dictionary<string, string>
	{
		{ "WorldTerrain", "RW_RingTerrain" },
		{ "WorldIce", "RW_RingIce" },
		{ "WorldOcean", "RW_RingOcean" },
		{ "WorldOverlayTransparentLitPollution", "RW_RingPollution" },
		{ "WorldOverlayTransparentLit", "RW_RingOverlayLit" },
		{ "Rivers", "RW_RingRivers" },
		{ "Roads", "RW_RingRoads" },
		{ "UngeneratedPlanetParts", "RW_RingUngeneratedPlanetParts" },
	};

	// The ring's copy -> vanilla shader def name.
	private static readonly Dictionary<string, string> VanillaShaderNames = RingShaderNames.ToDictionary(pair => pair.Value, pair => pair.Key);

	private static readonly Dictionary<string, Shader> LoadedShaders = new Dictionary<string, Shader>();

	// Shader -> its def name, so the many per-tile lookups don't build strings.
	private static readonly Dictionary<Shader, string> DefNames = new Dictionary<Shader, string>();

	private static readonly int RingRadiusID = Shader.PropertyToID("_RingRadius");

	// The ring radius the live materials were last switched for, or -1 if they were last switched back to vanilla.
	private static float switchedRadius = -1f;

	// The material to draw with on a ring world: the same material, switched to the ring's shader if it used a vanilla
	// lit world shader.
	public static Material For(Material material, RingLayer ring)
	{
		if (material != null)
		{
			SwitchToRing(material, ring.Radius, setRingRadius: false);
		}
		return material;
	}

	// Switches every live material to the ring's shaders if the world is a ring world, or back to vanilla's if not. Unless
	// forced, does nothing if they already were for this world; call it whenever the world may have changed.
	public static void Sync(World world, bool force = false)
	{
		float radius = SurfaceRing.Of(world?.grid) is { } ring ? ring.Radius : -1f;
		if (radius == switchedRadius && !force)
		{
			return;
		}
		switchedRadius = radius;
		foreach (Material material in LiveMaterials())
		{
			if (radius > 0f)
			{
				// Also sets the radius on materials switched for an earlier ring of another size.
				SwitchToRing(material, radius, setRingRadius: true);
			}
			else
			{
				SwitchToVanilla(material);
			}
		}
	}

	// One of the ring's shaders, by its def name, or null if it has no def. ShaderDatabase.LoadShader never returns null:
	// for a shader with no def it returns a late-bound placeholder, so the def is what shows it's there.
	public static Shader RingShader(string ringShaderName)
	{
		if (!LoadedShaders.TryGetValue(ringShaderName, out Shader shader))
		{
			shader = DefDatabase<ShaderDef>.GetNamedSilentFail(ringShaderName) == null ? null : ShaderDatabase.LoadShader("World/" + ringShaderName);
			LoadedShaders[ringShaderName] = shader;
		}
		return shader;
	}

	// Whether one of the ring's shaders loaded and runs on this machine.
	public static bool RingShaderLoaded(string ringShaderName)
	{
		Shader shader = RingShader(ringShaderName);
		return shader != null && shader.isSupported;
	}

	// Every material that exists now, apart from deferred ones: reading their shader would make them, and they're
	// switched when drawn.
	public static IEnumerable<Material> LiveMaterials()
	{
		return Resources.FindObjectsOfTypeAll<Material>().Where(material => material != null && !material.IsDeferredMaterial);
	}

	// The material's shader def name; shader names can be paths ("World/WorldTerrain").
	public static string ShaderDefName(Material material)
	{
		Shader shader = material.shader;
		if (shader == null)
		{
			return string.Empty;
		}
		if (!DefNames.TryGetValue(shader, out string defName))
		{
			string name = shader.name ?? string.Empty;
			defName = name.Substring(name.LastIndexOf('/') + 1);
			DefNames[shader] = defName;
		}
		return defName;
	}

	private static void SwitchToRing(Material material, float radius, bool setRingRadius)
	{
		string name = ShaderDefName(material);
		if (RingShaderNames.TryGetValue(name, out string ringShaderName))
		{
			Shader ringShader = RingShader(ringShaderName);
			if (ringShader == null)
			{
				return;
			}
			material.shader = ringShader;
			material.SetFloat(RingRadiusID, radius);
		}
		else if (setRingRadius && VanillaShaderNames.ContainsKey(name))
		{
			material.SetFloat(RingRadiusID, radius);
		}
	}

	private static void SwitchToVanilla(Material material)
	{
		if (VanillaShaderNames.TryGetValue(ShaderDefName(material), out string vanillaShaderName))
		{
			material.shader = ShaderDatabase.LoadShader("World/" + vanillaShaderName);
		}
	}
}

// Every world draw layer except pollution picks its submesh by material here, so this covers the terrain layer (all
// biomes, ice and ocean, landmark-masked tiles included), hills, rivers, roads, landmarks, the stand-in span, and any
// other layer drawing with these shaders, on every layer of a ring world.
[HarmonyPatch(typeof(WorldDrawLayerBase), "GetSubMesh", new[] { typeof(Material), typeof(int) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
public static class Patch_WorldDrawLayerBase_GetSubMesh
{
	// By the surface ring, not the draw layer's: materials are shared between layers and keep one _RingRadius (Sync).
	public static void Prefix(ref Material material)
	{
		if (SurfaceRing.Current is { } ring)
		{
			material = RingMaterials.For(material, ring);
		}
	}
}
