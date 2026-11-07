using System;
using System.Collections.Generic;

namespace GalHack.Music;

/// <summary>One chosen piece of music: a row of the data, credited whatever its licence.</summary>
public sealed record MusicTrack(string Mood, string File, string Title, string Author, string Source, string Licence, string Attribution);

/// <summary>What the player has to do this tick.</summary>
public enum TrackCue { None, Start, Stop }

/// <summary>
/// Chooses when a recorded track plays over the drones. Nothing but the clock and the place you
/// are in decides: it is never told what the player is doing. Long quiet gaps, one track at a time,
/// a slow fade in and out, and only tracks of the mood of the place. No tracks for a mood simply
/// means drones alone, so taking a track away can never break anything.
/// </summary>
public sealed class TrackDirector
{
    public const float FadeSeconds = 14f, MaxSeconds = 420f;
    const float FirstGapMin = 90f, FirstGapMax = 240f, GapMin = 240f, GapMax = 540f;

    enum State { Resting, Waiting, Playing, Fading }

    readonly Dictionary<string, List<MusicTrack>> _byMood = new();
    readonly Dictionary<string, Queue<MusicTrack>> _bags = new();
    readonly DroneRng _rng;
    State _state;
    float _gap, _played, _length, _level;
    string _mood;

    /// <summary>The track playing, or being loaded to play.</summary>
    public MusicTrack Current { get; private set; }

    /// <summary>How far faded in it is, 0 to 1, eased.</summary>
    public float Level => _level * _level * (3f - 2f * _level);

    public TrackDirector(IEnumerable<MusicTrack> tracks, ulong seed)
    {
        _rng = new DroneRng(seed ^ 0x7AC4D1EC70UL);
        foreach (MusicTrack t in tracks)
        {
            if (!_byMood.TryGetValue(t.Mood, out List<MusicTrack> list)) _byMood[t.Mood] = list = new List<MusicTrack>();
            list.Add(t);
        }
        _gap = _rng.Range(FirstGapMin, FirstGapMax);
    }

    public TrackCue Tick(float dt, string mood)
    {
        bool moved = _mood != null && _mood != mood;
        _mood = mood;
        switch (_state)
        {
            case State.Resting:
                if (moved) _gap = MathF.Min(_gap, _rng.Range(FirstGapMin, FirstGapMax)); // somewhere new needn't wait as long
                _gap -= dt;
                if (_gap > 0f) return TrackCue.None;
                Current = Next(mood);
                if (Current == null)
                {
                    _gap = _rng.Range(GapMin, GapMax);
                    return TrackCue.None;
                }
                _state = State.Waiting;
                return TrackCue.Start;

            case State.Waiting:
                return TrackCue.None;

            case State.Playing:
                _played += dt;
                _level = MathF.Min(1f, _level + dt / FadeSeconds);
                if (mood != Current.Mood || _played >= _length - FadeSeconds) _state = State.Fading;
                return TrackCue.None;

            default:
                _played += dt;
                _level = MathF.Max(0f, _level - dt / FadeSeconds);
                if (_level > 0f) return TrackCue.None;
                Current = null;
                _state = State.Resting;
                _gap = _rng.Range(GapMin, GapMax);
                return TrackCue.Stop;
        }
    }

    /// <summary>The track has loaded and started. Very long pieces leave early, faded out, so nothing outstays the quiet.</summary>
    public void Began(float seconds)
    {
        _length = MathF.Min(seconds, MaxSeconds);
        _played = 0f;
        _level = 0f;
        _state = State.Playing;
    }

    /// <summary>It could not be played. Dropped for this run, and the quiet goes on.</summary>
    public void Failed()
    {
        if (Current != null && _byMood.TryGetValue(Current.Mood, out List<MusicTrack> list)) list.Remove(Current);
        if (Current != null) _bags.Remove(Current.Mood);
        Current = null;
        _state = State.Resting;
        _gap = _rng.Range(GapMin, GapMax);
    }

    /// <summary>Each track of a mood in turn, shuffled afresh each time round, so none comes back too soon.</summary>
    MusicTrack Next(string mood)
    {
        if (!_byMood.TryGetValue(mood, out List<MusicTrack> list) || list.Count == 0) return null;
        if (!_bags.TryGetValue(mood, out Queue<MusicTrack> bag) || bag.Count == 0)
        {
            var order = new List<MusicTrack>(list);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = _rng.Int(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            _bags[mood] = bag = new Queue<MusicTrack>(order);
        }
        return bag.Dequeue();
    }
}
