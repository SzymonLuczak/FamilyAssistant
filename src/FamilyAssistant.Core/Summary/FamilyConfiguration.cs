using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FamilyAssistant.Core.Summary;

public sealed class FamilySettings
{
    public string Timezone { get; set; } = "Europe/Warsaw";
    public string Language { get; set; } = "pl";
    public FamilyPerson[] Family { get; set; } = [];
    public FamilyRules Rules { get; set; } = new();
    public NotificationTimes Notifications { get; set; } = new();
    public CalendarSettings GoogleCalendar { get; set; } = new();
}
public sealed class CalendarSettings { public string[] CalendarIds { get; set; } = []; }
public sealed class FamilyPerson
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "child";
    public string? VulcanStudentId { get; set; }
    public string[] CalendarAliases { get; set; } = [];
    public bool Enabled { get; set; } = true;
}
public sealed class FamilyRules
{
    public int MinimumTravelMinutes { get; set; } = 30;
    public PickupRule[] Pickups { get; set; } = [];
}
public sealed class PickupRule
{
    public string Day { get; set; } = "";
    public string Child { get; set; } = "";
    public string PickedUpBy { get; set; } = "";
}
public sealed class NotificationTimes
{
    public string MorningSummary { get; set; } = "07:00";
    public string TomorrowSummary { get; set; } = "20:00";
    public int ChangeCheckIntervalMinutes { get; set; } = 30;
}
public sealed class SummaryFailure(string code) : Exception(code);

public sealed class FamilyConfiguration(IConfiguration config)
{
    public FamilySettings Read()
    {
        var path = config["Summary:FamilyPath"] ?? "/app/config/family.yaml";
        if (!File.Exists(path)) throw new SummaryFailure("family_not_configured");
        try
        {
            if (new FileInfo(path).Length > 100_000) throw new Exception();
            var value = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithDuplicateKeyChecking().Build().Deserialize<FamilySettings>(File.ReadAllText(path));
            Validate(value);
            return value;
        }
        catch { throw new SummaryFailure("invalid_family_configuration"); }
    }

    public static void Validate(FamilySettings value)
    {
        if (value.Timezone != "Europe/Warsaw" || value.Language != "pl" || value.Family.Length is 0 or > 20)
            throw new SummaryFailure("invalid_family_configuration");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var students = new HashSet<string>();
        foreach (var person in value.Family)
        {
            if (string.IsNullOrWhiteSpace(person.Name) || person.Name.Length > 100 || person.Name.Any(char.IsControl)
                || !names.Add(person.Name) || person.Type is not ("child" or "adult") || person.CalendarAliases is null)
                throw new SummaryFailure("invalid_family_configuration");
            if (person.Enabled && person.Type == "child" &&
                (person.VulcanStudentId is null || !Regex.IsMatch(person.VulcanStudentId, "^[a-f0-9]{24}$") || !students.Add(person.VulcanStudentId)))
                throw new SummaryFailure("invalid_student_mapping");
        }
        var enabled = value.Family.Where(p => p.Enabled).ToArray();
        foreach (var rule in value.Rules.Pickups)
        {
            if (!Enum.TryParse<DayOfWeek>(rule.Day, out var day) || !Enum.IsDefined(day)
                || !enabled.Any(p => p.Name == rule.Child && p.Type == "child")
                || !enabled.Any(p => p.Name == rule.PickedUpBy) || rule.Child == rule.PickedUpBy)
                throw new SummaryFailure("invalid_pickup_rule");
        }
        if (value.Rules.Pickups.GroupBy(r => (r.Day, r.Child)).Any(g => g.Count() > 1)
            || value.Rules.MinimumTravelMinutes is < 0 or > 240)
            throw new SummaryFailure("invalid_pickup_rule");
        foreach (var time in new[] { value.Notifications.MorningSummary, value.Notifications.TomorrowSummary })
            if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new SummaryFailure("invalid_notification_time");
    }
}
