using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Ring-view camera checks: its furthest zoom, panning across the ring, and the facing it hands to and takes from the orbit view. That scrolling
// switches between the two views, and lands where it should, is checked by eye.
public static partial class RingSelfCheck
{
	// Fully zoomed out, the ring's full width must be at least this much smaller than the view's height.
	private const float RingViewMinFitMargin = 1.05f;

	// Ring sizes the furthest zoom is checked at: every tiles-across from the Create World page, at these tiles-around.
	private static readonly int[] RingViewCheckedTilesAround = { RingSize.MinTilesAround, RingSize.Default.tilesAround, RingSize.MaxTilesAround };

	// Screen shapes (width over height) the furthest zoom is checked at, besides the game's own: 16:9 and 21:9.
	private static readonly float[] RingViewCheckedAspects = { 16f / 9f, 21f / 9f };

	// How far a ring angle may move going to the orbit view's facing and back.
	private const float FacingToleranceDegrees = 0.01f;

	// Ring angles checked going to the orbit view's facing and back, evenly around the ring.
	private const int FacingSamples = 36;

	// At every ring size the Create World page offers, fully zoomed out, both rim walls' outer top corners are inside
	// the ring view, with a margin. The wall tops curve around the camera, so they come nearest it, and look tallest,
	// at the screen's left and right edges; the check measures them there. The wall's height (2 hexes) and its outer
	// side (a hex past the rim's hex tips, a little wider than drawn) are the spec's, written out rather than read from
	// the draw layer. Narrow rings keep the def's furthest zoom.
	private static void CheckRingViewFitsWidth(RingLayer ring, RingCheckResult result)
	{
		RingworldExtension settings = ring.Settings;
		float tanHalfFov = Mathf.Tan(settings.cameraFieldOfView * 0.5f * Mathf.Deg2Rad);
		ForEachCheckedRingView(ring, (size, geometry, aspect, farthest) =>
		{
			float hex = geometry.tileSize;
			float wallTopRadius = size.Radius - 2f * hex;
			float axisToCamera = size.Radius - farthest;
			// Where the screen's edge ray, at angle edge from the view direction, meets the wall tops' cylinder.
			float edge = Mathf.Atan(aspect * tanHalfFov);
			float along = Mathf.Sqrt(wallTopRadius * wallTopRadius - axisToCamera * axisToCamera * Mathf.Sin(edge) * Mathf.Sin(edge)) - axisToCamera * Mathf.Cos(edge);
			float halfHeight = along * Mathf.Cos(edge) * tanHalfFov;
			float wallOuterY = geometry.HalfWidth + hex;
			if (!(halfHeight >= wallOuterY * RingViewMinFitMargin))
			{
				result.Fail($"at {size} on a {aspect:F2}:1 screen, fully zoomed out the view's edges reach {halfHeight:F1} from the midline at the rim walls' tops; they reach {wallOuterY:F1}");
			}
			float defFarthest = Mathf.Min(settings.cameraMaxDistance, size.Radius);
			if (settings.cameraMaxDistance > 0f && farthest < defFarthest)
			{
				result.Fail($"at {size}, the furthest zoom is {farthest:F1}, short of the def's {defFarthest:F1}");
			}
		});
	}

	// How far the beyond-rim share may stray from 30% of the view's height.
	private const float PanShareTolerance = 0.005f;

	// The spec's share of the view's height that the area beyond a rim may take, written out rather than read from the camera.
	private const float SpecBeyondRimShare = 0.3f;

