using Slice.Vessels;

namespace Slice.Game;

/// <summary>
/// The structure's toolbar from a command chair (<see cref="StructureBar"/>): a number toggles a toggle slot, picks a gun
/// slot or runs a scan, and the trigger fires the picked guns. Standing up hands the bottom of the visor back to BAG.
/// </summary>
public partial class Game
{
    /// <summary>The numbers, seated: each presses its slot of the structure's bar.</summary>
    void StepBar(Vessel v, Controls c)
    {
        if (Menu.IsOpen) return;
        for (int i = 0; i < StructureBar.Slots; i++)
            if (c.WasPressed(Godot.Key.Key1 + i)) v.Bar.Press(this, i);
    }

    /// <summary>After the physics, seated: the pilot's trigger on the picked guns (the other guns finish their reloads).</summary>
    void FireBar(float dt)
    {
        if (Survivor.Seated is not Vessel v || Suit.Dead) return;
        bool trigger = !Menu.IsOpen && v.Roles.Pilot == Vessel.Player;
        v.Bar.Fire(Arms, dt, trigger && Controls.Primary, trigger && Controls.PrimaryPressed);
    }

    /// <summary>
    /// The structure's scanner (its toolbar's scan slot): the places within its range are marked for a while. The one hook
    /// for proto/scanner's place marks, which take the structure's position and range when it merges; until then it is
    /// the scanner's ping from the seat.
    /// </summary>
    public bool StructureScan(Vessel v, float range) => Scan();
}
