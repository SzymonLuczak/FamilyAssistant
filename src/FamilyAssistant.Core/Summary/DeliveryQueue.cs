using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyAssistant.Core.Summary;

public sealed class Delivery
{
    public string Id { get; set; } = "";
    public string Slot { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Text { get; set; } = "";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public long NextAttempt { get; set; }
    public long ExpiresAt { get; set; }
    public long? SentAt { get; set; }
}
public sealed class DeliveryDatabase(DbContextOptions<DeliveryDatabase> options) : DbContext(options)
{
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Delivery>().HasKey(d => d.Id);
        model.Entity<Delivery>().HasIndex(d => d.Slot).IsUnique();
    }
}

public sealed class DeliveryQueue(IConfiguration config)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private async Task<DeliveryDatabase> Open(CancellationToken token)
    {
        var path = Path.GetFullPath(config["Summary:OutboxPath"] ?? "/app/data/outbox.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var db = new DeliveryDatabase(new DbContextOptionsBuilder<DeliveryDatabase>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path }.ToString()).Options);
        try
        {
            if (!initialized)
            {
                await db.Database.EnsureCreatedAsync(token);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                initialized = true;
            }
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }
    public async Task Enqueue(SummaryDraft draft, string group, DateTimeOffset expires, CancellationToken token = default)
    {
        if (draft.Kind is not ("morning" or "tomorrow") || string.IsNullOrWhiteSpace(group))
            throw new InvalidOperationException("invalid_delivery");
        await gate.WaitAsync(token);
        try
        {
            await using var db = await Open(token);
            if (await db.Deliveries.AnyAsync(d => d.Slot == draft.Id, token)) return;
            db.Deliveries.Add(new() { Id = "summary_" + SummaryStore.Hash(draft.Id), Slot = draft.Id,
                GroupId = group, Text = draft.Text, ExpiresAt = expires.ToUnixTimeSeconds() });
            await db.SaveChangesAsync(token);
        }
        finally { gate.Release(); }
    }
    public async Task<Delivery?> Claim(DateTimeOffset now, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await using var db = await Open(token);
            var unix = now.ToUnixTimeSeconds();
            await db.Deliveries.Where(d => (d.Status == "pending" || d.Status == "sending") && d.ExpiresAt <= unix)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, d => d.Attempts > 0 ? "unknown" : "expired"), token);
            var row = await db.Deliveries.Where(d => (d.Status == "pending" || d.Status == "sending")
                && d.NextAttempt <= unix && d.Attempts < 5).OrderBy(d => d.ExpiresAt).FirstOrDefaultAsync(token);
            if (row is null) return null;
            // Lease is durable before contacting WhatsApp. Recovery always uses the same message ID.
            var changed = await db.Deliveries.Where(d => d.Id == row.Id && d.NextAttempt <= unix
                && (d.Status == "pending" || d.Status == "sending"))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, "sending")
                    .SetProperty(d => d.Attempts, d => d.Attempts + 1).SetProperty(d => d.NextAttempt, unix + 90), token);
            if (changed == 0) return null;
            row.Attempts++;
            return row;
        }
        finally { gate.Release(); }
    }
    public async Task Finish(string id, string result, DateTimeOffset now, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await using var db = await Open(token);
            var row = await db.Deliveries.SingleAsync(d => d.Id == id, token);
            row.Status = result == "retry" ? (row.Attempts >= 5 ? "unknown" : "pending") : result;
            row.NextAttempt = now.AddSeconds(Math.Min(300, 30 * Math.Pow(2, row.Attempts - 1))).ToUnixTimeSeconds();
            if (result == "sent") row.SentAt = now.ToUnixTimeSeconds();
            await db.SaveChangesAsync(token);
        }
        finally { gate.Release(); }
    }
    public async Task<object[]> History(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await using var db = await Open(token);
            return await db.Deliveries.AsNoTracking().OrderByDescending(d => d.ExpiresAt).Take(20)
                .Select(d => new { d.Slot, d.Status, d.Attempts, d.SentAt }).ToArrayAsync(token);
        }
        finally { gate.Release(); }
    }
}
