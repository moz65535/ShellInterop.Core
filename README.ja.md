# ShellInterop.Core

[English](README.md) | 日本語

Windows 専用で UI フレームワークに依存しない、3 つの .NET ライブラリです。

- `ShellInterop.Core`: プロセスの AppUserModelID（AUMID）と Win32 の共通基盤。
- `ShellInterop.JumpList`: ジャンプリスト（Tasks、カスタムカテゴリ）の公開と Shell COM の実装。Core を参照します。
- `ShellInterop.Notifications`: Toast 通知の送信（Windows SDK .NET 投影を使用し、Windows App SDK は使いません）と、スタートメニューのショートカットの読み取り専用検査。Core / JumpList は参照しません。

> **プレリリースの分割版です。** ソースツリーの版は Core / JumpList が 0.2.0-preview.1（旧 Core コードからの破壊的な分割版。0.1.0-preview.1 の旧 Core 内部ビルドは NuGet.org では公開していません）、Notifications が 0.1.0-preview.1 です。
> 英語版 README が公開上の主な入口で、この文書は日本語の利用者・保守者向けの対応文書です。内容は英語版と矛盾しないようにしますが、逐語訳ではありません。API 名・コード・識別子は原文のままです。

これらのライブラリは独自に開発されたもので、Microsoft の製品ではありません。

## 1. 動作環境と依存関係

- .NET 10。`net10.0-windows`（またはそれ以降の Windows 向け）のプロジェクト。
- アプリ固有の AppUserModelID（例: `Company.Product`。128 文字以内、空白なし、各セグメントは ASCII 英数字・`-`・`_`）。
- `ShellInterop.Core` と `ShellInterop.JumpList` は `net10.0-windows` のみが対象です。素の `net10.0` プロジェクトからは参照できません（NU1201 / NU1202）。クロスプラットフォームのアプリは `net10.0;net10.0-windows` のマルチターゲットにして、Windows ターゲットからだけ参照します。
- `ShellInterop.Notifications` は `net10.0-windows10.0.19041.0` が対象です（Windows SDK .NET 投影がバージョン付きの Windows TFM を必要とするため）。利用側も `net10.0-windows10.0.19041.0` 以上が必要で、素の `net10.0-windows` では NU1201 になります（WPF の一時プロジェクトで確認。Avalonia の利用側はビルドしていません）。
- 動作 OS: Windows 11 を主対象とします。正式な最低 OS 要件は決めておらず、OS バージョンのチェックも実装していません。`net10.0-windows` は Windows 7 のサポートを意味しません。他の OS バージョンでの動作は未検証です。

## 2. ビルドと導入

```
dotnet restore ShellInterop.Core.sln
dotnet build ShellInterop.Core.sln -c Release
dotnet test ShellInterop.Core.sln -c Release
```

テストはフェイク Shell を使い、実際のジャンプリストには書き込みません（メモリ内の COM Shell オブジェクトを作るテストはあります）。

現時点では `ProjectReference` で参照します。

- ジャンプリスト: `src/ShellInterop.JumpList/ShellInterop.JumpList.csproj`（Core を推移的に参照。`ProcessAppIdentity` を直接使うなら Core も明示的に参照してよい）。
- 通知: `src/ShellInterop.Notifications/ShellInterop.Notifications.csproj`。

NuGet パッケージ ID は `ShellInterop.Core`、`ShellInterop.JumpList`、`ShellInterop.Notifications` で、Core と JumpList は 0.2.0-preview.1 です（JumpList は `ShellInterop.Core >= 0.2.0-preview.1` に依存）。`ShellInterop.Notifications` は 0.1.0-preview.1 の独立パッケージで、パッケージ用 README と LICENSE を同梱します。3 つとも隔離環境でローカル pack を確認しています。NuGet パッケージの公開は法的承認ではなく、公開と、それを使うアプリの配布は別の問題です。

JumpList プロジェクトを旧 0.1.0-preview.1 のまま pack することはガードで止めてあります（古い Core には分割後に必要な内部型がないため）。現在の 0.2.0-preview.1 は両プロジェクトとも pack できます。他の版をローカル試験する場合は両プロジェクトに同じ `-p:Version=...` を渡し、その試験版は公開しないでください。

