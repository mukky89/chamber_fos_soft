using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VotschVc3.Core.Notifications;
using Xunit;
namespace VotschVc3.Core.Tests;

public sealed class ModernEmailTemplateTests
{
    [Theory]
    [InlineData("Test – Vötsch riadenie komôr", "Typ: Test e-mailu\nZdroj: Lab Control\n\nTestovacia správa.", "INFORMÁCIA")]
    [InlineData("ALARM – Komora 1", "Komora: Komora 1\n\nPrekročený limit teploty.", "CHYBA / ALARM")]
    [InlineData("CHYBA – rozdiel teploty WIKA a komory", "Komora: Komora 1\n\nOdchýlka 5 °C.", "CHYBA / ALARM")]
    [InlineData("Kalibrácia FBG – WARNING – P-001", "Run ID: 01\n\nNestabilný peak.", "UPOZORNENIE")]
    [InlineData("Kalibrácia FBG – COMPLETED WITH WARNINGS – P-001", "Upozornenia: 2", "UPOZORNENIE")]
    [InlineData("Kalibrácia FBG – COMPLETED – P-001", "Upozornenia: 0", "DOKONČENÉ")]
    public void AllNotificationFamiliesKeepTheirSeverity(string subject, string body, string badge)
    {
        string html = WebUtility.HtmlDecode(LabControlEmailTemplate.Create(subject, body));
        Assert.Contains(badge, html);
        Assert.Contains("SYLEX · LAB CONTROL", html);
        Assert.Contains(subject, html);
        Assert.Contains("<!--[if mso]>", html);
    }

    [Fact]
    public void EncodesUntrustedSubjectMetadataAndMessage()
    {
        string html = LabControlEmailTemplate.Create("<script>alert(1)</script>", "Komora: <img src=x onerror=alert(1)>\n\nSpráva <b> & \"test\"");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img", html);
        Assert.Contains("&lt;b&gt; &amp;", html);
    }

    [Fact]
    public void PreservesMultilineMessageAndDoesNotPromoteLaterProseToMetadata()
    {
        string html = WebUtility.HtmlDecode(LabControlEmailTemplate.Create("Info", "Komora: VC3\r\n\r\nPrvý riadok\rDôvod: výpadok\nPosledný riadok"));
        Assert.Contains("Prvý riadok<br>Dôvod: výpadok<br>Posledný riadok", html);
        Assert.Contains("VC3", html);
    }

    [Theory]
    [InlineData(true, "DOKONČENÉ")]
    [InlineData(false, "UPOZORNENIE")]
    public void CompletionUsesSharedShellAndDoesNotPromiseMissingCsv(bool poweredOff, string badge)
    {
        DateTime start = new(2026, 9, 4, 8, 0, 0);
        var result = ProfileCompletionEmail.Create(new("VC3", ["Alarm <test>"], start, start.AddHours(2), poweredOff, [], null));
        string decoded = WebUtility.HtmlDecode(result.Html);
        Assert.Contains(badge, decoded); // Explicit power state wins over the profile name.
        Assert.Contains("SYLEX · LAB CONTROL", decoded);
        Assert.Contains("CSV záznam pre tento beh nie je dostupný", result.Text);
        Assert.DoesNotContain("<test>", result.Html);
        Assert.Contains("cid:temperature-chart", result.Html);
        Assert.Single(result.Attachments);
    }

    [Fact]
    public void ChartCoordinatesAreValidUnderSlovakCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sk-SK");
            DateTime start = new(2026, 9, 4, 8, 0, 0);
            var result = ProfileCompletionEmail.Create(new("VC3", ["Test"], start, start.AddHours(2), true,
                [new(start, 23.5, 22.8, null, null), new(start.AddHours(2), 80, 79.7, null, null)], null));
            string svg = Encoding.UTF8.GetString(result.Attachments[0].Content);
            foreach (Match match in Regex.Matches(svg, "points=\"([^\"]+)\""))
                foreach (string pair in match.Groups[1].Value.Split(' '))
                    Assert.Matches(@"^\d+\.\d,\d+\.\d$", pair);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
