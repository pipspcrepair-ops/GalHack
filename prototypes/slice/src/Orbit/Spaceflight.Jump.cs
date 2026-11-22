using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Items.Bridge;
using Godot;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;
using Survivor = Slice.Player.Survivor;

namespace Slice.Orbit;

/// <summary>
/// Running a <see cref="Jump"/>: the spool on the structure's power, the tunnel while the destination is made, the
/// arrival through the ledger and the handover, and what becomes of the zone left. The warp drive and the fold drive
/// both start theirs here (<see cref="StartJump"/>), each with its own <see cref="IJumpDestination"/>.
/// </summary>
public partial class Spaceflight
{
    /// <summary>The jump under way, or the last one (see <see cref="Jump.Running"/>).</summary>
    public Jump Jumping { get; private set; }

    /// <summary>Every jump so far, in order (tests read these).</summary>
    public readonly List<Jump> Jumps = new();

    /// <summary>Most a tick's arrival work took on the main thread, ms (tests read it).</summary>
    public double WorstArrivalMs { get; private set; }

    (int Items, int Blocks) _jumpTotals;

    /// <summary>
    /// Spools a drive on a structure for a jump of so far (its fuel's unit), with at least <paramref name="tripSeconds"/> in
    /// the tunnel, to <paramref name="to"/>. Null once it spools; else what stops it, as the console says it.
    /// </summary>
    public string StartJump(Vessel carrier, string drive, double distance, double tripSeconds, IJumpDestination to)
    {
        if (Jumping?.Running == true) return "SPOOLING";
        if (carrier?.Power == null) return "NOT FITTED";
        string no = carrier.Power.Engage(drive, distance);
        if (no != null) return no;
        var j = new Jump(carrier, drive, distance, tripSeconds, to) { From = Game.Zone };
        j.OnJumped = (function, _) => j.Fired |= function == drive;
        carrier.Power.Jumped += j.OnJumped;
        Jumping = j;
        Jumps.Add(j);
        to.Begin(this);
        GD.Print($"[jump: {drive} spooling for {to.Name}, {distance:F2} {carrier.Power.Range(drive)?.Unit}, {tripSeconds:F1} s in the tunnel at least]");
        return null;
    }

    /// <summary>Calls the jump off while it spools (later it is committed). Nothing burns.</summary>
    public void CancelJump(string why = "CANCELLED")
    {
        Jump j = Jumping;
        if (j?.Now != Jump.Stage.Spool) return;
        j.Carrier.Power.Disengage();
        Stop(j, why);
    }

    void Stop(Jump j, string why)
    {
        End(j, Jump.Stage.Cancelled);
        j.Trouble = why;
        j.To.Dropped(this);
        GD.Print($"[jump to {j.To.Name} called off: {why}]");
    }

    void End(Jump j, Jump.Stage stage)
    {
        j.Now = stage;
        j.Carrier.Power.Jumped -= j.OnJumped;
    }

