using System.Collections.Generic;
using System;
using Godot;
using Invasions.Audio;

namespace Invasions.World;

/// <summary>
/// Air-raid sirens across the base: red rotating beacons on poles above the walls, a red wash of
/// light, and a synthesised WW2-style mechanical siren (<see cref="SirenSynth"/>). The alert wails
/// from the one-minute warning until the last invader is down or gone; then a steady all-clear
/// sounds and the rotor runs down. No recorded audio is used.
/// </summary>
public partial class SirenSystem : Node3D
{
    public bool On { get; private set; }
    /// <summary>The listener, for distance falloff of the audio.</summary>
    public Node3D Listener;

    readonly List<Node3D> _rotors = new();
    readonly List<MeshInstance3D> _domes = new();
    readonly List<Light3D> _lights = new();
    AudioStreamPlayer _audio;
    AudioStreamGeneratorPlayback _playback;
    SirenSynth _synth;
    float[] _left = new float[4096], _right = new float[4096];
    double _time;
    const int MixRate = 44100;
    /// <summary>How long the steady all-clear note holds before the rotor runs down (a wartime all-clear ran two minutes).</summary>
    const double AllClearSeconds = 9;

    /// <summary>The siren's audio state, for tests and the HUD.</summary>
    public SirenSynth.Mode AudioMode => _synth?.Current ?? (On ? SirenSynth.Mode.Alert : SirenSynth.Mode.Off);

    public void AddBeacon(Vector3 position)
    {
        var beacon = new Node3D { Name = $"Siren{_rotors.Count}", Position = position };
        AddChild(beacon);
        Material dark = Palette.Mat(Palette.Dark, 0.6f);
        Palette.Cylinder(beacon, new Vector3(0, 2.9f, 0), 0.1f, 5.8f, dark, 8);
        Palette.Box(beacon, new Vector3(0, 5.75f, 0), new Vector3(0.9f, 0.18f, 0.9f), Palette.Mat(Palette.Yellow));
        MeshInstance3D dome = Palette.Cylinder(beacon, new Vector3(0, 6.1f, 0), 0.34f, 0.55f, Palette.Mat(Palette.Maroon, 0.3f), 12);
        _domes.Add(dome);

        var rotor = new Node3D { Position = new Vector3(0, 6.1f, 0) };
        beacon.AddChild(rotor);
        Palette.Box(rotor, new Vector3(0, 0, 0), new Vector3(0.62f, 0.3f, 0.06f), Palette.Mat(Palette.Bone, 0.1f, 0.8f)); // reflector
        foreach (float yaw in new[] { 0f, Mathf.Pi })
        {
            var spot = new SpotLight3D
            {
                LightColor = Palette.SirenRed,
                LightEnergy = 4f,
                SpotRange = 38f,
                SpotAngle = 24f,
                SpotAttenuation = 0.8f,
                ShadowEnabled = false,
                Rotation = new Vector3(-0.22f, yaw, 0),
                Visible = false,
            };
            rotor.AddChild(spot);
            _lights.Add(spot);
        }
        var wash = new OmniLight3D { LightColor = Palette.SirenRed, OmniRange = 12f, LightEnergy = 0f, Position = new Vector3(0, 5.5f, 0), Visible = false };
        beacon.AddChild(wash);
        _lights.Add(wash);
        _rotors.Add(rotor);
    }

    public override void _Ready()
    {
        // No audio device headless; the beacons still work.
        if (DisplayServer.GetName() == "headless") return;
        _synth = new SirenSynth(MixRate);
        _audio = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.3f },
            VolumeDb = -6f,
        };
        AddChild(_audio);
        _audio.Play();
        _playback = _audio.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public void SetOn(bool on)
    {
        if (On == on) return;
        On = on;
        // Alert while invaders are about; when the last is down or gone, the steady all-clear.
        if (on) _synth?.Alert();
        else _synth?.AllClear(AllClearSeconds);
        foreach (Light3D light in _lights) light.Visible = on;
        foreach (MeshInstance3D dome in _domes) dome.MaterialOverride = on ? Palette.Glow(Palette.SirenRed, 4f) : Palette.Mat(Palette.Maroon, 0.3f);
    }

    /// <summary>0..1 brightness of the red pulse, for the HUD to match.</summary>
    public float Pulse => On ? 0.5f + 0.5f * Mathf.Sin((float)_time * Mathf.Tau * 1.1f) : 0f;

    public override void _Process(double delta)
    {
        _time += delta;
        if (On)
        {
            foreach (Node3D rotor in _rotors) rotor.Rotation = new Vector3(0, (float)(_time * 5.5), 0);
            foreach (Light3D light in _lights)
                if (light is OmniLight3D wash) wash.LightEnergy = 0.3f + 0.9f * Pulse;
        }
        FeedAudio();
    }

    /// <summary>Streams the synthesised siren, quieter the further the listener is from the base.</summary>
    void FeedAudio()
    {
        if (_playback == null) return;
        int frames = _playback.GetFramesAvailable();
        if (frames <= 0) return;
        if (_left.Length < frames)
        {
            _left = new float[frames];
            _right = new float[frames];
        }
        _synth.Render(_left.AsSpan(0, frames), _right.AsSpan(0, frames));
        float distance = Listener != null ? Listener.GlobalPosition.Length() : 0f;
        float volume = Mathf.Clamp(1f - distance / 450f, 0.1f, 1f);
        var buffer = new Vector2[frames];
        for (int i = 0; i < frames; i++) buffer[i] = new Vector2(_left[i], _right[i]) * volume;
        _playback.PushBuffer(buffer);
    }
}
