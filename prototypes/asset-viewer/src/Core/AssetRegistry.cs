using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace AssetViewer;

/// <summary>Finds every <see cref="IAssetProvider"/> in the assembly and lists their assets, once.</summary>
public sealed class AssetRegistry
{
    public readonly List<AssetEntry> Entries = new();
    public readonly List<string> Categories = new();
    public readonly List<(string Provider, string Error)> Failures = new();
    readonly Dictionary<string, AssetEntry> _byKey = new();
    public readonly List<IAssetProvider> Providers = new();

    public AssetEntry Find(string key) => key != null && _byKey.TryGetValue(key, out var e) ? e : null;

    /// <summary>Category order in the browser: the viewer's own reference pieces last.</summary>
    public static readonly string[] CategoryOrder =
    {
        "Bestiary", "People", "Places", "Settlements", "Infrastructure", "Space", "Vehicles", "Blocks", "Items", "Deco", "Signs",
        "Weather", "Flora", "Terrain", "Sky", "Worlds",
    };

    public static AssetRegistry Discover()
    {
        var reg = new AssetRegistry();
        foreach (Type t in Assembly.GetExecutingAssembly().GetTypes()
                     .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IAssetProvider).IsAssignableFrom(t))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            try
            {
                var provider = (IAssetProvider)Activator.CreateInstance(t);
                reg.Providers.Add(provider);
                int added = 0;
                foreach (AssetEntry e in provider.Entries())
                {
                    if (e == null || string.IsNullOrEmpty(e.Id) || e.Build == null) continue;
                    e.Category = provider.Category;
                    if (reg._byKey.ContainsKey(e.Key))
                    {
                        reg.Failures.Add((t.Name, $"duplicate id {e.Key}"));
                        continue;
                    }
                    reg._byKey[e.Key] = e;
                    reg.Entries.Add(e);
                    added++;
                }
                if (!reg.Categories.Contains(provider.Category)) reg.Categories.Add(provider.Category);
            }
            catch (Exception ex)
            {
                reg.Failures.Add((t.Name, ex.GetBaseException().Message));
                GD.PrintErr($"asset provider {t.Name} failed to list: {ex}");
            }
        }
        reg.Categories.Sort((a, b) => Rank(a).CompareTo(Rank(b)) is var c && c != 0 ? c : string.CompareOrdinal(a, b));
        return reg;
    }

    static int Rank(string category)
    {
        if (category == "Reference") return CategoryOrder.Length + 1;
        int i = Array.IndexOf(CategoryOrder, category);
        return i < 0 ? CategoryOrder.Length : i;
    }
}