    /// <summary>Once a physics tick: the destination set up a piece at a time, the spool, the tunnel, the arrival.</summary>
    void TickJump(double dt)
    {
        Jump j = Jumping;
        if (j?.Running != true) return;
        long started = Stopwatch.GetTimestamp();
        if (!j.Ready)
        {
            j.Ready = j.To.Step(this);
            j.LongestStepMs = Math.Max(j.LongestStepMs, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        StructurePower p = j.Carrier.Power;
        switch (j.Now)
        {
            case Jump.Stage.Spool:
                if (j.Fired)
                {
                    j.Burnt = p.LastJump?.Burnt ?? 0;
                    j.Spooled = 1;
                    j.HeldAt = j.Carrier.Body.Position;
                    j.HeldTurn = j.Carrier.Body.Rotation;
                    j.Now = Jump.Stage.Tunnel;
                    GD.Print($"[jump: into the tunnel, {j.Burnt} burnt]");
                }
                else if (!p.Engaged(j.Drive)) Stop(j, "NO POWER");
                else if (Game.Zone != j.From) { p.Disengage(); Stop(j, "LEFT SPACE"); }
                else j.Spooled = p.SpoolShare;
                break;
            case Jump.Stage.Tunnel:
                j.InTunnel += dt;
                Hold(j);
                if (j.Ready && j.InTunnel >= j.TripSeconds) BeginArrival(j);
                break;
            case Jump.Stage.Arriving:
                Hold(j);
                if (!Ledger.Committing) FinishArrival(j);
                break;
        }
        WorstArrivalMs = Math.Max(WorstArrivalMs, j.Holding || j.Now == Jump.Stage.Done ? Stopwatch.GetElapsedTime(started).TotalMilliseconds : 0);
    }

    /// <summary>Held still in the tunnel where it went in, and nothing else moves it.</summary>
    static void Hold(Jump j)
    {
        var b = j.Carrier.Body;
        b.Position = j.HeldAt;
        b.Rotation = j.HeldTurn;
        b.Velocity = N.Vector3.Zero;
        b.AngularVelocity = N.Vector3.Zero;
        j.Carrier.Binding.Teleport();
    }

    /// <summary>Out of the tunnel: where it arrives is worked out now, and its record committed if it changes zone.</summary>
    void BeginArrival(Jump j)
    {
        JumpArrival a = j.To.Arrival(this, j.Carrier);
        j.Arrived = a;
        if (a.Zone == Game.Zone)
        {
            Arrive(j, a);
            return;
        }
        _jumpTotals = ZoneLedger.Totals(Holders());
        if (!Ledger.Begin(Holders(), IdOf(j.Carrier), ZoneName(a.Zone)))
        {
            Refused();
            Stop(j, "REFUSED");
            return;
        }
        j.Now = Jump.Stage.Arriving;
    }

    void FinishArrival(Jump j)
    {
        if (!Ledger.Finish())
        {
            Refused();
            Stop(j, "REFUSED");
            return;
        }
        Arrive(j, j.Arrived.Value);
    }

    /// <summary>The structure and everyone aboard, at rest at the arrival, in its zone; the zone left goes as the destination says.</summary>
    void Arrive(Jump j, JumpArrival a)
    {
        var watch = Stopwatch.StartNew();
        Slice.Game.Game g = Game;
        SurfaceZone from = g.Zone, to = a.Zone;
        Survivor s = g.Survivor;
        Vessel v = j.Carrier;
        v.Body.AngularVelocity = N.Vector3.Zero;
        if (to == from)
        {
            v.Body.Position = a.Position;
            v.Body.Rotation = a.Rotation;
            v.Body.Velocity = N.Vector3.Zero;
            v.Binding.Teleport();
            v.Node.ResetPhysicsInterpolation();
            if (s.Body.Frame == v.Body) s.Node.ResetPhysicsInterpolation();
        }
        else
        {
            StreamAhead(false, default);
            Wake(to);
            Handover.Move(from, to, s, v, a.Position, a.Rotation, N.Vector3.Zero);
            g.EnterZone(to);
            if (to is OrbitZone orbit) InOrbitOf(orbit);
        }
        End(j, Jump.Stage.Done);
        if (to != from)
        {
            j.To.Left(this, from);
            foreach (string problem in Ledger.Audit(Holders(), _jumpTotals)) GD.PrintErr($"jump audit: {problem}");
        }
        GD.Print($"[jump: arrived at {j.To.Name} in {to.Name} after {j.InTunnel:F1} s in the tunnel, {watch.Elapsed.TotalMilliseconds:F1} ms]");
    }

    /// <summary>The orbit you fly in is now this one: the seam starts over in its terms, every sky on the one clock.</summary>
    void InOrbitOf(OrbitZone orbit)
    {
        double days = Below.Sky.Days;
        Orbit = orbit;
        _aligned = false;
        _band = 0f; // the next frame lays it all out as space (AllSpace)
        _wreckFade = -1f;
        _sky = null;
        foreach (SurfaceZone z in AllSurfaces())
            if (z.Sky != null) z.Sky.Days = days;
        Game.Camera.Far = Rules.Space.CameraFar;
    }
}
