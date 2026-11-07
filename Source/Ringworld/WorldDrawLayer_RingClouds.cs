using System.Collections;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// The ring's clouds and blue haze, in place of vanilla's WorldDrawLayer_Clouds sphere: one band round the full loop, the
// stand-in span included, CloudAltitude inside the inner surface (vanilla's cloud altitude, well below the rim-wall tops) and reaching
// rim wall to rim wall. Visual only: it builds no colliders, so clicking and hovering find the tiles under it.
[StaticConstructorOnStartup]
public class WorldDrawLayer_RingClouds : WorldDrawLayer, IRingNativeDrawLayer
{
	// In world units towards the axis from the inner surface, as vanilla lifts its clouds off the planet.
	public const float CloudAltitude = WorldAltitudeOffsets.Clouds;

	// Ring positions per GetSubMesh call. GetSubMesh starts a new submesh only once one holds 40000 verts, so each
	// call adds just a little, keeping every submesh under 16-bit indices.
	private const int PositionsPerChunk = 4096;

	// Seconds to fade in or out, as vanilla's.
	private const float OpacityChangeTime = 0.15f;

	// The ring's copy of vanilla's Clouds shader (Defs/ShaderDefs/Shaders_Ring.xml).
	public const string ShaderName = "RW_RingClouds";

	// Vanilla's planet radius, which its cloud map is sized for.
	private const float VanillaRadius = 100f;

	// Scale on the raw cloud map at 100% density; above 1 for more clouds than v0.1.1 had. Tuned by eye.
	private const float BaseDensityScale = 1.25f;

	// The blue haze, drawn by the same shader and fading with the clouds. Alpha of the even base tint, and of the share
	// added at the rim walls; their sum is the haze's most opaque, kept well below 1. Tuned by eye.
	private const float HazeBaseAlpha = 0.08f;

	private const float HazeEdgeAlpha = 0.30f;

	internal static readonly int RimLineID = Shader.PropertyToID("_RimLine");

	internal static readonly int HazeBaseID = Shader.PropertyToID("_HazeBase");

	internal static readonly int HazeEdgeID = Shader.PropertyToID("_HazeEdge");

	private static readonly int RingRadiusID = Shader.PropertyToID("_RingRadius");

	internal static readonly int Repeats1ID = Shader.PropertyToID("_Repeats1");

	internal static readonly int Repeats2ID = Shader.PropertyToID("_Repeats2");

	internal static readonly int DensityScaleID = Shader.PropertyToID("_DensityScale");

	// Vanilla's cloud textures, loaded on the main thread at startup, as ContentFinder finds nothing off it.
	private static readonly Texture2D NoiseTexture = ContentFinder<Texture2D>.Get("Other/Perlin");

	private static readonly Texture2D SunsetColorRamp = ContentFinder<Texture2D>.Get("World/SunsetGradient");

	private static readonly Texture2D CloudTexture = ContentFinder<Texture2D>.Get("World/CloudMap");

	private Material material;

	private float opacity = 1f;

	private float opacityVelocity;

	private bool opacitySet;

	public Material Material => material;

	// How many times the first cloud sample repeats round the loop: vanilla's texels per unit, rounded to whole repeats
	// so there's no seam.
	private static int PrimaryRepeats(float radius)
	{
		return Mathf.Max(1, Mathf.RoundToInt(radius / VanillaRadius));
	}

	// The second sample's repeat count: about 1.6 times the first, above it and sharing no factor with it, so the two
	// patterns line up only once round the loop.
	private static int SecondaryRepeats(float radius)
	{
		int primary = PrimaryRepeats(radius);
		int secondary = Mathf.Max(primary + 1, Mathf.RoundToInt(primary * 1.6f));
		while (Gcd(primary, secondary) != 1)
		{
			secondary++;
		}
		return secondary;
	}

	private static int Gcd(int a, int b)
	{
		return b == 0 ? a : Gcd(b, a % b);
	}

	// The shader's coverage for a density setting (1 = 100%); 0 gives none.
	internal static float DensityScaleFor(float density)
	{
		return BaseDensityScale * Mathf.Max(0f, density);
	}

	// Follows the mod option live, every frame.
	private void ApplyDensity(float density)
	{
		material?.SetFloat(DensityScaleID, DensityScaleFor(density));
	}

	// The haze's strengths and the rim walls' distance from the midline, which its rim share grows towards.
	private void ApplyHaze(RingGeometry geometry)
	{
		material.SetFloat(RimLineID, geometry.RimLine);
		material.SetFloat(HazeBaseID, HazeBaseAlpha);
		material.SetFloat(HazeEdgeID, HazeEdgeAlpha);
	}

