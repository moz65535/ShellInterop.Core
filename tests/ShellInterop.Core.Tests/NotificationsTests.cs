using ShellInterop.Notifications;

namespace ShellInterop.Core.Tests;

// Fake backends only: nothing here shows a toast, so passing proves logic/contract, NOT real Windows display.
public class NotificationDefinitionTests
{
    [Theory]
    [InlineData("Notif ASCII", "Hello")]
    [InlineData("通知テスト", "日本語の本文です")]
    [InlineData("Special & <chars>", "a & b < c > d \"q\" 'a' &amp; literal")]
    [InlineData("Emoji \U0001F600", "pair \U0001F680 \U0001F1EF\U0001F1F5")]
    [InlineData("Multi", "line1\nline2\r\nline3\ttab")]
    public void ValidTextAccepted(string title, string body)
    {
        var d = new NotificationDefinition(title, body);
        Assert.Equal(title, d.Title);
        Assert.Equal(body, d.Body);
    }

    // Lone surrogates do not survive xunit's string serialization (attribute metadata and theory data both turn them
    // into U+FFFD), so theory rows pass an index and the strings are built inside the test.
    private static readonly string[] InvalidTexts =
    [
        "",
        "lone high " + (char)0xD800 + " end",
        "lone low " + (char)0xDC00 + " end",
        "reversed " + (char)0xDC00 + (char)0xD800,
        "ctrl \u0001 end",
        "nul \0 end",
        "nonchar \uFFFE end",
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void InvalidTextRejectedInTitleAndBody(int index)
    {
        string bad = InvalidTexts[index];
        Assert.Throws<ArgumentException>(() => new NotificationDefinition(bad, "ok"));
        Assert.Throws<ArgumentException>(() => new NotificationDefinition("ok", bad));
    }

    [Fact]
    public void NullTextRejected() => Assert.Throws<ArgumentNullException>(() => new NotificationDefinition(null!, "x"));

    [Fact]
    public void XmlIsEscapedAndRoundTrips()
    {
        var d = new NotificationDefinition("T & <t>", "a \"b\" 'c' &amp; \U0001F680");
        string xml = d.ToToastXml();
        Assert.DoesNotContain("T & <t>", xml);
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var texts = doc.Descendants("text").Select(t => t.Value).ToArray();
        Assert.Equal(["T & <t>", "a \"b\" 'c' &amp; \U0001F680"], texts);
    }
}

public class NotificationPublisherTests
{
    private sealed class FakeBackend : IToastBackend
    {
        public NotificationSettingState Setting = NotificationSettingState.Enabled;
        public Exception? SettingError;
        public Exception? ShowError;
        public List<(string Aumid, string Xml)> Shown { get; } = [];

        public NotificationSettingState GetSetting(string aumid) => SettingError is null ? Setting : throw SettingError;

        public void Show(string aumid, string xml)
        {
            if (ShowError is not null) throw ShowError;
            Shown.Add((aumid, xml));
        }
    }

    private sealed class FakeScanner(ScanResult? result = null, Exception? error = null) : IShortcutScanner
    {
        public ScanResult Scan(string aumid) => error is null ? result ?? new ScanResult([], []) : throw error;
    }

    private const string Aumid = "Test.Notifications.Unit";
    private static NotificationPublisher Make(FakeBackend b, IShortcutScanner? s = null) =>
        new(new NotificationPublisherOptions { Aumid = Aumid }, b, s ?? new FakeScanner());

    [Theory]
    [InlineData("")]
    [InlineData("NoDot")]
    [InlineData("Has Space.X")]
    [InlineData("A..B")]
    [InlineData("日本.語")]
    public void InvalidAumidRejected(string aumid) =>
        Assert.Throws<ArgumentException>(() => new NotificationPublisher(new NotificationPublisherOptions { Aumid = aumid }));

    [Fact]
    public void AumidTooLongRejected() =>
        Assert.Throws<ArgumentException>(() => new NotificationPublisher(new NotificationPublisherOptions { Aumid = "A." + new string('b', 127) }));

    [Fact]
    public void SubmittedMeansApiSuccessOnly_SettingReportedSeparately()
    {
        var b = new FakeBackend { Setting = NotificationSettingState.DisabledForUser };
        var r = Make(b).Send(new NotificationDefinition("t", "b"));
        Assert.True(r.Submitted);                                  // API ok ...
        Assert.Equal(NotificationSettingState.DisabledForUser, r.Setting); // ... independent of setting
        Assert.Equal(0, r.HResult);
        Assert.Single(b.Shown);                                    // disabled setting does not block sending
        Assert.Equal(Aumid, b.Shown[0].Aumid);
    }

