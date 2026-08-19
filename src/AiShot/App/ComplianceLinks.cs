namespace AiShot.App;

/// <summary>Destinos publicos exigidos para transparencia e suporte.</summary>
internal static class ComplianceLinks
{
    public static readonly Uri PrivacyPolicy =
        new("https://aishot.tecnologia.dev.br/aishot/privacidade/");

    public static Uri BuildReportUri(string subject)
    {
        var encodedSubject = Uri.EscapeDataString(subject);
        return new Uri($"mailto:suporte@tecnologia.dev.br?subject={encodedSubject}");
    }
}