	private static float CurrentDensity => WorldShape.settings?.cloudDensity ?? RingworldSettings.DefaultCloudDensity;

	// Every vertex of the cloud mesh, layer-local.
	public IEnumerable<Vector3> Vertices
	{
		get
		{
			foreach (LayerSubMesh subMesh in subMeshes)
			{
				foreach (Vector3 vert in subMesh.verts)
				{
					yield return vert;
				}
			}
		}
	}

	public override IEnumerable Regenerate()
	{
		foreach (object item in base.Regenerate())
		{
			yield return item;
		}
		if (planetLayer is RingLayer { Geometry: RingGeometry geometry } && !RingDrawLayerBuild.Run(this, () => BuildWithMaterial(geometry)))
		{
			yield break;
		}
		FinalizeMesh(MeshParts.All);
	}

	// The material is made on the first build. It copies vanilla's deferred clouds material, which can only be made on the
	// main thread; a failed attempt throws before the field is set, so the next build tries again.
	private void BuildWithMaterial(RingGeometry geometry)
	{
		material ??= NewMaterial();
		material.SetFloat(RingRadiusID, geometry.radius);
		material.SetFloat(Repeats1ID, PrimaryRepeats(geometry.radius));
		material.SetFloat(Repeats2ID, SecondaryRepeats(geometry.radius));
		ApplyHaze(geometry);
		ApplyDensity(CurrentDensity);
		Build(geometry);
	}

	// Vanilla's rule (WorldDrawLayer_Clouds.GetTargetOpacity): no clouds in the ring view, so they never hide the ground
	// being worked on, and full clouds when the ring is seen from another layer or behind an orbit map. The ring view of
	// an atmosphere layer (another ring-shaped layer) is a ring view too: it looks down at the same ground.
	public static float TargetOpacity(PlanetLayer ring, PlanetLayer selected, bool background)
	{
		return (selected == ring || selected is RingLayer) && !background ? 0f : 1f;
	}

	// Fades towards the target opacity as vanilla does, starting at it on the first frame.
	public override void Render()
	{
		float target = TargetOpacity(planetLayer, PlanetLayer.Selected, WorldRendererUtility.WorldBackgroundNow);
		opacity = opacitySet ? Mathf.SmoothDamp(opacity, target, ref opacityVelocity, OpacityChangeTime) : target;
		opacitySet = true;
		ApplyDensity(CurrentDensity);
		material?.SetFloat(ShaderPropertyIDs.CloudShaderOpacity, opacity);
		// At 0% density the clouds have no cover, but the haze stays, so the band is still drawn.
		base.Render();
	}

	// Vanilla's cloud material, seed and textures, drawn with the ring's copy of its shader in vanilla's render queue.
	private static Material NewMaterial()
	{
		Material created = new Material(WorldMaterials.Clouds);
		Shader shader = RingMaterials.RingShader(ShaderName);
		if (shader != null)
		{
			created.shader = shader;
		}
		created.renderQueue = WorldMaterials.Clouds.renderQueue;
		created.SetVector(ShaderPropertyIDs.Seed, new Vector2(Random.value, Random.value));
		created.SetTexture(ShaderPropertyIDs.NoiseTex, NoiseTexture);
		created.SetTexture(ShaderPropertyIDs.SunsetColorRamp, SunsetColorRamp);
		created.SetTexture(ShaderPropertyIDs.CloudMap, CloudTexture);
		return created;
	}

	// One quad per column round the full loop; the last position wraps to 0, so the band closes exactly at 0 degrees.
	private void Build(RingGeometry geometry)
	{
		int positions = geometry.tilesAround;
		float radius = geometry.radius - CloudAltitude;
		float y = geometry.RimLine;
		for (int start = 0; start < positions; start += PositionsPerChunk)
		{
			int end = Mathf.Min(start + PositionsPerChunk, positions);
			LayerSubMesh subMesh = GetSubMesh(material);
			int first = subMesh.verts.Count;
			for (int i = start; i <= end; i++)
			{
				Vector3 outward = RingGeometry.Outward(2f * Mathf.PI * (i % positions) / positions);
				subMesh.verts.Add(new Vector3(outward.x * radius, -y, outward.z * radius));
				subMesh.verts.Add(new Vector3(outward.x * radius, y, outward.z * radius));
			}
			for (int q = 0; q < end - start; q++)
			{
				int a = first + 2 * q;
				Vector3 facing = RingGeometry.InwardNormal(subMesh.verts[a]);
				WorldDrawLayer_RingThickness.AddFacing(subMesh, a, a + 2, a + 1, facing);
				WorldDrawLayer_RingThickness.AddFacing(subMesh, a + 1, a + 2, a + 3, facing);
			}
		}
	}
}
