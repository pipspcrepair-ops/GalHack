using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Draws a den's veins (<see cref="Veins"/>): every one of them in a single mesh lying on the
/// place's surfaces, wet and dark, casting no shadow. They are grown once, so this is built once.
/// </summary>
public partial class VeinView : MeshInstance3D
{
    static StandardMaterial3D _material;

    /// <summary>Wet flesh: the colour is the vertices', the sheen is the material's.</summary>
    static StandardMaterial3D Material => _material ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.32f,
        MetallicSpecular = 0.7f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public void Build(IReadOnlyList<Vein> veins)
    {
        Name = "Veins";
        Mesh = Veins.Build(veins);
        MaterialOverride = Material;
        CastShadow = ShadowCastingSetting.Off;
    }
}
