using System.Runtime.ExceptionServices;
using System.Text;
using ShellInterop.Core.Interop;
using ShellInterop.Core.JumpLists;
using ShellInterop.Core.JumpLists.Native;

namespace ShellInterop.Core.Tests;

public class SeparatorTests
{
    private static JumpListEntry Task(string id) => JumpListEntry.ForTask(Samples.Task(id));
    private static JumpListEntry Sep() => JumpListEntry.Separator();

    [Fact]
    public void LegacyNullTasksConstructor_RemainsUnambiguous()
    {
        var definition = new JumpListDefinition(Samples.AppId, null);
        Assert.Empty(definition.Tasks);
        Assert.Empty(definition.Entries);
    }

    [Fact]
    public async Task MixedEntries_AreOrdered_AndTaskCountExcludesSeparators()
    {
        var entries = new[] { Sep(), Task("A"), Sep(), Sep(), Task("B"), Sep() };
        var definition = JumpListDefinition.FromEntries(Samples.AppId, entries);
        entries[1] = Task("changed");
        Assert.Equal(["Task A", "Task B"], definition.Tasks.Select(x => x!.Title));
        Assert.Equal([true, false, true, true, false, true], definition.Entries.Select(e => e!.IsSeparator));
        Assert.Empty(JumpListValidator.Validate(definition, Samples.Options()));
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);
        var result = await publisher.ApplyAsync(definition);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.RequestedTaskCount);
        Assert.Equal([true, false, true, true, false, true], shell.LastEntries.Select(e => e.IsSeparator));
        Assert.Equal(["Task A", "Task B"], shell.Store.Select(t => t.Title));
        Assert.Equal(["Create", "SetAppId", "BeginList", "CreateTasks", "AddUserTasks", "CommitList", "DisposeTasks", "DisposeList"], shell.Calls);
    }

    [Fact]
    public async Task SeparatorOnly_IsNotEmptyInput_AndKeepsZeroTaskCount()
    {
        var definition = JumpListDefinition.FromEntries(Samples.AppId, [Sep(), Sep()]);
        Assert.Empty(definition.Tasks);
        Assert.Empty(JumpListValidator.Validate(definition, Samples.Options()));
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);
        var result = await publisher.ApplyAsync(definition);
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.RequestedTaskCount);
        Assert.Contains("AddUserTasks", shell.Calls);
        Assert.All(shell.LastEntries, e => { Assert.True(e.IsSeparator); Assert.Null(e.Task); });
    }

    [Fact]
    public void Limit_CountsOnlyTasks_AndInvalidEntriesUseOrderedIndex()
    {
        var hundred = Enumerable.Range(0, 100).Select(i => Task(i.ToString())).Concat(Enumerable.Range(0, 120).Select(_ => Sep())).ToArray();
        Assert.Empty(JumpListValidator.Validate(JumpListDefinition.FromEntries(Samples.AppId, hundred), Samples.Options()));
        Assert.Contains(JumpListValidator.Validate(JumpListDefinition.FromEntries(Samples.AppId, hundred.Append(Task("overflow"))), Samples.Options()), e => e.Code == "Tasks.TooMany");
        var invalid = JumpListDefinition.FromEntries(Samples.AppId, [Sep(), null, Task("A")]);
        Assert.Contains(JumpListValidator.Validate(invalid, Samples.Options()), e => e.Code == "Entry.Null" && e.TaskIndex == 1);
    }

    [Fact]
    public async Task FailedSeparatorCreation_AbortsAndPropagatesIndexAndHResult()
    {
        var shell = new FakeShell { CreateTasksHr = Hresults.AccessDenied, FailTaskIndex = 1 };
        using var publisher = Samples.Publisher(shell);
        var result = await publisher.ApplyAsync(JumpListDefinition.FromEntries(Samples.AppId, [Task("A"), Sep(), Task("B")]));
        Assert.Equal(JumpListApplyStatus.TaskCreationFailed, result.Status);
        Assert.Equal(JumpListFailurePhase.CreateTask, result.FailurePhase);
        Assert.Equal(Hresults.AccessDenied, result.HResult);
        Assert.Equal(1, result.Error!.TaskIndex);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.Contains("AbortList", shell.Calls);
        Assert.DoesNotContain("AddUserTasks", shell.Calls);
        Assert.DoesNotContain("CommitList", shell.Calls);
    }

    [Fact]
    public void InMemoryCom_UsesBooleanSeparatorPropertyAndPreservesOrder()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var entries = new[] { Sep(), Task("A"), Sep(), Sep(), Task("B"), Sep() };
                Assert.Equal(0, ShellLinkTaskBuilder.BuildEntries(entries, out var handle, out int failed));
                Assert.Equal(-1, failed);
                using (handle!)
                {
                    var coll = ((ComTaskCollection)handle!).Collection;
                    Assert.Equal(0, coll.GetCount(out uint count));
                    Assert.Equal((uint)entries.Length, count);
                    for (uint i = 0; i < count; i++)
                    {
                        Assert.Equal(0, coll.GetAt(i, in ShellGuids.IidShellLinkW, out object? obj));
                        var link = (IShellLinkW)obj!;
                        var store = (IPropertyStore)obj!;
                        Assert.Equal(0, store.GetValue(in ShellGuids.PkeyIsDestListSeparator, out PropVariant value));
                        if (entries[i].IsSeparator)
                        {
                            Assert.Equal(PropVariant.VtBool, value.Vt);
                            Assert.Equal(0xffff, unchecked((ushort)(long)value.Data1));
                            var path = new StringBuilder(520);
                            link.GetPath(path, path.Capacity, 0, 0);
                            Assert.Equal("", path.ToString());
                            var args = new StringBuilder(520);
                            Assert.Equal(0, link.GetArguments(args, args.Capacity));
                            Assert.Equal("", args.ToString());
                        }
                        else
                        {
                            var args = new StringBuilder(520);
                            Assert.Equal(0, link.GetArguments(args, args.Capacity));
                            Assert.Equal(entries[i].Task!.Arguments, args.ToString());
                            Assert.Equal(0, store.GetValue(in ShellGuids.PkeyTitle, out PropVariant title));
                            Assert.Equal(entries[i].Task!.Title, title.AsString());
                            NativeMethods.PropVariantClear(ref title);
                        }
                        NativeMethods.PropVariantClear(ref value);
                    }
                }
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
