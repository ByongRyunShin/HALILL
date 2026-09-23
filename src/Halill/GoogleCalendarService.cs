using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Halill;

public sealed class GoogleCalendarService : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly LocalStore store;
    private OAuthClient? client;
    private TokenSet? tokens;
    public bool IsConnected => tokens is not null;
    public GoogleCalendarService(LocalStore store)
    {
        this.store = store;
        client = store.Read<OAuthClient>("client.bin", true);
        tokens = store.Read<TokenSet>("tokens.bin", true);
    }
    public static string Query(IEnumerable<KeyValuePair<string, string>> values) => string.Join("&", values.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task ConnectAsync(CancellationToken cancellation)
    {
        var nextClient = GoogleOAuthConfiguration.Load();
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/callback";
        var query = Query(new Dictionary<string, string> {
            ["client_id"] = nextClient.ClientId, ["redirect_uri"] = redirect, ["response_type"] = "code",
            ["scope"] = "https://www.googleapis.com/auth/calendar.events.readonly https://www.googleapis.com/auth/calendar.calendarlist.readonly",
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"] = "S256",
            ["state"] = state, ["access_type"] = "offline", ["prompt"] = "consent" });
        Process.Start(new ProcessStartInfo("https://accounts.google.com/o/oauth2/v2/auth?" + query) { UseShellExecute = true });
        string? code = null;
        while (code is null)
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            string line;
            try { line = await reader.ReadLineAsync(requestTimeout.Token) ?? ""; }
            catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { continue; }
            var pieces = line.Split(' ');
            Uri? uri = null;
            var validPath = pieces.Length >= 2 && pieces[0] == "GET" && Uri.TryCreate("http://127.0.0.1" + pieces[1], UriKind.Absolute, out uri) && uri.AbsolutePath == "/callback";
            var args = new Dictionary<string, string>();
            if (validPath)
                foreach (var pair in uri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('=', 2);
                    args[Uri.UnescapeDataString(parts[0])] = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
                }
            bool valid = validPath && args.GetValueOrDefault("state") == state;
            var body = Encoding.UTF8.GetBytes(valid ? "<html><meta charset='utf-8'><body>HALILL 앱으로 돌아가 주세요. 이 창은 닫아도 됩니다.</body></html>" : "Invalid callback");
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
            if (!valid) continue;
            if (args.ContainsKey("error")) throw new InvalidOperationException("Google 로그인이 취소되었거나 권한이 승인되지 않았습니다.");
            code = args.GetValueOrDefault("code");
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Google 인증 응답에 코드가 없습니다. 다시 로그인해 주세요.");
        }
        var nextTokens = await ExchangeAsync(new Dictionary<string, string> { ["client_id"] = nextClient.ClientId, ["client_secret"] = nextClient.ClientSecret,
            ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code" }, null, timeout.Token);
        store.Write("client.bin", nextClient, true);
        store.Write("tokens.bin", nextTokens, true);
        client = nextClient;
        tokens = nextTokens;
    }

    private async Task<TokenSet> ExchangeAsync(Dictionary<string, string> form, string? refreshToken, CancellationToken cancellation)
    {
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), cancellation);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Google 인증을 갱신하지 못했습니다. Google 연결을 다시 진행해 주세요.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var root = json.RootElement;
        return new() { AccessToken = root.GetProperty("access_token").GetString()!,
            RefreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString()! : refreshToken ?? "",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()) };
    }
    private async Task RefreshAsync(CancellationToken cancellation)
    {
        if (client is null || tokens is null || string.IsNullOrEmpty(tokens.RefreshToken)) throw new InvalidOperationException("Google 계정을 다시 연결해 주세요.");
        tokens = await ExchangeAsync(new() { ["client_id"] = client.ClientId, ["client_secret"] = client.ClientSecret,
            ["refresh_token"] = tokens.RefreshToken, ["grant_type"] = "refresh_token" }, tokens.RefreshToken, cancellation);
        store.Write("tokens.bin", tokens, true);
    }
    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellation)
    {
        if (tokens is null) throw new InvalidOperationException("먼저 Google 계정을 연결해 주세요.");
        if (tokens.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(1)) await RefreshAsync(cancellation);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            using var response = await http.SendAsync(request, cancellation);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { await RefreshAsync(cancellation); continue; }
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode switch {
                HttpStatusCode.Forbidden => "조회 권한이 없습니다. Calendar API 활성화와 로그인 권한 승인을 확인해 주세요.",
                HttpStatusCode.NotFound => "캘린더를 찾을 수 없습니다. 다른 캘린더를 선택해 주세요.",
                (HttpStatusCode)429 => "Google 요청 한도에 도달했습니다. 잠시 후 새로고침해 주세요.",
                _ => $"Google 서버 요청에 실패했습니다 (HTTP {(int)response.StatusCode}). 다시 연결하거나 새로고침해 주세요." });
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        }
        throw new InvalidOperationException("Google 인증이 만료되었습니다. 다시 연결해 주세요.");
    }
    public async Task<List<CalendarChoice>> CalendarsAsync(CancellationToken cancellation)
    {
        var result = new List<CalendarChoice>();
        string? page = null;
        do
        {
            using var json = await GetAsync("https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=250" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)), cancellation);
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
                result.Add(new(item.GetProperty("id").GetString()!, item.GetProperty("summary").GetString()!));
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (page is not null);
        return result;
    }
    public async Task<List<CalendarEvent>> EventsAsync(string calendarId, DateTime from, DateTime to, CancellationToken cancellation)
    {
        var result = new List<CalendarEvent>();
        var query = new Dictionary<string, string> { ["timeMin"] = new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Local)).ToString("o"),
            ["timeMax"] = new DateTimeOffset(DateTime.SpecifyKind(to, DateTimeKind.Local)).ToString("o"),
            ["singleEvents"] = "true", ["orderBy"] = "startTime", ["maxResults"] = "2500" };
        string? page;
        do
        {
            using var json = await GetAsync("https://www.googleapis.com/calendar/v3/calendars/" + Uri.EscapeDataString(calendarId) + "/events?" + Query(query), cancellation);
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
                if (CalendarEvent.Parse(item) is { } entry) result.Add(entry);
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (page is not null) query["pageToken"] = page;
        } while (page is not null);
        return result;
    }
    public void Disconnect()
    {
        store.Delete("tokens.bin"); store.Delete("client.bin"); tokens = null; client = null;
    }
    public void Dispose() => http.Dispose();
}
