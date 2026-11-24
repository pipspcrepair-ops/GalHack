using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// The outside of a body's torso piece as the rig sees it: a stack of slices up the body, each
/// an ellipse with its own half-width and its own depth in front and behind, measured from the
/// built piece itself (lofts, plates, coats, a big chest piece, a pack), so whatever someone wears
/// is what their arms keep out of. In the piece's own space: +Y up from the hips, -Z the front.
/// </summary>
public sealed class TorsoShell
{
    const int Slices = 16;
    readonly float[] _w = new float[Slices], _front = new float[Slices], _back = new float[Slices];
    public readonly float Bottom, Top;
    readonly float _step;

    public TorsoShell(Piece body, float bottom, float top)
    {
        Bottom = bottom;
        Top = top;
        _step = (top - bottom) / (Slices - 1);
        var pts = new List<Vector3>(512);
        foreach (Prim p in body.Prims) Sample(p, pts);
        foreach (Vector3 v in pts)
        {
            float s = (v.Y - bottom) / _step;
            // Each point widens the slices it falls in, and half a slice either side (the ends round off).
            int a = (int)MathF.Floor(s - 0.5f), b = (int)MathF.Ceiling(s + 0.5f);
            for (int i = Math.Max(0, a); i <= Math.Min(Slices - 1, b); i++)
            {
                _w[i] = MathF.Max(_w[i], MathF.Abs(v.X));
                if (v.Z < 0f) _front[i] = MathF.Max(_front[i], -v.Z);
                else _back[i] = MathF.Max(_back[i], v.Z);
            }
        }
        for (int i = 0; i < Slices; i++)
        {
            _w[i] = MathF.Max(_w[i], 0.02f);
            _front[i] = MathF.Max(_front[i], 0.02f);
            _back[i] = MathF.Max(_back[i], 0.02f);
        }
    }

    /// <summary>Points on the outside of a primitive, in the piece's space.</summary>
    static void Sample(in Prim p, List<Vector3> into)
    {
        Transform3D t = p.Local;
        const int ring = 10;
        switch (p.Shape)
        {
            case Shape.Loft:
            {
                LoftProfile l = p.Loft;
                for (int i = 0; i < l.Count; i++)
                    for (int k = 0; k < ring; k++)
                    {
                        float a = MathF.Tau * k / ring;
                        into.Add(t * new Vector3(l.W[i] * MathF.Cos(a), l.Y[i] + l.H[i] * MathF.Sin(a), l.Z[i]));
                    }
                break;
            }
            case Shape.Box:
            {
                Vector3 h = p.Size * 0.5f;
                for (int k = 0; k < 8; k++) into.Add(t * new Vector3((k & 1) == 0 ? -h.X : h.X, (k & 2) == 0 ? -h.Y : h.Y, (k & 4) == 0 ? -h.Z : h.Z));
                break;
            }
            case Shape.Sphere:
            {
                Vector3 h = p.Size * 0.5f;
                for (int j = -1; j <= 1; j++)
                    for (int k = 0; k < ring; k++)
                    {
                        float a = MathF.Tau * k / ring, c = MathF.Cos(j * 0.7f);
                        into.Add(t * new Vector3(h.X * MathF.Cos(a) * c, h.Y * MathF.Sin(j * 0.7f), h.Z * MathF.Sin(a) * c));
                    }
                into.Add(t * new Vector3(0f, h.Y, 0f));
                into.Add(t * new Vector3(0f, -h.Y, 0f));
                break;
            }
            case Shape.Taper:
            {
                // From the origin along +Y: a sphere of the first radius at each end, the second at the far one.
                float r0 = p.Size.X * 0.5f, r1 = p.Size.Z * 0.5f, len = p.Size.Y;
                for (int k = 0; k < ring; k++)
                {
                    float a = MathF.Tau * k / ring;
                    into.Add(t * new Vector3(r0 * MathF.Cos(a), 0f, r0 * MathF.Sin(a)));
                    into.Add(t * new Vector3(r1 * MathF.Cos(a), len, r1 * MathF.Sin(a)));
                }
                into.Add(t * new Vector3(0f, -r0, 0f));
                into.Add(t * new Vector3(0f, len + r1, 0f));
                break;
            }
            default:
            {
                // Cylinders, capsules and cones: centred, along +Y.
                float r0 = p.Size.X * 0.5f, r1 = (p.Shape == Shape.Cone ? 0f : p.Size.Z * 0.5f), h = p.Size.Y * 0.5f;
                if (p.Shape == Shape.Capsule) r1 = r0;
                for (int k = 0; k < ring; k++)
                {
                    float a = MathF.Tau * k / ring;
                    into.Add(t * new Vector3(r0 * MathF.Cos(a), -h, r0 * MathF.Sin(a)));
                    into.Add(t * new Vector3(r1 * MathF.Cos(a), h, r1 * MathF.Sin(a)));
                }
                break;
            }
        }
    }