    [Fact]
    public void SettingReadFailureIsUnknownNotDisabled_AndStillSends()
    {
        var b = new FakeBackend { SettingError = new InvalidOperationException("x") };
        var r = Make(b).Send(new NotificationDefinition("t", "b"));
        Assert.True(r.Submitted);
        Assert.Equal(NotificationSettingState.Unknown, r.Setting);
        Assert.NotNull(r.SettingHResult);
        Assert.Equal(NotificationSettingState.Unknown, Make(b).GetSetting());
    }

    [Fact]
    public void ShowFailureReportedWithHResultAndReason_NotThrown()
    {
        var b = new FakeBackend { ShowError = new System.Runtime.InteropServices.COMException("boom", unchecked((int)0x80070490)) };
        var r = Make(b).Send(new NotificationDefinition("t", "b"));
        Assert.False(r.Submitted);
        Assert.Equal(unchecked((int)0x80070490), r.HResult);
        Assert.Contains("COMException", r.FailureReason);
        Assert.Equal(NotificationSettingState.Enabled, r.Setting);
    }

    [Fact]
    public void SendNullThrows() => Assert.Throws<ArgumentNullException>(() => Make(new FakeBackend()).Send(null!));

    [Fact]
    public void InspectionPresent_WithExpectedTargetComparison()
    {
        var scan = new ScanResult([new ShortcutMatch(@"C:\sm\a.lnk", @"C:\App\X.exe"), new ShortcutMatch(@"C:\sm\b.lnk", @"C:\Other\Y.exe")], []);
        var i = Make(new FakeBackend(), new FakeScanner(scan)).InspectRegistration(@"c:\app\x.EXE");
        Assert.Equal(NotificationRegistrationState.ShortcutFound, i.State);
        Assert.Equal([true, false], i.Shortcuts.Select(s => s.TargetMatchesExpected));
        Assert.Null(Make(new FakeBackend(), new FakeScanner(scan)).InspectRegistration().Shortcuts[0].TargetMatchesExpected);
    }

    [Fact]
    public void InspectionMissingOnlyWhenScanCompletedCleanly()
    {
        Assert.Equal(NotificationRegistrationState.ShortcutNotFound,
            Make(new FakeBackend(), new FakeScanner(new ScanResult([], []))).InspectRegistration().State);
    }

    [Fact]
    public void InspectionUnknownOnWarningsOrScannerFailure_NeverMissing()
    {
        var warn = Make(new FakeBackend(), new FakeScanner(new ScanResult([], ["read x.lnk: denied"]))).InspectRegistration();
        Assert.Equal(NotificationRegistrationState.Unknown, warn.State);
        var boom = Make(new FakeBackend(), new FakeScanner(error: new IOException("io"))).InspectRegistration();
        Assert.Equal(NotificationRegistrationState.Unknown, boom.State);
        Assert.NotEmpty(boom.Warnings);
    }

    [Fact]
    public void InspectionFoundEvenWithWarnings()
    {
        var scan = new ScanResult([new ShortcutMatch(@"C:\sm\a.lnk", null)], ["read y.lnk: denied"]);
        var i = Make(new FakeBackend(), new FakeScanner(scan)).InspectRegistration();
        Assert.Equal(NotificationRegistrationState.ShortcutFound, i.State);
        Assert.Single(i.Warnings);
    }
}

public class NotificationAumidParityTests
{
    // Notifications duplicates Core's AUMID rule (no project reference); both must accept/reject the same inputs, unmodified.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NoDot")]
    [InlineData("Has Space.X")]
    [InlineData("A..B")]
    [InlineData(".A.B")]
    [InlineData("A.B.")]
    [InlineData(" A.B")]
    [InlineData("A.B\n")]
    [InlineData("日本.語")]
    [InlineData("Ａ.Ｂ")]
    [InlineData("A.B")]
    [InlineData("a-b_1.C-d_2.e")]
    public void SameVerdictAsCore(string? id) =>
        Assert.Equal(ShellInterop.Core.AppUserModelIdValidation.Validate(id, null) is null,
            NotificationPublisherOptions.ValidateAumid(id) is null);

    [Fact]
    public void SameLengthBoundaryAsCore()
    {
        foreach (int len in new[] { 128, 129 })
        {
            string id = "A." + new string('b', len - 2);
            Assert.Equal(ShellInterop.Core.AppUserModelIdValidation.Validate(id, null) is null,
                NotificationPublisherOptions.ValidateAumid(id) is null);
        }
    }
}

public class StaScanGateTests
{
    private static ScanGate NewGate() => new();

