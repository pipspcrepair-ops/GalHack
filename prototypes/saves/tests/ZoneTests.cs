using System;
using Godot;

namespace Saves.Tests;

/// <summary>Placeholder until the zone manager exists.</summary>
public partial class ZoneTests : Node
{
    public event Action Finished;
    public ZoneTests() { }
    public ZoneTests(TestLog log, string root) { }
    public override void _Process(double delta) => Finished?.Invoke();
}