旧 `ShellInterop.Core` はジャンプリスト API を含んでいました。分割で公開型は `ShellInterop.JumpList` アセンブリに移りましたが、名前空間 `ShellInterop.Core.JumpLists` とメソッドシグネチャは変わりません。移行時は `ShellInterop.JumpList`（と、`ProcessAppIdentity` を使うなら Core）への参照を追加して再ビルドします。型フォワーダーはなく、コンパイル済みの既存利用側とのバイナリ互換は保証しません。

## 3. 最小限の使用例

### ジャンプリスト（Tasks）

```csharp
using ShellInterop.Core;
using ShellInterop.Core.JumpLists;

// 1. Main の最初（ウィンドウ生成前）でプロセスの AppUserModelID を設定する（不正な ID は ArgumentException）。
int hr = ProcessAppIdentity.Set("YourCompany.YourApp");   // hr < 0: Shell 呼び出しの失敗。ジャンプリスト登録はスキップする

// 2. publisher はアプリに 1 つ（STA ワーカースレッドが 1 本起動する）。
var publisher = new JumpListPublisher();

// 3. Tasks を置き換える（呼ぶたびにリスト全体が置換される）。
string exe = Environment.ProcessPath!;
JumpListApplyResult result = await publisher.ApplyAsync(new JumpListDefinition("YourCompany.YourApp",
[
    new JumpTaskDefinition { Title = "New window", ApplicationPath = exe, Arguments = "--new-window", IconResourcePath = exe },
]));

// 4. 結果を確認する。ShellState.Applied は「Shell API が成功した」意味で、タスクバーで誰かが見たという意味ではない。
if (!result.Succeeded)
{
    Console.Error.WriteLine($"{result.Status} phase={result.FailurePhase} hr={result.Error?.HResultText} shell={result.ShellState}");
}

// 5. 終了時: ワーカーの完了を待ってから Dispose する。
JumpListShutdownResult shutdown = await publisher.ShutdownAsync(TimeSpan.FromSeconds(10));
if (shutdown.Completed) publisher.Dispose();
```

### 通知（Toast）

```csharp
using ShellInterop.Notifications;

var publisher = new NotificationPublisher(new NotificationPublisherOptions { Aumid = "YourCompany.YourApp" });

// A. スタートメニューのショートカットの静的・読み取り専用検査（ShortcutFound / ShortcutNotFound / Unknown）。
NotificationRegistrationInspection registration = publisher.InspectRegistration(Environment.ProcessPath);

// B. Windows の通知設定（Unknown は「読めなかった」であり「無効」ではない）。
NotificationSettingState setting = publisher.GetSetting();

// C. Toast を送る。OS 側の失敗では例外を投げず、結果で返す。
NotificationSendResult sent = publisher.Send(new NotificationDefinition("Title", "Body"));
Console.WriteLine($"registration={registration.State} setting={sent.Setting} submitted={sent.Submitted} hr=0x{sent.HResult:X8}");
```

より詳しい手順（Avalonia のサンプル、エラー処理、UI フレームワーク内での終了処理）は [docs/USAGE-AVALONIA.md](docs/USAGE-AVALONIA.md)（日本語）にあります。

## 4. 主な公開 API

| アセンブリ | 主な型 | 役割 |
|---|---|---|
| Core | `ProcessAppIdentity`（`Set` / `TryGet`） | プロセスの明示 AUMID の設定・取得 |
| JumpList | `JumpListPublisher` / `IJumpListPublisher`（`ApplyAsync`・`DeleteListAsync`・`ShutdownAsync`・`State`） | ジャンプリストの適用・削除・安全な終了 |
| JumpList | `JumpListDefinition`（コンストラクタ、`FromEntries`、`FromEntriesWithCategories`）、`JumpListEntry`（`ForTask`・`ForKeyedTask`・`Separator`）、`JumpTaskDefinition` | 入力モデル |
| JumpList | `JumpCategoryDefinition`、`JumpDestinationDefinition`、`JumpRemovedDestinationTracking` | カスタムカテゴリと削除済み宛先の追跡 |
| JumpList | `JumpListApplyResult`、`JumpListPublisherOptions` | 結果、安全オプション（`RequireProcessAppIdMatch` など） |
| Notifications | `NotificationPublisher`（`Send`・`GetSetting`・`InspectRegistration`）、`NotificationPublisherOptions`、`NotificationDefinition`（`Title`・`Body`・`Silent`・`LaunchUri`） | Toast 送信と登録状態の検査 |
| Notifications | `NotificationSendResult`、`NotificationSettingState`、`NotificationRegistrationInspection` | 結果・設定状態・検査結果 |

