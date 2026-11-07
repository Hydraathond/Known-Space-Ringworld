using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Cloud checks: where the cloud band reaches, what it draws with, and when it shows. How it looks, that night shadows
// darken it, that it fades smoothly, and that clicking or hovering tiles still works are checked by eye.
public static partial class RingSelfCheck
{
	// Neighbouring cloud-band vertices round the ring are at most this many columns apart, so it leaves no gap.
	private const float MaxCloudStepColumns = 1.5f;

	// The cloud band reaches the rim walls' inner faces to within this many hexes.
	private const float CloudRimWallReachTolerance = 0.01f;

	// The cloud band runs round the full loop whatever the land coverage, the stand-in span included, between the inner
	// surface and the rim-wall tops, out to the rim walls.
	private static void CheckCloudBand(RingLayer ring, RingCheckResult result)
	{
		if (!TryGetClouds(ring, result, out WorldDrawLayer_RingClouds clouds))
		{
			return;
		}
		RingGeometry geometry = ring.Geometry;
		List<Vector3> verts = clouds.Vertices.ToList();
		if (verts.Count == 0)
		{
			result.Fail("the cloud band mesh is empty");
			return;
		}
		float columnsPerRadian = geometry.tilesAround / (2f * Mathf.PI);
		List<float> around = verts.Select(vert => Mathf.Repeat(RingGeometry.AngleOf(vert) * columnsPerRadian, geometry.tilesAround)).OrderBy(c => c).ToList();
		// Wrapping round from the last position to the first closes the loop.
		around.Add(around[0] + geometry.tilesAround);
		for (int i = 1; i < around.Count; i++)
		{
			if (around[i] - around[i - 1] > MaxCloudStepColumns)
			{
				result.Fail($"the cloud band has a gap from column {around[i - 1]:F2} to {Mathf.Repeat(around[i], geometry.tilesAround):F2}");
				break;
			}
		}
		float wallTopRadius = ring.Radius - WorldDrawLayer_RingThickness.WallHeight * geometry.tileSize;
		float minY = float.MaxValue;
		float maxY = float.MinValue;
		bool reported = false;
		foreach (Vector3 vert in verts)
		{
			float radius = RingGeometry.DistanceFromAxis(vert);
			if (!reported && (radius >= ring.Radius || radius <= wallTopRadius))
			{
				result.Fail($"a cloud-band vertex is {radius:F2} from the axis, outside the inner surface {ring.Radius:F2} to the rim-wall tops {wallTopRadius:F2}");
				reported = true;
			}
			minY = Mathf.Min(minY, vert.y);
			maxY = Mathf.Max(maxY, vert.y);
		}
		float reach = geometry.RimLine - CloudRimWallReachTolerance * geometry.tileSize;
		if (minY > -reach || maxY < reach || minY < -geometry.HalfWidth || maxY > geometry.HalfWidth)
		{
			result.Fail($"the cloud band runs across from y {minY:F2} to {maxY:F2}, the rim walls stand at +-{geometry.RimLine:F2}");
		}
	}

	// The cloud band draws with the ring's copy of vanilla's Clouds shader, with vanilla's noise, sunset and cloud textures.
	private static void CheckCloudShader(RingLayer ring, RingCheckResult result)
	{
		if (!RingMaterials.RingShaderLoaded(WorldDrawLayer_RingClouds.ShaderName))
		{
			result.Fail($"the ring shader {WorldDrawLayer_RingClouds.ShaderName} didn't load");
		}
		if (!TryGetClouds(ring, result, out WorldDrawLayer_RingClouds clouds))
		{
			return;
		}
		Material material = clouds.Material;
		if (material == null)
		{
			result.Fail("the cloud band has no material");
			return;
		}
		if (RingMaterials.ShaderDefName(material) != WorldDrawLayer_RingClouds.ShaderName)
		{
			result.Fail($"the cloud band draws with {RingMaterials.ShaderDefName(material)}, not {WorldDrawLayer_RingClouds.ShaderName}");
		}
		// By name: ContentFinder finds nothing off the main thread, so names hold wherever the texture was loaded.
		foreach ((int id, string name, string path) in new[]
		{
			(ShaderPropertyIDs.NoiseTex, "noise", "Other/Perlin"),
			(ShaderPropertyIDs.SunsetColorRamp, "sunset", "World/SunsetGradient"),
			(ShaderPropertyIDs.CloudMap, "cloud", "World/CloudMap"),
		})
		{
			Texture actual = material.GetTexture(id);
			if (actual == null || actual.name != path.Substring(path.LastIndexOf('/') + 1))
			{
				result.Fail($"the cloud band's {name} texture is {actual?.name ?? "none"}, not vanilla's {path}");
			}
		}
	}

