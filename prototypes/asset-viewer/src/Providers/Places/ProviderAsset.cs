using System.Linq;
using Godot;

namespace AssetViewer.Providers;

/// <summary>
/// The base of every asset these providers return. Windowed runs of the game's engine pieces were found hanging
/// after their last frame, holding a couple of gigabytes until somebody ended them (see GalHack.Testing.Backstop).
/// So a run that was asked for one screenshot (<c>--shot=</c>) gets a fuse: if the process is still here five
/// minutes after its first asset was built, it ends itself. And whenever the viewer swaps an asset out it calls
/// <see cref="Release"/>; leaving the tree any other way means the application is shutting down, and the process is then
/// given 25 seconds to be gone before it ends itself.
/// </summary>
public partial class ProviderAsset : AssetInstance
{
    bool _released;

    public ProviderAsset()
    {
        if (OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--shot=", System.StringComparison.Ordinal))) GalHack.Testing.Backstop.Arm(0, 300);
    }

    public override void Release()
    {
        _released = true;
        base.Release();
    }

    public override void _ExitTree()
    {
        if (!_released) GalHack.Testing.Backstop.Arm(0);
    }
}