    void At(float y, out float w, out float front, out float back)
    {
        float s = Math.Clamp((y - Bottom) / _step, 0f, Slices - 1.001f);
        int i = (int)s;
        float f = s - i;
        w = _w[i] + (_w[i + 1] - _w[i]) * f;
        front = _front[i] + (_front[i + 1] - _front[i]) * f;
        back = _back[i] + (_back[i + 1] - _back[i]) * f;
    }

    /// <summary>
    /// How far out a point is, in the piece's space: under 1 inside the torso, 1 on its skin, over 1
    /// outside. Above the shoulders or below the hips nothing is torso.
    /// </summary>
    public float Out(Vector3 p)
    {
        if (p.Y < Bottom || p.Y > Top) return 9f;
        At(p.Y, out float w, out float front, out float back);
        float z = p.Z / (p.Z < 0f ? front : back);
        float x = p.X / w;
        return MathF.Sqrt(x * x + z * z);
    }

    /// <summary>The point moved straight out across its slice until it is <paramref name="clear"/> m outside the skin (unchanged if it already is).</summary>
    public Vector3 PushOut(Vector3 p, float clear)
    {
        if (p.Y < Bottom || p.Y > Top) return p;
        At(p.Y, out float w, out float front, out float back);
        float d = p.Z < 0f ? front : back;
        // Outside by "clear" means inside a slice grown by it.
        float x = p.X / (w + clear), z = p.Z / (d + clear);
        float r = MathF.Sqrt(x * x + z * z);
        if (r >= 1f) return p;
        if (r < 1e-4f) return new Vector3(p.X, p.Y, -(d + clear)); // dead centre: out the front
        return new Vector3(p.X / r, p.Y, p.Z / r);
    }

    /// <summary>The front of the torso at a height and a sideways offset (the chest under a hand laid on it), in the piece's space.</summary>
    public Vector3 FrontAt(float y, float x) => Surface(y, x, true);

    /// <summary>The back of the torso at a height and a sideways offset, in the piece's space.</summary>
    public Vector3 BackAt(float y, float x) => Surface(y, x, false);

    Vector3 Surface(float y, float x, bool front)
    {
        At(y, out float w, out float f, out float b);
        float k = Math.Clamp(x / w, -0.95f, 0.95f), z = (front ? f : b) * MathF.Sqrt(1f - k * k);
        return new Vector3(x, y, front ? -z : z);
    }

    /// <summary>How high the torso reaches at a sideways offset: the top of the shoulder there.</summary>
    public float TopAt(float x)
    {
        x = MathF.Abs(x);
        for (int i = Slices - 1; i >= 0; i--)
            if (_w[i] >= x) return i == Slices - 1 ? Top : Bottom + _step * (i + (_w[i] - x) / MathF.Max(1e-4f, _w[i] - _w[i + 1]));
        return Bottom;
    }

    /// <summary>Half the torso's width at a height.</summary>
    public float HalfWidth(float y)
    {
        At(y, out float w, out _, out _);
        return w;
    }
}
