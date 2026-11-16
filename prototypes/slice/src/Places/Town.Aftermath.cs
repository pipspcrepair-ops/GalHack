using Wildlife;

namespace Slice.Places;

public sealed partial class Town
{
    /// <summary>Whether the one they'd fight is down: nobody fights, halts or holds a gun on a body.</summary>
    bool YouDown => Brain.Down(_places?.Zone?.Wildlife?.Player);

    /// <summary>
    /// The sleeper is dead: everyone in the fight stands down and goes back to their day, and nobody
    /// holds them at a checkpoint or keeps a gun on them. The place stays as it was (hostile stays hostile
    /// for when they come back).
    /// </summary>
    public void StandDown()
    {
        _alert = false;
        foreach (Person p in People)
        {
            if (p.Engaged) Disengage(p);
            p.Halt = 0f;
            p.Holding = false;
            p.Pursue = 0f;
        }
    }
}
