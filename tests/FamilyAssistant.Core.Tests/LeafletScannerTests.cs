using FamilyAssistant.Core.Shopping;
using Xunit;

public sealed class LeafletScannerTests
{
    [Fact]
    public void FindsLeafletLinksAndViewerId()
    {
        var html = """
            <a href="/pl/press,id,j1am7fyua,title,codziennie-niskie-ceny-p-oferta-od-21-09">x</a>
            <a href="https://www.biedronka.pl/pl/press,id,j1am7fyua,title,codziennie-niskie-ceny-p-oferta-od-21-09#page=1">dup</a>
            <a href="/pl/pressadult,id,j1dkl65pr,title,alkohol-p-od-21-09">alk</a>
            """;
        var links = LeafletScanner.ParseLeafletLinks(html);
        Assert.Equal(new[] { "j1am7fyua", "j1dkl65pr" }, links.Select(l => l.Id).ToArray());
        Assert.Equal("https://www.biedronka.pl/pl/pressadult,id,j1dkl65pr,title,alkohol-p-od-21-09", links[1].Url);
        Assert.Equal("30624646-7cc4-44f9-9e4a-eb2a0abd171d",
            LeafletScanner.ParseLeafletUuid("""window.galleryLeaflet.init("30624646-7cc4-44f9-9e4a-eb2a0abd171d");"""));
        Assert.Null(LeafletScanner.ParseLeafletUuid("<html></html>"));
    }

    [Fact]
    public void ParsesOffersFromModelAnswer()
    {
        var answer = """
            Oto produkty:
            ```json
            [{"name":"Mleko UHT 3,2% 1 l","price":"2,99","regular_price":3.79,"conditions":"przy zakupie 6 szt.","valid_to":"2026-09-26"},
             {"name":"","price":1},{"name":"Masło 200 g","price":null}]
            ```
            """;
        var offers = LeafletScanner.ParseOffers(answer, "gazetka", "https://x", 3);
        Assert.Equal(2, offers.Count);
        Assert.Equal(2.99m, offers[0].Price); Assert.Equal(3.79m, offers[0].RegularPrice);
        Assert.Equal("przy zakupie 6 szt.", offers[0].Conditions); Assert.Equal(3, offers[0].Page);
        Assert.Null(offers[1].Price);
        Assert.Empty(LeafletScanner.ParseOffers("brak produktów", "g", "u", 0));
    }

    [Fact]
    public void ExpiredOffersAreInactive()
    {
        var offer = new LeafletOffer("Mleko", 2.99m, null, null, null, "2026-09-26", "g", "u", 0);
        Assert.True(LeafletScanner.Active(offer, new DateOnly(2026, 9, 26)));
        Assert.False(LeafletScanner.Active(offer, new DateOnly(2026, 9, 27)));
        Assert.True(LeafletScanner.Active(offer with { ValidTo = null }, new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void ProposalShowsDealNextToProduct()
    {
        var product = new ProductView("a", "Mleko UHT 3,2% 1 l", "Suggested", 5, "2026-09-01", 1, ["Szymon"], "", 0.5, null, 5, null, null, false);
        var deal = new LeafletMatch("a", product.Name, new LeafletOffer("Mleko Łaciate UHT 3,2%", 2.99m, 3.79m, "przy zakupie 6 szt.", null, "2026-09-26", "g", "u", 0), true, "");
        var text = ShoppingMessenger.ProposalText([product], [deal]);
        Assert.Contains("1. Mleko UHT 3,2% 1 l — 🏷 2,99 zł, przy zakupie 6 szt., do 26.09", text);
        Assert.Equal(new[] { "b", "a" }, ShoppingMessenger.Candidates([product with { Id = "a" }, product with { Id = "b" }], 5, new HashSet<string> { "b" }).Select(p => p.Id).ToArray());
    }
}
