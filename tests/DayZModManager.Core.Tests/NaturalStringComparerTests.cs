using DayZModManager.Core;

namespace DayZModManager.Core.Tests;

public class NaturalStringComparerTests
{
    [Fact]
    public void SortsDigitRunsNumerically()
    {
        var names = new[] { "10_weapons.xml", "2_base.xml", "1_core.xml", "99_override.xml" };

        List<string> sorted = names.OrderBy(name => name, NaturalStringComparer.Instance).ToList();

        Assert.Equal(new[] { "1_core.xml", "2_base.xml", "10_weapons.xml", "99_override.xml" }, sorted);
    }

    [Fact]
    public void ComparesEqualNumericValuesWithDifferentLeadingZeros()
    {
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("01_a.xml", "1_a.xml"));
    }

    [Fact]
    public void FallsBackToCaseInsensitiveTextOrder()
    {
        var names = new[] { "b.xml", "A.xml" };

        List<string> sorted = names.OrderBy(name => name, NaturalStringComparer.Instance).ToList();

        Assert.Equal(new[] { "A.xml", "b.xml" }, sorted);
    }
}
