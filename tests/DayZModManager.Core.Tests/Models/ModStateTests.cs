using DayZModManager.Core.Models;

namespace DayZModManager.Core.Tests.Models;

public class ModStateTests
{
    private static ModState CreateState(IEnumerable<string>? workshop = null, IEnumerable<string>? loaded = null)
    {
        var state = new ModState();
        if (workshop is not null) state.SetWorkshopMods(workshop);
        if (loaded is not null) state.ReplaceLoadedMods(loaded);
        return state;
    }

    [Fact]
    public void Load_AddsAvailableMod_ToEndOfLoadedList()
    {
        var state = CreateState(workshop: new[] { "@CF", "@Expansion", "@Zen" });

        bool result = state.Load("@Expansion");
        state.Load("@CF");

        Assert.True(result);
        Assert.Equal(new[] { "@Expansion", "@CF" }, state.LoadedMods);
    }

    [Fact]
    public void Load_ReturnsFalse_WhenAlreadyLoaded()
    {
        var state = CreateState(workshop: new[] { "@CF" }, loaded: new[] { "@CF" });

        Assert.False(state.Load("@CF"));
        Assert.Equal(new[] { "@CF" }, state.LoadedMods);
    }

    [Fact]
    public void Load_ReturnsFalse_WhenModNotInWorkshop()
    {
        var state = CreateState(workshop: new[] { "@CF" });

        Assert.False(state.Load("@Ghost"));
        Assert.Empty(state.LoadedMods);
    }

    [Fact]
    public void Unload_RemovesMod()
    {
        var state = CreateState(loaded: new[] { "@CF", "@Expansion" });

        Assert.True(state.Unload("@CF"));
        Assert.Equal(new[] { "@Expansion" }, state.LoadedMods);
    }

    [Fact]
    public void Unload_ReturnsFalse_WhenNotLoaded()
    {
        var state = CreateState(loaded: new[] { "@CF" });

        Assert.False(state.Unload("@Zen"));
    }

    [Fact]
    public void MoveUp_MovesModTowardStart()
    {
        var state = CreateState(loaded: new[] { "@A", "@B", "@C" });

        Assert.True(state.MoveUp("@B"));
        Assert.Equal(new[] { "@B", "@A", "@C" }, state.LoadedMods);
    }

    [Fact]
    public void MoveUp_ReturnsFalse_AtTop()
    {
        var state = CreateState(loaded: new[] { "@A", "@B" });

        Assert.False(state.MoveUp("@A"));
        Assert.Equal(new[] { "@A", "@B" }, state.LoadedMods);
    }

    [Fact]
    public void MoveDown_MovesModTowardEnd()
    {
        var state = CreateState(loaded: new[] { "@A", "@B", "@C" });

        Assert.True(state.MoveDown("@B"));
        Assert.Equal(new[] { "@A", "@C", "@B" }, state.LoadedMods);
    }

    [Fact]
    public void MoveDown_ReturnsFalse_AtBottom()
    {
        var state = CreateState(loaded: new[] { "@A", "@B" });

        Assert.False(state.MoveDown("@B"));
    }

    [Fact]
    public void AvailableMods_IsWorkshopMinusLoaded_Sorted()
    {
        var state = CreateState(
            workshop: new[] { "@Zen", "@CF", "@Expansion", "@Aim" },
            loaded: new[] { "@Expansion" });

        Assert.Equal(new[] { "@Aim", "@CF", "@Zen" }, state.AvailableMods);
    }

    [Fact]
    public void MissingMods_IsLoadedMinusWorkshop()
    {
        var state = CreateState(
            workshop: new[] { "@CF", "@Expansion" },
            loaded: new[] { "@CF", "@Expansion", "@OldMod" });

        Assert.Equal(new[] { "@OldMod" }, state.MissingMods);
    }

    [Fact]
    public void SetWorkshopMods_DoesNotSilentlyDropMissingLoadedMods()
    {
        var state = CreateState(loaded: new[] { "@CF", "@OldMod" });
        state.SetWorkshopMods(new[] { "@CF", "@New" });

        Assert.Equal(new[] { "@CF", "@OldMod" }, state.LoadedMods);
        Assert.Equal(new[] { "@OldMod" }, state.MissingMods);
        Assert.Equal(new[] { "@New" }, state.AvailableMods);
    }

    [Fact]
    public void ReplaceLoadedMods_DeduplicatesAndPreservesOrder()
    {
        var state = new ModState();
        state.ReplaceLoadedMods(new[] { "@CF", "@CF", "@Expansion", "  " });

        Assert.Equal(new[] { "@CF", "@Expansion" }, state.LoadedMods);
    }

    [Fact]
    public void Move_MovesModToTargetIndex()
    {
        var state = CreateState(loaded: new[] { "@A", "@B", "@C", "@D" });

        Assert.True(state.Move("@A", 2));

        Assert.Equal(new[] { "@B", "@C", "@A", "@D" }, state.LoadedMods);
    }

    [Fact]
    public void Move_ReturnsFalse_WhenPositionUnchanged()
    {
        var state = CreateState(loaded: new[] { "@A", "@B", "@C" });

        Assert.False(state.Move("@B", 1));
        Assert.Equal(new[] { "@A", "@B", "@C" }, state.LoadedMods);
    }

    [Fact]
    public void Move_ClampsIndexToValidRange()
    {
        var state = CreateState(loaded: new[] { "@A", "@B", "@C" });

        state.Move("@A", 99);

        Assert.Equal(new[] { "@B", "@C", "@A" }, state.LoadedMods);
    }

    [Fact]
    public void Reconciliation_IsCaseInsensitive()
    {
        var state = CreateState(workshop: new[] { "@CF", "@Expansion" }, loaded: new[] { "@cf" });

        Assert.Empty(state.MissingMods);
        Assert.Equal(new[] { "@Expansion" }, state.AvailableMods);
    }

    [Fact]
    public void Load_ReturnsFalse_WhenAlreadyLoaded_DifferentCase()
    {
        var state = CreateState(workshop: new[] { "@CF" }, loaded: new[] { "@cf" });

        Assert.False(state.Load("@CF"));
        Assert.Equal(new[] { "@cf" }, state.LoadedMods);
    }
}