COM の型は公開していません。

## 5. COM・Shell 操作の注意事項

- `JumpListPublisher` は、渡した AUMID の **実際のジャンプリスト** を書き換えます。承認や隔離のための仕組みはライブラリにはなく、いつ・何を書くかは呼び出し側が決めます。開発中のビルドで製品と同じ AUMID を使うと、実際のジャンプリストを書き換えます。開発用には別の AUMID を使ってください。
- AUMID は最初のウィンドウ生成前に設定します。自動設定はしません。既定の `RequireProcessAppIdMatch = true` により、プロセスの AUMID が要求と一致しない `ApplyAsync` は COM 呼び出しの前に拒否されます。
- COM 処理は専用の STA ワーカースレッドで行います。アプリを閉じる前に `await publisher.ShutdownAsync(timeout)` を待ってください。`Completed == false` は「まだ終わっていない（Shell の状態は不明）」で、強制中断はしません。`Dispose()` は同期のベストエフォートです。UI スレッドでは `ShutdownAsync` を使います。
- `DeleteListAsync` はアンインストール・クリーンアップ用です。グループ切替には使わず、切替は `ApplyAsync` の全置換で行います。
- `ApplyAsync` の成功（`Succeeded`、`ShellState == Applied`）は Shell API の成功であって、表示の確認ではありません（`VisuallyConfirmed` は常に false）。
- 区切り線（`JumpListEntry.Separator()`）: Windows は先頭・末尾の区切り線を無視し、隣り合う区切り線の一方を抑止します。ライブラリは入力順をそのまま渡し、正規化しません。

詳しい結果モデルは [docs/ERROR-MODEL.md](docs/ERROR-MODEL.md)、構造は [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)、COM 宣言の確認方法は [docs/API-CONTRACT-REVIEW.md](docs/API-CONTRACT-REVIEW.md) を参照してください（いずれも日本語）。

## 6. JumpList の既知の表示制限

登録と表示は別です。`ApplyAsync` が成功しても、要求したすべてのカテゴリが登録されたとは限らず（`E_ACCESSDENIED` 後の `TasksFallback` やカテゴリの省略）、登録された宛先がすべて表示されるとも限りません。

SHELL-JL-08 の表示件数の実測（検証用 VM、1 つの Windows ビルド、既定設定）では、すべての `AppendCategory` と `CommitList` が成功したのに、Shell は送った件数より少なく表示しました。

- 5×6、5×3、3×6、5×5 のカテゴリ構成で、いずれも 12 件の宛先が表示されました（5×5 は 3 つ目のカテゴリの途中で切れました）。
- 宛先 1 件のカテゴリでは 11 カテゴリが表示されました（12 や 13 を要求しても 11）。
- 削除済み宛先が 1 件ある 5×6 でも 11 件でした。理由（画面の高さ、削除済み宛先の扱い、その他）は特定できておらず、画面解像度への依存という仮説も未検証です。11 件表示そのものは観測されましたが、他の環境で同じ結果になる保証はなく、`JumpListItems_Maximum` との対応式も分かっていません。
- 生の `BeginList` は、その VM で `pcMinSlots = 10` を返しました。これは「最小スロット数」で、表示上限ではありません。

重要な宛先は最初のカテゴリに置いてください。これは 1 つの環境での観測で、文書化された固定の上限ではありません。

### `JumpListItems_Maximum`

Shell は表示するとき、`Explorer\Advanced`（HKCU と HKLM）の DWORD 値 `JumpListItems_Maximum` を読みます。

