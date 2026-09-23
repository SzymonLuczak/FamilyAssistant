using Microsoft.Extensions.Configuration;
using FamilyAssistant.Core.Shopping;
using Xunit;

public sealed class ShoppingMessengerTests
{
    private static ProductView Product(string id, string name, string status = "Suggested", int days = 3, bool runOut = false, string? due = null) =>
        new(id, name, status, days, "2026-09-01", 1, ["Szymon"], "", 0.5, due, 1, null, null, runOut);

    [Theory]
    [InlineData("1 3 5", 5, new[] { 1, 3, 5 })]
    [InlineData(" 2-4, 9 ", 5, new[] { 2, 3, 4 })]
    [InlineData("3;1;3", 5, new[] { 1, 3 })]
    [InlineData("2–3", 5, new[] { 2, 3 })]
    [InlineData("0 7", 5, new int[0])]
    public void ParsesNumberedReply(string text, int count, int[] expected) => Assert.Equal(expected, ShoppingMessenger.ParseSelection(text, count));

    [Fact]
    public void ProposesRecurringSuggestionsWithRunningOutFirst()
    {
        var picked = ShoppingMessenger.Candidates([
            Product("a", "Chleb", due: "2026-09-30"), Product("b", "Mleko", runOut: true, due: "2026-09-24"),
            Product("c", "Jednorazowy", days: 1), Product("d", "Masło", status: "Confirmed"), Product("e", "Jajka", due: "2026-09-26")], 10);
        Assert.Equal(new[] { "b", "e", "a" }, picked.Select(p => p.Id).ToArray());
        var text = ShoppingMessenger.ProposalText(picked);
        Assert.Contains("1. Mleko (może się kończyć)", text); Assert.Contains("3. Chleb", text);
        Assert.Single(ShoppingMessenger.Candidates(picked, 1));
    }

    [Fact]
    public void ListContainsOnlyConfirmedProducts()
    {
        Assert.Equal("Lista zakupów:\n- Chleb\n- Mleko", ShoppingMessenger.ListText([Product("a", "Mleko", "Confirmed"), Product("b", "Chleb", "Confirmed"), Product("c", "Ser")]));
        Assert.Equal("Lista zakupów jest pusta.", ShoppingMessenger.ListText([Product("c", "Ser")]));
    }

    [Fact]
    public void ParsesSeveralProposalDays()
    {
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Thursday }, ShoppingMessenger.ParseDays("Monday,thursday"));
        Assert.Empty(ShoppingMessenger.ParseDays(""));
        Assert.Equal(new[] { DayOfWeek.Friday }, ShoppingMessenger.ParseDays("Friday, 3, nope"));
    }

    [Fact]
    public void ReadableNamesFromDictionaryOrFallback()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{\"ĆWIARTKAKURCZAVAC KG\": \"Ćwiartka z kurczaka (na wagę)\"}");
        var names = new ProductNames(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Shopping:ProductNamesPath"] = path }).Build());
        Assert.Equal("Ćwiartka z kurczaka (na wagę)", names.Display("ĆwiartkaKurczaVac kg"));
        Assert.Equal("Masło Ekstra 200 g", names.Display("MasłoEkstra200g"));
        Assert.Equal("Banan (na wagę)", names.Display("Banan Luz"));
        Assert.Equal("Mleko", names.Display("Mleko"));
        File.Delete(path);
    }
}
