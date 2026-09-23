using System.Text.Json;
using Halill;

var checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
CalendarEvent Parse(string json) { using var doc = JsonDocument.Parse(json); return CalendarEvent.Parse(doc.RootElement)!; }
var allDay = Parse("""{"id":"a","start":{"date":"2026-09-22"},"end":{"date":"2026-09-24"}}""");
Check(allDay.OccursOn(new(2026,9,22)) && allDay.OccursOn(new(2026,9,23)), "Multi-day all-day events cover both days");
Check(!allDay.OccursOn(new(2026,9,24)), "All-day end date is exclusive");
Check(allDay.Title == "(제목 없음)", "Missing title has a fallback");
var midnight = new CalendarEvent("b", "Overnight", new(2026,9,22,23,0,0), new(2026,9,23,0,0,0), false, "");
Check(midnight.OccursOn(new(2026,9,22)) && !midnight.OccursOn(new(2026,9,23)), "Midnight end does not spill into next day");
var instant = midnight with { Start = new(2026,9,22,0,0,0), End = new(2026,9,22,0,0,0) };
Check(instant.OccursOn(new(2026,9,22)), "Zero-duration midnight event remains visible");
var timed = Parse("""{"id":"c","start":{"dateTime":"2026-09-22T23:30:00-07:00"},"end":{"dateTime":"2026-09-23T00:30:00-07:00"}}""");
Check(timed.Start == DateTimeOffset.Parse("2026-09-22T23:30:00-07:00").LocalDateTime, "Offset timestamps convert to local time");
using var cancelled = JsonDocument.Parse("""{"status":"cancelled","id":"d"}""");
Check(CalendarEvent.Parse(cancelled.RootElement) is null, "Cancelled entries are skipped before parsing dates");
Check(new DateTime(2026,8,1).AddDays(-(int)new DateTime(2026,8,1).DayOfWeek).AddDays(41) == new DateTime(2026,9,5), "Six-week month grid covers month boundary");
using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<GoogleOAuthClient><ClientId> 123-example.apps.googleusercontent.com </ClientId><ClientSecret> test-secret </ClientSecret></GoogleOAuthClient>")))
{
    var registration = GoogleOAuthConfiguration.Read(stream);
    Check(registration.ClientId == "123-example.apps.googleusercontent.com" && registration.ClientSecret == "test-secret", "Developer registration loads and trims values");
}
foreach (var xml in new[] { "<GoogleOAuthClient/>", "<GoogleOAuthClient><ClientId>invalid</ClientId><ClientSecret>test</ClientSecret></GoogleOAuthClient>", "<GoogleOAuthClient><ClientId>123.apps.googleusercontent.com</ClientId></GoogleOAuthClient>" })
{
    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
    bool rejected = false;
    try { GoogleOAuthConfiguration.Read(stream); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Incomplete developer registration is rejected before browser launch");
}
Console.WriteLine($"{checks} checks passed.");
