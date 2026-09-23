using System.Text.Json;

namespace FamilyAssistant.Core.Google;

public sealed record GoogleTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
public sealed record CalendarMember(string Id, string Name, string[] CalendarAliases);
public sealed record CalendarChoice(string Id, string Name, string? TimeZone);
public sealed record FamilyCalendarEvent(string ExternalId, string CalendarId, string Title,
    DateTimeOffset? Start, DateTimeOffset? End, bool IsAllDay,
    DateOnly? StartDate, DateOnly? EndDateExclusive, string[] AssignedMembers, string? Location);
public sealed record CalendarFailure(string CalendarId, string Error);
public sealed record DayEvents(DateOnly Date, string TimeZone, FamilyCalendarEvent[] Events, CalendarFailure[] Errors);
public sealed class GoogleFailure(string code, int status = 503) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class GoogleState(IConfiguration config)
{
    private readonly string directory = config["Google:StatePath"] ?? "/app/secrets/google";
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> Read<T>(string name)
    {
        await gate.WaitAsync();
        try
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Json);
        }
        finally { gate.Release(); }
    }

    public async Task Write<T>(string name, T value)
    {
        await gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Json));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
        }
        finally { gate.Release(); }
    }
}
