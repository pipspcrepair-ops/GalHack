using Godot;

namespace Saves.Tests;

/// <summary>PASS/FAIL/INFO lines in the same style as the other prototypes' tests.</summary>
public sealed class TestLog
{
    public int Failures { get; private set; }
    public int Passes { get; private set; }

    public void Check(bool ok, string what)
    {
        if (ok)
        {
            Passes++;
            GD.Print($"PASS  {what}");
        }
        else Fail(what);
    }

    public void Fail(string what)
    {
        Failures++;
        GD.PrintErr($"FAIL  {what}");
    }

    public void Info(string what) => GD.Print($"INFO  {what}");

    public void Section(string name) => GD.Print($"\n== {name} ==");
}
