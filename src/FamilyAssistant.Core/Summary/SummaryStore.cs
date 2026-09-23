using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyAssistant.Core.Summary;

public sealed class SummaryDraft
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Date { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string Text { get; set; } = "";
    public string Status { get; set; } = "draft";
    public bool Incomplete { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}

public sealed class SummaryDatabase(DbContextOptions<SummaryDatabase> options) : DbContext(options)
{
    public DbSet<SummaryDraft> Drafts => Set<SummaryDraft>();
    protected override void OnModelCreating(ModelBuilder model) => model.Entity<SummaryDraft>().HasKey(d => d.Id);
}

public sealed class SummaryStore(IConfiguration config)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private async Task<SummaryDatabase> Open(CancellationToken token)
    {
        var path = Path.GetFullPath(config["Summary:DatabasePath"] ?? "/app/data/summary.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var db = new SummaryDatabase(new DbContextOptionsBuilder<SummaryDatabase>()
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

    public async Task<bool> Exists(string kind, DateOnly day, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await using var db = await Open(token);
            return await db.Drafts.AnyAsync(d => d.Id == Key(kind, day), token);
        }
        finally { gate.Release(); }
    }
    public static string Key(string kind, DateOnly day) => $"{kind}:{day:yyyy-MM-dd}";
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public async Task<SummaryDraft> Save(string kind, SummaryPreview preview, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await using var db = await Open(token);
            var key = Key(kind, preview.Date);
            var row = await db.Drafts.SingleOrDefaultAsync(d => d.Id == key, token);
            var hash = Hash(preview.Text);
            var now = preview.GeneratedAt.ToUnixTimeSeconds();
            if (row is null)
            {
                row = new() { Id = key, Kind = kind, Date = preview.Date.ToString("yyyy-MM-dd"), CreatedAt = now };
                db.Drafts.Add(row);
            }
            if (row.PayloadHash != hash || row.Incomplete != preview.Incomplete)
            {
                row.PayloadHash = hash;
                row.Text = preview.Text;
                row.Incomplete = preview.Incomplete;
                row.UpdatedAt = now;
                await db.SaveChangesAsync(token);
            }
            await db.Drafts.Where(d => d.CreatedAt < now - 30 * 86400).ExecuteDeleteAsync(token);
            return row;
        }
        finally { gate.Release(); }
    }

    public async Task<SummaryDraft[]> Latest(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await using var db = await Open(token);
            return await db.Drafts.AsNoTracking().OrderByDescending(d => d.UpdatedAt).Take(10).ToArrayAsync(token);
        }
        finally { gate.Release(); }
    }
}
