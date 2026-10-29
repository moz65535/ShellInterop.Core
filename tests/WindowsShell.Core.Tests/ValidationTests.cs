using WindowsShell.Core.JumpLists;

namespace WindowsShell.Core.Tests;

public class ValidationTests
{
    private static readonly JumpListPublisherOptions NoFs = Samples.Options();

    private static IReadOnlyList<JumpListValidationError> Check(JumpTaskDefinition task)
        => JumpListValidator.Validate(new JumpListDefinition(Samples.AppId, [task]), NoFs);

    // T01
    [Fact]
    public void T01_ValidDefinition_HasNoErrors()
        => Assert.Empty(JumpListValidator.Validate(Samples.Definition("A1", "A2"), NoFs));

    // T02
    [Theory]
    [InlineData(null, "AppId.Null")]
    [InlineData("", "AppId.Empty")]
    [InlineData("Has Space.Product", "AppId.InvalidFormat")]
    [InlineData("NoDotSegment", "AppId.InvalidFormat")]
    [InlineData("A..B", "AppId.InvalidFormat")]
    [InlineData(".A.B", "AppId.InvalidFormat")]
    [InlineData("A.B.", "AppId.InvalidFormat")]
    [InlineData("Bad/Char.Product", "AppId.InvalidFormat")]
    [InlineData("Jump\tList.Product", "AppId.InvalidFormat")]
    public void T02_InvalidAppId_IsRejected(string? appId, string code)
    {
        var errors = JumpListValidator.Validate(new JumpListDefinition(appId, []), NoFs);
        Assert.Contains(errors, e => e.Code == code);
    }

    [Fact]
    public void T02_TooLongAppId_IsRejected_ButExactly128IsAccepted()
    {
        string ok = "Aa." + new string('b', 125);
        Assert.Equal(128, ok.Length);
        Assert.Empty(JumpListValidator.ValidateAppId(ok));
        Assert.Contains(JumpListValidator.ValidateAppId(ok + "c"), e => e.Code == "AppId.TooLong");
    }

    // T03
    [Theory]
    [InlineData(null, "ApplicationPath.Empty")]
    [InlineData("", "ApplicationPath.Empty")]
    [InlineData(@"relative\host.exe", "ApplicationPath.NotAbsolute")]
    [InlineData(@"host.exe", "ApplicationPath.NotAbsolute")]
    [InlineData(@"C:\a\..\b.exe", "ApplicationPath.NotAbsolute")]
    [InlineData(@"\\?\C:\a.exe", "ApplicationPath.DevicePath")]
    [InlineData(@"\\.\pipe\x.exe", "ApplicationPath.DevicePath")]
    [InlineData(@"C:\a|b.exe", "ApplicationPath.InvalidChars")]
    [InlineData(@"C:\a\b.txt", "ApplicationPath.NotExe")]
    [InlineData("C:\\a\\b.exe\0", "ApplicationPath.InvalidChars")]
    public void T03_InvalidExePath_IsRejected(string? path, string code)
        => Assert.Contains(Check(Samples.Task("x") with { ApplicationPath = path! }), e => e.Code == code && e.TaskIndex == 0);

    [Fact]
    public void T03_TooLongExePath_IsRejected()
        => Assert.Contains(Check(Samples.Task("x") with { ApplicationPath = @"C:\" + new string('a', 300) + ".exe" }), e => e.Code == "ApplicationPath.TooLong");

    [Fact]
    public void T03_MissingExe_IsRejected_WhenExistenceRequired()
    {
        var options = new JumpListPublisherOptions { RequireExistingFiles = true };
        string missing = Path.Combine(Path.GetTempPath(), "definitely-missing-" + Guid.NewGuid().ToString("N") + ".exe");
        var task = Samples.Task("x") with { ApplicationPath = missing, IconResourcePath = missing, WorkingDirectory = null };
        var errors = JumpListValidator.Validate(new JumpListDefinition(Samples.AppId, [task]), options);
        Assert.Contains(errors, e => e.Code == "ApplicationPath.NotFound");
        Assert.Contains(errors, e => e.Code == "IconResourcePath.NotFound");
    }

    [Fact]
    public void T03_ExistingFiles_Pass_WhenExistenceRequired()
    {
        string exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var task = Samples.Task("x") with { ApplicationPath = exe, IconResourcePath = exe, WorkingDirectory = Environment.SystemDirectory };
        Assert.Empty(JumpListValidator.Validate(new JumpListDefinition(Samples.AppId, [task]), new JumpListPublisherOptions { RequireExistingFiles = true }));
    }

    // T04
    [Theory]
    [InlineData(null, "Title.Null")]
    [InlineData("", "Title.Empty")]
    [InlineData("   ", "Title.Empty")]
    [InlineData("line1\nline2", "Title.ControlChar")]
    [InlineData("nul\0inside", "Title.ControlChar")]
    public void T04_InvalidTitle_IsRejected(string? title, string code)
        => Assert.Contains(Check(Samples.Task("x") with { Title = title! }), e => e.Code == code);

    [Fact]
    public void T04_OtherFieldRules()
    {
        Assert.Contains(Check(Samples.Task("x") with { Title = new string('t', 257) }), e => e.Code == "Title.TooLong");
        Assert.Contains(Check(Samples.Task("x") with { Description = new string('d', 1024) }), e => e.Code == "Description.TooLong");
        Assert.Contains(Check(Samples.Task("x") with { Arguments = "" }), e => e.Code == "Arguments.Empty");
        Assert.Contains(Check(Samples.Task("x") with { Arguments = null! }), e => e.Code == "Arguments.Null");
        Assert.Contains(Check(Samples.Task("x") with { Arguments = new string('a', 2049) }), e => e.Code == "Arguments.TooLong");
        Assert.Contains(Check(Samples.Task("x") with { WorkingDirectory = @"rel\dir" }), e => e.Code == "WorkingDirectory.NotAbsolute");
        Assert.Contains(Check(Samples.Task("x") with { IconResourcePath = "" }), e => e.Code == "IconResourcePath.Empty");
        Assert.Empty(Check(Samples.Task("x") with { Description = null, WorkingDirectory = null }));
    }

    [Fact]
    public void T04_NullTaskEntry_And_TooManyTasks_AreRejected()
    {
        Assert.Contains(JumpListValidator.Validate(new JumpListDefinition(Samples.AppId, [null]), NoFs), e => e.Code == "Task.Null");
        var many = Enumerable.Range(0, 101).Select(i => Samples.Task(i.ToString()));
        Assert.Contains(JumpListValidator.Validate(new JumpListDefinition(Samples.AppId, many), NoFs), e => e.Code == "Tasks.TooMany");
        Assert.Contains(JumpListValidator.Validate(null, NoFs), e => e.Code == "Definition.Null");
    }

    // T05 (data side)
    [Fact]
    public void T05_Definition_SnapshotsInputAndPreservesOrder()
    {
        var source = new List<JumpTaskDefinition?> { Samples.Task("C"), Samples.Task("A"), Samples.Task("B") };
        var def = new JumpListDefinition(Samples.AppId, source);
        source.Clear();
        Assert.Equal(["Task C", "Task A", "Task B"], def.Tasks.Select(t => t!.Title));
        Assert.Throws<NotSupportedException>(() => ((IList<JumpTaskDefinition?>)def.Tasks).Add(null));
    }
}
