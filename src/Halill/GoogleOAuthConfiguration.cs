using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace Halill;

/// <summary>App registration is supplied by the developer, never selected by end users.</summary>
public static class GoogleOAuthConfiguration
{
    public static OAuthClient Load()
    {
        var id = Environment.GetEnvironmentVariable("HALILL_GOOGLE_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("HALILL_GOOGLE_CLIENT_SECRET");
        if (!string.IsNullOrWhiteSpace(id)) return Validate(id, secret);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Halill.GoogleOAuthClient.xml");
        if (resource is null)
            throw new InvalidOperationException("이 앱에는 Google 로그인 설정이 아직 등록되지 않았습니다. 개발자가 Google OAuth 클라이언트를 설정한 버전이 필요합니다.");
        return Read(resource);
    }

    public static OAuthClient Read(Stream stream)
    {
        var root = XDocument.Load(stream).Root;
        return Validate(root?.Element("ClientId")?.Value, root?.Element("ClientSecret")?.Value);
    }

    private static OAuthClient Validate(string? id, string? secret)
    {
        id = id?.Trim();
        secret = secret?.Trim();
        if (string.IsNullOrEmpty(id) || !id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)
            || id.Any(char.IsWhiteSpace) || string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("앱의 Google 로그인 설정이 올바르지 않습니다. 개발자가 데스크톱 OAuth 클라이언트 ID와 시크릿을 확인해야 합니다.");
        return new OAuthClient { ClientId = id, ClientSecret = secret };
    }
}
