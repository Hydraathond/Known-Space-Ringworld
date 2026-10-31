using System.Collections;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// The sun on the ring's axis: a glow at the ring's centre, drawn only in the orbit view of a ring world, from any orbit
// layer. It's a quad turned to face the camera, drawn with vanilla's sun glow (Defs/ShaderDefs/Shaders_Ring.xml,
// RW_RingSun), so it looks like a glowing sphere from every angle. It's additive and writes no depth, so the orbit
// layer can't hide it, but it is depth-tested, so the near half of the ring passes in front of it when seen edge-on.
// The ring view never shows it: there the camera sits on the axis looking outward, and the layer isn't visible anyway.
public class GlobalDrawLayer_RingSun : WorldDrawLayerBase
{
	// Of the ring's radius: well inside every orbit layer's sphere (half the ring radius for Odyssey's orbit), and easy
	// to see fully zoomed out.
	public const float GlowRadiusFactor = 0.15f;

	private static Material material;

	private float builtGlowRadius = -1f;

	// How far from the centre the glow reaches; it fades to nothing there.
	public static float GlowRadius(RingLayer ring)
	{
		return ring.Radius * GlowRadiusFactor;
	}

	private static Material Material => material ??= new Material(ShaderDatabase.LoadShader("World/RW_RingSun"));

	public override bool Visible => base.Visible && OrbitViewCamera.TryGetActive(out _, out _);

	public override bool ShouldRegenerate => base.ShouldRegenerate || (Visible && SurfaceRing.Current is { } ring && builtGlowRadius != GlowRadius(ring));

	public override Vector3 Position => Find.WorldGrid.Surface.Origin;

	// The quad lies in its local XY plane; face it to the camera.
	protected override Quaternion Rotation
	{
		get
		{
			Transform camera = Find.WorldCamera.transform;
			return Quaternion.LookRotation(camera.forward, camera.up);
		}
	}

	public override IEnumerable Regenerate()
	{
		foreach (object item in base.Regenerate())
		{
			yield return item;
		}
		if (SurfaceRing.Current is not { } ring)
		{
			yield break;
		}
		builtGlowRadius = GlowRadius(ring);
		LayerSubMesh subMesh = GetSubMesh(Material);
		float r = builtGlowRadius;
		subMesh.verts.Add(new Vector3(-r, -r, 0f));
		subMesh.verts.Add(new Vector3(-r, r, 0f));
		subMesh.verts.Add(new Vector3(r, r, 0f));
		subMesh.verts.Add(new Vector3(r, -r, 0f));
		subMesh.uvs.Add(new Vector2(0f, 0f));
		subMesh.uvs.Add(new Vector2(0f, 1f));
		subMesh.uvs.Add(new Vector2(1f, 1f));
		subMesh.uvs.Add(new Vector2(1f, 0f));
		subMesh.tris.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
		FinalizeMesh(MeshParts.All);
	}
}