	// At every ring size and several zooms, panned as far as it goes the area beyond the rim takes 30% of the view's
	// height (or the limit is 0 because the width already fits); fully zoomed out the limit is 0; and jumping to a
	// wall-foot tile (the rim row, at either rim) leaves that tile inside the view, at the zoom the ring view starts at
	// and at the nearest zoom.
	private static void CheckRingViewPanAcross(RingLayer ring, RingCheckResult result)
	{
		RingworldExtension settings = ring.Settings;
		float tanHalfFov = Mathf.Tan(settings.cameraFieldOfView * 0.5f * Mathf.Deg2Rad);
		ForEachCheckedRingView(ring, (size, geometry, aspect, farthest) =>
		{
			float nearest = settings.cameraMinDistance;
			float[] distances = { nearest, settings.cameraStartDistance, Mathf.Lerp(nearest, farthest, 0.5f), Mathf.Lerp(nearest, farthest, 0.75f), farthest };
			foreach (float distance in distances)
			{
				float viewHeight = 2f * distance * tanHalfFov;
				// The rim line is where the rim row's hexes end (a hex tip's half corner radius inside HalfWidth); the wall begins there.
				float rimLine = geometry.HalfWidth - 0.5f * geometry.cornerRadius;
				float wallSide = rimLine + geometry.tileSize;
				float limit = RingCamera.AcrossLimit(settings, geometry, distance);
				if (limit < 0f)
				{
					result.Fail($"at {size}, {distance:F1} from the surface, the pan limit is {limit:F1}, below 0");
				}
				else if (limit > 0f)
				{
					float beyond = (limit + viewHeight * 0.5f - rimLine) / viewHeight;
					bool eased = viewHeight > 0.6f * 2f * wallSide;
					if (beyond > SpecBeyondRimShare + PanShareTolerance || (!eased && beyond < SpecBeyondRimShare - PanShareTolerance))
					{
						result.Fail($"at {size}, {distance:F1} from the surface, panned to the limit ({limit:F1}) the area beyond the rim takes {beyond:P1} of the view's height, not {SpecBeyondRimShare:P0}");
					}
				}
				float wallFootY = geometry.HalfWidth - geometry.cornerRadius;
				float panned = RingCamera.ClampAcross(settings, geometry, distance, wallFootY);
				if (Mathf.Abs(wallFootY - panned) + geometry.cornerRadius >= viewHeight * 0.5f)
				{
					result.Fail($"at {size}, {distance:F1} from the surface, a jump to a wall-foot tile ({wallFootY:F1} from the midline) pans to {panned:F1}, leaving it off screen or cut off (the view is {viewHeight:F1} tall)");
				}
			}
			float farthestLimit = RingCamera.AcrossLimit(settings, geometry, farthest);
			if (farthestLimit > 1E-4f)
			{
				result.Fail($"at {size} on a {aspect:F2}:1 screen, fully zoomed out the pan limit is {farthestLimit:F1}, not 0");
			}
		});
	}

	// At every ring size, and for the world's ring, the ring-view camera never backs off past the ring's axis.
	private static void CheckRingViewInsideRing(RingLayer ring, RingCheckResult result)
	{
		float worldFarthest = RingCamera.MaxDistance(ring);
		if (worldFarthest > ring.Radius)
		{
			result.Fail($"the furthest zoom is {worldFarthest:F1}, past the axis (ring radius {ring.Radius:F1})");
		}
		ForEachCheckedRingView(ring, (size, geometry, aspect, farthest) =>
		{
			if (farthest > size.Radius)
			{
				result.Fail($"at {size} on a {aspect:F2}:1 screen, the furthest zoom is {farthest:F1}, past the axis (ring radius {size.Radius:F1})");
			}
		});
	}

	// The spec's ground band end and the step between atmosphere bands on the default settings (40, then 40 / 60 / 90 / 135),
	// written out rather than worked from the camera's formula.
	private const float SpecGroundFurthest = 40f;

	private static readonly float[] SpecDefaultBands = { 40f, 60f, 90f, 135f };

	// How far the default settings' band edges may stray from the spec's rounded figures.
	private const float SpecBandTolerance = 0.03f;

