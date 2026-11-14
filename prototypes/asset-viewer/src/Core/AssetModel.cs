using System;
using System.Collections.Generic;
using Godot;

namespace AssetViewer;

/// <summary>
/// One source of assets for the viewer: a top-level category in the browser ("Bestiary", "Places",
/// "Blocks"...). Drop a class that implements this into src/Providers/&lt;Group&gt;/ with a
/// parameterless constructor and the viewer finds it by itself; nothing else needs editing.
/// </summary>
public interface IAssetProvider
{
    /// <summary>The category this provider fills. Several providers may share one category name.</summary>
    string Category { get; }

    /// <summary>
    /// The assets, cheaply: names, groups and builders only. Do not build meshes or generate worlds
    /// here; <see cref="AssetEntry.Build"/> does that when the asset is picked. May be called once at start-up.
    /// </summary>
    IEnumerable<AssetEntry> Entries();

    /// <summary>
    /// Shader and font folders of other prototypes that this provider's code loads from res:// paths:
    /// (prototype folder, folder inside it), for example ("wildlife", "shaders"). They are packed and
    /// mounted at res:// before anything is built, as the slice does.
    /// </summary>
    IEnumerable<(string Prototype, string Folder)> Mounts => Array.Empty<(string, string)>();
}

/// <summary>A choice a provider offers for an asset (state, theme, age, family...), shown as a drop-down.</summary>
public sealed class AssetOption
{
    public string Name = "";
    public string[] Values = Array.Empty<string>();
    public int Default;
    /// <summary>True: the randomiser may roll this one too.</summary>
    public bool Rollable;

    public AssetOption() { }
    public AssetOption(string name, params string[] values) { Name = name; Values = values; }
}

/// <summary>What the viewer hands a builder: the seed and the chosen options.</summary>
public sealed class BuildRequest
{
    public ulong Seed;
    public readonly Dictionary<string, string> Options = new();

    /// <summary>The chosen value of an option, or <paramref name="fallback"/> if the asset has no such option.</summary>
    public string Get(string option, string fallback = "") => Options.TryGetValue(option, out string v) ? v : fallback;

    /// <summary>Index of the chosen value among <paramref name="values"/> (0 if absent or unknown).</summary>
    public int IndexOf(string option, string[] values)
    {
        int i = Array.IndexOf(values, Get(option));
        return i < 0 ? 0 : i;
    }
}

/// <summary>One thing in the browser.</summary>
public sealed class AssetEntry
{
    /// <summary>Unique within its category and stable between runs: notes are filed under it. Never use a seed or an index that can shift.</summary>
    public string Id = "";
    /// <summary>Where it sits in the tree under its category, "/" separated ("Surface/Predators"). Empty for the top.</summary>
    public string Group = "";
    public string Name = "";
    /// <summary>True: the viewer shows a Randomise button and a seed box, and the builder should use <see cref="BuildRequest.Seed"/> for everything it rolls.</summary>
    public bool Randomisable;
    public AssetOption[] Options = Array.Empty<AssetOption>();
    /// <summary>Builds the asset. Runs on the main thread when the asset is picked or re-rolled.</summary>
    public Func<BuildRequest, AssetInstance> Build;

    // Filled in by the registry.
    public string Category = "";
    public string Key => Category + "|" + Id;
}

/// <summary>
/// The node a builder returns. Add your meshes, lights and sub-nodes as children; the viewer puts it
/// on a turntable, frames it and shows its animations. Anything that moves can simply use _Process.
/// </summary>
public partial class AssetInstance : Node3D
{
    /// <summary>Names of the animations or behaviours the viewer can play, in order. Empty: not animated.</summary>
    public virtual IReadOnlyList<string> Animations => Array.Empty<string>();

    /// <summary>The one playing now (empty if none).</summary>
    public string CurrentAnimation { get; protected set; } = "";

    /// <summary>Starts an animation by name, from the beginning. Called when the user picks one in the list.</summary>
    public virtual void PlayAnimation(string name) => CurrentAnimation = name;

    /// <summary>Scales the speed of whatever is moving (the viewer's speed slider, 0 pauses). Default does nothing.</summary>
    public virtual void SetSpeed(float scale) { }

    /// <summary>Label and value pairs the viewer lists under the name: sizes, counts, species traits. Short.</summary>
    public virtual IEnumerable<(string Label, string Value)> Facts() => Array.Empty<(string, string)>();

    /// <summary>
    /// The box to frame. Null: the viewer measures every visible mesh below this node. Return it
    /// yourself when the meshes aren't all there on the first frame (streamed terrain, particles).
    /// </summary>
    public virtual Aabb? FrameBounds => null;

    /// <summary>
    /// True (default): the turntable spins this node slowly. False: the camera drifts around it instead,
    /// for things that must keep their orientation (weather, anything driven by a world-space simulation).
    /// </summary>
    public virtual bool Spins => true;

    /// <summary>What the stage should give this asset.</summary>
    public StageSpec Stage { get; set; } = new();

    /// <summary>Called when the viewer is about to remove it. Release anything not owned by its children.</summary>
    public virtual void Release() { }
}

/// <summary>What an asset wants from the stage around it.</summary>
public sealed class StageSpec
{
    /// <summary>A ground disc under it (the viewer's toggle can still hide it). False for things that bring their own ground or float in space.</summary>
    public bool Ground = true;
    /// <summary>Radius of the ground disc in metres; 0 sizes it from the asset.</summary>
    public float GroundRadius;
    /// <summary>True: the asset adds its own WorldEnvironment and lights, and the stage's sky and sun are removed.</summary>
    public bool OwnsEnvironment;
    /// <summary>The stage background: "sky" (default), "space" (black with stars) or "studio" (plain grey).</summary>
    public string Background = "sky";
    /// <summary>Closest the orbit camera may come, as a share of the asset's radius. Raise it for things you fly inside.</summary>
    public float MinDistanceShare = 0.02f;
    /// <summary>True: start the camera inside/near the asset in free mode (a room, a city street) instead of orbiting outside it.</summary>
    public bool StartInside;
}
