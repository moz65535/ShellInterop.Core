using System.Runtime.InteropServices;
using ShellInterop.Core.Interop;

namespace ShellInterop.Core.JumpLists.Native;

/// <summary>Real ICustomDestinationList wrapper. Owns the list RCW and the removed-destinations array from BeginList.</summary>
internal sealed class ShellJumpListNative : IJumpListNative
{
    private ICustomDestinationList? _list;
    private object? _removedDestinations;

    public ShellJumpListNative(ICustomDestinationList list) => _list = list;

    public int SetAppId(string appId) => Call(l => l.SetAppID(appId));

    public int BeginList()
    {
        int hr = Call(l =>
        {
            // riid = IObjectArray: the removed-destinations array. It is released in Dispose.
            int h = l.BeginList(out _, in ShellGuids.IidObjectArray, out object? removed);
            _removedDestinations = removed;
            return h;
        });
        return hr;
    }

    public int CreateTasks(IReadOnlyList<JumpListEntry> tasks, out ITaskCollectionHandle? collection, out int failedTaskIndex)
        => ShellLinkTaskBuilder.BuildEntries(tasks, out collection, out failedTaskIndex);

    public int AddUserTasks(ITaskCollectionHandle collection)
        => Call(l => l.AddUserTasks(((ComTaskCollection)collection).Array));

    public int CreateDestinations(IReadOnlyList<JumpTaskDefinition> links, out ITaskCollectionHandle? collection, out int failedIndex)
        => ShellLinkTaskBuilder.Build(links, out collection, out failedIndex);

    public int AppendCategory(string name, ITaskCollectionHandle collection)
        => Call(l => l.AppendCategory(name, ((ComTaskCollection)collection).Array));

    private const int LinkBufferChars = 32768;

    public int ReadRemovedDestinations(out IReadOnlyList<RawRemovedDestination> items)
    {
        var result = new List<RawRemovedDestination>();
        items = result;
        if (_removedDestinations is not IObjectArray array)
        {
            return Hr.Unexpected;
        }

        try
        {
            int hr = array.GetCount(out uint count);
            if (Hr.Failed(hr))
            {
                return hr;
            }

            for (uint i = 0; i < count; i++)
            {
                result.Add(ReadElement(array, i));
            }

            return Hr.Ok;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException or MarshalDirectiveException)
        {
            return Hr.FromException(ex);
        }
    }

    private static RawRemovedDestination ReadElement(IObjectArray array, uint index)
    {
        object? obj = null;
        try
        {
            if (Hr.Succeeded(array.GetAt(index, in ShellGuids.IidShellLinkW, out obj)) && obj is IShellLinkW link)
            {
                // Anything but a clean S_OK (S_FALSE = no file-system path, truncation) means the identity is not trustworthy.
                var path = new System.Text.StringBuilder(LinkBufferChars);
                var args = new System.Text.StringBuilder(LinkBufferChars);
                if (link.GetPath(path, LinkBufferChars, 0, 0) == Hr.Ok
                    && link.GetArguments(args, LinkBufferChars) == Hr.Ok
                    && path.Length < LinkBufferChars - 1 && args.Length < LinkBufferChars - 1)
                {
                    return new RawRemovedDestination(RawRemovedKind.Link, path.ToString(), args.ToString());
                }

                return new RawRemovedDestination(RawRemovedKind.Unreadable);
            }

            ComRelease.Final(obj);
            obj = null;
            if (Hr.Succeeded(array.GetAt(index, in ShellGuids.IidShellItem, out obj)) && obj is not null)
            {
                return new RawRemovedDestination(RawRemovedKind.ShellItem);
            }

            return new RawRemovedDestination(RawRemovedKind.Unreadable);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException or MarshalDirectiveException)
        {
            return new RawRemovedDestination(RawRemovedKind.Unreadable);
        }
        finally
        {
            ComRelease.Final(obj);
        }
    }

    public int CommitList() => Call(l => l.CommitList());

    public int AbortList() => Call(l => l.AbortList());

    public int DeleteList(string appId) => Call(l => l.DeleteList(appId));

    public void Dispose()
    {
        ComRelease.Final(_removedDestinations);
        _removedDestinations = null;
        ComRelease.Final(_list);
        _list = null;
    }

    private int Call(Func<ICustomDestinationList, int> action)
    {
        if (_list is null)
        {
            return Hr.Unexpected;
        }

        try
        {
            return action(_list);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException or MarshalDirectiveException)
        {
            return Hr.FromException(ex);
        }
    }
}

