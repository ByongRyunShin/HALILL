using System.Globalization;
using System.Text.Json;

namespace Halill;

public sealed record CalendarEvent(string Id, string Title, DateTime Start, DateTime End, bool AllDay, string Location)
{
    public bool OccursOn(DateTime day) => Start < day.Date.AddDays(1) && (End > day.Date || (End == Start && Start.Date == day.Date));
    public string TimeLabel => AllDay ? "하루 종일" : Start.ToString("HH:mm") + " – " + End.ToString("HH:mm");

    public static CalendarEvent? Parse(JsonElement item)
    {
        if (item.TryGetProperty("status", out var status) && status.GetString() == "cancelled") return null;
        var start = item.GetProperty("start");
        var end = item.GetProperty("end");
        bool allDay = start.TryGetProperty("date", out _);
        static DateTime ReadTime(JsonElement part, bool allDay) => allDay
            ? DateTime.ParseExact(part.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateTimeOffset.Parse(part.GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture).LocalDateTime;
        return new(item.GetProperty("id").GetString()!,
            item.TryGetProperty("summary", out var title) ? title.GetString() ?? "(제목 없음)" : "(제목 없음)",
            ReadTime(start, allDay), ReadTime(end, allDay), allDay,
            item.TryGetProperty("location", out var location) ? location.GetString() ?? "" : "");
    }
}

public sealed record CalendarChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class AppSettings
{
    public string CalendarId { get; set; } = "primary";
    public bool AlwaysOnTop { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
}

public sealed class OAuthClient
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

public sealed class TokenSet
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

public static class DemoData
{
    public static List<CalendarEvent> ForMonth(DateTime month)
    {
        var day = month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month ? DateTime.Today : month.AddDays(9);
        return [new("demo1", "이번 주 계획 정리", day.AddHours(9), day.AddHours(9.5), false, "나만의 집중 시간"),
            new("demo2", "팀 프로젝트 미팅", day.AddHours(14), day.AddHours(15), false, "회의실 A"),
            new("demo5", "디자인 시안 검토 및 피드백", day.AddHours(16), day.AddHours(17), false, ""),
            new("demo6", "내일 할 일 정리", day.AddHours(20), day.AddHours(20.5), false, ""),
            new("demo3", "산책하며 쉬어 가기", day.AddDays(2).AddHours(18), day.AddDays(2).AddHours(19), false, ""),
            new("demo4", "새로운 아이디어 기록", day.AddDays(4), day.AddDays(5), true, "")];
    }
}
