# USAGE-AVALONIA

WindowsShell.Core の公開 API だけを使って、Avalonia アプリに Jump List の **Tasks** を登録する手順。
WindowsShell.Core は Avalonia 専用ではなく、WPF など他の .NET アプリでも同じ呼び出しで使える（UI フレームワーク非依存）。

この文書のコードは、公開型・公開コンストラクタ・公開メソッドだけで書かれ、`WindowsOnly`（`net10.0-windows`）と `CrossPlat`（`net10.0;net10.0-windows`）の
2 構成でビルド（`dotnet build` のみ。起動も実 Shell 操作もしていない）して警告 0・エラー 0 を確認済み。実行時の挙動は、単体テスト（フェイク Shell）と
隔離された Windows 検証環境での Tasks 2 件の登録 1 回の実機確認（後述）の範囲でしか確認していない。

## 0. 先に知っておくこと

- 実 Shell で確認済みなのは、検証用ホストによる **Tasks 2 件の登録（ApplyAsync 1 回）** だけ（ゲスト VM 上）。
- 実 Shell **未検証**: 空 Tasks による消去、`DeleteListAsync`、別 Tasks セットへの置換、Jump List タスクからのアプリ起動、再起動後の永続性。
- 扱えるのは Tasks のみ。カテゴリ、Recent/Frequent、区切り線は未実装。
- `JumpListApplyResult.ShellState == Applied` は **Shell API が成功を返した**という意味で、タスクバーでの表示を確認したという意味ではない（`VisuallyConfirmed` は常に false）。

## 1. 前提

| 項目 | 内容 |
|---|---|
| .NET | .NET 10 SDK |
| ライブラリの TFM | `net10.0-windows`（Windows 専用。クロスプラットフォームではない） |
| 参照方法 | 現時点は `ProjectReference` のみ（NuGet パッケージは未提供） |
| Avalonia | サンプルは Avalonia / Avalonia.Desktop 12.1.4 でビルド確認 |
| AUMID | アプリ固有の ID を 1 つ決める。`Company.Product[.Sub]` 形式、128 文字以内、空白なし、各セグメントは ASCII 英数字・`-`・`_` |

アプリ側の csproj（Windows 専用ビルドの場合）:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Avalonia" Version="12.1.4" />
  <PackageReference Include="Avalonia.Desktop" Version="12.1.4" />
  <ProjectReference Include="..\path\to\WindowsShell.Core\WindowsShell.Core.csproj" />
</ItemGroup>
```

## 2. 全体の流れ

1. `Main` の最初（ウィンドウ生成前）で `ProcessAppIdentity.Set(appId)` を呼ぶ。
2. `JumpListPublisher` を 1 つ作る（STA ワーカースレッドが 1 本起動する。アプリ中は使い回す）。
3. `ApplyAsync(new JumpListDefinition(appId, tasks))` を `await` し、`JumpListApplyResult` を確認する。
4. 終了時に `ShutdownAsync` を待ち、完了したら `Dispose`。

## 3. サンプルコード

Windows 固有のコードは `#if WINDOWS` の中に閉じ込めている（`net10.0-windows` ターゲットでは SDK が `WINDOWS` を自動定義する）。Windows 専用ビルドだけなら `#if` は不要。

### Program.cs

```csharp
using Avalonia;

namespace JumpListSample;

internal static class Program
{
    // Use your own ID: CompanyName.ProductName, at most 128 chars, no spaces.
    internal const string AppId = "YourCompany.YourApp";

    /// <summary>True when the process AppUserModelID was set, so Jump List work is allowed.</summary>
    internal static bool IdentityReady { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
#if WINDOWS
        // Must run before the first window is created and before any Jump List work.
        IdentityReady = WindowsIntegration.TrySetProcessIdentity(AppId);
#endif
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
```

AUMID の設定失敗の扱い:
- `ProcessAppIdentity.Set` は、不正な ID（形式・長さ）では `ArgumentException` を投げ、Shell には到達しない。
- Shell API が失敗した場合は負の HRESULT を返す。
- どちらの場合も、サンプルは Jump List 登録をスキップしてアプリを続行する。既定の `RequireProcessAppIdMatch = true` により、プロセスの AUMID が定義と一致しないまま `ApplyAsync` を呼んでも実 Shell 呼び出しの前に `ValidationFailed` で拒否される。

### WindowsIntegration.cs

