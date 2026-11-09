using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ShellInterop.Core.Interop;
using ShellInterop.Core.JumpLists;
using ShellInterop.Core.JumpLists.Native;

namespace ShellInterop.Core.Tests;

public class InteropAndArchitectureTests
{
    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private static string[] DeclaredMethods(Type t)
        => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(m => m.MetadataToken).Select(m => m.Name).ToArray();

    // ---- COM definition consistency (values read from the Windows SDK 10.0.26100.0 headers, see API-CONTRACT-REVIEW.md)

    [Fact]
    public void ComGuids_MatchSdkHeaders()
    {
        Assert.Equal(new Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), typeof(ICustomDestinationList).GUID);
        Assert.Equal(new Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"), typeof(IObjectArray).GUID);
        Assert.Equal(new Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), typeof(IObjectCollection).GUID);
        Assert.Equal(new Guid("000214F9-0000-0000-C000-000000000046"), typeof(IShellLinkW).GUID);
        Assert.Equal(new Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), typeof(IPropertyStore).GUID);
        Assert.Equal(typeof(ICustomDestinationList).GUID, ShellGuids.IidCustomDestinationList);
        Assert.Equal(typeof(IObjectArray).GUID, ShellGuids.IidObjectArray);
        Assert.Equal(typeof(IObjectCollection).GUID, ShellGuids.IidObjectCollection);
        Assert.Equal(typeof(IShellLinkW).GUID, ShellGuids.IidShellLinkW);
        Assert.Equal(typeof(IPropertyStore).GUID, ShellGuids.IidPropertyStore);
        Assert.Equal(new Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6"), ShellGuids.ClsidDestinationList);
        Assert.Equal(new Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a"), ShellGuids.ClsidEnumerableObjectCollection);
        Assert.Equal(new Guid("00021401-0000-0000-C000-000000000046"), ShellGuids.ClsidShellLink);
    }

    [Fact]
    public void VtableOrder_MatchesSdkHeaders()
    {
        Assert.Equal(["SetAppID", "BeginList", "AppendCategory", "AppendKnownCategory", "AddUserTasks", "CommitList", "GetRemovedDestinations", "DeleteList", "AbortList"],
            DeclaredMethods(typeof(ICustomDestinationList)));
        Assert.Equal(["GetCount", "GetAt"], DeclaredMethods(typeof(IObjectArray)));
        // IObjectCollection : IObjectArray in the header; managed COM interop needs the base slots redeclared first.
        Assert.Equal(["GetCount", "GetAt", "AddObject", "AddFromArray", "RemoveObjectAt", "Clear"], DeclaredMethods(typeof(IObjectCollection)));
        Assert.DoesNotContain(typeof(IObjectArray), typeof(IObjectCollection).GetInterfaces());
        Assert.Equal(["GetPath", "GetIDList", "SetIDList", "GetDescription", "SetDescription", "GetWorkingDirectory", "SetWorkingDirectory", "GetArguments", "SetArguments",
                "GetHotkey", "SetHotkey", "GetShowCmd", "SetShowCmd", "GetIconLocation", "SetIconLocation", "SetRelativePath", "Resolve", "SetPath"],
            DeclaredMethods(typeof(IShellLinkW)));
        Assert.Equal(["GetCount", "GetAt", "GetValue", "SetValue", "Commit"], DeclaredMethods(typeof(IPropertyStore)));
    }

    [Fact]
    public void AllComMethods_PreserveHResults()
    {
        foreach (Type t in new[] { typeof(ICustomDestinationList), typeof(IObjectArray), typeof(IObjectCollection), typeof(IShellLinkW), typeof(IPropertyStore) })
        {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.True(m.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.PreserveSig), $"{t.Name}.{m.Name}");
                Assert.Equal(typeof(int), m.ReturnType);
            }
        }
    }

    /// <summary>Cross-checks the hand-written interop against the SDK header text itself when the SDK is installed.</summary>
    [Fact]
    public void Interop_MatchesSdkHeaderText_WhenSdkIsInstalled()
    {
        string? um = FindSdkUmDirectory();
        if (um is null)
        {
            return; // SDK not present on this machine; the hard-coded checks above still apply.
        }

        string core = File.ReadAllText(Path.Combine(um, "ShObjIdl_core.h"));
        string objArray = File.ReadAllText(Path.Combine(um, "ObjectArray.h"));
        string propsys = File.ReadAllText(Path.Combine(um, "propsys.h"));

        (Guid Iid, string[] Methods) Parse(string text, string name)
        {
            var m = Regex.Match(text, @"MIDL_INTERFACE\(""([0-9a-fA-F-]+)""\)\s*" + name + @"\s*:\s*public\s+(\w+)\s*\{(.*?)\n\s*\};", RegexOptions.Singleline);
            Assert.True(m.Success, name);
            var names = Regex.Matches(m.Groups[3].Value, @"STDMETHODCALLTYPE\s+(\w+)").Select(x => x.Groups[1].Value).ToArray();
            return (Guid.Parse(m.Groups[1].Value), names);
        }

        var cdl = Parse(core, "ICustomDestinationList");
        Assert.Equal(typeof(ICustomDestinationList).GUID, cdl.Iid);
        Assert.Equal(DeclaredMethods(typeof(ICustomDestinationList)), cdl.Methods);

        var sl = Parse(core, "IShellLinkW");
        Assert.Equal(typeof(IShellLinkW).GUID, sl.Iid);
        Assert.Equal(DeclaredMethods(typeof(IShellLinkW)), sl.Methods);

        var oa = Parse(objArray, "IObjectArray");
        Assert.Equal(typeof(IObjectArray).GUID, oa.Iid);
        Assert.Equal(DeclaredMethods(typeof(IObjectArray)), oa.Methods);

        var oc = Parse(objArray, "IObjectCollection");
        Assert.Equal(typeof(IObjectCollection).GUID, oc.Iid);
        Assert.Equal("IObjectArray", Regex.Match(objArray, @"IObjectCollection\s*:\s*public\s+(\w+)").Groups[1].Value);
        Assert.Equal(DeclaredMethods(typeof(IObjectCollection)), oa.Methods.Concat(oc.Methods).ToArray());

        var ps = Parse(propsys, "IPropertyStore");
        Assert.Equal(typeof(IPropertyStore).GUID, ps.Iid);
        Assert.Equal(DeclaredMethods(typeof(IPropertyStore)), ps.Methods);

        Assert.Matches(@"DECLSPEC_UUID\(""77f10cf0-3db5-4966-b520-b7c54fd35ed6""\)\s*DestinationList;", core);
        Assert.Matches(@"DECLSPEC_UUID\(""2d3468c1-36a7-43b6-ac24-d3f02fd9607a""\)\s*EnumerableObjectCollection;", core);
        Assert.Matches(@"DECLSPEC_UUID\(""00021401-0000-0000-C000-000000000046""\)\s*ShellLink;", core);
    }

    private static string? FindSdkUmDirectory()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Include");
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.GetDirectories(root).OrderDescending()
            .Select(d => Path.Combine(d, "um"))
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "ShObjIdl_core.h")) && File.Exists(Path.Combine(d, "ObjectArray.h")) && File.Exists(Path.Combine(d, "propsys.h")));
    }

    // ---- T17 real part: in-memory Shell objects only (CLSID_ShellLink + CLSID_EnumerableObjectCollection). No Jump List is touched.

    [Fact]
    public void T17_RealShellLinkCollection_RoundTripsFields_AndReleasesCleanly()
    {
        OnSta(() =>
        {
            var a1 = Samples.Task("A1") with { Description = "desc A1" };
            var a2 = Samples.Task("A2") with { Description = null, WorkingDirectory = null };

            int hr = ShellLinkTaskBuilder.Build([a1, a2], out ITaskCollectionHandle? handle, out int failedIndex);
            Assert.Equal(0, hr);
            Assert.Equal(-1, failedIndex);
            var coll = (ComTaskCollection)handle!;

            Assert.Equal(0, coll.Collection.GetCount(out uint count));
            Assert.Equal(2u, count);

            Assert.Equal(0, coll.Collection.GetAt(0, in ShellGuids.IidShellLinkW, out object? first));
            var link = (IShellLinkW)first!;
            var sb = new StringBuilder(520);

            Assert.Equal(0, link.GetPath(sb, sb.Capacity, 0, 0));
            Assert.Equal(a1.ApplicationPath, sb.ToString(), ignoreCase: true);
            sb.Clear(); Assert.Equal(0, link.GetArguments(sb, sb.Capacity));
            Assert.Equal(a1.Arguments, sb.ToString());
            sb.Clear(); Assert.Equal(0, link.GetDescription(sb, sb.Capacity));
            Assert.Equal("desc A1", sb.ToString());
            sb.Clear(); Assert.Equal(0, link.GetWorkingDirectory(sb, sb.Capacity));
            Assert.Equal(a1.WorkingDirectory, sb.ToString(), ignoreCase: true);
            sb.Clear(); Assert.Equal(0, link.GetIconLocation(sb, sb.Capacity, out int iconIndex));
            Assert.Equal(a1.IconResourcePath, sb.ToString(), ignoreCase: true);
            Assert.Equal(0, iconIndex);

            var store = (IPropertyStore)first!;
            Assert.Equal(0, store.GetValue(in ShellGuids.PkeyTitle, out PropVariant title));
            Assert.Equal("Task A1", title.AsString());
            NativeMethods.PropVariantClear(ref title);

            // Order inside the collection is the order of insertion.
            Assert.Equal(0, coll.Collection.GetAt(1, in ShellGuids.IidShellLinkW, out object? second));
            Assert.Equal(0, ((IPropertyStore)second!).GetValue(in ShellGuids.PkeyTitle, out PropVariant t2));
            Assert.Equal("Task A2", t2.AsString());
            NativeMethods.PropVariantClear(ref t2);

            handle!.Dispose();
            Assert.True(coll.IsDisposed);
            Assert.Throws<InvalidComObjectException>(() => link.GetArguments(sb, sb.Capacity)); // RCW is gone
            handle.Dispose(); // second dispose is a no-op (no double release)
        });
    }

    [Fact]
    public void T17_ShellLinkBuilder_EmptyList_YieldsEmptyCollection()
    {
        OnSta(() =>
        {
            Assert.Equal(0, ShellLinkTaskBuilder.Build([], out ITaskCollectionHandle? handle, out _));
            var coll = (ComTaskCollection)handle!;
            Assert.Equal(0, coll.Collection.GetCount(out uint count));
            Assert.Equal(0u, count);
            handle!.Dispose();
        });
    }

    [Fact]
    public void ProcessAppId_ReadIsSafe_AndSetRejectsForbiddenIdsBeforeTouchingTheShell()
    {
        _ = ProcessAppIdentity.TryGet(); // read-only
        Assert.Throws<ArgumentException>(() => ProcessAppIdentity.Set("Has Space.X"));
        Assert.Throws<ArgumentException>(() => ProcessAppIdentity.Set("Other.App", id => id.StartsWith("ShellInterop.Core.Poc.", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => ProcessAppIdentity.Set(""));
    }

    // ---- T19 UI-framework independence

    [Fact]
    public void T19_CoreAssembly_ReferencesNoUiFrameworkOrThirdPartyAssembly()
    {
        string[] forbidden = ["Avalonia", "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms", "WinRT", "Microsoft.Windows", "Microsoft.WindowsAppSDK", "Microsoft.UI", "JumpLists", "Newtonsoft"];
        var refs = typeof(JumpListPublisher).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.All(refs, r => Assert.DoesNotContain(forbidden, f => r.StartsWith(f, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => (a.GetName().Name ?? "").StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void T19_CoreProjectFile_HasNoPackagesOrUiFrameworkSwitches()
    {
        string? dir = Environment.GetEnvironmentVariable("SHELLINTEROP_SOURCE_ROOT") ?? AppContext.BaseDirectory;
        string? csproj = null;
        while (dir is not null && csproj is null)
        {
            string candidate = Path.Combine(dir, "src", "ShellInterop.Core", "ShellInterop.Core.csproj");
            csproj = File.Exists(candidate) ? candidate : null;
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(csproj);
        string xml = File.ReadAllText(csproj!);
        Assert.DoesNotContain("<PackageReference", xml);
        Assert.DoesNotContain("<ProjectReference", xml);
        Assert.DoesNotContain("<UseWPF>", xml);
        Assert.DoesNotContain("<UseWindowsForms>", xml);
        Assert.DoesNotContain("Avalonia", xml);
        Assert.DoesNotContain("WindowsAppSDK", xml);
    }

    [Fact]
    public void SplitAssemblies_AreOneWay_AndJumpListTypesAreOnlyInJumpList()
    {
        var core = typeof(ProcessAppIdentity).Assembly;
        var jump = typeof(JumpListPublisher).Assembly;
        Assert.Equal("ShellInterop.Core", core.GetName().Name);
        Assert.Equal("ShellInterop.JumpList", jump.GetName().Name);
        Assert.DoesNotContain(core.GetReferencedAssemblies(), a => a.Name == "ShellInterop.JumpList");
        Assert.Contains(jump.GetReferencedAssemblies(), a => a.Name == "ShellInterop.Core");
        Assert.DoesNotContain(core.GetTypes(), t => t.Namespace?.StartsWith("ShellInterop.Core.JumpLists", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(core.GetTypes(), t => t.Name == "ICustomDestinationList");
    }

    [Theory]
    [InlineData("", "AppId.Empty")]
    [InlineData("Bad Name.App", "AppId.InvalidFormat")]
    [InlineData("OneSegment", "AppId.InvalidFormat")]
    public void ProcessAppIdentity_RejectsInvalidIdsWithSameCodeAsJumpList(string id, string code)
    {
        var error = Assert.Single(JumpListValidator.ValidateAppId(id));
        Assert.Equal(code, error.Code);
        Assert.Contains(code, Assert.Throws<ArgumentException>(() => ProcessAppIdentity.Set(id)).Message);
    }

    [Fact]
    public void T19_PublicApi_ExposesNoComTypes()
    {
        var publicTypes = typeof(JumpListPublisher).Assembly.GetExportedTypes();
        Assert.DoesNotContain(publicTypes, t => t.IsImport
            || t.Namespace?.Split('.').Contains("Interop") == true
            || t.Namespace?.Split('.').Contains("Native") == true);
        Assert.Contains(typeof(IJumpListPublisher), publicTypes);
        foreach (MethodInfo m in typeof(IJumpListPublisher).GetMethods())
        {
            Assert.All(m.GetParameters(), p => Assert.DoesNotContain("Dispatcher", p.ParameterType.Name));
        }
    }
}
