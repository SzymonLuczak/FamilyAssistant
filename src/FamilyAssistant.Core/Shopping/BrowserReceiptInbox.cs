using System.Security.Cryptography;

namespace FamilyAssistant.Core.Shopping;

// The browser owns the download directory. Never move or delete its files.
public sealed class BrowserReceiptInbox(IConfiguration config, ShoppingStore store)
{
    private string Archive => Path.Combine(config["Shopping:ImportDirectory"] ?? "/app/imports/biedronka", "browser-imports");
    public object Status() => new { mode = "browser_extension", accepted = Count("*.ok"), rejected = Count("*.error"), note = "Liczby dotyczą importu plików. Nie potwierdzają zalogowania kont ani pełnej historii." };
    private int Count(string pattern) => Directory.Exists(Archive) ? Directory.EnumerateFiles(Archive, pattern, SearchOption.AllDirectories).Count() : 0;
    public async Task Scan(CancellationToken token)
    {
        var source = config["Shopping:BrowserDirectory"] ?? "/app/browser-receipts";
        for (var account = 1; account <= 2; account++)
        {
            var directory = Path.Combine(source, $"account-{account}");
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                var info = new FileInfo(path);
                if (info.LastWriteTimeUtc > DateTime.UtcNow.AddSeconds(-5) || info.Length > ReceiptParser.MaxBytes) continue;
                byte[] bytes;
                // The browser may still hold the file; skip it and retry on the next scan.
                try { bytes = await File.ReadAllBytesAsync(path, token); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                var target = Path.Combine(Archive, $"account-{account}", hash);
                if (File.Exists(target + ".ok") || File.Exists(target + ".error")) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                try
                {
                    var parsed = ReceiptParser.Parse(bytes);
                    await store.Import(parsed, config[$"Shopping:Account{account}Label"] ?? $"Konto {account}");
                    await File.WriteAllBytesAsync(target + ".json", bytes, token);
                    await File.WriteAllTextAsync(target + ".ok", DateTimeOffset.UtcNow.ToString("O"), token);
                }
                catch (ShoppingFailure ex)
                {
                    await File.WriteAllTextAsync(target + ".error", ex.Message, token);
                }
            }
        }
    }
}
