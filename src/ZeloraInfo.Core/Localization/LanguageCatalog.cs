// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Globalization;

namespace ZeloraInfo.Core.Localization;

/// <summary>
/// Central, compile-time checked language resources. Each key carries all three translations side by side,
/// which makes a missing translation impossible to overlook (and is verified by unit tests).
/// The WPF layer projects the active language into a ResourceDictionary so that
/// <c>{DynamicResource Key}</c> bindings update instantly when the language changes.
/// </summary>
public static partial class LanguageCatalog
{
    public static readonly string[] SupportedLanguages = ["tr", "en", "de", "fr", "it", "es"];

    public static readonly IReadOnlyDictionary<string, string> LanguageNames = new Dictionary<string, string>
    {
        ["tr"] = "Türkçe",
        ["en"] = "English",
        ["de"] = "Deutsch",
        ["fr"] = "Français",
        ["it"] = "Italiano",
        ["es"] = "Español",
    };

    public static string DefaultLanguage
    {
        get
        {
            var two = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
            return SupportedLanguages.Contains(two) ? two : "en";
        }
    }

    /// <summary>Active language code. Set by the UI localization manager.</summary>
    public static string CurrentLanguage { get; set; } = "en";

    public static CultureInfo CurrentCulture => CultureInfo.GetCultureInfo(CurrentLanguage switch
    {
        "tr" => "tr-TR",
        "de" => "de-DE",
        "fr" => "fr-FR",
        "it" => "it-IT",
        "es" => "es-ES",
        _ => "en-US"
    });

    public static IReadOnlyDictionary<string, Entry> Entries => Table;

    /// <summary>French / Italian / Spanish translations (LanguageCatalog.Romance.cs), keyed like <see cref="Entries"/>.</summary>
    public static IReadOnlyDictionary<string, RomanceEntry> RomanceEntries => Romance;

    public static string Get(string key) => Get(key, CurrentLanguage);

    public static string Get(string key, string language)
    {
        if (!Table.TryGetValue(key, out var e)) return key;
        switch (language)
        {
            case "tr": return e.Tr;
            case "de": return e.De;
            case "fr" or "it" or "es" when Romance.TryGetValue(key, out var r):
                var value = language switch { "fr" => r.Fr, "it" => r.It, _ => r.Es };
                return string.IsNullOrEmpty(value) ? e.En : value;
            default: return e.En;
        }
    }

    public static string Format(string key, params object?[] args)
        => string.Format(CurrentCulture, Get(key), args);

    public readonly record struct Entry(string Tr, string En, string De);

    public readonly record struct RomanceEntry(string Fr, string It, string Es);

    private static Entry E(string tr, string en, string de) => new(tr, en, de);

    private static RomanceEntry R(string fr, string it, string es) => new(fr, it, es);

