using Ratio.Application.Abstractions;
using Ratio.Application.Filters;

namespace Ratio.Application.Tests.Filters;

public class LevelOptionsTests
{
    [Fact]
    public void Offers_the_first_and_second_levels_of_the_state_courts()
    {
        var options = LevelOptions.Describe(["First", "Second"]);

        Assert.Equal([new LevelOption("first", "Primeiro grau"), new LevelOption("second", "Segundo grau")], options);
    }

    [Fact]
    public void Offers_the_first_level_for_a_special_court_alone()
    {
        Assert.Equal(["first"], LevelOptions.Describe(["SpecialCourt"]).Select(option => option.Code));
    }

    [Fact]
    public void Offers_the_second_level_for_an_appeal_panel_alone()
    {
        Assert.Equal(["second"], LevelOptions.Describe(["AppealPanel"]).Select(option => option.Code));
    }

    [Fact]
    public void Leaves_out_the_superior_level_without_a_superior_court_case()
    {
        Assert.DoesNotContain(LevelOptions.Describe(["First", "Second", "AppealPanel"]), option => option.Code == "superior");
    }

    [Fact]
    public void Offers_the_superior_level_with_a_superior_court_case()
    {
        Assert.Contains(new LevelOption("superior", "Superior"), LevelOptions.Describe(["First", "Superior"]));
    }

    [Fact]
    public void Keeps_the_options_in_level_order()
    {
        var options = LevelOptions.Describe(["Superior", "Second", "First"]);

        Assert.Equal(["first", "second", "superior"], options.Select(option => option.Code));
    }

    [Fact]
    public void Offers_nothing_when_no_level_is_loaded()
    {
        Assert.Empty(LevelOptions.Describe([]));
    }

    [Fact]
    public void Resolves_the_levels_selected_by_an_option()
    {
        Assert.True(LevelOptions.TryGetLevels("second", out var levels));
        Assert.Equal(["Second", "AppealPanel"], levels);
    }

    [Fact]
    public void Rejects_an_unknown_option()
    {
        Assert.False(LevelOptions.TryGetLevels("terceiro", out _));
    }
}