/// <summary>Shell links plus the EnumerableObjectCollection that holds them. Dispose is idempotent (no double release).</summary>
internal sealed class ComTaskCollection : ITaskCollectionHandle
{
    private readonly List<object> _links = [];
    private IObjectCollection? _collection;
    private bool _disposed;

    public ComTaskCollection(IObjectCollection collection) => _collection = collection;

    /// <summary>The same COM object viewed as IObjectArray (QueryInterface through the RCW).</summary>
    public IObjectArray Array => (IObjectArray)(_collection ?? throw new ObjectDisposedException(nameof(ComTaskCollection)));

    internal IObjectCollection Collection => _collection ?? throw new ObjectDisposedException(nameof(ComTaskCollection));

    internal void TrackLink(object link) => _links.Add(link);

    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (object link in _links)
        {
            ComRelease.Final(link);
        }

        _links.Clear();
        ComRelease.Final(_collection);
        _collection = null;
    }
}

/// <summary>
/// Builds the IObjectCollection of IShellLinkW tasks. Touches only in-memory shell objects (CLSID_ShellLink and
/// CLSID_EnumerableObjectCollection); it never reads or writes any Jump List, so it is safe to exercise in tests.
/// </summary>
internal static class ShellLinkTaskBuilder
{
    public static int Build(IReadOnlyList<JumpTaskDefinition> tasks, out ITaskCollectionHandle? handle, out int failedTaskIndex)
        => BuildEntries(tasks.Select(JumpListEntry.ForTask).ToArray(), out handle, out failedTaskIndex);

    public static int BuildEntries(IReadOnlyList<JumpListEntry> tasks, out ITaskCollectionHandle? handle, out int failedTaskIndex)
    {
        handle = null;
        failedTaskIndex = -1;

        int hr = ComActivator.Create(ShellGuids.ClsidEnumerableObjectCollection, ShellGuids.IidObjectCollection, out object? coll);
        if (Hr.Failed(hr))
        {
            return hr;
        }

        var result = new ComTaskCollection((IObjectCollection)coll!);
        try
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                hr = tasks[i].IsSeparator ? AddSeparator(result) : AddTask(result, tasks[i].Task!);
                if (Hr.Failed(hr))
                {
                    failedTaskIndex = i;
                    result.Dispose();
                    return hr;
                }
            }

            handle = result;
            return Hr.Ok;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException or MarshalDirectiveException)
        {
            result.Dispose();
            return Hr.FromException(ex);
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static int AddSeparator(ComTaskCollection target)
    {
        int hr = ComActivator.Create(ShellGuids.ClsidShellLink, ShellGuids.IidShellLinkW, out object? obj);
        if (Hr.Failed(hr)) return hr;
        target.TrackLink(obj!);
        var store = (IPropertyStore)obj!;
        var value = PropVariant.FromBoolean(true);
        try { hr = store.SetValue(in ShellGuids.PkeyIsDestListSeparator, in value); }
        finally { NativeMethods.PropVariantClear(ref value); }
        if (Hr.Failed(hr) || Hr.Failed(hr = store.Commit())) return hr;
        return target.Collection.AddObject(obj!);
    }

    private static int AddTask(ComTaskCollection target, JumpTaskDefinition task)
    {
        int hr = ComActivator.Create(ShellGuids.ClsidShellLink, ShellGuids.IidShellLinkW, out object? obj);
        if (Hr.Failed(hr))
        {
            return hr;
        }

        target.TrackLink(obj!);   // released with the collection, even if configuring it fails below
        var link = (IShellLinkW)obj!;

        if (Hr.Failed(hr = link.SetPath(task.ApplicationPath))
            || Hr.Failed(hr = link.SetArguments(task.Arguments))
            || Hr.Failed(hr = link.SetIconLocation(task.IconResourcePath, task.IconResourceIndex))
            || (task.Description is not null && Hr.Failed(hr = link.SetDescription(task.Description)))
            || (task.WorkingDirectory is not null && Hr.Failed(hr = link.SetWorkingDirectory(task.WorkingDirectory))))
        {
            return hr;
        }

        // The display name lives in System.Title on the link's property store (same RCW; cast is a QueryInterface).
        var store = (IPropertyStore)obj!;
        var title = PropVariant.FromString(task.Title);
        try
        {
            hr = store.SetValue(in ShellGuids.PkeyTitle, in title);
        }
        finally
        {
            NativeMethods.PropVariantClear(ref title);
        }

        if (Hr.Failed(hr) || Hr.Failed(hr = store.Commit()))
        {
            return hr;
        }

        return target.Collection.AddObject(obj!);
    }
}