    private static readonly Dictionary<string, Entry> Table = new(StringComparer.Ordinal)
    {
        // ---------- Application ----------
        ["App.Title"] = E("Zelora Info", "Zelora Info", "Zelora Info"),
        ["App.Tagline"] = E("Disk sağlığı, performans ve sistem bilgisi", "Disk health, performance & system information", "Datenträgerzustand, Leistung & Systeminformationen"),

        // ---------- Navigation ----------
        ["Nav.Group.Storage"] = E("DEPOLAMA", "STORAGE", "SPEICHER"),
        ["Nav.Group.Hardware"] = E("DONANIM", "HARDWARE", "HARDWARE"),
        ["Nav.Group.App"] = E("UYGULAMA", "APPLICATION", "ANWENDUNG"),
        ["Nav.DiskHealth"] = E("Disk Sağlık", "Disk Health", "Datenträgerzustand"),
        ["Nav.Benchmark"] = E("Disk Benchmark", "Disk Benchmark", "Datenträger-Benchmark"),
        ["Nav.System"] = E("Sistem", "System", "System"),
        ["Nav.Display"] = E("Ekran", "Display", "Bildschirm"),
        ["Nav.Memory"] = E("RAM", "Memory", "Arbeitsspeicher"),
        ["Nav.Settings"] = E("Ayarlar", "Settings", "Einstellungen"),
        ["Nav.About"] = E("Hakkında", "About", "Info"),
        ["Nav.License"] = E("Apache 2.0 Lisansı", "Apache 2.0 License", "Apache-2.0-Lizenz"),
        ["Nav.ThirdParty"] = E("3. Parti Lisanslar", "Third-Party Licenses", "Drittanbieter-Lizenzen"),

        ["Page.DiskHealth.Subtitle"] = E("Fiziksel disklerin S.M.A.R.T. sağlık, sıcaklık ve kullanım verileri", "S.M.A.R.T. health, temperature and usage data of physical disks", "S.M.A.R.T.-Zustand, Temperatur und Nutzungsdaten der physischen Laufwerke"),
        ["Page.Benchmark.Subtitle"] = E("Sıralı ve rastgele okuma/yazma performansını ölçün", "Measure sequential and random read/write performance", "Sequentielle und zufällige Lese-/Schreibleistung messen"),
        ["Page.System.Subtitle"] = E("İşletim sistemi, işlemci, anakart ve ekran kartı", "Operating system, processor, mainboard and graphics", "Betriebssystem, Prozessor, Mainboard und Grafik"),
        ["Page.Display.Subtitle"] = E("Bağlı monitörler, çözünürlük, yenileme hızı ve ölçekleme", "Connected monitors, resolution, refresh rate and scaling", "Angeschlossene Monitore, Auflösung, Bildwiederholrate und Skalierung"),
        ["Page.Memory.Subtitle"] = E("Fiziksel bellek kullanımı, modüller ve sanal bellek", "Physical memory usage, modules and virtual memory", "Physische Speichernutzung, Module und virtueller Speicher"),
        ["Page.Settings.Subtitle"] = E("Tercihler otomatik olarak kaydedilir", "Preferences are saved automatically", "Einstellungen werden automatisch gespeichert"),
        ["Page.About.Subtitle"] = E("Sürüm, geliştirici ve lisans bilgileri", "Version, developer and license information", "Version, Entwickler und Lizenzinformationen"),
        ["Page.License.Subtitle"] = E("Apache License, Version 2.0 — tam metin", "Apache License, Version 2.0 — full text", "Apache License, Version 2.0 — vollständiger Text"),
        ["Page.ThirdParty.Subtitle"] = E("Kullanılan açık kaynak bileşenler ve lisansları", "Open-source components in use and their licenses", "Verwendete Open-Source-Komponenten und ihre Lizenzen"),

        // ---------- Common ----------
        ["Common.Refresh"] = E("Yenile", "Refresh", "Aktualisieren"),
        ["Common.Copy"] = E("Kopyala", "Copy", "Kopieren"),
        ["Common.Copied"] = E("Panoya kopyalandı", "Copied to clipboard", "In die Zwischenablage kopiert"),
        ["Common.Cancel"] = E("İptal", "Cancel", "Abbrechen"),
        ["Common.Close"] = E("Kapat", "Close", "Schließen"),
        ["Common.Yes"] = E("Evet", "Yes", "Ja"),
        ["Common.No"] = E("Hayır", "No", "Nein"),
        ["Common.Unknown"] = E("Bilinmiyor", "Unknown", "Unbekannt"),
        ["Common.Loading"] = E("Yükleniyor…", "Loading…", "Wird geladen…"),
        ["Common.Error"] = E("Hata", "Error", "Fehler"),
        ["Common.Warning"] = E("Uyarı", "Warning", "Warnung"),
        ["Common.Browse"] = E("Gözat…", "Browse…", "Durchsuchen…"),
        ["Common.Search"] = E("Ara…", "Search…", "Suchen…"),
        ["Common.Enabled"] = E("Etkin", "Enabled", "Aktiviert"),
        ["Common.Disabled"] = E("Devre dışı", "Disabled", "Deaktiviert"),
        ["Common.Supported"] = E("Destekleniyor", "Supported", "Unterstützt"),
        ["Common.NotSupported"] = E("Desteklenmiyor", "Not supported", "Nicht unterstützt"),
        ["Common.OpenLink"] = E("Bağlantıyı aç", "Open link", "Link öffnen"),
        ["Common.ExportFailed"] = E("Dışa aktarma başarısız: {0}", "Export failed: {0}", "Export fehlgeschlagen: {0}"),
        ["Common.Saved"] = E("Kaydedildi: {0}", "Saved: {0}", "Gespeichert: {0}"),
        ["Common.LoadFailed"] = E("Veriler okunamadı: {0}", "Could not read data: {0}", "Daten konnten nicht gelesen werden: {0}"),
        ["Common.LastUpdated"] = E("Son güncelleme: {0}", "Last updated: {0}", "Zuletzt aktualisiert: {0}"),
        ["Common.DaysShort"] = E("g", "d", "T"),
        ["Common.Hours"] = E("{0:N0} saat", "{0:N0} hours", "{0:N0} Stunden"),
        ["Common.Times"] = E("{0:N0} kez", "{0:N0} times", "{0:N0}-mal"),

        // ---------- Privileges / UAC ----------
        ["Priv.Admin"] = E("Yönetici modu", "Administrator mode", "Administratormodus"),
        ["Priv.Restricted"] = E("Kısıtlı mod", "Restricted mode", "Eingeschränkter Modus"),
        ["Priv.RestartAsAdmin"] = E("Yönetici olarak yeniden başlat", "Restart as administrator", "Als Administrator neu starten"),
        ["Priv.RestrictedBanner"] = E(
            "Kısıtlı moddasınız. S.M.A.R.T. öznitelikleri, sıcaklık ve kullanım sayaçları yalnızca yönetici izniyle okunabilir. Disk listesi ve Windows sağlık durumu gösterilmeye devam eder.",
            "You are in restricted mode. S.M.A.R.T. attributes, temperature and usage counters can only be read with administrator rights. The disk list and Windows health status are still shown.",
            "Sie befinden sich im eingeschränkten Modus. S.M.A.R.T.-Attribute, Temperatur und Nutzungszähler können nur mit Administratorrechten gelesen werden. Laufwerksliste und Windows-Zustand werden weiterhin angezeigt."),
        ["Priv.UacDeclined"] = E("Yönetici izni verilmedi. Uygulama kısıtlı modda çalışmaya devam ediyor.", "Administrator permission was not granted. The app continues in restricted mode.", "Administratorrechte wurden nicht erteilt. Die App läuft im eingeschränkten Modus weiter."),

        // ---------- Health ----------
        ["Health.Good"] = E("İyi", "Good", "Gut"),
        ["Health.Caution"] = E("Dikkat", "Caution", "Vorsicht"),
        ["Health.Bad"] = E("Kritik", "Critical", "Kritisch"),
        ["Health.Unknown"] = E("Bilinmiyor", "Unknown", "Unbekannt"),
        ["Health.Reason.ThresholdExceeded"] = E("Bir veya daha fazla S.M.A.R.T. özniteliği üretici eşiğinin altına düştü.", "One or more S.M.A.R.T. attributes fell below the manufacturer threshold.", "Ein oder mehrere S.M.A.R.T.-Attribute liegen unter dem Herstellergrenzwert."),
        ["Health.Reason.Reallocated"] = E("Yeniden atanmış sektörler mevcut (05).", "Reallocated sectors are present (05).", "Neu zugewiesene Sektoren vorhanden (05)."),
        ["Health.Reason.Pending"] = E("Bekleyen (kararsız) sektörler mevcut (C5).", "Pending (unstable) sectors are present (C5).", "Schwebende (instabile) Sektoren vorhanden (C5)."),
        ["Health.Reason.Uncorrectable"] = E("Düzeltilemeyen sektörler mevcut (C6).", "Uncorrectable sectors are present (C6).", "Nicht korrigierbare Sektoren vorhanden (C6)."),
        ["Health.Reason.NvmeCriticalWarning"] = E("NVMe denetleyicisi kritik uyarı bildiriyor.", "The NVMe controller reports a critical warning.", "Der NVMe-Controller meldet eine kritische Warnung."),
        ["Health.Reason.NvmeSpare"] = E("Kullanılabilir yedek alan eşiğin altında.", "Available spare is below the threshold.", "Verfügbare Reserve liegt unter dem Grenzwert."),
        ["Health.Reason.NvmeMediaErrors"] = E("Ortam ve veri bütünlüğü hataları kaydedilmiş.", "Media and data integrity errors have been logged.", "Medien- und Datenintegritätsfehler wurden protokolliert."),
        ["Health.Reason.WindowsUnhealthy"] = E("Windows bu diski sağlıksız olarak bildiriyor.", "Windows reports this disk as unhealthy.", "Windows meldet dieses Laufwerk als fehlerhaft."),
        ["Health.Reason.WindowsWarning"] = E("Windows bu disk için uyarı bildiriyor.", "Windows reports a warning for this disk.", "Windows meldet eine Warnung für dieses Laufwerk."),
        ["Health.Reason.LowLife"] = E("Tahmini kalan ömür %10 veya altında.", "Estimated remaining life is 10% or less.", "Geschätzte Restlebensdauer liegt bei 10 % oder weniger."),
        ["Health.Reason.None"] = E("Herhangi bir sorun tespit edilmedi.", "No issues detected.", "Keine Probleme erkannt."),

        ["Temp.Normal"] = E("Normal", "Normal", "Normal"),
        ["Temp.Caution"] = E("Yüksek", "High", "Hoch"),
        ["Temp.Critical"] = E("Kritik", "Critical", "Kritisch"),
        ["Temp.CriticalBanner"] = E("{0} sıcaklığı kritik eşiğin üzerinde: {1}", "{0} temperature is above the critical threshold: {1}", "Temperatur von {0} liegt über dem kritischen Grenzwert: {1}"),

        // ---------- Disk health page ----------
        ["Disk.Health"] = E("Sağlık Durumu", "Health Status", "Zustand"),
        ["Disk.Temperature"] = E("Sıcaklık", "Temperature", "Temperatur"),
        ["Disk.HostReads"] = E("Toplam Okunan", "Total Host Reads", "Gesamt gelesen"),
        ["Disk.HostWrites"] = E("Toplam Yazılan", "Total Host Writes", "Gesamt geschrieben"),
        ["Disk.PowerOnHours"] = E("Çalışma Saati", "Power-On Hours", "Betriebsstunden"),
        ["Disk.PowerOnCount"] = E("Güç Açma Sayısı", "Power-On Count", "Einschaltvorgänge"),
        ["Disk.Firmware"] = E("Firmware", "Firmware", "Firmware"),
        ["Disk.Serial"] = E("Seri Numarası", "Serial Number", "Seriennummer"),
        ["Disk.Model"] = E("Model", "Model", "Modell"),
        ["Disk.Interface"] = E("Arayüz", "Interface", "Schnittstelle"),
        ["Disk.TransferMode"] = E("Aktarım Modu", "Transfer Mode", "Übertragungsmodus"),
        ["Disk.Capacity"] = E("Kapasite", "Capacity", "Kapazität"),
        ["Disk.DriveLetters"] = E("Sürücü Harfleri", "Drive Letters", "Laufwerksbuchstaben"),
        ["Disk.Media"] = E("Ortam Türü", "Media Type", "Medientyp"),
        ["Disk.Rotation"] = E("Dönüş Hızı", "Rotation Rate", "Drehzahl"),
        ["Disk.Life"] = E("Kalan Ömür", "Remaining Life", "Restlebensdauer"),
        ["Disk.SmartSource"] = E("Veri Kaynağı", "Data Source", "Datenquelle"),
        ["Disk.SystemDisk"] = E("Sistem diski", "System disk", "Systemlaufwerk"),
        ["Disk.Removable"] = E("Harici / çıkarılabilir", "External / removable", "Extern / wechselbar"),
        ["Disk.NoDisks"] = E("Fiziksel disk bulunamadı.", "No physical disks were found.", "Keine physischen Laufwerke gefunden."),
        ["Disk.Scanning"] = E("Diskler taranıyor…", "Scanning disks…", "Laufwerke werden gescannt…"),
        ["Disk.AutoRefresh"] = E("Otomatik yenileme: {0} sn", "Auto refresh: {0} s", "Automatische Aktualisierung: {0} s"),
        ["Disk.AutoRefreshOff"] = E("Otomatik yenileme kapalı", "Auto refresh off", "Automatische Aktualisierung aus"),
        ["Disk.Attributes"] = E("S.M.A.R.T. Öznitelikleri", "S.M.A.R.T. Attributes", "S.M.A.R.T.-Attribute"),
        ["Disk.Assessment"] = E("Değerlendirme", "Assessment", "Bewertung"),
        ["Disk.CopySummary"] = E("Disk raporunu kopyala", "Copy disk report", "Laufwerksbericht kopieren"),
        ["Disk.Col.Id"] = E("ID", "ID", "ID"),
        ["Disk.Col.Name"] = E("Öznitelik Adı", "Attribute Name", "Attributname"),
        ["Disk.Col.Current"] = E("Mevcut", "Current", "Aktuell"),
        ["Disk.Col.Worst"] = E("En Kötü", "Worst", "Schlechtester"),
        ["Disk.Col.Threshold"] = E("Eşik", "Threshold", "Grenzwert"),
        ["Disk.Col.Raw"] = E("Ham Değer", "Raw Value", "Rohwert"),
        ["Disk.Col.Status"] = E("Durum", "Status", "Status"),
        ["Attr.Ok"] = E("İyi", "OK", "OK"),
        ["Attr.Info"] = E("Bilgi", "Info", "Info"),
        ["Attr.Caution"] = E("Dikkat", "Caution", "Vorsicht"),
        ["Attr.Bad"] = E("Kritik", "Critical", "Kritisch"),
        ["Smart.Source.None"] = E("Yok", "None", "Keine"),
        ["Smart.Source.Ata"] = E("ATA S.M.A.R.T.", "ATA S.M.A.R.T.", "ATA S.M.A.R.T."),
        ["Smart.Source.Nvme"] = E("NVMe Sağlık Günlüğü (02h)", "NVMe Health Log (02h)", "NVMe-Zustandsprotokoll (02h)"),
        ["Smart.Source.StorageReliability"] = E("Windows güvenilirlik sayaçları", "Windows reliability counters", "Windows-Zuverlässigkeitszähler"),
        ["Smart.Unavailable.NeedsAdmin"] = E("S.M.A.R.T. verisi için yönetici izni gerekiyor.", "Administrator rights are required to read S.M.A.R.T. data.", "Zum Lesen der S.M.A.R.T.-Daten sind Administratorrechte erforderlich."),
        ["Smart.Unavailable.Unsupported"] = E("Bu disk veya denetleyici S.M.A.R.T. verisini Windows'a sunmuyor.", "This disk or controller does not expose S.M.A.R.T. data to Windows.", "Dieses Laufwerk bzw. dieser Controller stellt Windows keine S.M.A.R.T.-Daten bereit."),
        ["Smart.Unavailable.Usb"] = E("USB köprüsü S.M.A.R.T. geçişini desteklemiyor.", "The USB bridge does not support S.M.A.R.T. pass-through.", "Die USB-Brücke unterstützt keine S.M.A.R.T.-Durchleitung."),
        ["Smart.Unavailable.Virtual"] = E("Sanal / RAID disk: S.M.A.R.T. verisi yok.", "Virtual / RAID disk: no S.M.A.R.T. data.", "Virtuelles / RAID-Laufwerk: keine S.M.A.R.T.-Daten."),
        ["Smart.Unavailable.Error"] = E("S.M.A.R.T. okunurken hata oluştu.", "An error occurred while reading S.M.A.R.T.", "Beim Lesen von S.M.A.R.T. ist ein Fehler aufgetreten."),
        ["Media.Hdd"] = E("HDD (manyetik)", "HDD (magnetic)", "HDD (magnetisch)"),
        ["Media.Ssd"] = E("SSD (flash)", "SSD (flash)", "SSD (Flash)"),
        ["Media.Scm"] = E("SCM", "SCM", "SCM"),
        ["Media.Unknown"] = E("Bilinmiyor", "Unknown", "Unbekannt"),
        ["Disk.Rpm"] = E("{0:N0} RPM", "{0:N0} RPM", "{0:N0} U/min"),
        ["Disk.SsdNoRotation"] = E("— (SSD)", "— (SSD)", "— (SSD)"),

        // ---------- Benchmark page ----------
        ["Bench.Target"] = E("Hedef sürücü / klasör", "Target drive / folder", "Ziellaufwerk / Ordner"),
        ["Bench.FreeSpace"] = E("Boş alan: {0} / {1}", "Free space: {0} / {1}", "Freier Speicher: {0} / {1}"),
        ["Bench.TestSize"] = E("Test boyutu", "Test size", "Testgröße"),
        ["Bench.Iterations"] = E("Tekrar sayısı", "Iterations", "Durchläufe"),
        ["Bench.Duration"] = E("Ölçüm süresi (sn)", "Measure time (s)", "Messdauer (s)"),
        ["Bench.Interval"] = E("Testler arası bekleme (sn)", "Interval between tests (s)", "Pause zwischen Tests (s)"),
        ["Bench.Mode"] = E("Test modu", "Test mode", "Testmodus"),
        ["Bench.Mode.ReadWrite"] = E("Okuma + Yazma", "Read + Write", "Lesen + Schreiben"),
        ["Bench.Mode.ReadOnly"] = E("Yalnız okuma", "Read only", "Nur lesen"),
        ["Bench.Mode.WriteOnly"] = E("Yalnız yazma", "Write only", "Nur schreiben"),
        ["Bench.Profile"] = E("Test profili", "Test profile", "Testprofil"),
        ["Bench.Access"] = E("Erişim", "Access", "Zugriff"),
        ["Bench.Access.Sequential"] = E("SEQ — Sıralı", "SEQ — Sequential", "SEQ — Sequentiell"),
        ["Bench.Access.Random"] = E("RND — Rastgele", "RND — Random", "RND — Zufällig"),
        ["Bench.BlockSize"] = E("Blok boyutu", "Block size", "Blockgröße"),
        ["Bench.QueueDepth"] = E("Kuyruk derinliği", "Queue depth", "Warteschlangentiefe"),
        ["Bench.Threads"] = E("İş parçacığı", "Threads", "Threads"),
        ["Bench.ResetProfile"] = E("Varsayılan profil", "Default profile", "Standardprofil"),
        ["Bench.Start"] = E("Testi başlat", "Start test", "Test starten"),
        ["Bench.Cancel"] = E("İptal et", "Cancel", "Abbrechen"),
        ["Bench.Read"] = E("Okuma", "Read", "Lesen"),
        ["Bench.Write"] = E("Yazma", "Write", "Schreiben"),
        ["Bench.Test"] = E("Test", "Test", "Test"),
        ["Bench.MBps"] = E("MB/s", "MB/s", "MB/s"),
        ["Bench.Iops"] = E("IOPS", "IOPS", "IOPS"),
        ["Bench.Latency"] = E("Ort. gecikme", "Avg. latency", "Ø Latenz"),
        ["Bench.Results"] = E("Sonuçlar", "Results", "Ergebnisse"),
        ["Bench.Phase.Idle"] = E("Hazır", "Ready", "Bereit"),
        ["Bench.Phase.Preparing"] = E("Test dosyası hazırlanıyor…", "Preparing test file…", "Testdatei wird vorbereitet…"),
        ["Bench.Phase.Reading"] = E("Okuma testi: {0}", "Read test: {0}", "Lesetest: {0}"),
        ["Bench.Phase.Writing"] = E("Yazma testi: {0}", "Write test: {0}", "Schreibtest: {0}"),
        ["Bench.Phase.Interval"] = E("Sonraki test bekleniyor…", "Waiting for next test…", "Warte auf nächsten Test…"),
        ["Bench.Phase.CleaningUp"] = E("Geçici dosyalar temizleniyor…", "Cleaning up temporary files…", "Temporäre Dateien werden entfernt…"),
        ["Bench.Phase.Completed"] = E("Tamamlandı", "Completed", "Abgeschlossen"),
        ["Bench.Phase.Cancelled"] = E("İptal edildi — geçici dosyalar silindi", "Cancelled — temporary files removed", "Abgebrochen — temporäre Dateien entfernt"),
        ["Bench.Phase.Failed"] = E("Başarısız: {0}", "Failed: {0}", "Fehlgeschlagen: {0}"),
        ["Bench.Iteration"] = E("Tur {0}/{1}", "Pass {0}/{1}", "Durchlauf {0}/{1}"),
        ["Bench.SystemDrive.Title"] = E("Sistem sürücüsü seçildi", "System drive selected", "Systemlaufwerk ausgewählt"),
        ["Bench.SystemDrive.Text"] = E(
            "{0} Windows sistem sürücüsüdür. Test sırasında arka plan işlemleri sonuçları etkileyebilir ve SSD'ye {1} veri yazılacaktır. Devam etmek istiyor musunuz?",
            "{0} is the Windows system drive. Background activity can affect the results and {1} of data will be written to the drive. Do you want to continue?",
            "{0} ist das Windows-Systemlaufwerk. Hintergrundaktivität kann die Ergebnisse beeinflussen und es werden {1} Daten auf das Laufwerk geschrieben. Fortfahren?"),
        ["Bench.InsufficientSpace"] = E("Yetersiz boş alan: {0} gerekli, {1} mevcut.", "Insufficient free space: {0} required, {1} available.", "Nicht genügend freier Speicher: {0} benötigt, {1} verfügbar."),
        ["Bench.InvalidTarget"] = E("Hedef klasöre erişilemiyor veya yazılamıyor.", "The target folder is not accessible or not writable.", "Der Zielordner ist nicht zugänglich oder nicht beschreibbar."),
        ["Bench.CopyResults"] = E("Panoya kopyala", "Copy to clipboard", "In Zwischenablage kopieren"),
        ["Bench.ExportJson"] = E("JSON", "JSON", "JSON"),
        ["Bench.ExportCsv"] = E("CSV", "CSV", "CSV"),
        ["Bench.ExportTxt"] = E("TXT", "TXT", "TXT"),
        ["Bench.Export"] = E("Dışa aktar", "Export", "Exportieren"),
        ["Bench.NoResults"] = E("Henüz sonuç yok.", "No results yet.", "Noch keine Ergebnisse."),
        ["Bench.Note"] = E(
            "Test, hedefte ZeloraInfoBench klasöründe geçici bir dosya oluşturur (önbelleksiz G/Ç) ve test bittiğinde ya da iptal edildiğinde siler. MB/s = 1.000.000 bayt/sn.",
            "The test creates a temporary file in a ZeloraInfoBench folder on the target (unbuffered I/O) and deletes it when the test finishes or is cancelled. MB/s = 1,000,000 bytes/s.",
            "Der Test erstellt eine temporäre Datei im Ordner ZeloraInfoBench auf dem Ziel (ungepufferte E/A) und löscht sie nach Abschluss oder Abbruch. MB/s = 1.000.000 Byte/s."),
        ["Bench.CleanupDone"] = E("{0} artık test dosyası silindi.", "{0} orphaned test file(s) removed.", "{0} verwaiste Testdatei(en) entfernt."),
        ["Bench.SelectFolder"] = E("Benchmark için klasör seçin", "Select a folder for the benchmark", "Ordner für den Benchmark auswählen"),
        ["Bench.Elevated"] = E("Not: Sonuçlar yönetici izni gerektirmez.", "Note: results do not require administrator rights.", "Hinweis: Für die Ergebnisse sind keine Administratorrechte erforderlich."),

        // ---------- System page ----------
        ["Sys.OsSection"] = E("İşletim Sistemi", "Operating System", "Betriebssystem"),
        ["Sys.OsName"] = E("Ad", "Name", "Name"),
        ["Sys.Version"] = E("Sürüm", "Version", "Version"),
        ["Sys.DisplayVersion"] = E("Özellik güncellemesi", "Feature update", "Funktionsupdate"),
        ["Sys.Build"] = E("Yapı numarası", "Build number", "Buildnummer"),
        ["Sys.Architecture"] = E("Mimari", "Architecture", "Architektur"),
        ["Sys.ComputerName"] = E("Bilgisayar adı", "Computer name", "Computername"),
        ["Sys.UserName"] = E("Kullanıcı adı", "User name", "Benutzername"),
        ["Sys.Uptime"] = E("Çalışma süresi", "Uptime", "Betriebszeit"),
        ["Sys.LastBoot"] = E("Son yeniden başlatma", "Last restart", "Letzter Neustart"),
        ["Sys.InstallDate"] = E("Kurulum tarihi", "Install date", "Installationsdatum"),
        ["Sys.Firmware"] = E("Ürün yazılımı", "Firmware type", "Firmwaretyp"),
        ["Sys.SecureBoot"] = E("Güvenli önyükleme", "Secure Boot", "Sicherer Start"),
        ["Sys.CpuSection"] = E("İşlemci (CPU)", "Processor (CPU)", "Prozessor (CPU)"),
        ["Sys.CpuName"] = E("Model", "Model", "Modell"),
        ["Sys.Cores"] = E("Çekirdek", "Cores", "Kerne"),
        ["Sys.Threads"] = E("İş parçacığı", "Threads", "Threads"),
        ["Sys.BaseClock"] = E("Temel hız", "Base clock", "Basistakt"),
        ["Sys.CurrentClock"] = E("Anlık hız", "Current clock", "Aktueller Takt"),
        ["Sys.CpuUsage"] = E("Kullanım", "Usage", "Auslastung"),
        ["Sys.L2"] = E("L2 önbellek", "L2 cache", "L2-Cache"),
        ["Sys.L3"] = E("L3 önbellek", "L3 cache", "L3-Cache"),
        ["Sys.Socket"] = E("Soket", "Socket", "Sockel"),
        ["Sys.Virtualization"] = E("Sanallaştırma", "Virtualization", "Virtualisierung"),
        ["Sys.BoardSection"] = E("Anakart ve BIOS/UEFI", "Mainboard & BIOS/UEFI", "Mainboard & BIOS/UEFI"),
        ["Sys.SystemManufacturer"] = E("Sistem üreticisi", "System manufacturer", "Systemhersteller"),
        ["Sys.SystemModel"] = E("Sistem modeli", "System model", "Systemmodell"),
        ["Sys.BoardManufacturer"] = E("Anakart üreticisi", "Board manufacturer", "Mainboard-Hersteller"),
        ["Sys.BoardModel"] = E("Anakart modeli", "Board model", "Mainboard-Modell"),
        ["Sys.BiosVendor"] = E("BIOS üreticisi", "BIOS vendor", "BIOS-Hersteller"),
        ["Sys.BiosVersion"] = E("BIOS sürümü", "BIOS version", "BIOS-Version"),
        ["Sys.BiosDate"] = E("BIOS tarihi", "BIOS date", "BIOS-Datum"),
        ["Sys.GpuSection"] = E("Ekran Kartı (GPU)", "Graphics (GPU)", "Grafik (GPU)"),
        ["Sys.GpuName"] = E("Model", "Model", "Modell"),
        ["Sys.GpuDriver"] = E("Sürücü sürümü", "Driver version", "Treiberversion"),
        ["Sys.GpuDriverDate"] = E("Sürücü tarihi", "Driver date", "Treiberdatum"),
        ["Sys.GpuMemory"] = E("Ayrılmış bellek", "Dedicated memory", "Dedizierter Speicher"),
        ["Sys.GpuMode"] = E("Görüntü modu", "Video mode", "Videomodus"),
        ["Sys.CopyReport"] = E("Sistem raporunu kopyala", "Copy system report", "Systembericht kopieren"),

        // ---------- Display page ----------
        ["Disp.Count"] = E("{0} ekran bağlı", "{0} display(s) connected", "{0} Bildschirm(e) angeschlossen"),
        ["Disp.VirtualDesktop"] = E("Sanal masaüstü: {0} × {1} (başlangıç {2}, {3})", "Virtual desktop: {0} × {1} (origin {2}, {3})", "Virtueller Desktop: {0} × {1} (Ursprung {2}, {3})"),
        ["Disp.Primary"] = E("Birincil", "Primary", "Primär"),
        ["Disp.Resolution"] = E("Aktif çözünürlük", "Active resolution", "Aktive Auflösung"),
        ["Disp.Preferred"] = E("Önerilen çözünürlük", "Recommended resolution", "Empfohlene Auflösung"),
        ["Disp.Refresh"] = E("Yenileme hızı", "Refresh rate", "Bildwiederholrate"),
        ["Disp.MaxRefresh"] = E("En yüksek yenileme hızı", "Maximum refresh rate", "Maximale Bildwiederholrate"),
        ["Disp.Dpi"] = E("DPI", "DPI", "DPI"),
        ["Disp.Scale"] = E("Ölçekleme", "Scaling", "Skalierung"),
        ["Disp.ColorDepth"] = E("Renk derinliği", "Color depth", "Farbtiefe"),
        ["Disp.BitsPerChannel"] = E("{0} bit/kanal", "{0} bits/channel", "{0} Bit/Kanal"),
        ["Disp.Hdr"] = E("HDR", "HDR", "HDR"),
        ["Disp.HdrOn"] = E("Açık", "On", "Ein"),
        ["Disp.HdrOff"] = E("Kapalı (destekleniyor)", "Off (supported)", "Aus (unterstützt)"),
        ["Disp.HdrNo"] = E("Desteklenmiyor", "Not supported", "Nicht unterstützt"),
        ["Disp.Size"] = E("Fiziksel boyut", "Physical size", "Physische Größe"),
        ["Disp.Aspect"] = E("En-boy oranı", "Aspect ratio", "Seitenverhältnis"),
        ["Disp.Orientation"] = E("Yönlendirme", "Orientation", "Ausrichtung"),
        ["Disp.Orientation.Landscape"] = E("Yatay", "Landscape", "Querformat"),
        ["Disp.Orientation.Portrait"] = E("Dikey", "Portrait", "Hochformat"),
        ["Disp.Orientation.LandscapeFlipped"] = E("Yatay (ters)", "Landscape (flipped)", "Querformat (gedreht)"),
        ["Disp.Orientation.PortraitFlipped"] = E("Dikey (ters)", "Portrait (flipped)", "Hochformat (gedreht)"),
        ["Disp.Position"] = E("Sanal masaüstü koordinatları", "Virtual desktop coordinates", "Koordinaten im virtuellen Desktop"),
        ["Disp.WorkArea"] = E("Çalışma alanı", "Work area", "Arbeitsbereich"),
        ["Disp.Adapter"] = E("Ekran kartı", "Graphics adapter", "Grafikadapter"),
        ["Disp.Connection"] = E("Bağlantı", "Connection", "Verbindung"),
        ["Disp.Device"] = E("Aygıt adı", "Device name", "Gerätename"),
        ["Disp.Internal"] = E("Dahili ekran", "Internal display", "Internes Display"),

        // ---------- Memory page ----------
        ["Mem.Total"] = E("Toplam fiziksel bellek", "Total physical memory", "Physischer Speicher gesamt"),
        ["Mem.Used"] = E("Kullanılan", "In use", "Belegt"),
        ["Mem.Free"] = E("Boş (kullanılabilir)", "Available", "Verfügbar"),
        ["Mem.Usage"] = E("Kullanım", "Usage", "Auslastung"),
        ["Mem.Commit"] = E("Kaydedilen (commit)", "Committed", "Zugesichert (Commit)"),
        ["Mem.Modules"] = E("Bellek modülleri (DIMM)", "Memory modules (DIMM)", "Speichermodule (DIMM)"),
        ["Mem.Slots"] = E("{0} / {1} slot dolu", "{0} of {1} slots used", "{0} von {1} Steckplätzen belegt"),
        ["Mem.MaxCapacity"] = E("Desteklenen en yüksek kapasite: {0}", "Maximum supported capacity: {0}", "Maximal unterstützte Kapazität: {0}"),
        ["Mem.Slot"] = E("Slot", "Slot", "Steckplatz"),
        ["Mem.Capacity"] = E("Kapasite", "Capacity", "Kapazität"),
        ["Mem.Speed"] = E("Hız", "Speed", "Geschwindigkeit"),
        ["Mem.ConfiguredSpeed"] = E("Yapılandırılmış hız", "Configured speed", "Konfigurierte Geschwindigkeit"),
        ["Mem.Type"] = E("Tip", "Type", "Typ"),
        ["Mem.FormFactor"] = E("Form faktörü", "Form factor", "Formfaktor"),
        ["Mem.Manufacturer"] = E("Üretici", "Manufacturer", "Hersteller"),
        ["Mem.PartNumber"] = E("Parça numarası", "Part number", "Teilenummer"),
        ["Mem.Serial"] = E("Seri no", "Serial", "Seriennr."),
        ["Mem.Voltage"] = E("Gerilim", "Voltage", "Spannung"),
        ["Mem.DataWidth"] = E("Veri genişliği", "Data width", "Datenbreite"),
        ["Mem.PageFile"] = E("Sayfa dosyası / sanal bellek", "Page file / virtual memory", "Auslagerungsdatei / virtueller Speicher"),
        ["Mem.PageFile.Path"] = E("Konum", "Location", "Speicherort"),
        ["Mem.PageFile.Allocated"] = E("Ayrılan", "Allocated", "Zugewiesen"),
        ["Mem.PageFile.Current"] = E("Kullanılan", "In use", "Belegt"),
        ["Mem.PageFile.Peak"] = E("En yüksek", "Peak", "Spitze"),
        ["Mem.PageFile.Auto"] = E("Boyut Windows tarafından otomatik yönetiliyor", "Size is managed automatically by Windows", "Größe wird automatisch von Windows verwaltet"),
        ["Mem.PageFile.None"] = E("Sayfa dosyası yapılandırılmamış", "No page file configured", "Keine Auslagerungsdatei konfiguriert"),
        ["Mem.NoModules"] = E("Modül bilgisi okunamadı (sanal makine veya desteklenmeyen SMBIOS).", "Module information unavailable (virtual machine or unsupported SMBIOS).", "Modulinformationen nicht verfügbar (virtuelle Maschine oder nicht unterstütztes SMBIOS)."),

        // ---------- Settings page ----------
        ["Set.Language"] = E("Dil", "Language", "Sprache"),
        ["Set.Theme"] = E("Tema", "Theme", "Design"),
        ["Set.StartWithWindows"] = E("Windows açılışında başlat", "Start with Windows", "Mit Windows starten"),
        ["Set.StartMinimized"] = E("Küçültülmüş başlat", "Start minimized", "Minimiert starten"),
        ["Set.MinimizeToTray"] = E("Küçültünce sistem tepsisine gönder", "Minimize to system tray", "In den Infobereich minimieren"),
        ["Set.Smart"] = E("Disk Sağlık (S.M.A.R.T.)", "Disk Health (S.M.A.R.T.)", "Datenträgerzustand (S.M.A.R.T.)"),
        ["Set.RefreshInterval"] = E("Yenileme sıklığı (sn, 0 = kapalı)", "Refresh interval (s, 0 = off)", "Aktualisierungsintervall (s, 0 = aus)"),
        ["Set.TempUnit"] = E("Sıcaklık birimi", "Temperature unit", "Temperatureinheit"),
        ["Set.TempUnit.Celsius"] = E("Santigrat (°C)", "Celsius (°C)", "Celsius (°C)"),
        ["Set.TempUnit.Fahrenheit"] = E("Fahrenhayt (°F)", "Fahrenheit (°F)", "Fahrenheit (°F)"),
        ["Set.CautionTemp"] = E("Dikkat sıcaklığı (°C)", "Caution temperature (°C)", "Warntemperatur (°C)"),
        ["Set.CriticalTemp"] = E("Kritik sıcaklık (°C)", "Critical temperature (°C)", "Kritische Temperatur (°C)"),
        ["Set.TreatReallocated"] = E("Yeniden atanmış / bekleyen sektörlerde \"Dikkat\" göster", "Show \"Caution\" for reallocated / pending sectors", "Bei neu zugewiesenen / schwebenden Sektoren \"Vorsicht\" anzeigen"),
        ["Set.Benchmark"] = E("Benchmark varsayılanları", "Benchmark defaults", "Benchmark-Standardwerte"),
        ["Set.Cleanup"] = E("Geçici dosya temizliği", "Temporary file cleanup", "Bereinigung temporärer Dateien"),
        ["Set.DeleteAfterRun"] = E("Her testten sonra test dosyasını sil", "Delete the test file after every run", "Testdatei nach jedem Lauf löschen"),
        ["Set.CleanupOnStartup"] = E("Açılışta yarım kalmış test dosyalarını temizle", "Remove orphaned test files on startup", "Verwaiste Testdateien beim Start entfernen"),
        ["Set.WarnSystemDrive"] = E("Sistem sürücüsünde test öncesi uyar", "Warn before testing the system drive", "Vor dem Test des Systemlaufwerks warnen"),
        ["Set.CleanupNow"] = E("Şimdi temizle", "Clean up now", "Jetzt bereinigen"),
        ["Set.ResetDefaults"] = E("Varsayılanlara sıfırla", "Reset to defaults", "Auf Standard zurücksetzen"),
        ["Set.ResetConfirm"] = E("Tüm ayarlar varsayılan değerlere döndürülsün mü?", "Reset all settings to their default values?", "Alle Einstellungen auf Standardwerte zurücksetzen?"),
        ["Set.ResetDone"] = E("Ayarlar varsayılanlara döndürüldü.", "Settings were reset to defaults.", "Einstellungen wurden zurückgesetzt."),
        ["Set.FilePath"] = E("Ayar dosyası", "Settings file", "Einstellungsdatei"),
        ["Set.OpenFolder"] = E("Klasörü aç", "Open folder", "Ordner öffnen"),
        ["Set.StartupFailed"] = E("Başlangıç kaydı değiştirilemedi: {0}", "Could not change the startup entry: {0}", "Autostart-Eintrag konnte nicht geändert werden: {0}"),

        // ---------- Tray ----------
        ["Tray.Open"] = E("Zelora Info'yu aç", "Open Zelora Info", "Zelora Info öffnen"),
        ["Tray.Exit"] = E("Çıkış", "Exit", "Beenden"),
        ["Tray.Running"] = E("Zelora Info sistem tepsisinde çalışmaya devam ediyor.", "Zelora Info keeps running in the system tray.", "Zelora Info läuft im Infobereich weiter."),

        // ---------- About ----------
        ["About.Description"] = E(
            "Zelora Info; disk sağlığı (S.M.A.R.T.), disk performans testi, sistem, ekran ve bellek bilgilerini tek bir modern arayüzde birleştiren ücretsiz ve açık kaynak bir Windows aracıdır.",
            "Zelora Info is a free, open-source Windows utility that combines disk health (S.M.A.R.T.), disk benchmarking and system, display and memory information in a single modern interface.",
            "Zelora Info ist ein kostenloses Open-Source-Windows-Tool, das Datenträgerzustand (S.M.A.R.T.), Datenträger-Benchmark sowie System-, Bildschirm- und Speicherinformationen in einer modernen Oberfläche vereint."),
        ["About.Version"] = E("Sürüm", "Version", "Version"),
        ["About.Build"] = E("Derleme", "Build", "Build"),
        ["About.Runtime"] = E("Çalışma zamanı", "Runtime", "Laufzeit"),
        ["About.Developer"] = E("Geliştirici", "Developer", "Entwickler"),
        ["About.LicenseSection"] = E("Lisans — Apache License 2.0", "License — Apache License 2.0", "Lizenz — Apache License 2.0"),
        ["About.LicenseIntro"] = E(
            "Zelora Info, Apache License, Version 2.0 (\"Lisans\") kapsamında lisanslanmıştır. Bu dosyayı yalnızca Lisans'a uygun olarak kullanabilirsiniz.",
            "Zelora Info is licensed under the Apache License, Version 2.0 (the \"License\"). You may not use this software except in compliance with the License.",
            "Zelora Info ist unter der Apache License, Version 2.0 (die \"Lizenz\") lizenziert. Sie dürfen diese Software nur in Übereinstimmung mit der Lizenz verwenden."),
        ["About.Permissions"] = E("İzinler", "Permissions", "Erlaubnisse"),
        ["About.PermissionsText"] = E(
            "Ticari kullanım, değiştirme, dağıtım, patent kullanımı ve özel kullanım serbesttir.",
            "Commercial use, modification, distribution, patent use and private use are permitted.",
            "Kommerzielle Nutzung, Änderung, Weitergabe, Patentnutzung und private Nutzung sind erlaubt."),
        ["About.Conditions"] = E("Yükümlülükler", "Conditions", "Bedingungen"),
        ["About.ConditionsText"] = E(
            "Dağıtımda lisans ve telif bildirimi korunmalı, değiştirilen dosyalarda değişiklik belirtilmeli ve varsa NOTICE dosyası eklenmelidir.",
            "Redistributions must keep the license and copyright notice, state changes made to modified files and include the NOTICE file, if any.",
            "Bei Weitergabe müssen Lizenz- und Urheberrechtshinweis erhalten bleiben, Änderungen an Dateien kenntlich gemacht und eine vorhandene NOTICE-Datei beigefügt werden."),
        ["About.Limitations"] = E("Sınırlamalar", "Limitations", "Einschränkungen"),
        ["About.LimitationsText"] = E(
            "Lisans, ticari marka kullanım hakkı vermez; sorumluluk ve garanti kapsam dışıdır.",
            "The license grants no trademark rights; liability and warranty are excluded.",
            "Die Lizenz gewährt keine Markenrechte; Haftung und Gewährleistung sind ausgeschlossen."),
        ["About.Warranty"] = E("Garanti reddi (\"OLDUĞU GİBİ\")", "Disclaimer of warranty (\"AS IS\")", "Gewährleistungsausschluss (\"WIE BESEHEN\")"),
        ["About.WarrantyText"] = E(
            "Yürürlükteki yasaların gerektirdiği veya yazılı olarak kabul edilen durumlar dışında, Lisans kapsamında dağıtılan yazılım AÇIK VEYA ZIMNİ HİÇBİR GARANTİ VEYA KOŞUL OLMAKSIZIN \"OLDUĞU GİBİ\" dağıtılır. Disk benchmark testleri diske veri yazar; önemli verilerinizi yedekleyin.",
            "Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an \"AS IS\" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Disk benchmarks write data to the drive; back up important data.",
            "Sofern nicht durch geltendes Recht vorgeschrieben oder schriftlich vereinbart, wird unter der Lizenz vertriebene Software \"WIE BESEHEN\" OHNE GEWÄHRLEISTUNGEN ODER BEDINGUNGEN JEGLICHER ART, weder ausdrücklich noch stillschweigend, bereitgestellt. Datenträger-Benchmarks schreiben Daten auf das Laufwerk; sichern Sie wichtige Daten."),
        ["About.OpenLicense"] = E("Apache 2.0 lisans metnini görüntüle", "View Apache 2.0 license text", "Apache-2.0-Lizenztext anzeigen"),
        ["About.ThirdPartySection"] = E("3. Parti Uygulama ve Kütüphaneler", "Third-Party Applications & Libraries", "Drittanbieter-Anwendungen & Bibliotheken"),
        ["About.ThirdPartyIntro"] = E(
            "Zelora Info aşağıdaki açık kaynak bileşenleri kullanır. Her bileşen kendi lisansına tabidir.",
            "Zelora Info uses the following open-source components. Each component is subject to its own license.",
            "Zelora Info verwendet die folgenden Open-Source-Komponenten. Jede Komponente unterliegt ihrer eigenen Lizenz."),
        ["About.OpenThirdParty"] = E("3. parti lisansları görüntüle", "View third-party licenses", "Drittanbieter-Lizenzen anzeigen"),
        ["Col.Component"] = E("Bileşen", "Component", "Komponente"),
        ["Col.Version"] = E("Sürüm", "Version", "Version"),
        ["Col.License"] = E("Lisans", "License", "Lizenz"),
        ["Col.Purpose"] = E("Kullanım amacı", "Purpose", "Verwendungszweck"),
        ["Col.Link"] = E("Proje", "Project", "Projekt"),

        // ---------- License screens ----------
        ["Lic.Copy"] = E("Panoya kopyala", "Copy to clipboard", "In Zwischenablage kopieren"),
        ["Lic.ExportTxt"] = E(".txt olarak kaydet", "Save as .txt", "Als .txt speichern"),
        ["Lic.Holder"] = E("Telif hakkı", "Copyright", "Urheberrecht"),
        ["Tp.Search"] = E("Bileşen, lisans veya sürüm ara…", "Search component, license or version…", "Komponente, Lizenz oder Version suchen…"),
        ["Tp.Count"] = E("{0} bileşen", "{0} component(s)", "{0} Komponente(n)"),
        ["Tp.SelectHint"] = E("Ayrıntıları görmek için listeden bir bileşen seçin.", "Select a component from the list to see its details.", "Wählen Sie eine Komponente aus der Liste, um Details zu sehen."),
        ["Tp.Copyright"] = E("Telif", "Copyright", "Urheberrecht"),
        ["Tp.Source"] = E("Kaynak", "Source", "Quelle"),
        ["Tp.LicenseUrl"] = E("Lisans bağlantısı", "License link", "Lizenzlink"),
        ["Tp.LicenseText"] = E("Lisans metni", "License text", "Lizenztext"),
        ["Tp.CopyLicense"] = E("Lisans metnini kopyala", "Copy license text", "Lizenztext kopieren"),
        ["Tp.Scope"] = E("Kapsam", "Scope", "Umfang"),
        ["Tp.Scope.runtime"] = E("Uygulama ile dağıtılır", "Distributed with the app", "Mit der App verteilt"),
        ["Tp.Scope.development"] = E("Yalnızca derleme / test", "Build / test only", "Nur Build / Test"),
        ["Tp.ManifestError"] = E("third-party-licenses.json okunamadı: {0}", "Could not read third-party-licenses.json: {0}", "third-party-licenses.json konnte nicht gelesen werden: {0}"),

        // ---------- Common (additional) ----------
        ["Common.CopyReport"] = E("Raporu kopyala", "Copy report", "Bericht kopieren"),
        ["Common.ExportTxt"] = E("TXT kaydet", "Save TXT", "TXT speichern"),
        ["Common.ExportJson"] = E("JSON kaydet", "Save JSON", "JSON speichern"),
        ["Common.NA"] = E("N/A", "N/A", "N/V"),
        ["Common.UnexpectedError"] = E("Beklenmeyen bir hata oluştu: {0}\n\nAyrıntılar %LOCALAPPDATA%\\ZeloraInfo\\logs\\error.log dosyasına yazıldı.", "An unexpected error occurred: {0}\n\nDetails were written to %LOCALAPPDATA%\\ZeloraInfo\\logs\\error.log.", "Ein unerwarteter Fehler ist aufgetreten: {0}\n\nDetails wurden in %LOCALAPPDATA%\\ZeloraInfo\\logs\\error.log geschrieben."),

        // ---------- Navigation (hardware details) ----------
        ["Nav.CpuDetails"] = E("CPU Ayrıntı", "CPU Details", "CPU-Details"),
        ["Nav.RamDetails"] = E("RAM Ayrıntı", "Memory Details", "Speicher-Details"),
        ["Page.CpuDetails.Subtitle"] = E("İşlemci kimliği, komut setleri, önbellekler, canlı frekans ve hızlı CPU testi", "Processor identity, instruction sets, caches, live clocks and a quick CPU test", "Prozessorkennung, Befehlssätze, Caches, Live-Takt und schneller CPU-Test"),
        ["Page.RamDetails.Subtitle"] = E("Bellek denetleyicisi, kanal, frekans, zamanlamalar ve SPD / slot ayrıntıları", "Memory controller, channels, frequency, timings and SPD / slot details", "Speichercontroller, Kanäle, Frequenz, Timings und SPD-/Steckplatzdetails"),

        // ---------- CPU details ----------
        ["Cpu.Processor"] = E("İşlemci", "Processor", "Prozessor"),
        ["Cpu.Name"] = E("Ad", "Name", "Name"),
        ["Cpu.CodeName"] = E("Kod adı", "Code name", "Codename"),
        ["Cpu.Package"] = E("Paket / soket", "Package / socket", "Gehäuse / Sockel"),
        ["Cpu.Technology"] = E("Üretim teknolojisi", "Technology", "Fertigung"),
        ["Cpu.CoreVoltage"] = E("Çekirdek voltajı (VID)", "Core voltage (VID)", "Kernspannung (VID)"),
        ["Cpu.Specification"] = E("Tanım", "Specification", "Spezifikation"),
        ["Cpu.Family"] = E("Aile", "Family", "Familie"),
        ["Cpu.Model"] = E("Model", "Model", "Modell"),
        ["Cpu.Stepping"] = E("Stepping", "Stepping", "Stepping"),
        ["Cpu.ExtFamily"] = E("Genişletilmiş aile", "Ext. family", "Erw. Familie"),
        ["Cpu.ExtModel"] = E("Genişletilmiş model", "Ext. model", "Erw. Modell"),
        ["Cpu.Revision"] = E("Revizyon", "Revision", "Revision"),
        ["Cpu.Microcode"] = E("Mikrokod", "Microcode", "Microcode"),
        ["Cpu.Instructions"] = E("Komut setleri", "Instructions", "Befehlssätze"),
        ["Cpu.Clocks"] = E("Saat hızları", "Clocks", "Taktraten"),
        ["Cpu.CoreSpeed"] = E("Çekirdek hızı (ortalama)", "Core speed (average)", "Kerntakt (Durchschnitt)"),
        ["Cpu.MaxCoreSpeed"] = E("Çekirdek hızı (en yüksek)", "Core speed (highest)", "Kerntakt (höchster)"),
        ["Cpu.Multiplier"] = E("Çarpan", "Multiplier", "Multiplikator"),
        ["Cpu.BusSpeed"] = E("Veri yolu hızı (BCLK)", "Bus speed (BCLK)", "Bustakt (BCLK)"),
        ["Cpu.BaseSpeed"] = E("Temel hız", "Base speed", "Basistakt"),
        ["Cpu.MaxSpeed"] = E("Azami hız (CPUID)", "Max speed (CPUID)", "Max. Takt (CPUID)"),
        ["Cpu.Estimated"] = E("(tahmini)", "(estimated)", "(geschätzt)"),
        ["Cpu.Topology"] = E("Topoloji", "Topology", "Topologie"),
        ["Cpu.Packages"] = E("Paket sayısı", "Packages", "Pakete"),
        ["Cpu.Cores"] = E("Çekirdekler", "Cores", "Kerne"),
        ["Cpu.Threads"] = E("İş parçacıkları", "Threads", "Threads"),
        ["Cpu.HybridLabel"] = E("Hibrit mimari", "Hybrid architecture", "Hybridarchitektur"),
        ["Cpu.Hybrid"] = E("{0} performans + {1} verimlilik çekirdeği", "{0} performance + {1} efficiency cores", "{0} Performance- + {1} Effizienzkerne"),
        ["Cpu.Hypervisor"] = E("Hiper yönetici etkin", "Hypervisor present", "Hypervisor aktiv"),
        ["Cpu.Caches"] = E("Önbellekler", "Caches", "Caches"),
        ["Cpu.FullyAssoc"] = E("tam ilişkili", "fully associative", "vollassoziativ"),
        ["Cpu.SharedBy"] = E("Her örneği {0} iş parçacığı paylaşır", "Each instance shared by {0} thread(s)", "Jede Instanz wird von {0} Thread(s) geteilt"),
        ["Cpu.Usage"] = E("Kullanım", "Usage", "Auslastung"),
        ["Cpu.PerCore"] = E("Mantıksal işlemci başına frekans ve kullanım", "Frequency and usage per logical processor", "Takt und Auslastung je logischem Prozessor"),
        ["Cpu.NA.Driver"] = E("Okunamıyor: MSR / SMBus erişimi çekirdek (ring-0) sürücüsü gerektirir; Zelora Info güvenlik nedeniyle sürücü yüklemez.", "Not readable: MSR / SMBus access requires a kernel (ring-0) driver; Zelora Info installs no driver for safety.", "Nicht lesbar: MSR-/SMBus-Zugriff erfordert einen Kernel-Treiber (Ring 0); Zelora Info installiert aus Sicherheitsgründen keinen Treiber."),
        ["Cpu.NoCpuId"] = E("CPUID bu mimaride kullanılamıyor (ör. ARM64); bilgiler WMI'dan alındı.", "CPUID is unavailable on this architecture (e.g. ARM64); data comes from WMI.", "CPUID ist auf dieser Architektur nicht verfügbar (z. B. ARM64); Daten stammen aus WMI."),
        ["Cpu.HypervisorNote"] = E("Hiper yönetici (Hyper-V / VBS) etkin: bazı sanallaştırma bitleri konuk sistemden gizlenebilir (* ile işaretli).", "A hypervisor (Hyper-V / VBS) is active: some virtualization bits may be hidden from the guest (marked with *).", "Ein Hypervisor (Hyper-V / VBS) ist aktiv: einige Virtualisierungsbits können verborgen sein (mit * markiert)."),
        ["Cpu.Bench"] = E("Hızlı CPU testi", "Quick CPU test", "Schneller CPU-Test"),
        ["Cpu.Bench.Start"] = E("Testi başlat", "Start test", "Test starten"),
        ["Cpu.Bench.Single"] = E("Tek çekirdek", "Single thread", "Single-Thread"),
        ["Cpu.Bench.Multi"] = E("Çok çekirdek ({0} iş parçacığı)", "Multi thread ({0} threads)", "Multi-Thread ({0} Threads)"),
        ["Cpu.Bench.MultiShort"] = E("Çok çekirdek", "Multi thread", "Multi-Thread"),
        ["Cpu.Bench.Ratio"] = E("Ölçekleme oranı", "Scaling ratio", "Skalierungsfaktor"),
        ["Cpu.Bench.Running"] = E("Ölçülüyor: {0}", "Measuring: {0}", "Messung: {0}"),
        ["Cpu.Bench.Cancelled"] = E("Test iptal edildi.", "Test cancelled.", "Test abgebrochen."),
        ["Cpu.Bench.Note"] = E("Sentetik tamsayı + kayan nokta yükü. Puanlar yalnızca Zelora Info ölçümleri arasında karşılaştırılabilir; arayüz donmaz ve test iptal edilebilir.", "Synthetic integer + floating-point workload. Scores are only comparable between Zelora Info runs; the UI stays responsive and the test can be cancelled.", "Synthetische Integer- und Gleitkommalast. Punkte sind nur zwischen Zelora-Info-Läufen vergleichbar; die Oberfläche bleibt bedienbar, der Test ist abbrechbar."),

        // ---------- RAM details ----------
        ["Ram.General"] = E("Genel", "General", "Allgemein"),
        ["Ram.Type"] = E("Bellek tipi", "Memory type", "Speichertyp"),
        ["Ram.Size"] = E("Takılı kapasite", "Installed capacity", "Installierte Kapazität"),
        ["Ram.Channels"] = E("Kanal modu", "Channel mode", "Kanalmodus"),
        ["Ram.ChannelInferred"] = E("{0} (slot etiketlerinden / doluluktan çıkarım)", "{0} (inferred from slot labels / population)", "{0} (abgeleitet aus Steckplatzbezeichnung / Bestückung)"),
        ["Ram.DramFreq"] = E("DRAM frekansı", "DRAM frequency", "DRAM-Frequenz"),
        ["Ram.Effective"] = E("Etkin aktarım hızı", "Effective data rate", "Effektive Datenrate"),
        ["Ram.Rated"] = E("Modül azami hızı (SMBIOS)", "Module rated speed (SMBIOS)", "Nenngeschwindigkeit (SMBIOS)"),
        ["Ram.Uncore"] = E("NB / Uncore frekansı", "NB / uncore frequency", "NB-/Uncore-Frequenz"),
        ["Ram.Ecc"] = E("ECC", "ECC", "ECC"),
        ["Ram.Source"] = E("Veri kaynağı", "Data source", "Datenquelle"),
        ["Ram.Timings"] = E("Zamanlamalar", "Timings", "Timings"),
        ["Ram.tCL"] = E("CAS# Latency (tCL)", "CAS# Latency (tCL)", "CAS# Latency (tCL)"),
        ["Ram.tRCD"] = E("RAS# to CAS# Delay (tRCD)", "RAS# to CAS# Delay (tRCD)", "RAS# to CAS# Delay (tRCD)"),
        ["Ram.tRP"] = E("RAS# Precharge (tRP)", "RAS# Precharge (tRP)", "RAS# Precharge (tRP)"),
        ["Ram.tRAS"] = E("Cycle Time (tRAS)", "Cycle Time (tRAS)", "Cycle Time (tRAS)"),
        ["Ram.CR"] = E("Command Rate (CR)", "Command Rate (CR)", "Command Rate (CR)"),
        ["Ram.tRFC"] = E("Refresh Cycle Time (tRFC)", "Refresh Cycle Time (tRFC)", "Refresh Cycle Time (tRFC)"),
        ["Ram.Vdd"] = E("DRAM voltajı (VDD)", "DRAM voltage (VDD)", "DRAM-Spannung (VDD)"),
        ["Ram.Vddq"] = E("VDDQ", "VDDQ", "VDDQ"),
        ["Ram.Profile"] = E("XMP / EXPO / JEDEC profili", "XMP / EXPO / JEDEC profile", "XMP-/EXPO-/JEDEC-Profil"),
        ["Ram.ProfileNA"] = E("SPD EEPROM'una erişim çekirdek sürücüsü gerektirir.", "Reading the SPD EEPROM requires a kernel driver.", "Das Lesen des SPD-EEPROMs erfordert einen Kernel-Treiber."),
        ["Ram.TimingsNote"] = E("Zamanlamalar SPD EEPROM'unda ve bellek denetleyicisi yazmaçlarında bulunur; bunlara yalnızca çekirdek (ring-0) sürücüsüyle erişilebilir. Zelora Info güvenlik nedeniyle sürücü yüklemez; değer uydurmak yerine N/A gösterir. Voltaj SMBIOS'tan okunur.", "Timings live in the SPD EEPROM and memory-controller registers, which are only reachable through a kernel (ring-0) driver. Zelora Info installs no driver for safety and shows N/A instead of guessing. Voltage comes from SMBIOS.", "Timings liegen im SPD-EEPROM und in Registern des Speichercontrollers, die nur über einen Kernel-Treiber (Ring 0) erreichbar sind. Zelora Info installiert keinen Treiber und zeigt N/V statt Schätzwerten. Die Spannung stammt aus SMBIOS."),
        ["Ram.Slots"] = E("SPD / Slotlar", "SPD / Slots", "SPD / Steckplätze"),
        ["Ram.SlotsSummary"] = E("{0} / {1} slot dolu", "{0} of {1} slots populated", "{0} von {1} Steckplätzen belegt"),
        ["Ram.Empty"] = E("Boş slot", "Empty slot", "Leerer Steckplatz"),
        ["Ram.ModuleType"] = E("Modül tipi", "Module type", "Modultyp"),
        ["Ram.Rank"] = E("Rank", "Rank", "Rank"),
        ["Ram.Width"] = E("Genişlik (veri / toplam)", "Width (data / total)", "Breite (Daten / gesamt)"),
        ["Ram.Channel"] = E("Kanal", "Channel", "Kanal"),
        ["Ram.Voltage"] = E("Gerilim (min / maks / yapılandırılmış)", "Voltage (min / max / configured)", "Spannung (min / max / konfiguriert)"),
        ["Ram.Usage"] = E("Bellek kullanımı", "Memory usage", "Speichernutzung"),
        ["Mem.MaxCapacityLabel"] = E("Desteklenen en yüksek kapasite", "Maximum supported capacity", "Maximal unterstützte Kapazität"),

        // ---------- Close dialog ----------
        ["Close.Title"] = E("Zelora Info kapatılsın mı?", "Close Zelora Info?", "Zelora Info schließen?"),
        ["Close.Message"] = E("Uygulamayı tamamen kapatabilir ya da sistem tepsisine küçülterek arka planda çalışmaya devam ettirebilirsiniz.", "You can exit the app completely or minimize it to the system tray and keep it running in the background.", "Sie können die App vollständig beenden oder in den Infobereich minimieren und im Hintergrund weiterlaufen lassen."),
        ["Close.ToTray"] = E("Sistem tepsisine küçült", "Minimize to tray", "In den Infobereich minimieren"),
        ["Close.Exit"] = E("Uygulamayı kapat", "Exit the app", "App beenden"),
        ["Close.Remember"] = E("Seçimimi hatırla", "Remember my choice", "Auswahl merken"),
        ["Close.RememberHint"] = E("Bu tercihi daha sonra Ayarlar › Davranış bölümünden değiştirebilirsiniz.", "You can change this later in Settings › Behavior.", "Sie können dies später unter Einstellungen › Verhalten ändern."),

        // ---------- Themes ----------
        ["Theme.Midnight"] = E("Gece Yarısı", "Midnight", "Mitternacht"),
        ["Theme.Midnight.Desc"] = E("Koyu lacivert, yüksek kontrast (varsayılan)", "Deep navy, high contrast (default)", "Tiefes Marineblau, hoher Kontrast (Standard)"),
        ["Theme.Graphite"] = E("Grafit", "Graphite", "Graphit"),
        ["Theme.Graphite.Desc"] = E("OLED dostu saf siyah, zümrüt vurgu", "OLED-friendly true black, emerald accent", "OLED-freundliches Schwarz, Smaragd-Akzent"),
        ["Theme.Aurora"] = E("Aurora", "Aurora", "Aurora"),
        ["Theme.Aurora.Desc"] = E("Koyu mor gece, pembe vurgu", "Deep violet night, pink accent", "Tiefviolette Nacht, Pink-Akzent"),
        ["Theme.Daylight"] = E("Gündüz", "Daylight", "Tageslicht"),
        ["Theme.Daylight.Desc"] = E("Ferah açık tema, mavi vurgu", "Airy light theme, blue accent", "Helles, luftiges Design, blauer Akzent"),
        ["Theme.Sand"] = E("Kum", "Sand", "Sand"),
        ["Theme.Sand.Desc"] = E("Sıcak kâğıt tonları, göz yormaz", "Warm paper tones, easy on the eyes", "Warme Papiertöne, augenschonend"),
        ["Theme.System"] = E("Sistemi izle", "Follow Windows", "Windows folgen"),
        ["Theme.System.Desc"] = E("Windows koyu ise Gece Yarısı, açık ise Gündüz", "Midnight when Windows is dark, Daylight when light", "Mitternacht bei dunklem, Tageslicht bei hellem Windows"),

        // ---------- Settings (additional) ----------
        ["Set.Appearance"] = E("Görünüm", "Appearance", "Darstellung"),
        ["Set.Language.Hint"] = E("Arayüz dili anında değişir.", "The interface language changes instantly.", "Die Oberflächensprache wechselt sofort."),
        ["Set.Theme.Hint"] = E("5 yerleşik tema veya Windows'u izleme. Seçim anında uygulanır.", "5 built-in themes or follow Windows. Applied instantly.", "5 integrierte Designs oder Windows folgen. Wird sofort übernommen."),
        ["Set.UiScale"] = E("Arayüz ölçeği", "Interface scale", "Oberflächenskalierung"),
        ["Set.UiScale.Hint"] = E("Tüm yazı ve öğeleri büyütür / küçültür (%90 – %130).", "Enlarges or shrinks all text and controls (90 % – 130 %).", "Vergrößert / verkleinert alle Texte und Elemente (90 % – 130 %)."),
        ["Set.StartPage"] = E("Açılış sayfası", "Start page", "Startseite"),
        ["Set.StartPage.Hint"] = E("Uygulama açıldığında gösterilecek sayfa.", "Page shown when the app starts.", "Seite, die beim Start angezeigt wird."),
        ["Set.Behavior"] = E("Davranış", "Behavior", "Verhalten"),
        ["Set.CloseAction"] = E("Kapat (X) düğmesi", "Close (X) button", "Schließen-Schaltfläche (X)"),
        ["Set.CloseAction.Hint"] = E("Pencere kapatıldığında ne yapılacağı.", "What happens when the window is closed.", "Was beim Schließen des Fensters geschieht."),
        ["Set.Close.Ask"] = E("Her seferinde sor", "Ask every time", "Jedes Mal fragen"),
        ["Set.Close.Tray"] = E("Sistem tepsisine küçült", "Minimize to system tray", "In den Infobereich minimieren"),
        ["Set.Close.Exit"] = E("Uygulamayı kapat", "Exit the app", "App beenden"),
        ["Set.AlwaysAdmin"] = E("Her açılışta yönetici izni iste", "Request administrator rights on every start", "Bei jedem Start Administratorrechte anfordern"),
        ["Set.AlwaysAdmin.Hint"] = E("S.M.A.R.T. verilerinin her zaman okunabilmesi için UAC onayı istenir. Reddedilirse kısıtlı modda devam edilir.", "A UAC prompt is shown so S.M.A.R.T. data is always readable. If declined, the app continues in restricted mode.", "Eine UAC-Abfrage sorgt dafür, dass S.M.A.R.T.-Daten immer lesbar sind. Bei Ablehnung läuft die App eingeschränkt weiter."),
        ["Set.Hardware"] = E("Donanım izleme (CPU / RAM / Sistem)", "Hardware monitoring (CPU / RAM / System)", "Hardwareüberwachung (CPU / RAM / System)"),
        ["Set.LiveRefresh"] = E("Canlı yenileme aralığı", "Live refresh interval", "Live-Aktualisierungsintervall"),
        ["Set.LiveRefresh.Hint"] = E("Frekans, kullanım ve bellek değerlerinin güncellenme sıklığı. Uzun aralık daha az kaynak tüketir.", "How often clocks, usage and memory values update. Longer intervals use fewer resources.", "Wie oft Takt, Auslastung und Speicherwerte aktualisiert werden. Längere Intervalle sparen Ressourcen."),
        ["Set.CpuBenchSeconds"] = E("CPU testi süresi (aşama başına)", "CPU test duration (per phase)", "CPU-Testdauer (pro Phase)"),
        ["Set.MaskSerials"] = E("Seri numaralarını maskele (ekranda ve raporlarda)", "Mask serial numbers (on screen and in reports)", "Seriennummern maskieren (Anzeige und Berichte)"),
        ["Set.Maintenance"] = E("Bakım", "Maintenance", "Wartung"),
    };
}