```csharp
#if WINDOWS
using System.Runtime.InteropServices;
using WindowsShell.Core;
using WindowsShell.Core.JumpLists;

namespace JumpListSample;

/// <summary>Everything that touches WindowsShell.Core lives here, so other targets never see it.</summary>
internal static class WindowsIntegration
{
    public static bool TrySetProcessIdentity(string appId)
    {
        try
        {
            int hr = ProcessAppIdentity.Set(appId);
            if (hr < 0)
            {
                Console.Error.WriteLine($"SetCurrentProcessExplicitAppUserModelID failed: 0x{unchecked((uint)hr):X8}");
                return false;
            }

            return true;
        }
        catch (ArgumentException ex)   // invalid AppUserModelID: rejected before reaching the Shell
        {
            Console.Error.WriteLine("Invalid AppUserModelID: " + ex.Message);
            return false;
        }
        catch (COMException ex)
        {
            Console.Error.WriteLine("AppUserModelID call failed: " + ex.Message);
            return false;
        }
    }
}

internal sealed class JumpListService
{
    private readonly IJumpListPublisher _publisher = new JumpListPublisher();   // starts one STA worker; keep one instance

    public async Task RegisterAsync(string appId)
    {
        string exe = Environment.ProcessPath!;
        var definition = new JumpListDefinition(appId,
        [
            new JumpTaskDefinition
            {
                Title = "New window",
                ApplicationPath = exe,
                Arguments = "--new-window",
                IconResourcePath = exe,
                IconResourceIndex = 0,
                WorkingDirectory = AppContext.BaseDirectory.TrimEnd('\\'),
            },
            new JumpTaskDefinition
            {
                Title = "Settings",
                Description = "Open the settings",
                ApplicationPath = exe,
                Arguments = "--settings",
                IconResourcePath = exe,
            },
        ]);

        JumpListApplyResult result = await _publisher.ApplyAsync(definition);
        if (!result.Succeeded)
        {
            // Not fatal for the app. ShellState says whether the stored list may have changed.
            Console.Error.WriteLine(
                $"Jump List {result.Status}: phase={result.FailurePhase} hr={result.Error?.HResultText ?? "n/a"} shell={result.ShellState}");
        }
    }

    /// <summary>Returns true when the worker has stopped and every COM object is released.</summary>
    public async Task<bool> ShutdownAsync()
    {
        JumpListShutdownResult result = await _publisher.ShutdownAsync(TimeSpan.FromSeconds(10));
        if (result.Completed)
        {
            _publisher.Dispose();   // returns immediately: the worker has already exited
        }

        return result.Completed;
    }
}
#endif
```

タスクの必須項目: `Title`、`ApplicationPath`（存在する絶対パスの `.exe`）、`Arguments`（空でない。Shell の要件）、`IconResourcePath`（存在する絶対パス）。
`Description`、`WorkingDirectory`、`IconResourceIndex`（既定 0）は任意。順序は Shell まで保たれる。1 回の `ApplyAsync` は、その AUMID の Tasks 全体を置き換える。

### App.cs

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace JumpListSample;

public class App : Application
{
#if WINDOWS
    private JumpListService? _jumpList;
    private bool _shutdownDone;
#endif

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window { Title = "Jump List sample" };
#if WINDOWS
            if (Program.IdentityReady)
            {
                _jumpList = new JumpListService();
                desktop.ShutdownRequested += OnShutdownRequested;
                _ = RegisterJumpListAsync();
            }
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

#if WINDOWS
    private async Task RegisterJumpListAsync()
    {
        try
        {
            await _jumpList!.RegisterAsync(Program.AppId);
        }
        catch (Exception ex)   // fire-and-forget: never leave an unobserved exception
        {
            Console.Error.WriteLine("Jump List registration failed: " + ex.Message);
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownDone || _jumpList is null)
        {
            return;
        }

        // Do not block the UI thread: hold the shutdown, wait for the COM worker, then shut down again.
        e.Cancel = true;
        _ = ShutdownWhenSafeAsync((IClassicDesktopStyleApplicationLifetime)sender!);
    }

    private async Task ShutdownWhenSafeAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            await _jumpList!.ShutdownAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Jump List shutdown failed: " + ex.Message);
        }
        finally
        {
            _shutdownDone = true;      // exit even if the worker did not stop in time (it is a background thread)
            desktop.Shutdown();
        }
    }
#endif
}
```

## 4. 結果の読み方（`JumpListApplyResult`）

| メンバー | 意味 |
|---|---|
| `Succeeded` | `Status == Success`。Shell API が最後まで成功を返した |
| `Status` | `Success` / `ValidationFailed`（Shell に到達せず）/ `BeginFailed` / `TaskCreationFailed` / `AddTasksFailed` / `CommitFailed` / `AbortFailed` / `DeleteFailed` / `UnknownState` / `Cancelled` / `PublisherDisposed` |
| `FailurePhase` | 失敗した段階（`Validation`, `ProcessAppIdCheck`, `CreateList`, `SetAppId`, `BeginList`, `CreateTask`, `AddUserTasks`, `CommitList`, `AbortList`, `DeleteList`, `Unexpected`、成功時 `None`） |
| `HResult` / `Error.HResultText` | 主な失敗の HRESULT。得られない場合は null（推測しない） |
| `ValidationErrors` | 入力検証エラーのコードと、対象タスクのインデックス |
| `ShellState` | `Unchanged`＝変更は確定していない／`Applied`＝Shell API 成功（**目視確認ではない**）／`Unknown`＝`CommitList` 失敗など、保存済みリストが変わったか不明 |
| `VisuallyConfirmed` | 常に false。ライブラリはタスクバーを観測しない |

`ShellState == Unknown` のときは、次回起動時などに改めて `ApplyAsync` で全置換すれば、望ましい状態へ揃えられる。

## 5. 消去と削除

```csharp
// Replace the Tasks with an empty list (clear). Not yet verified against the real Shell.
await publisher.ApplyAsync(new JumpListDefinition(appId, []));

