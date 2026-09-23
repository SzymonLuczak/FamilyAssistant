using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyAssistant.Core.Shopping;

public sealed class Purchase
{
    public string Id { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Account { get; set; } = "";
    public long PurchasedAt { get; set; }
    public long Total { get; set; }
}
public sealed class PurchaseLine
{
    public int Id { get; set; }
    public string ReceiptId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string RawName { get; set; } = "";
    public decimal Quantity { get; set; }
    public long UnitPrice { get; set; }
    public long Total { get; set; }
}
public sealed class ShoppingProduct
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "Suggested";
    public long UpdatedAt { get; set; }
}
public sealed class ShoppingDatabase(DbContextOptions<ShoppingDatabase> options) : DbContext(options)
{
    public DbSet<Purchase> Receipts => Set<Purchase>();
    public DbSet<PurchaseLine> Lines => Set<PurchaseLine>();
    public DbSet<ShoppingProduct> Products => Set<ShoppingProduct>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PurchaseLine>().HasOne<Purchase>().WithMany().HasForeignKey(l => l.ReceiptId);
        model.Entity<PurchaseLine>().HasOne<ShoppingProduct>().WithMany().HasForeignKey(l => l.ProductId);
    }
}
public sealed record ProductView(string Id, string Name, string Status, int PurchaseDays, string LastPurchase, decimal LastQuantity, string[] Accounts, string Reason, double Confidence, string? SuggestedDate, decimal TotalQuantity, double? TypicalIntervalDays, decimal? EstimatedDailyQuantity, bool MayRunOut);
public sealed record ShoppingView(Purchase[] Receipts, ProductView[] Products);