	// The zoom bands (ZoomBands) at every ring size and screen shape: the bands are contiguous and in order, start at the
	// nearest zoom and end at F, which is G x the def's furthestZoomRatio or, where that would pass the axis, squeezed
	// onto it with the atmosphere bands still spanning equal factors. The ring view's stack follows the chain from the
	// surface ring, one band per layer. On the default settings, on a ring wide enough not to squeeze, three atmosphere
	// layers give the spec's 40 / 60 / 90 / 135.
	private static void CheckRingViewZoomBands(RingLayer ring, RingCheckResult result)
	{
		RingworldExtension settings = ring.Settings;
		ForEachCheckedRingView(ring, (size, geometry, aspect, ground) =>
		{
			float axis = size.Radius;
			float furthest = ground * settings.furthestZoomRatio;
			float squeeze = Mathf.Min(1f, axis / furthest);
			for (int layers = 1; layers <= 4; layers++)
			{
				float[] bands = RingCamera.ZoomBands(settings, geometry, axis, aspect, layers);
				string where = $"at {size} on a {aspect:F2}:1 screen with {layers - 1} atmosphere layer(s)";
				if (bands.Length != layers + 1)
				{
					result.Fail($"{where}, {bands.Length - 1} zoom bands, not {layers}");
					continue;
				}
				if (Mathf.Abs(bands[0] - settings.cameraMinDistance) > 1E-3f)
				{
					result.Fail($"{where}, the ground's band starts at {bands[0]:F2}, not the nearest zoom {settings.cameraMinDistance:F2}");
				}
				if (Mathf.Abs(bands[layers] - furthest * squeeze) > 1E-2f * furthest)
				{
					result.Fail($"{where}, the bands end at {bands[layers]:F1}, not {furthest * squeeze:F1}");
				}
				if (bands[layers] > axis + 1E-3f)
				{
					result.Fail($"{where}, the bands end at {bands[layers]:F1}, past the axis ({axis:F1})");
				}
				if (layers > 1 && Mathf.Abs(bands[1] - ground * squeeze) > 1E-2f * ground)
				{
					result.Fail($"{where}, the ground's band ends at {bands[1]:F1}, not {ground * squeeze:F1}");
				}
				// The Starting-site page zooms over the ground's band only: with atmosphere layers it ends short of F, without it is F.
				float pageFurthest = RingCamera.StartingSiteFurthest(bands);
				if ((layers == 1) != (Mathf.Abs(pageFurthest - bands[layers]) < 1E-3f))
				{
					result.Fail($"{where}, the Starting-site page's zoom ends at {pageFurthest:F1}, not the ground band's end {bands[1]:F1} (the stack's furthest is {bands[layers]:F1})");
				}
				for (int i = 1; i < layers; i++)
				{
					if (!(bands[i + 1] > bands[i] && bands[i] > bands[i - 1]))
					{
						result.Fail($"{where}, the bands aren't in order: edge {i - 1} is {bands[i - 1]:F2}, {i} is {bands[i]:F2}, {i + 1} is {bands[i + 1]:F2}");
					}
				}
				// Each atmosphere band spans the same factor, so a scroll tick moves through them evenly.
				for (int i = 2; i < layers; i++)
				{
					float step = bands[i + 1] / bands[i];
					float first = bands[2] / bands[1];
					if (Mathf.Abs(step - first) > 1E-2f * first)
					{
						result.Fail($"{where}, atmosphere band {i} spans a factor of {step:F3} but the first spans {first:F3}");
					}
				}
			}
		});
		bool defaultSettings = Mathf.Approximately(settings.cameraMaxDistance, SpecGroundFurthest) && Mathf.Approximately(settings.furthestZoomRatio, 3.4f);
		if (defaultSettings)
		{
			RingSize wide = new RingSize(RingSize.MaxTilesAround, RingSize.MinTilesAcross);
			RingGeometry wideGeometry = new RingGeometry(wide, RingSize.FullLandCoverage, wide.NewWorldShadowSquareCount);
			float[] wideBands = RingCamera.ZoomBands(settings, wideGeometry, wide.Radius, RingCamera.ScreenAspect, 4);
			for (int i = 0; i < SpecDefaultBands.Length; i++)
			{
				if (Mathf.Abs(wideBands[i + 1] - SpecDefaultBands[i]) > SpecBandTolerance * SpecDefaultBands[i])
				{
					result.Fail($"at {wide}, with three atmosphere layers, band {i} ends at {wideBands[i + 1]:F1}, not about {SpecDefaultBands[i]:F0}");
				}
			}
		}
		List<RingLayer> stack = RingCamera.ZoomStack(ring);
		if (stack[0] != SurfaceRing.Current)
		{
			result.Fail("the ring view's zoom stack doesn't start at the surface ring");
		}
		for (int i = 1; i < stack.Count; i++)
		{
			if (stack[i - 1].zoomOutToLayer != stack[i])
			{
				result.Fail($"zoom band {i} is {stack[i].Def.defName}, but {stack[i - 1].Def.defName} zooms out to {stack[i - 1].zoomOutToLayer?.Def.defName ?? "nothing"}");
			}
			if (!(stack[i].Radius < stack[i - 1].Radius))
			{
				result.Fail($"zoom band {i} ({stack[i].Def.defName}) isn't nearer the camera than the band below it ({stack[i - 1].Def.defName})");
			}
		}
	}

