using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyAssistant.Core.Shopping;

public sealed record ReceiptLine(string RawName, string Name, string Key, decimal Quantity, long UnitPrice, long Total);
public sealed record ParsedReceipt(string Id, string Hash, DateTimeOffset PurchasedAt, long Total, ReceiptLine[] Lines);
public sealed class ShoppingFailure(string message) : Exception(message);

public static class ReceiptParser
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static ParsedReceipt Parse(byte[] bytes)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new ShoppingFailure("Plik jest za duży (maksymalnie 8 MB).");
            using var envelope = JsonDocument.Parse(bytes);
            var parts = envelope.RootElement.GetProperty("data").GetString()!.Split('.');
            if (parts.Length != 3) throw new FormatException();
            var encoded = parts[1].Replace('-', '+').Replace('_', '/');
            using var payload = JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
            var doc = payload.RootElement.GetProperty("dokument");
            if (doc.GetProperty("naglowek").GetProperty("wersja").GetString() != "JPK_KASA_PARAGON_v2-0" ||
                doc.GetProperty("podmiot1").GetProperty("NIP").GetString() != "7791011327") throw new FormatException();
            var receipt = doc.GetProperty("paragon");
            if (receipt.GetProperty("podsum").GetProperty("waluta").GetString() != "PLN") throw new FormatException();
            var date = receipt.GetProperty("zakSprzed").GetDateTimeOffset();
            var total = receipt.GetProperty("podsum").GetProperty("sumaBrutto").GetInt64();
            var lines = new List<ReceiptLine>();
            long receiptDiscount = 0;
            foreach (var position in receipt.GetProperty("pozycja").EnumerateArray())
            {
                if (position.TryGetProperty("rabat", out var discount))
                {
                    if (discount.GetProperty("oper").GetBoolean() || discount.GetProperty("wart").GetInt64() > 0) throw new FormatException();
                    receiptDiscount = checked(receiptDiscount + discount.GetProperty("wart").GetInt64());
                    continue;
                }
                var item = position.GetProperty("towar");
                if (item.GetProperty("oper").GetBoolean()) throw new ShoppingFailure("Paragon zawiera anulowanie lub zwrot — ten format wymaga sprawdzenia.");
                var rawName = item.GetProperty("nazwa").GetString()!;
                var name = Regex.Replace(rawName.Trim(), @"\s+", " ");
                var vat = item.GetProperty("idStPTU").GetString();
                if (name.EndsWith(" " + vat, StringComparison.Ordinal)) name = name[..^(vat!.Length + 1)];
                var quantity = decimal.Parse(item.GetProperty("ilosc").GetString()!.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                var price = item.GetProperty("cena").GetInt64();
                var amount = item.GetProperty("brutto").GetInt64(); // Already includes discounts.
                if (name.Length is < 1 or > 200 || quantity <= 0 || quantity > 100000 || price < 0 || amount < 0) throw new FormatException();
                lines.Add(new(rawName, name, Hash(name.ToUpperInvariant()), quantity, price, amount));
            }
            if (lines.Count is < 1 or > 2000 || total <= 0 || checked(lines.Sum(l => l.Total) + receiptDiscount) != total) throw new ShoppingFailure("Suma pozycji nie zgadza się z kwotą paragonu. Niczego nie zaimportowano.");
            var identity = doc.GetProperty("podmiot1").GetProperty("nrUnik").GetString() + ":" + receipt.GetProperty("pamiecChr") + ":" + receipt.GetProperty("JPKID");
            var normalized = JsonSerializer.Serialize(new { date, total, lines = lines.Select(l => new { l.Key, l.Quantity, l.UnitPrice, l.Total }) });
            return new(Hash(identity), Hash(normalized), date, total, lines.ToArray());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or NullReferenceException or OverflowException)
        { throw new ShoppingFailure("Nieobsługiwany lub uszkodzony paragon. Wybierz oryginalny plik JSON z Biedronki."); }
    }
}