// Remove the whole custom Jump List of an AppUserModelID (cleanup/uninstall only). Not yet verified against the real Shell.
await publisher.DeleteListAsync(appId);
```

どちらも単体テスト（フェイク Shell）でのみ確認済みで、実 Shell では未検証。

## 6. 終了処理

- `ShutdownAsync(timeout)` は、新しい要求の受付を止め、開始前の要求を `PublisherDisposed` で完了させ、実行中の COM 処理の完了と STA ワーカーの終了を待つ。
- `Completed == false` は「まだ終わっていない」（Shell の状態は不明）。強制中断はしない。
- `Dispose()` は同期のベストエフォート停止で、実行中の処理が長引くと最大 `DisposeTimeout`（既定 5 秒）呼び出しスレッドを止める。UI スレッドでは `ShutdownAsync` を使い、`Completed` になってから `Dispose` する。
- Avalonia では `IClassicDesktopStyleApplicationLifetime.ShutdownRequested` で `e.Cancel = true` として終了を保留し、待機後に `Shutdown()` を呼ぶ（サンプルのとおり）。`Shutdown()` で `ShutdownRequested` が再度発生しうるため、フラグで 2 回目を素通りさせる。
- ワーカーはバックグラウンドスレッドなので、止まらなくてもプロセス終了は妨げない。

## 7. Windows 以外の OS・クロスプラットフォームアプリ

WindowsShell.Core は `net10.0-windows` のみを対象とし、他 OS では動かない。参照条件は実際の `dotnet build` で次のとおり確認した（確認時点の `WindowsShell.Core` は 0.1.0。初回公開版は 0.1.0-preview.1 で、参照条件に関わるTFMは変更していない）。

| 参照側の構成 | 結果（実ビルド） |
|---|---|
| `net10.0-windows` のアプリが直接 `ProjectReference` | 成功（警告 0・エラー 0） |
| `net10.0;net10.0-windows` のマルチターゲットで、Windows ターゲットだけ条件付き参照 | 両ターゲット成功（警告 0・エラー 0）。`net10.0` の出力に `WindowsShell.Core.dll` は含まれない |
| `net10.0`（Windows 指定なし）のアプリが直接 `ProjectReference` | **失敗 NU1201**（Core は `net10.0-windows7.0` のみ対応） |

マルチターゲットの csproj:

```xml
<PropertyGroup>
  <TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>
</PropertyGroup>

<!-- Only the Windows target references Core; the net10.0 target never sees it. -->
<ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows'">
  <ProjectReference Include="..\path\to\WindowsShell.Core\WindowsShell.Core.csproj" />
</ItemGroup>
```

Core を使うコードは `#if WINDOWS` で囲むか、Windows ターゲットだけでコンパイルされるファイルに分ける。`net10.0-windows` ターゲットで作った実行ファイルが Windows 以外で実行されることは想定しない。
`net10.0` ターゲットでは Core が参照されないため、Jump List は何もしない（サンプルの `#if WINDOWS` が外れる）。
`net10.0-windows` は、コンパイルと参照の互換性を表す指定であり、Windows 7 のサポートを意味しない（README の「OS support」参照）。

## 8. 利用上の注意

- WindowsShell.Core 本体には、署名付き承認や VM 隔離のような書込み制御はない。`JumpListPublisher` は渡された AppUserModelID の実 Jump List に書き込むため、いつ何を書くかは呼び出し側のアプリが決める。
- 一般アプリでは `JumpListPublisher` がそのまま実 Shell（ユーザーの Jump List）を操作する。いつ・どの AUMID に対して書くかは呼び出し側が管理する（`RequireProcessAppIdMatch` と `AppIdFilter` オプションで誤操作を減らせる）。
- 開発中のビルドで製品と同じ AUMID を使うと、実際の Jump List を書き換える。開発用には別の AUMID を使う。
- AUMID は最初のウィンドウ生成前に設定する。ライブラリは自動設定しない。
- `Delete` はアンインストール・クリーンアップ用で、グループ切替には使わない（切替は `ApplyAsync` の全置換）。
