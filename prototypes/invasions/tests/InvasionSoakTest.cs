namespace Invasions.Tests;

/// <summary>
/// The full navigation soak: 50 seeded invasions for each of the five invading factions (250 in all),
/// with the same limits as the gate's reduced soak (nobody stuck over 3 s, nobody late). About 13
/// minutes, so test-all skips it (InvasionSoakTest.manual); run it before releases or after
/// navigation changes:
///   .\tools\test.ps1 -Project prototypes/invasions -Scene res://tests/InvasionSoakTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class InvasionSoakTest : InvasionTest
{
    protected override int SoakRunsPerFaction => 50;
    protected override bool SoakOnly => true;
}
