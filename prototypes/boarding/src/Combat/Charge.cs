using System.Linq;
using Godot;

namespace Boarding;

/// <summary>
/// A breaching charge stuck to a block. It beeps faster and faster, then blows: the block
/// it's stuck to takes enough damage to open any hull plate or security door, the blocks
/// around it take splash, anyone close gets hurt, and the bang is heard ship-wide. Fast,
/// reliable, and the opposite of quiet.
/// </summary>
public partial class Charge : Node3D
{
    public Main Game;
    public Block Target;
    public Vector3 Normal = Vector3.Back;

    public float FuseLeft { get; private set; }
    public bool Detonated { get; private set; }

    MeshInstance3D _light;
    float _blink;

    public override void _Ready()
    {
        FuseLeft = Game.Tunables.ChargeFuseSeconds;
        AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.34f, 0.34f, 0.12f)), Art.Stripes(Palette.Yellow, Palette.Dark, 0.08f), Vector3.Zero, false));
        AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.2f, 0.12f, 0.08f)), Art.Flat(Palette.Dark), new Vector3(0, -0.06f, -0.08f), false));
        _light = Art.Mesh(Art.BoxMesh(new Vector3(0.07f, 0.07f, 0.07f)), Art.Glow(Palette.AlarmRed, 6f), new Vector3(0, 0.08f, -0.09f), false);
        AddChild(_light);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Detonated) return;
        float dt = (float)delta;
        FuseLeft -= dt;
        // Beeps quicken as the fuse burns down.
        _blink += dt * Mathf.Lerp(10f, 2f, FuseLeft / Game.Tunables.ChargeFuseSeconds);
        _light.Visible = Mathf.Sin(_blink * Mathf.Pi) > 0f;
        if (FuseLeft <= 0f) Detonate();
    }

    public void Detonate()
    {
        if (Detonated) return;
        Detonated = true;
        Tunables t = Game.Tunables;
        Vector3 at = GlobalPosition;

        Game.Effects.Explosion(at + Normal * 0.3f, 1.6f, new Color("ffb347"));

        if (IsInstanceValid(Target) && !Target.Destroyed && Target.Kind != BlockKind.Armour)
            Target.Damage(t.ChargeDamage, at, Normal);

        // Splash: neighbouring blocks get cracked, never the vault's armour.
        foreach (Block block in Game.Ship.Blocks.Values.ToList())
        {
            if (block == Target || !IsInstanceValid(block) || block.Destroyed || block.Kind == BlockKind.Armour) continue;
            float d = block.GlobalPosition.DistanceTo(at);
            if (d < t.ChargeSplashRadius) block.Damage(t.ChargeSplash * (1f - d / t.ChargeSplashRadius), at, Normal);
        }

        foreach (Guard guard in Game.Guards.ToList())
        {
            float d = guard.GlobalPosition.DistanceTo(at);
            if (d < 5f) guard.TakeHit(160f * (1f - d / 5f), guard.GlobalPosition + Vector3.Up, (guard.GlobalPosition - at).Normalized(), false);
        }

        // Your own charge can hurt, but it isn't an NPC: capped low so it never kills outright.
        float pd = Game.Player.GlobalPosition.DistanceTo(at);
        if (pd < 4.5f) Game.Player.TakeEnvironmentDamage(25f * (1f - pd / 4.5f), canKill: false);
        Game.Player.Shake(Mathf.Clamp(1.4f - pd / 20f, 0.2f, 1.4f));

        Game.Noise.Emit(at, t.ChargeNoise, NoiseKind.Explosion, fromPlayer: true);
        QueueFree();
    }
}