	// Calls visit with each checked ring size and screen shape, and the ring view's furthest zoom there.
	private static void ForEachCheckedRingView(RingLayer ring, System.Action<RingSize, RingGeometry, float, float> visit)
	{
		List<float> aspects = new List<float>(RingViewCheckedAspects) { RingCamera.ScreenAspect };
		foreach (int tilesAround in RingViewCheckedTilesAround)
		{
			for (int tilesAcross = RingSize.MinTilesAcross; tilesAcross <= RingSize.MaxTilesAcross; tilesAcross++)
			{
				RingSize size = new RingSize(tilesAround, tilesAcross);
				RingGeometry geometry = new RingGeometry(size, RingSize.FullLandCoverage, size.NewWorldShadowSquareCount);
				foreach (float aspect in aspects)
				{
					visit(size, geometry, aspect, RingCamera.MaxDistance(ring.Settings, geometry, size.Radius, aspect));
				}
			}
		}
	}

	// A ring-view angle turned into the orbit camera's rotation puts the camera across the sun from that point of the
	// ring, raised by the tilt, so it sees that point's inner surface; and the orbit camera's look-at turned back gives
	// the same angle, however far it is tilted off the ring's plane.
	// Reads the look-at from vanilla's own camera driver where there is one (its rotation is put back afterwards).
	private static void CheckRingOrbitFacingRoundTrip(RingLayer ring, RingCheckResult result)
	{
		WorldCameraDriver driver = Find.WorldCameraDriver;
		Quaternion saved = driver != null ? driver.sphereRotation : Quaternion.identity;
		try
		{
			for (int i = 0; i < FacingSamples; i++)
			{
				float angle = 2f * Mathf.PI * i / FacingSamples;
				Quaternion rotation = RingCamera.OrbitRotationFacing(angle);
				Vector3 lookAt = OrbitLookAt(driver, rotation);
				// The camera is across the sun from the spot, raised by the tilt, so it sees the spot's inner surface.
				float tilt = RingCamera.OrbitTiltDegrees * Mathf.Deg2Rad;
				Vector3 expected = -RingGeometry.Outward(angle) * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt);
				// Compared as vectors: Vector3.Angle's acos is off by about 0.02 degrees for nearly equal directions.
				if ((lookAt - expected).magnitude > FacingToleranceDegrees * Mathf.Deg2Rad)
				{
					result.Fail($"at {angle * Mathf.Rad2Deg:F0} degrees around the ring, the orbit camera is at {lookAt} from the sun; across the sun from that spot, {RingCamera.OrbitTiltDegrees} degrees up, is {expected}");
				}
				float back = RingCamera.AngleFacing(lookAt);
				if (Mathf.Abs(Mathf.DeltaAngle(back * Mathf.Rad2Deg, angle * Mathf.Rad2Deg)) > FacingToleranceDegrees)
				{
					result.Fail($"{angle * Mathf.Rad2Deg:F2} degrees around the ring comes back from the orbit view as {back * Mathf.Rad2Deg:F2}");
				}
				Vector3 tilted = (-RingGeometry.Outward(angle) - 0.5f * Vector3.up).normalized;
				float fromTilted = RingCamera.AngleFacing(tilted);
				if (Mathf.Abs(Mathf.DeltaAngle(fromTilted * Mathf.Rad2Deg, angle * Mathf.Rad2Deg)) > FacingToleranceDegrees)
				{
					result.Fail($"an orbit camera tilted above {angle * Mathf.Rad2Deg:F2} degrees around the ring faces the ring view to {fromTilted * Mathf.Rad2Deg:F2}");
				}
			}
		}
		finally
		{
			if (driver != null)
			{
				driver.sphereRotation = saved;
			}
		}
	}

	// What the orbit camera looks at with this rotation: vanilla's CurrentlyLookingAtPointOnSphere.
	private static Vector3 OrbitLookAt(WorldCameraDriver driver, Quaternion rotation)
	{
		if (driver == null)
		{
			return -(Quaternion.Inverse(rotation) * Vector3.forward);
		}
		driver.sphereRotation = rotation;
		return driver.CurrentlyLookingAtPointOnSphere;
	}
}