- 値を変えると、リストの再適用・Explorer の再起動・アプリの再起動なしで反映されました。
- この VM では HKCU が HKLM より優先されました（双方向で確認: HKLM=20 と HKCU=5 で 4 件、HKLM=5 と HKCU=20 で 18 件）。HKLM だけでも効きました。したがって HKCU に値がなくても HKLM の値が有効なまま残ることがあります。
- HKCU と HKLM の両方の値を削除すると、既定の表示に戻りました。
- 5×6 の例: 値 20 で 18 件、5 で 4 件、未設定で 12 件。**値と表示件数の対応式は分かっていません**。値は表示件数の保証ではありません。確認できていない対応式を推測で補ってはいけません。
- `RuntimeBroker.exe` が表示のたびにこの値を参照するという観察は保守者によるもので、記録は残っていません。
- ライブラリはこの値を読み書きせず、入力の件数も切り詰めません。

## 7. Notifications の責務と制限

`ShellInterop.Notifications` が行うのは、タイトルと本文の Toast 送信、Windows の通知設定の取得、AUMID を持つスタートメニューのショートカットがあるかの **読み取り専用の検査** です。ショートカットやレジストリ値の作成・編集・修復・削除は一切しません。

詳細は [docs/ja/NOTIFICATIONS.md](docs/ja/NOTIFICATIONS.md) にあります。要点は次のとおりです。

- 送信（`Send`）の `Submitted == true` は、Windows の送信 API 呼び出しが成功したという意味だけです。Toast が見えたことは意味しません。
- `Silent = true` は Toast の XML に `<audio silent="true"/>` を付けるだけです。実際に無音になるかは実機で確認していません。
- `LaunchUri` は、本文クリック時に Windows が起動する絶対 URI を XML に出力するだけです。**プロトコルの登録、スタートメニューのショートカットの作成、AUMID の設定、クリックの受信処理は、すべてアプリ側の責務です。** `LaunchUri` を指定しただけでこれらが完成するわけではありません。受け取った URI は信頼できない入力として検証してください。
- 実装していないもの: クリック処理そのもの、COM アクティベーターの登録、アクションボタン、通知履歴、Toast の置換・削除、ショートカットの自動登録、OS 登録の自動修復。
- 登録直後の Toast が表示されなかった例（Windows が新しい AUMID 登録を拾うまでの遅れの可能性。原因と必要な待ち時間は未確認）や、ショートカット削除直後も表示された例が観測されています。

### Windows App SDK 非依存

Windows App SDK の通知コンポーネントには依存していません。次の 4 つを混同しないでください。

| 名前 | このライブラリとの関係 |
|---|---|
| Windows App SDK（NuGet パッケージ、`AppNotificationManager`、ネイティブのブリッジ DLL） | **使いません**。以前の Tracker のビルドで使っていましたが、撤去されました |
| Windows App Runtime（OS 側のフレームワークパッケージ） | このライブラリは使いません。検証対象のアプリでは Windows App SDK の通知関連モジュールをロードしておらず、検証 VM の OS には Windows App Runtime が存在していました。その有無が結果に影響するかは **未検証**: 入っていないマシンでの動作と、すべての Windows 環境での非依存性は未確認 |
| Windows SDK .NET 投影（`Microsoft.Windows.SDK.NET.dll`） | **使います**。利用側アプリに同梱されます |
| `WinRT.Runtime.dll`（C#/WinRT） | 同じ投影パッケージの一部として使います。Windows App SDK ではありません |

## 8. 検証状況

検証の種類を区別してください。単体テスト（フェイク）の成功は、実 Shell で確認済みという意味ではありません。

