using System.Collections.Concurrent;
using Godot;

namespace PlanetSurface.Fx;

/// <summary>
/// Shader parameters' names as the engine's own strings, made once each. A C# string passed where a StringName goes
/// makes a new one on every call (a native string, a tracked handle and a finalizer): the sky, the sea and the ground set
/// some sixty parameters a frame, and that was most of what the main thread allocated.
/// </summary>
public static class Names
{
    static readonly ConcurrentDictionary<string, StringName> Made = new();

    public static StringName Of(string name) => Made.GetOrAdd(name, static n => new StringName(n));
}
