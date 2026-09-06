namespace VotschVc3.Core.Notifications;

public sealed record NotificationTemplateSample(
    NotificationType Type, string Title, string Trigger, string Subject, string Body, string Html);

/// <summary>Human-readable catalogue used by Administration for explanations, previews and tests.</summary>
public static class NotificationTemplateCatalog
{
    public static IReadOnlyList<NotificationTemplateSample> All { get; } =
        Enum.GetValues<NotificationType>().Select(CreateSample).ToArray();

    public static NotificationTemplateSample CreateSample(NotificationType type)
    {
        (string title, string trigger, string subject, string body) = type switch
        {
            NotificationType.DeviceAlarm => (
                "Alarm zariadenia", "Pri strate spojenia, bezpečnostnom zásahu alebo inom alarme komory.",
                "⚠ ALARM – Komora 2", "Komora: Komora 2\nČas: 06.09.2026 12:30:00\n\nUkážka alarmu: zariadenie vyžaduje kontrolu operátora."),
            NotificationType.ProfileCompleted => (
                "Dokončenie profilu", "Po riadnom dokončení teplotného profilu a pokuse o bezpečné vypnutie výkonu.",
                "Profil dokončený: Ukážkový profil (Komora 2)", "Profil: Ukážkový profil\nZariadenie: Komora 2\nDokončené: 06.09.2026 12:30:00\n\nVýkon komory bol bezpečne vypnutý. Ostrý e-mail obsahuje graf a CSV."),
            NotificationType.CalibrationWarning => (
                "Varovanie FBG kalibrácie", "Pri timeoute, nestabilnom peaku alebo udalosti, ktorá vyžaduje zásah operátora.",
                "Kalibrácia FBG – WARNING – TEST-001", "Run ID: TEST-001\nKomora: Komora 2\nPlato: 3\nPeak: 1.3/P1\n\nPeak nedokončil meranie v povolenom čase."),
            NotificationType.CalibrationCompleted => (
                "Výsledok FBG kalibrácie", "Po ukončení kalibrácie v stave COMPLETED, COMPLETED WITH WARNINGS alebo FAILED.",
                "Kalibrácia FBG – COMPLETED – TEST-001", "Výsledok: PASS\nRun ID: TEST-001\nKomora: Komora 2\nKalibračné modely: 24 PASS / 0 FAIL\n\nOstrý e-mail obsahuje tabuľku peakov, koeficienty, Excel, CSV a ZIP."),
            _ => (
                "Rozdiel WIKA–komora", "Keď absolútny rozdiel referencie WIKA a komory prekročí limit; opakovanie chráni cooldown.",
                "CHYBA – rozdiel teploty WIKA a komory – Komora 2", "Komora: Komora 2\nWIKA: COM4 / kanál B\nRozdiel: 10,500 °C\n\nPovolený rozdiel teplôt bol prekročený."),
        };
        return new(type, title, trigger, subject, body, LabControlEmailTemplate.Create(subject, body));
    }
}