| 証拠 | 結果 |
|---|---|
| 単体テスト（フェイク Shell、メモリ内 COM、SDK ヘッダーとの照合） | `tests/ShellInterop.Core.Tests` に含まれる（SHELL-JL-06-VERIFY で 158 件成功。この件数は Notifications のテスト追加前のもので、今回は再測定していない）。ジャンプリストには触れない |
| 実 Shell、分割後のアセンブリ（区切り線なし） | SHELL-SPLIT-02: apply・操作・RunItem・delete・再登録を検証用 VM で通過 |
| 実 Shell、単一のカスタムカテゴリ | SHELL-JL-05: 確認済み |
| 実 Shell、複数カテゴリ・削除済み宛先の追跡・`E_ACCESSDENIED` 時の `TasksFallback`・`TasksFallback` 内の区切り線 | SHELL-JL-07（Windows 11 VM）: 条件付き PASS。実際の `E_ACCESSDENIED`（プライバシー設定オフ）が最初のカテゴリで発生し、フォールバック・カテゴリの省略・回復を確認 |
| 実 Shell、区切り線の表示 | SHELL-JL-02: このライブラリの公開 API を使うスタンドアロンのテストアプリで確認 |
| 実 Shell、追加確認（Windows 11 Home 10.0.26300 VM） | SHELL-JL-08: 条件付き PASS。削除済み宛先は Pending・後続の Apply・プライバシー設定のオフ/オン・Explorer 再起動のいずれでも再表示されなかった。トランザクション中にプライバシー設定を切り替えて起こした後続カテゴリの `E_ACCESSDENIED` で、`AbortList`・新しい `BeginList`・Tasks のみのコミットになった。ピン留めと Explorer 再起動は、試した範囲で結果を変えなかった |
| Notifications、実 Windows 11（SHELL-NOTIF-04、専用 AUMID、VM） | 登録検査（前・後・削除後）、Toast 表示、通知センターへの残存を通過。登録直後の注意点は 7 章 |
| Notifications の `Silent` と `LaunchUri`、実 Windows 11 VM（SHELL-NOTIF-07-D-VERIFY）: CONDITIONAL_PASS | AI Usage Tracker（framework-dependent、.NET 10 Desktop Runtime、HKCU プロトコル登録、AUMID 付きのアプリ単位ショートカット）で使用。確認: Toast 表示、通知センターへの残存、最小化中・他ウィンドウの背面（2 回）・トレイ（非表示ウィンドウ、1 回のみ）からのクリック復帰、アプリ終了後のクリック起動（2 回）、Windows App SDK モジュールのロードなし、Windows 設定で通知をオフ→オンにした回復。未検証または一部のみ: `Silent` の実聴、一部の失敗経路、Windows App Runtime のないマシン、他の Windows ビルド。1 アプリ・1 VM であり、他のアプリへの保証ではない。このビルドには未コミットの `ShellInterop` 変更が含まれており、コミット済みリリースの検証ではない |
| Notifications、クリーンな Release 再生成（SHELL-NOTIF-10）: PASS | 静的・ビルドの結果で、実 Shell の結果ではない。Tracker のリリース ZIP（19 ファイル、Release / win-x64 / framework-dependent、.NET SDK 10.0.402）を、コミット済みソースのみ（ShellInterop 1aa7e48、Tracker a55f539）から再生成し、Windows App SDK 通知への依存が 0 であることを確認（ファイル名・`deps.json`・バイナリ文字列）。バイト単位の完全再現は要求せず、一致もしていない |
| Notifications、新 Release ZIP の実 Windows 11 VM 最小スモーク（SHELL-NOTIF-10-VM-SMOKE）: PASS（最小スモーク） | Windows 11 Home build 26300、1 ユーザー、1 台の VM、1 回の実行。上の ZIP を新しいフォルダーから起動し、Toast バナーの表示と通知センターへの残存、最小化したウィンドウの Toast クリックによる復帰（新フォルダーの EXE から起動した短命な 2 つ目のプロセス〔約 0.65 秒〕のあと、実行中のインスタンスが前面化）、2 つ目のインスタンスが残らないこと、Windows App SDK モジュールのロードがないことを確認。引き継ぎ（IPC）は直接観測せず、プロセスとウィンドウ状態のログから推定した。繰り返したのはこの最小セットのみで、07-D-VERIFY の他の項目は再実行していない。未検証: 実聴、失敗注入、他の Windows ビルド、Windows App Runtime のないマシン、他のアプリ・環境 |

**実 Shell で未検証（ジャンプリスト）:** 空の Tasks によるクリア、OS 再起動後の永続性、`E_ACCESSDENIED` が後続カテゴリだけで自然に発生するケース（JL-08 は誘発）、ピン留め項目と `TasksFallback` や削除との組み合わせ、削除済み宛先があるときの 11 件表示になった原因（11 件表示の観測自体は確認済み。他の環境での再現、画面解像度への依存、`JumpListItems_Maximum` との関係は未検証または不明）、他の Windows ビルド。

## 9. ライセンスと再配布の現状

MIT License、Copyright (c) 2026 moz。正式な文面は [LICENSE](LICENSE)（英語）です。翻訳で置き換えません。

MIT を採用しても、第三者が持つ権利は与えられません。次の 2 点は未解決で、このライセンスでは解決しません。

