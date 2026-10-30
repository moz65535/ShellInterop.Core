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

    public int CreateTasks(IReadOnlyList<JumpTaskDefinition> tasks, out ITaskCollectionHandle? collection, out int failedTaskIndex)
        => ShellLinkTaskBuilder.Build(tasks, out collection, out failedTaskIndex);

    public int AddUserTasks(ITaskCollectionHandle collection)
        => Call(l => l.AddUserTasks(((ComTaskCollection)collection).Array));

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
                hr = AddTask(result, tasks[i]);
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