	// Both cloud samples repeat a whole number of times round the loop (no seam), their counts differ, and the material's
	// density scale follows the density setting: none at 0%, twice at 200% what it is at 100%, more than v0.1.1's 1 at 100%.
	private static void CheckCloudVariety(RingLayer ring, RingCheckResult result)
	{
		if (!TryGetClouds(ring, result, out WorldDrawLayer_RingClouds clouds) || clouds.Material == null)
		{
			return;
		}
		Material material = clouds.Material;
		float first = material.GetFloat(WorldDrawLayer_RingClouds.Repeats1ID);
		float second = material.GetFloat(WorldDrawLayer_RingClouds.Repeats2ID);
		if (first < 1f || second < 1f || first != Mathf.Round(first) || second != Mathf.Round(second))
		{
			result.Fail($"the cloud samples repeat {first} and {second} times round the loop, not whole numbers of at least 1");
		}
		if (first == second)
		{
			result.Fail($"both cloud samples repeat {first} times round the loop, so they would line up");
		}
		float actual = material.GetFloat(WorldDrawLayer_RingClouds.DensityScaleID);
		float expected = WorldDrawLayer_RingClouds.DensityScaleFor(WorldShape.settings.cloudDensity);
		if (Mathf.Abs(actual - expected) > 0.0001f)
		{
			result.Fail($"the clouds' density scale is {actual}, expected {expected} for the {WorldShape.settings.cloudDensity:P0} density setting");
		}
		float none = WorldDrawLayer_RingClouds.DensityScaleFor(0f);
		float normal = WorldDrawLayer_RingClouds.DensityScaleFor(1f);
		float doubled = WorldDrawLayer_RingClouds.DensityScaleFor(2f);
		if (none != 0f || normal <= 1f || Mathf.Abs(doubled - 2f * normal) > 0.0001f)
		{
			result.Fail($"the clouds' density scale is {none}, {normal} and {doubled} at 0%, 100% and 200%, expected 0, more than v0.1.1's 1, and twice the 100% value");
		}
	}

	// Vanilla's rule for when clouds show, pinned case by case: none in the ring view, of the ring or of an atmosphere
	// layer, full as an orbit-map background behind the ring view and seen from any other layer (the orbit view).
	private static void CheckCloudOpacity(RingLayer ring, RingCheckResult result)
	{
		List<(string view, PlanetLayer selected, bool background, float expected)> cases = new List<(string, PlanetLayer, bool, float)>
		{
			("the ring view", ring, false, 0f),
			("an orbit-map background with the ring selected", ring, true, 1f),
		};
		foreach (PlanetLayer layer in Find.WorldGrid.PlanetLayers.Values.Where(layer => layer != ring))
		{
			bool atmosphere = RingAtmosphere.IsAtmosphere(layer.Def);
			cases.Add(($"the {(atmosphere ? "ring" : "orbit")} view from layer {layer.LayerID}", layer, false, atmosphere ? 0f : 1f));
			cases.Add(($"an orbit-map background from layer {layer.LayerID}", layer, true, 1f));
		}
		foreach ((string view, PlanetLayer selected, bool background, float expected) in cases)
		{
			float actual = WorldDrawLayer_RingClouds.TargetOpacity(ring, selected, background);
			if (actual != expected)
			{
				result.Fail($"the clouds' opacity in {view} is {actual}, expected {expected}");
			}
		}
	}

	// The haze shares the clouds' material, so it fades with their opacity (the material takes it) and, drawn at 0% density
	// too, never follows the density setting. Its strength stays under 0.5, well below opaque, with both shares above 0, and
	// its rim line lies within the ring's half-width.
	private static void CheckCloudHaze(RingLayer ring, RingCheckResult result)
	{
		if (!TryGetClouds(ring, result, out WorldDrawLayer_RingClouds clouds) || clouds.Material == null)
		{
			return;
		}
		Material material = clouds.Material;
		if (!material.HasProperty(ShaderPropertyIDs.CloudShaderOpacity))
		{
			result.Fail("the cloud material has no opacity property, so the haze couldn't fade with the clouds");
		}
		float baseAlpha = material.GetFloat(WorldDrawLayer_RingClouds.HazeBaseID);
		float edgeAlpha = material.GetFloat(WorldDrawLayer_RingClouds.HazeEdgeID);
		float rimLine = material.GetFloat(WorldDrawLayer_RingClouds.RimLineID);
		if (baseAlpha <= 0f || edgeAlpha <= 0f || baseAlpha + edgeAlpha >= 0.5f)
		{
			result.Fail($"the haze's base {baseAlpha} and rim {edgeAlpha} strengths aren't both above 0 with a sum below 0.5");
		}
		if (rimLine <= 0f || rimLine > ring.Geometry.HalfWidth)
		{
			result.Fail($"the haze's rim line is {rimLine}, outside 0 to the ring's half-width {ring.Geometry.HalfWidth}");
		}
	}

	private static bool TryGetClouds(RingLayer ring, RingCheckResult result, out WorldDrawLayer_RingClouds clouds)
	{
		clouds = ring.WorldDrawLayers.OfType<WorldDrawLayer_RingClouds>().FirstOrDefault();
		if (clouds == null)
		{
			result.Fail("the ring layer has no cloud draw layer");
			return false;
		}
		BuildIfNeeded(clouds);
		return true;
	}
}