- Windows SDK ヘッダー由来の COM 宣言（GUID、vtable 配置）の許諾状況。
- AI の支援を受けて作ったコードの権利確認。

`Microsoft.Windows.SDK.NET.dll` と `WinRT.Runtime.dll` の再配布について（事実のみで、法的な結論ではありません）:

- このリポジトリにあるのは MIT の `LICENSE` だけで、第三者通知のファイルはありません。
- ソースツリーとローカルの nupkg には、どちらの DLL も含まれていません（SHELL-LEGAL-03、SHELL-PACK-02/03 で確認）。ただし `ShellInterop.Notifications` を使うアプリは、この 2 ファイルを同梱して配布することになるため、そのアプリが Microsoft の再配布条件を自分で満たす必要があります。
- Tracker のリリース ZIP には `THIRD-PARTY-NOTICES.md` と Microsoft の Windows 10 SDK のライセンス文が同梱され、その通知は Microsoft の Windows SDK 再配布リストがこの 2 ファイルを挙げていると記載しています。
- Tracker ZIP について再配布条件を満たしているかは **確認できていません**。詳しくは [docs/PUBLICATION-CHECKLIST.md](docs/PUBLICATION-CHECKLIST.md)（英語）を参照してください。

COM 宣言の出典（Windows SDK のどのヘッダー由来か、独自実装との区別、未確定の点）は [docs/COM-PROVENANCE.md](docs/COM-PROVENANCE.md)（英語）に記録しています。出典を記録しても、権利関係が解決したことにはなりません。

公開準備の状況（GitHub のソース、NuGet、アプリの ZIP）は [docs/PUBLICATION-CHECKLIST.md](docs/PUBLICATION-CHECKLIST.md) にまとめています。Microsoft への問い合わせは、公開の一律の必須条件ではありません（確実性を高めたいときの任意の手段です）。ただし、Microsoft のファイルを再配布する場合は Microsoft のライセンス条件が適用され、これは法的助言でも許可でもありません。

ドキュメント整備の完了は、ライセンス問題の解決を意味しません。

## 10. 詳細資料

| 文書 | 内容 | 言語 |
|---|---|---|
| [docs/ja/JUMPLIST.md](docs/ja/JUMPLIST.md) | ジャンプリスト: 使い方、カテゴリ、削除済み宛先、表示制限 | 日本語 |
| [docs/ja/NOTIFICATIONS.md](docs/ja/NOTIFICATIONS.md) | 通知: 使い方、責務、制限 | 日本語 |
| [docs/USAGE-AVALONIA.md](docs/USAGE-AVALONIA.md) | Avalonia のサンプル付き利用ガイド | 日本語 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | ライブラリの構造 | 日本語 |
| [docs/ERROR-MODEL.md](docs/ERROR-MODEL.md) | 結果とエラーのモデル | 日本語 |
| [docs/API-CONTRACT-REVIEW.md](docs/API-CONTRACT-REVIEW.md) | COM 宣言を Windows SDK ヘッダーと照合した記録 | 日本語 |
| [docs/COM-PROVENANCE.md](docs/COM-PROVENANCE.md) | COM 宣言の出典と、未確定の点 | 英語 |
| [docs/PUBLICATION-CHECKLIST.md](docs/PUBLICATION-CHECKLIST.md) | GitHub・NuGet・アプリ ZIP の公開前チェックリスト | 英語 |
| [docs/VERSIONING.md](docs/VERSIONING.md) | パッケージ単位の独立バージョン管理方針 | 英語 |
| [docs/DOCS-MAP.md](docs/DOCS-MAP.md) | 日英文書の対応と、仕様変更時に一緒に更新するもの | 日本語・英語 |
| [docs/JUMPLIST-PACKAGE-README.md](docs/JUMPLIST-PACKAGE-README.md)、[docs/PACKAGE-README.md](docs/PACKAGE-README.md) | パッケージ用 README | 英語 |

## 開発と AI の支援

ShellInterop.Core は moz が開発・保守しており、OpenAI ChatGPT、OpenAI Codex、Anthropic Claude Code の大きな支援を受けています。アーキテクチャと API 設計の検討、実装計画、コード生成、デバッグ、テスト、コードレビューで AI を使いました。最終的なプロジェクトの判断と責任は保守者にあります。
