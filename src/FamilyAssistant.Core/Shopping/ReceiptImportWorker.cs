namespace FamilyAssistant.Core.Shopping;

public sealed class ReceiptImportWorker(IConfiguration config, ShoppingStore store, BrowserReceiptInbox browser, ILogger<ReceiptImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var root = config["Shopping:ImportDirectory"] ?? "/app/imports/biedronka";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await browser.Scan(stoppingToken);
                if (Directory.Exists(root))
                {
                    Directory.CreateDirectory(Path.Combine(root, "processed"));
                    Directory.CreateDirectory(Path.Combine(root, "rejected"));
                    var inboxes = new[] { root, Path.Combine(root, "account-1"), Path.Combine(root, "account-2") };
                    foreach (var path in inboxes.Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*.json")))
                    {
                        try
                        {
                            if (new FileInfo(path).LastWriteTimeUtc > DateTime.UtcNow.AddSeconds(-5)) continue;
                            if (new FileInfo(path).Length > ReceiptParser.MaxBytes) throw new ShoppingFailure("Plik przekracza 8 MB.");
                            var receipt = ReceiptParser.Parse(await File.ReadAllBytesAsync(path, stoppingToken));
                            var parent = Path.GetFileName(Path.GetDirectoryName(path));
                            var label = parent == "account-1" ? config["Shopping:Account1Label"] ?? "Konto 1" : parent == "account-2" ? config["Shopping:Account2Label"] ?? "Konto 2" : "";
                            await store.Import(receipt, label);
                            File.Move(path, Path.Combine(root, "processed", receipt.Id + "-" + Guid.NewGuid().ToString("N") + ".json"));
                        }
                        catch (ShoppingFailure ex)
                        {
                            var destination = Path.Combine(root, "rejected", Guid.NewGuid().ToString("N"));
                            File.Move(path, destination + ".json");
                            await File.WriteAllTextAsync(destination + ".txt", ex.Message, stoppingToken);
                            logger.LogWarning("Odrzucono paragon: {Reason}", ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogWarning("Import paragonów niedostępny; ponowna próba za 30 sekund."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