public sealed class ShoppingStore(IConfiguration config, TimeProvider clock)
{
    public ShoppingStore(IConfiguration config, TimeProvider clock, ProductNames names) : this(config, clock) => this.names = names;
    private readonly ProductNames? names;
    private readonly SemaphoreSlim gate = new(1, 1);
    private async Task<ShoppingDatabase> Open()
    {
        var path = Path.GetFullPath(config["Shopping:DatabasePath"] ?? "/app/data/shopping.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var db = new ShoppingDatabase(new DbContextOptionsBuilder<ShoppingDatabase>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = path }.ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return db;
    }
    public async Task<bool> Import(ParsedReceipt receipt, string account)
    {
        account = account.Trim();
        if (account.Length > 40 || account.Any(char.IsControl)) throw new ShoppingFailure("Nazwa karty może mieć najwyżej 40 znaków.");
        await gate.WaitAsync();
        try
        {
            await using var db = await Open();
            var existing = await db.Receipts.FindAsync(receipt.Id);
            if (existing is not null)
            {
                if (existing.Hash != receipt.Hash) throw new ShoppingFailure("Paragon o tym identyfikatorze ma inną zawartość. Wymaga sprawdzenia.");
                if (existing.Account.Length == 0 && account.Length > 0)
                {
                    existing.Account = account;
                    await db.SaveChangesAsync();
                }
                return false;
            }
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Receipts.Add(new() { Id = receipt.Id, Hash = receipt.Hash, Account = account, PurchasedAt = receipt.PurchasedAt.ToUnixTimeSeconds(), Total = receipt.Total });
            foreach (var group in receipt.Lines.GroupBy(l => l.Key))
            {
                if (!await db.Products.AnyAsync(p => p.Id == group.Key))
                    db.Products.Add(new() { Id = group.Key, Name = group.First().Name, UpdatedAt = clock.GetUtcNow().ToUnixTimeSeconds() });
                foreach (var line in group) db.Lines.Add(new() { ReceiptId = receipt.Id, ProductId = line.Key, RawName = line.RawName, Quantity = line.Quantity, UnitPrice = line.UnitPrice, Total = line.Total });
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        finally { gate.Release(); }
    }
    public async Task Update(string id, string status, string? name)
    {
        if (status is not ("Suggested" or "Confirmed" or "Purchased" or "Dismissed")) throw new ShoppingFailure("Nieprawidłowy status.");
        if (name is not null && (name.Trim().Length is < 1 or > 200 || name.Any(char.IsControl))) throw new ShoppingFailure("Nazwa produktu musi mieć od 1 do 200 znaków.");
        await gate.WaitAsync();
        try
        {
            await using var db = await Open();
            var product = await db.Products.FindAsync(id) ?? throw new ShoppingFailure("Nie znaleziono produktu.");
            product.Status = status;
            if (name is not null) product.Name = name.Trim();
            product.UpdatedAt = clock.GetUtcNow().ToUnixTimeSeconds();
            await db.SaveChangesAsync();
        }
        finally { gate.Release(); }
    }
    public async Task<ShoppingView> Read()
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await Open();
            var receipts = await db.Receipts.AsNoTracking().OrderByDescending(r => r.PurchasedAt).ToArrayAsync();
            var lookup = receipts.ToDictionary(r => r.Id);
            var lines = (await db.Lines.AsNoTracking().ToArrayAsync()).ToLookup(l => l.ProductId);
            var products = await db.Products.AsNoTracking().ToArrayAsync();
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
            DateOnly Day(long timestamp) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(timestamp), zone).DateTime);
            return new(receipts, products.Select(p =>
            {
                var history = lines[p.Id].ToArray();
                var days = history.Select(l => Day(lookup[l.ReceiptId].PurchasedAt)).Distinct().Order().ToArray();
                var last = days[^1];
                var lastQuantity = history.Where(l => Day(lookup[l.ReceiptId].PurchasedAt) == last).Sum(l => l.Quantity);
                string? expected = null;
                double? interval = null;
                decimal? dailyQuantity = null;
                var reason = "Za mało historii do prognozy — sprawdź zapasy przed dodaniem.";
                var mayRunOut = false;
                if (days.Length >= 3)
                {
                    var intervals = days.Zip(days.Skip(1), (a, b) => b.DayNumber - a.DayNumber).Order().ToArray();
                    interval = (intervals[(intervals.Length - 1) / 2] + intervals[intervals.Length / 2]) / 2.0;
                    // Each completed purchase cycle contributes its quantity per elapsed day.
                    // Exclude the latest batch: it has no observed next purchase yet.
                    var rates = days.Zip(days.Skip(1), (a, b) => history.Where(l => Day(lookup[l.ReceiptId].PurchasedAt) == a).Sum(l => l.Quantity) / (b.DayNumber - a.DayNumber)).Order().ToArray();
                    dailyQuantity = (rates[(rates.Length - 1) / 2] + rates[rates.Length / 2]) / 2;
                    var coverage = Math.Clamp((int)Math.Ceiling(decimal.Round(Math.Min(3650m, lastQuantity / dailyQuantity.Value), 10)), 1, 3650);
                    var due = last.AddDays(coverage);
                    expected = due.ToString("yyyy-MM-dd");
                    var today = Day(clock.GetUtcNow().ToUnixTimeSeconds());
                    var stale = today.DayNumber - last.DayNumber > Math.Max(90, coverage * 3);
                    mayRunOut = !stale && due <= today.AddDays(3);
                    reason = stale ? "Dawny zakup — wzorzec może być nieaktualny. Sprawdź, czy nadal kupujecie ten produkt." :
                        "Może wymagać uzupełnienia około " + expected + ". Oszacowanie uwzględnia ilość ostatniego zakupu i tempo poprzednich zakupów. Sprawdź zapasy.";
                }
                return new ProductView(p.Id, names?.Display(p.Name) ?? p.Name, p.Status, days.Length, last.ToString("yyyy-MM-dd"), lastQuantity, history.Select(l => lookup[l.ReceiptId].Account).Distinct().ToArray(), reason, expected is null ? 0.1 : 0.5, expected, history.Sum(l => l.Quantity), interval, dailyQuantity, mayRunOut);
            }).OrderByDescending(p => p.MayRunOut).ThenByDescending(p => p.PurchaseDays).ThenBy(p => p.Name).ToArray());
        }
        finally { gate.Release(); }
    }
}
