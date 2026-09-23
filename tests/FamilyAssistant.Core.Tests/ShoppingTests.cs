using System.Text;
using System.Text.Json;
using FamilyAssistant.Core.Shopping;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class ShoppingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shopping-tests-" + Guid.NewGuid());
    private ShoppingStore Store() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Shopping:DatabasePath"] = Path.Combine(directory, "shopping.sqlite") }).Build(), TimeProvider.System);
    private static byte[] Receipt(int id = 1, int total = 150, bool cancelled = false, string date = "2026-09-01T12:00:00Z")
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { dokument = new {
            naglowek = new { wersja = "JPK_KASA_PARAGON_v2-0" }, podmiot1 = new { NIP = "7791011327", nrUnik = "SYNTHETIC" },
            paragon = new { JPKID = id, pamiecChr = 1, zakSprzed = date, podsum = new { waluta = "PLN", sumaBrutto = total },
                pozycja = new[] { new { towar = new { nazwa = "Produkt Testowy     A", idStPTU = "A", ilosc = "0,500", cena = 400, brutto = 150, oper = cancelled, rabat = new { wart = -50 } } } } }
        } });
        return JsonSerializer.SerializeToUtf8Bytes(new { data = "header." + Convert.ToBase64String(payload).TrimEnd('=').Replace('+','-').Replace('/','_') + ".signature" });
    }
    [Fact] public void ParsesDiscountedMoneyAndCommaQuantity()
    {
        var parsed = ReceiptParser.Parse(Receipt());
        Assert.Equal(150, parsed.Total); Assert.Equal(150, parsed.Lines[0].Total);
        Assert.Equal(0.5m, parsed.Lines[0].Quantity); Assert.Equal("Produkt Testowy", parsed.Lines[0].Name);
    }
    [Fact] public void RejectsMismatchedTotalsAndCancellation()
    {
        Assert.Throws<ShoppingFailure>(() => ReceiptParser.Parse(Receipt(total: 200)));
        Assert.Throws<ShoppingFailure>(() => ReceiptParser.Parse(Receipt(cancelled: true)));
        Assert.Throws<ShoppingFailure>(() => ReceiptParser.Parse(Encoding.UTF8.GetBytes("{}")));
    }
    [Fact] public async Task DeduplicatesAcrossAccountsAndKeepsDecisions()
    {
        var store = Store(); var receipt = ReceiptParser.Parse(Receipt());
        Assert.True(await store.Import(receipt, "Karta 1"));
        var product = Assert.Single((await store.Read()).Products);
        await store.Update(product.Id, "Dismissed", "Czytelna nazwa");
        Assert.False(await store.Import(receipt, "Karta 2"));
        var saved = await Store().Read();
        Assert.Single(saved.Receipts); Assert.Equal("Dismissed", saved.Products[0].Status); Assert.Equal("Czytelna nazwa", saved.Products[0].Name);
        Assert.Null(saved.Products[0].SuggestedDate);
    }
    [Fact] public async Task CombinesCardsAndUsesDistinctPurchaseDays()
    {
        var store = Store();
        await store.Import(ReceiptParser.Parse(Receipt()), "Pierwsza");
        await store.Import(ReceiptParser.Parse(Receipt(2, date:"2026-09-08T12:00:00Z")), "Druga");
        Assert.Null(Assert.Single((await store.Read()).Products).SuggestedDate);
        await store.Import(ReceiptParser.Parse(Receipt(3, date:"2026-09-15T12:00:00Z")), "Pierwsza");
        var product = Assert.Single((await store.Read()).Products);
        Assert.Equal(3, product.PurchaseDays); Assert.Equal(2, product.Accounts.Length); Assert.Equal("2026-09-22", product.SuggestedDate);
    }
    [Fact] public async Task ConflictLeavesOriginalReceiptIntact()
    {
        var store = Store(); await store.Import(ReceiptParser.Parse(Receipt()), "");
        await Assert.ThrowsAsync<ShoppingFailure>(() => store.Import(ReceiptParser.Parse(Receipt(date:"2026-09-02T12:00:00Z")), ""));
        Assert.Single((await store.Read()).Receipts);
    }
    [Fact] public void AcceptsReceiptLevelVoucherWithoutSubtractingItFromQuantities()
    {
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(Receipt())!;
        var segments = envelope["data"]!.GetValue<string>().Split('.');
        var encoded = segments[1].Replace('-', '+').Replace('_', '/');
        var payload = System.Text.Json.Nodes.JsonNode.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')))!;
        var receipt = payload["dokument"]!["paragon"]!;
        receipt["podsum"]!["sumaBrutto"] = 100;
        receipt["pozycja"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("{\"rabat\":{\"oper\":false,\"wart\":-50}}"));
        envelope["data"] = "h." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToJsonString())).TrimEnd('=') + ".s";
        var parsed = ReceiptParser.Parse(Encoding.UTF8.GetBytes(envelope.ToJsonString()));
        Assert.Equal(100, parsed.Total); Assert.Equal(150, parsed.Lines[0].Total); Assert.Equal(.5m, parsed.Lines[0].Quantity);
    }
    [Fact] public async Task LargerLastPurchaseExtendsEstimatedCoverage()
    {
        var store = Store();
        await store.Import(ReceiptParser.Parse(Receipt()), "Pierwsza");
        await store.Import(ReceiptParser.Parse(Receipt(2, date:"2026-09-08T12:00:00Z")), "Druga");
        var latest = ReceiptParser.Parse(Receipt(3, date:"2026-09-15T12:00:00Z"));
        latest = latest with { Lines = [latest.Lines[0] with { Quantity = 1m }] };
        await store.Import(latest, "Pierwsza");
        var product = Assert.Single((await store.Read()).Products);
        Assert.Equal("2026-09-29", product.SuggestedDate); Assert.Equal(2m, product.TotalQuantity);
        Assert.Equal(7, product.TypicalIntervalDays);
    }
    [Theory] [InlineData("Confirmed")] [InlineData("Purchased")] [InlineData("Dismissed")] [InlineData("Suggested")]
    public async Task SavesStatuses(string status)
    {
        var store = Store(); await store.Import(ReceiptParser.Parse(Receipt()), "");
        var product = Assert.Single((await store.Read()).Products);
        await store.Update(product.Id, status, null);
        Assert.Equal(status, Assert.Single((await Store().Read()).Products).Status);
    }
    private BrowserReceiptInbox Inbox(string downloads) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Shopping:DatabasePath"] = Path.Combine(directory, "shopping.sqlite"), ["Shopping:ImportDirectory"] = Path.Combine(directory, "imports"),
        ["Shopping:BrowserDirectory"] = downloads, ["Shopping:Account2Label"] = "Joanna" }).Build(), Store());
    private static string Downloaded(string folder, string name, byte[] bytes)
    {
        Directory.CreateDirectory(folder); var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1)); return path;
    }
    [Fact] public async Task BrowserInboxImportsWithAccountLabelKeepsOriginalAndSkipsRepeats()
    {
        var downloads = Path.Combine(directory, "downloads");
        var original = Downloaded(Path.Combine(downloads, "account-2"), "1.json", Receipt());
        var fresh = Downloaded(Path.Combine(downloads, "account-2"), "2.json", Receipt(2, date: "2026-09-08T12:00:00Z"));
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);
        var inbox = Inbox(downloads);
        await inbox.Scan(CancellationToken.None); await inbox.Scan(CancellationToken.None);
        var saved = await Store().Read();
        Assert.Equal("Joanna", Assert.Single(saved.Receipts).Account);
        Assert.True(File.Exists(original)); Assert.True(File.Exists(fresh));
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddMinutes(-1));
        await inbox.Scan(CancellationToken.None);
        Assert.Equal(2, (await Store().Read()).Receipts.Length);
    }
    [Fact] public async Task BrowserInboxRecordsRejectedFileOnce()
    {
        var downloads = Path.Combine(directory, "downloads");
        var bad = Downloaded(Path.Combine(downloads, "account-1"), "bad.json", Encoding.UTF8.GetBytes("{}"));
        var inbox = Inbox(downloads);
        await inbox.Scan(CancellationToken.None); await inbox.Scan(CancellationToken.None);
        Assert.Empty((await Store().Read()).Receipts); Assert.True(File.Exists(bad));
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "imports", "browser-imports", "account-1"), "*.error"));
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
