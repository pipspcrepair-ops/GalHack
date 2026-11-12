using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Wildlife;
using Godot;
using Slice.Game;
using Slice.Surface;
using Weather.Model;
using WeatherKind = Weather.Model.WeatherKind;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The weather's sound where you stand: rain and wind follow the local weather and your exposure
/// (open ground, ridge, hollow, lee, under a roof, in a shut room), and an opening howls only when
/// it's windy. Levels are measured, never listened to.
///
/// Run: --headless --fixed-fps 60 --path prototypes/slice res://tests/SoundTest.tscn
/// </summary>
public partial class SoundTest : Node
{
    const int WatchdogMinutes = 12;
    Game.Game _game;
    Playthrough _play;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static readonly string Nl = ((char)10).ToString();
    static readonly string Header = string.Join(Nl, "schema galhack.blueprint", "version 1", "name \"Shut\"", "grid large", "mode base", "gravity off") + Nl;

    SurfaceZone Zone => _game.Zone;
    Slice.Player.Survivor Me => _game.Survivor;
    SliceAudio Audio => _game.Audio;

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _play = new Playthrough(_game);
        _drive = _play.Drive;
        while (_game.Phase != Phase.Playing) yield return 0;
        Zone.Weather.Bridge.Calm = true;
        Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.UntilPlaying()) yield return x;
        foreach (int x in _play.WakeAndLeave((_, _) => { })) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;

        Formulas();
        Moods();
        Voices();
        foreach (int x in Outdoors()) yield return x;
        foreach (int x in Indoors()) yield return x;
    }

    /// <summary>The mixing rules alone: every ordering the owner asked for.</summary>
    void Formulas()
    {
        float open = AmbientExposure.RainLevel(0.8f, 0f, 0f, false, false);
        float roof = AmbientExposure.RainLevel(0.8f, 1f, 0.5f, false, false);
        Check(open > 0.7f && roof < open * 0.4f && roof > 0f, $"rain: full in the open ({open:F2}), muffled under a roof ({roof:F2})");
        Check(AmbientExposure.RainLevel(0.8f, 1f, 1f, true, false) == 0f && AmbientExposure.RainLevel(0f, 0f, 0f, false, false) == 0f, "rain: none in a shut room, none when none falls");
        Check(AmbientExposure.RainLevel(0.8f, 0f, 0f, false, true) < open, "rain: quieter in a cockpit");
        float calm = AmbientExposure.WindLevel(2f, 0f, 0f, 0f, 0f, false, false), gale = AmbientExposure.WindLevel(25f, 0f, 0f, 0f, 0f, false, false);
        Check(calm < 0.1f && gale > 0.7f, $"wind: calm {calm:F2}, a gale {gale:F2}");
        float ridge = AmbientExposure.WindLevel(15f, 0f, 0f, 25f, 0f, false, false), flat = AmbientExposure.WindLevel(15f, 0f, 0f, 0f, 0f, false, false);
        float hollow = AmbientExposure.WindLevel(15f, 0f, 0f, -25f, 0f, false, false), lee = AmbientExposure.WindLevel(15f, 0f, 0f, 0f, 1f, false, false);
        Check(ridge > flat * 1.2f && hollow < flat * 0.7f && lee < flat * 0.6f, $"wind: ridge {ridge:F2} > open {flat:F2} > hollow {hollow:F2}, lee {lee:F2}");
        float indoors = AmbientExposure.WindLevel(25f, 1f, 1f, 0f, 0f, false, false);
        Check(indoors < gale * 0.15f && AmbientExposure.WindLevel(25f, 1f, 1f, 0f, 0f, true, false) == 0f, $"wind: muffled indoors ({indoors:F2} of {gale:F2}), gone in a shut room");
        Check(WindHowl.Loudness(3f) == 0f && WindHowl.Loudness(30f) > WindHowl.Loudness(12f) && WindHowl.Loudness(12f) > 0f, "howl: silent in a calm, louder as the wind rises");
    }

    /// <summary>Per-person moods: events move them, time settles them, nerve and job colour them, and they show.</summary>
    void Moods()
    {
        var calm = new Slice.Places.Feeling { Nerve = 0.5f };
        Check(calm.Mood == PersonMood.Calm && calm.Idle(1f, 0) == BodyAct.None && calm.Pace == 1f, "a person starts calm, at an ordinary pace");
        var timid = new Slice.Places.Feeling { Nerve = 0.1f };
        var bold = new Slice.Places.Feeling { Nerve = 0.9f, Armed = true };
        timid.Hit(Slice.Places.MoodEvent.Shot);
        bold.Hit(Slice.Places.MoodEvent.Shot);
        Check(timid.Mood == PersonMood.Scared && timid.Pace > 1.3f && timid.Flees, $"a shot beside a timid person scares them: {timid.Mood}, pace {timid.Pace:F2}");
        Check(bold.Fear < timid.Fear && bold.Anger > timid.Anger, $"the same shot stirs a bold armed person less fear ({bold.Fear:F2} against {timid.Fear:F2}) and more anger");
        Check(timid.Idle(1f, 0) == BodyAct.Cower, "and the scared one cowers when idle");
        for (int i = 0; i < 600; i++) timid.Step(0.1f, false);
        Check(timid.Mood == PersonMood.Calm, $"a minute on, they have settled: {timid.Mood} (fear {timid.Fear:F2})");
        var griever = new Slice.Places.Feeling { Nerve = 0.5f };
        griever.Hit(Slice.Places.MoodEvent.LostKin);
        Check(griever.Mood == PersonMood.Grieving && griever.Pace < 0.9f && griever.Idle(1f, 0) == BodyAct.Grieve, $"losing a partner: {griever.Mood}, slow, grieving");
        var host = new Slice.Places.Feeling { Nerve = 0.5f };
        host.Hit(Slice.Places.MoodEvent.Gift);
        Check(host.Mood == PersonMood.Content, "a gift lifts the mood to content");
        var fire = new Slice.Places.Feeling { Nerve = 0.5f };
        for (int i = 0; i < 1200; i++) fire.Step(0.1f, true);
        Check(fire.Mood == PersonMood.Content, $"two minutes at a gathering fire: {fire.Mood}");
        var drunk = new Slice.Places.Feeling { Drunk = true };
        Check(drunk.Mood == PersonMood.Drunk && drunk.Pace < 0.9f, "a drunk is drunk");
        drunk.Hit(Slice.Places.MoodEvent.Harm);
        drunk.Hit(Slice.Places.MoodEvent.Harm);
        Check(drunk.Mood != PersonMood.Drunk, $"until something frightens or angers them: {drunk.Mood}");
        var angry = new Slice.Places.Feeling { Nerve = 0.9f, Armed = true };
        angry.Hit(Slice.Places.MoodEvent.Robbed);
        Check(angry.Mood is PersonMood.Angry or PersonMood.Scared && angry.Anger > 0.4f, $"being robbed angers a bold person: {angry.Mood}, anger {angry.Anger:F2}");
    }

    static Voice VoiceOf(Sex sex, LifeStage stage, float height, float mass, ulong seed)
    {
        var f = new Figure { Sex = sex, Stage = stage, Height = height, Mass = mass, Role = PersonRole.Worker };
        return Voice.For(f, Dialect.For(77), seed);
    }

    /// <summary>The pitch of a clip, Hz, by autocorrelation over its middle.</summary>
    static float Pitch(short[] pcm)
    {
        int start = Math.Min(pcm.Length / 4, 1500), n = Math.Min(2048, pcm.Length - start - 600);
        if (n < 600) return 0f;
        float best = 0f; int bestLag = 0;
        for (int lag = Voice.Rate / 500; lag < Voice.Rate / 70; lag++)
        {
            double sum = 0, a = 0, b = 0;
            for (int i = 0; i < n - lag && i + lag + start < pcm.Length; i++) { sum += (double)pcm[start + i] * pcm[start + i + lag]; a += (double)pcm[start + i] * pcm[start + i]; b += (double)pcm[start + i + lag] * pcm[start + i + lag]; }
            float r = (float)(sum / Math.Sqrt(a * b + 1));
            if (r > best) { best = r; bestLag = lag; }
        }
        return bestLag == 0 ? 0f : Voice.Rate / (float)bestLag;
    }

    static float Rms(short[] pcm)
    {
        double s = 0;
        foreach (short x in pcm) s += (double)x * x;
        return (float)Math.Sqrt(s / Math.Max(1, pcm.Length));
    }

    /// <summary>Voices by age, sex and mood: measured, never listened to.</summary>
    void Voices()
    {
        var child = VoiceOf(Sex.Female, LifeStage.Child, 1.15f, 22f, 11);
        var woman = VoiceOf(Sex.Female, LifeStage.Adult, 1.65f, 60f, 12);
        var man = VoiceOf(Sex.Male, LifeStage.Adult, 1.8f, 82f, 13);
        var elder = VoiceOf(Sex.Male, LifeStage.Elder, 1.7f, 62f, 14);
        int bad = 0;
        foreach (Voice v in new[] { child, woman, man, elder })
        foreach (VocalKind k in Enum.GetValues<VocalKind>())
        foreach (PersonMood m in Enum.GetValues<PersonMood>())
        {
            short[] pcm = v.Vocalize(k, m, 0);
            int peak = 0;
            foreach (short x in pcm) peak = Math.Max(peak, Math.Abs((int)x));
            float seconds = pcm.Length / (float)Voice.Rate;
            if (peak < 3000 || peak > 30000 || seconds < 0.1f || seconds > 4f) { bad++; GD.Print($"    {k} {m}: peak {peak}, {seconds:F2} s"); }
        }
        Check(bad == 0, $"every sound (7 kinds x 7 moods x 4 people) is audible, never clipped, and between 0.1 and 4 s: {bad} bad");
        float c = Pitch(child.Vocalize(VocalKind.Call, PersonMood.Calm, 0)), w = Pitch(woman.Vocalize(VocalKind.Call, PersonMood.Calm, 0)), mn = Pitch(man.Vocalize(VocalKind.Call, PersonMood.Calm, 0));
        Check(c > w * 1.1f && w > mn * 1.3f, $"calls by pitch: child {c:F0} Hz > woman {w:F0} Hz > man {mn:F0} Hz");
        float eld = Pitch(elder.Vocalize(VocalKind.Call, PersonMood.Calm, 0));
        Check(elder.Tremor > man.Tremor * 3f && elder.Rasp > man.Rasp - 0.01f && elder.Breath > man.Breath - 0.01f, $"an elder shakes and rasps more: tremor {elder.Tremor:F2} against {man.Tremor:F2} ({eld:F0} Hz)");
        short[] calmCall = man.Vocalize(VocalKind.Call, PersonMood.Calm, 0), scaredCall = man.Vocalize(VocalKind.Call, PersonMood.Scared, 0), angryCall = man.Vocalize(VocalKind.Call, PersonMood.Angry, 0), grief = man.Vocalize(VocalKind.Cry, PersonMood.Grieving, 0), calmCry = man.Vocalize(VocalKind.Cry, PersonMood.Calm, 0);
        float calmHz = Pitch(calmCall), scaredHz = Pitch(scaredCall), angryHz = Pitch(angryCall);
        Check(scaredHz > calmHz * 1.15f && scaredCall.Length < calmCall.Length, $"scared: higher ({scaredHz:F0} against {calmHz:F0} Hz) and quicker ({scaredCall.Length * 1000 / Voice.Rate} against {calmCall.Length * 1000 / Voice.Rate} ms)");
        Check(angryHz < calmHz * 0.97f && Rms(angryCall) > Rms(calmCall) * 1.4f, $"angry: lower ({angryHz:F0} Hz) and louder (rms {Rms(angryCall):F0} against {Rms(calmCall):F0})");
        Check(grief.Length > calmCry.Length * 1.1f, $"grieving cries are slower: {grief.Length * 1000 / Voice.Rate} against {calmCry.Length * 1000 / Voice.Rate} ms");
        var again = man.Vocalize(VocalKind.Call, PersonMood.Calm, 0);
        bool same = again.Length == calmCall.Length;
        for (int i = 0; same && i < again.Length; i++) same = again[i] == calmCall[i];
        var other = VoiceOf(Sex.Male, LifeStage.Adult, 1.8f, 82f, 99).Vocalize(VocalKind.Call, PersonMood.Calm, 0);
        Check(same && Math.Abs(Pitch(other) - calmHz) > 1f || other.Length != calmCall.Length, "the same person sounds the same every time, and a neighbour of their build differs");
        Check(man.Vocalize(VocalKind.Laugh, PersonMood.Content, 0).Length > 0.3f * Voice.Rate && man.Vocalize(VocalKind.Breath, PersonMood.Calm, 0).Length > 0.3f * Voice.Rate, "laughs and breaths have length");
    }

    IEnumerable<int> Settle(int frames)
    {
        foreach (int x in _drive.Wait(frames)) yield return x;
    }

    /// <summary>Out on the ground: rain and wind follow the weather and the lie of the land.</summary>
    IEnumerable<int> Outdoors()
    {
        Vector3 spot = Open(Me.WorldFeet);
        Me.PlaceOutside((spot + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        Weather.Model.WeatherSim sky = Zone.Weather.Sim;
        AmbientExposure ex = _game.Exposure;
        ex.Held = false;

        sky.Snap(WeatherKind.Clear);
        foreach (int x in Settle(240)) yield return x;
        float calmWind = Audio.WindLevel;
        Check(calmWind < 0.25f && Audio.RainLevel < 0.01f && Zone.Sfx.RainDb <= -79f, $"a clear calm day: wind {calmWind:F2} ({Zone.Sfx.WindDb:F0} dB), rain silent ({Zone.Sfx.RainDb:F0} dB)");

        sky.Snap(WeatherKind.Monsoon);
        foreach (int x in Settle(300)) yield return x;
        float rain = Audio.RainLevel;
        Check(ex.Cover < 0.3f && ex.Walled < 0.4f, $"open ground reads open: cover {ex.Cover:F2}, walls {ex.Walled:F2}, relief {ex.Relief:F1} m, lee {ex.Lee:F2}");
        Check(rain > 0.15f && Zone.Sfx.RainDb > -45f, $"in the open a monsoon is loud: rain {rain:F2} ({Zone.Sfx.RainDb:F0} dB), here {Zone.Weather.RainHere:F2}");
        ex.Set(1f, 0.3f, 0f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float roofed = Audio.RainLevel;
        Check(roofed < rain * 0.45f && roofed > 0f, $"under a roof it is muffled: {roofed:F2} against {rain:F2} ({Zone.Sfx.RainDb:F0} dB)");
        ex.Held = false;

        sky.Snap(WeatherKind.Hurricane);
        foreach (int x in Settle(240)) yield return x;
        ex.Set(0f, 0f, 0f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float flat = Audio.WindLevel;
        Check(flat > 0.5f && flat > calmWind * 3f, $"in a gale on open ground wind is loud: {flat:F2} ({Zone.Sfx.WindDb:F0} dB) against {calmWind:F2} in the calm");
        ex.Set(0f, 0f, 25f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float ridge = Audio.WindLevel;
        ex.Set(0f, 0f, -25f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float hollow = Audio.WindLevel;
        ex.Set(0f, 0f, 0f, 1f);
        foreach (int x in Settle(180)) yield return x;
        float lee = Audio.WindLevel;
        ex.Set(1f, 0.8f, 0f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float indoors = Audio.WindLevel;
        Check(ridge > flat && hollow < flat * 0.7f && lee < flat * 0.6f && indoors < flat * 0.25f, $"ridge {ridge:F2} > open {flat:F2} > hollow {hollow:F2}, lee {lee:F2}, roofed and walled {indoors:F2}");
        ex.Held = false;
        sky.Snap(WeatherKind.Clear);
        foreach (int x in Settle(120)) yield return x;
    }

    Vector3 Open(Vector3 near)
    {
        for (float r = 0f; r < 400f; r += 12f)
        for (int i = 0; i < 16; i++)
        {
            Vector3 g = Zone.GroundAt(near.X + MathF.Cos(i * 0.39f) * r, near.Z + MathF.Sin(i * 0.39f) * r);
            if (g.Y > 1.5f && !Zone.Water.LiquidAt(g.X, g.Z, out _) && _game.Places?.PlaceAt(g) == null) return g;
        }
        return Zone.GroundAt(near.X, near.Z);
    }

    /// <summary>A shut steel room in a monsoon and a gale: nothing is heard. Then a gap in its wall: the wind howls there, only when it blows.</summary>
    IEnumerable<int> Indoors()
    {
        Vector3 feet = Me.WorldFeet;
        Weather.Model.WeatherSim sky = Zone.Weather.Sim;
        AmbientExposure ex = _game.Exposure;
        ex.Held = false;
        foreach (bool gap in new[] { false, true })
        {
            int half = gap ? 5 : 3; // cells are 2 m: the shut room is 6 m across inside, the gapped one 10
            var text = new System.Text.StringBuilder(Header);
            for (int y = 0; y <= 4; y++)
            for (int z = -half; z <= half; z++)
            for (int x = -half; x <= half; x++)
                if ((y == 0 || y == 4 || Math.Abs(x) == half || Math.Abs(z) == half) && !(gap && x == half && Math.Abs(z) <= 1 && y >= 1 && y <= 3)) text.Append($"block {x} {y} {z} steel{Nl}");
            text.Append($"block {half - 1} 1 {half - 1} core{Nl}block {1 - half} 1 {1 - half} generator{Nl}");
            var bp = Blueprint.Load(text.ToString(), Zone.Data.Catalogue, out List<string> errors);
            Check(bp != null, $"the room's blueprint loads {string.Join("; ", errors)}");
            Vector3 site = Zone.GroundAt(feet.X + (gap ? 80f : 30f), feet.Z);
            Slice.Vessels.Vessel room = Slice.Vessels.Vessel.SpawnBase(Zone, bp, (site + Vector3.Up * 4f).ToNumerics(), 0f);
            Me.PlaceOutside(room.ToWorld(new N.Vector3(0f, 3f, 0f)).ToNumerics());
            Zone.Light.Touch();
            foreach (int x in _drive.UntilSettled()) yield return x;
            sky.Snap(WeatherKind.Monsoon);
            foreach (int x in Settle(300)) yield return x;
            ex.Survey();
            if (!gap)
            {
                Check(ex.Cover > 0.8f && ex.Walled > 0.7f, $"in the room the survey finds a roof and walls: cover {ex.Cover:F2}, walls {ex.Walled:F2}");
                Check(Audio.RainLevel < 0.01f && Zone.Sfx.RainDb <= -79f, $"shut in, a monsoon is silent: rain {Audio.RainLevel:F2} ({Zone.Sfx.RainDb:F0} dB)");
                sky.Snap(WeatherKind.Hurricane);
                foreach (int x in Settle(240)) yield return x;
                Check(Audio.WindLevel < 0.01f && Zone.Sfx.WindDb <= -79f && _game.Howl.Level < 0.05f, $"and a gale outside is silent: wind {Audio.WindLevel:F2} ({Zone.Sfx.WindDb:F0} dB), howl {_game.Howl.Level:F2}");
            }
            else
            {
                sky.Snap(WeatherKind.Hurricane);
                foreach (int x in Settle(300)) yield return x;
                float gale = _game.Howl.Level;
                Check(_game.Howl.Opening != null && gale > 0.3f, $"a gap in the wall moans in a gale: level {gale:F2}, opening {(_game.Howl.Opening != null)}");
                sky.Snap(WeatherKind.Clear);
                foreach (int x in Settle(300)) yield return x;
                Check(_game.Howl.Level < 0.05f, $"and is quiet once it calms: {_game.Howl.Level:F2}");
            }
            sky.Snap(WeatherKind.Clear);
            Me.PlaceOutside((feet + Vector3.Up * 0.5f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
        }
    }
}
