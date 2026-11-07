using System;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Runs the build of one of the ring's draw layers so a failed build can't hide it for the whole session. Vanilla's
// WorldDrawLayerBase.Regenerate clears the dirty flag and the submeshes before an override builds, so a build that
// throws (a material made off the main thread, say) leaves the layer empty and clean, and Render never rebuilds it.
public static class RingDrawLayerBuild
{
	// Runs build; if it throws, logs the error (once per layer type) and sets the layer dirty again, so a later frame
	// rebuilds it. Returns whether the build finished: on false the caller stops before FinalizeMesh.
	public static bool Run(WorldDrawLayerBase layer, Action build)
	{
		try
		{
			build();
			return true;
		}
		catch (Exception e)
		{
			string name = layer.GetType().Name;
			Log.ErrorOnce($"[Ringworld] Building {name} threw; it will be rebuilt on a later frame: {e}", ("Ringworld.RingDrawLayerBuild." + name).GetHashCode());
			layer.SetDirty();
			return false;
		}
	}
}