    [Fact]
    public void NormalCompletion_ReturnsResult_AndReleasesGate()
    {
        var gate = NewGate();
        var s = new StartMenuShortcutScanner((a, m, w) => m.Add(new ShortcutMatch(@"C:\x.lnk", null)), TimeSpan.FromSeconds(10), gate);
        Assert.Single(s.Scan("A.B").Matches);
        Assert.Single(s.Scan("A.B").Matches); // slot was released: a second scan runs normally
    }

    [Fact]
    public void ExceptionInWorker_YieldsWarning_AndReleasesGate()
    {
        var gate = NewGate();
        var bad = new StartMenuShortcutScanner((a, m, w) => throw new InvalidOperationException("x"), TimeSpan.FromSeconds(10), gate);
        Assert.Single(bad.Scan("A.B").Warnings);
        var good = new StartMenuShortcutScanner((a, m, w) => { }, TimeSpan.FromSeconds(10), gate);
        Assert.Empty(good.Scan("A.B").Warnings);
    }

    [Fact]
    public void Timeout_IsUnknown_NoThreadMultiplication_AndRecoversAfterLateCompletion()
    {
        var gate = NewGate();
        using var release = new ManualResetEventSlim();
        int started = 0;
        var hung = new StartMenuShortcutScanner((a, m, w) =>
        {
            Interlocked.Increment(ref started);
            release.Wait();
            m.Add(new ShortcutMatch(@"C:\late.lnk", null)); // late result must never be observed
        }, TimeSpan.FromMilliseconds(100), gate);
        var pub = new NotificationPublisher(new NotificationPublisherOptions { Aumid = "A.B" }, new NullToast(), hung);

        var first = pub.InspectRegistration();
        Assert.Equal(NotificationRegistrationState.Unknown, first.State);
        Assert.Contains("timed out", first.Warnings[0]);

        // Repeated and cross-instance requests while the abandoned worker lives: refused (Unknown/busy), no new worker.
        var other = new NotificationPublisher(new NotificationPublisherOptions { Aumid = "A.B" }, new NullToast(),
            new StartMenuShortcutScanner((a, m, w) => Interlocked.Increment(ref started), TimeSpan.FromSeconds(5), gate));
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(NotificationRegistrationState.Unknown, pub.InspectRegistration().State);
            var r = other.InspectRegistration();
            Assert.Equal(NotificationRegistrationState.Unknown, r.State);
            Assert.Contains("busy", r.Warnings[0]);
        }
        Assert.Equal(1, Volatile.Read(ref started));

        release.Set(); // the abandoned worker finishes late
        var ok = new StartMenuShortcutScanner((a, m, w) => { }, TimeSpan.FromSeconds(5), gate);
        ScanResult? res = null;
        SpinWait.SpinUntil(() => (res = ok.Scan("A.B")).Warnings.Count == 0, TimeSpan.FromSeconds(10));
        Assert.NotNull(res);
        Assert.Empty(res!.Warnings);
        Assert.Empty(res.Matches); // the late "C:\late.lnk" did not leak into the new inspection
    }

    private sealed class NullToast : IToastBackend
    {
        public NotificationSettingState GetSetting(string aumid) => NotificationSettingState.Unknown;
        public void Show(string aumid, string toastXml) => throw new InvalidOperationException();
    }
}

// Real STA/COM scanner against a private temp directory (never the Start Menu; nothing is written to Shell locations).
public sealed class StartMenuScannerRealComTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notif03-" + Guid.NewGuid().ToString("N"));

    public StartMenuScannerRealComTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void EmptyAndMissingDirectories_AreCleanNotFound()
    {
        var r = new StartMenuShortcutScanner([_dir, Path.Combine(_dir, "missing")]).Scan("Test.Notifications.Unit");
        Assert.Empty(r.Matches);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void UnreadableShortcut_ProducesWarning_SoResultIsUnknownNotMissing()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.lnk"), "not a shortcut");
        var scanner = new StartMenuShortcutScanner([_dir]);
        var r = scanner.Scan("Test.Notifications.Unit");
        Assert.Empty(r.Matches);
        Assert.Single(r.Warnings);
        var pub = new NotificationPublisher(new NotificationPublisherOptions { Aumid = "Test.Notifications.Unit" }, new NullBackend(), scanner);
        Assert.Equal(NotificationRegistrationState.Unknown, pub.InspectRegistration().State);
    }

    private sealed class NullBackend : IToastBackend
    {
        public NotificationSettingState GetSetting(string aumid) => NotificationSettingState.Unknown;
        public void Show(string aumid, string toastXml) => throw new InvalidOperationException("must not be called");
    }
}
