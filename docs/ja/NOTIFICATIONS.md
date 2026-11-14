# ShellInterop.Notifications 利用ガイド（日本語）

対応する英語文書: ルートの [README.md](../../README.md) の「Notifications」章と「Verification status」。
この文書は同じ内容を日本語で読めるようにしたもので、新しい仕様や保証を加えるものではありません。食い違いがあれば、英語の README・ソース（XML ドキュメントコメント）を確認してください。

`ShellInterop.Notifications` は、タイトルと本文の Toast を送り、AUMID を持つスタートメニューのショートカットがあるかを検査します。ショートカットやレジストリ値の作成・編集・修復・削除は **しません**。ショートカットは、`NotificationPublisherOptions.Aumid` と同じ AUMID を持つものを、アプリが自分の配備の一部として用意します。

対象フレームワークは `net10.0-windows10.0.19041.0` で、Core / JumpList は参照しません。

## 1. 基本の使い方

```csharp
using ShellInterop.Notifications;

var publisher = new NotificationPublisher(new NotificationPublisherOptions { Aumid = "YourCompany.YourApp" });

// A. スタートメニューのショートカットの静的・読み取り専用検査
NotificationRegistrationInspection registration = publisher.InspectRegistration(Environment.ProcessPath);

// B. Windows の通知設定（Unknown は「読めなかった」であり「無効」ではない）
NotificationSettingState setting = publisher.GetSetting();

// C. Toast を送る。OS 側の失敗では例外を投げない。結果を確認する
NotificationSendResult sent = publisher.Send(new NotificationDefinition("Title", "Body"));
Console.WriteLine($"registration={registration.State} setting={sent.Setting} submitted={sent.Submitted} hr=0x{sent.HResult:X8}");
```

- `NotificationPublisherOptions.Aumid` は `Company.Product[.Sub...]` 形式です。2 つ以上の空でないセグメント、ASCII 英数字・`-`・`_` のみ、128 文字以内。不正な値は `NotificationPublisher` のコンストラクタが `ArgumentException` を投げます。ライブラリはこの ID を登録しません。
- `NotificationDefinition` は、タイトルと本文を構築時に検証します。XML 1.0 で使えない文字や空文字列は拒否され（置換はしません）、長さの制限はライブラリでは設けていません。長い・複数行のテキストは Windows が省略したり折りたたんだりすることがあります。
- `Send` と `GetSetting` は任意のスレッドから呼べます。`InspectRegistration` は専用の短命な STA スレッドで実行し、最大 60 秒待ちます。タイムアウトすると結果は `Unknown` になり、スレッドは中断せずに放置されます。1 プロセスで同時に走る走査は 1 つだけで、走査が終わらない間の次の検査は `Unknown`（busy）を返します。送信には影響しません。

## 2. Silent

`new NotificationDefinition("Title", "Body") { Silent = true }` とすると、Toast の XML に `<audio silent="true"/>` が付き、Windows に通知音を鳴らさないよう求めます。`false`（既定）なら XML は変わりません。`Silent` については、生成される Toast XML の内容を単体テストで確認していますが、それだけでは実際の無音動作を保証しません。**実機で実際に無音になるかは確認していません。**

## 3. LaunchUri（プロトコルによる起動）

`NotificationDefinition.LaunchUri`（既定 `null`）は、Toast の本文がクリックされたとき Windows が起動する絶対 URI です。

```csharp
new NotificationDefinition("Title", "Body") { LaunchUri = "my-app-x://activate?a=1" }
```

- 指定すると、Toast の XML に `launch="<uri>"` と `activationType="protocol"` が付きます（値は XML エスケープされます）。`null` なら XML は変わりません。`Silent` と併用できます。
- 整形式の絶対 URI でない値、空白や制御文字を含む値は `ArgumentException` になります。URI のスキームは制限しません。
- **ライブラリが行うのは URI を出力することだけです。** プロトコルの登録、スタートメニューのショートカットの作成、AUMID の設定、クリックの受信処理はしません。プロトコル登録（たとえば HKCU 配下）と、Windows がそれを起動したときに動くコードは、アプリが持ちます。
- **受け取った URI は信頼できない入力として扱ってください。** どのプロセスでも、登録済みのプロトコルを任意の引数で起動できます。アプリ側で検証してください。
- Toast がクリックされたときアプリが動いていなければ、Windows は登録されたプロトコルのハンドラーを起動します。ハンドラーは、アプリを起動できる（または実行中のインスタンスに引き継げる）ものでなければなりません。

### 検証された、アプリ側の構成の一例

AI Usage Tracker での構成です（このライブラリの機能ではなく、他のアプリへの保証でもありません）。アプリ自身が AUMID 付きのスタートメニューショートカットを作り、HKCU に URL プロトコルを登録します。Toast は `LaunchUri` にそのプロトコルを持ちます。クリックすると Windows がアプリの実行ファイルを URI 付きで起動し、その短命な 2 つ目のプロセスがシングルインスタンスのゲート経由で実行中のインスタンスに引き継いで終了し、実行中のインスタンスがメインウィンドウを復帰させます（閉じていたアプリは単に起動されます）。これらの処理は、すべてアプリ側にあります。

## 4. 責務の分担

| 項目 | 担当 |
|---|---|
| スタートメニューのショートカットと AUMID | アプリ（このライブラリは読み取り専用の検査のみ） |
| プロトコル登録 | アプリ |
| 送信（`NotificationPublisher.Send`） | ライブラリ。`Submitted` は Windows の API 呼び出しが成功したという意味だけ |
| Windows による表示 | 保証されない |
| クリック時のアプリの起動・復帰 | アプリ側のプロトコルハンドラー |

互いに独立した 3 つの事実があり、どれも Toast が見えることを保証しません。

| 事実 | 場所 | 意味 |
|---|---|---|
| Submitted | `NotificationSendResult.Submitted` | Windows の送信 API 呼び出しが成功した |
| Setting | `NotificationSendResult.Setting` / `GetSetting()` | その AUMID に対する Windows の通知設定 |
| Registration | `InspectRegistration()` | スタートメニューのショートカットがあるかという静的な証拠だけ |

## 5. 実 Windows 11 での観測（SHELL-NOTIF-02 / SHELL-NOTIF-04、専用 VM）

- **登録直後の伝播。** ショートカットを登録した直後（約 1 秒後）に送った Toast は `Submitted=True`、検査は `ShortcutFound` でしたが、約 4.5 分後に確認した時点では画面にも通知センターにもありませんでした（短時間の表示は否定できません）。のちに同じ Toast を再送すると、表示され通知センターにも残りました。新しい AUMID 登録を Windows が拾うまでの遅れが原因かもしれませんが、原因と必要な待ち時間は未確認です。
- **登録解除の後。** ショートカットを削除した直後に送った Toast は、まだ表示されました。ゲストの再起動後は表示されませんでした（`Submitted=True`、`Setting=Enabled`）。登録解除の直後の効果は保証されません。再起動が必要か、サインアウトや時間経過で足りるかは未確認です。

## 6. 検証状況

README の「Verification status」が正です。要点だけ示します。

| 工程 | 意味 |
|---|---|
| SHELL-NOTIF-04 | 登録検査、Toast 表示、通知センターへの残存（専用 AUMID、VM） |
| SHELL-NOTIF-07-D-VERIFY（CONDITIONAL_PASS） | `Silent` と `LaunchUri` を使う AI Usage Tracker を実 Windows 11 VM で検証。表示、残存、各状態からのクリック復帰、終了後のクリック起動、Windows App SDK モジュールなし、通知のオフ→オンの回復。ただしビルドに未コミットの変更が含まれていた |
| SHELL-NOTIF-10（PASS） | コミット済みソースだけからの Release ZIP の再生成。Windows App SDK 通知への依存 0。静的・ビルドの結果で、実 Shell の結果ではない |
| SHELL-NOTIF-10-VM-SMOKE（PASS、最小スモーク） | 上の新 ZIP を実 Windows 11 VM で、起動、Toast 実表示、最小化からのクリック復帰、引き継ぎ、旧 SDK モジュールのロード 0 を確認。1 アプリ・1 VM・1 回 |

**未検証:** `Silent` の実聴、通知の失敗注入（ショートカットやプロトコルの競合、実際の送信失敗）、他の Windows ビルド、Windows App Runtime がないマシン、すべての OS・環境での動作保証。`Silent` と `LaunchUri` の単体テストは生成される Toast XML の内容を確認するもので、Windows 上での実際の無音動作やクリック処理は保証しません。クリック復帰は、別途、実 Windows 11 VM の最小スモーク（SHELL-NOTIF-10-VM-SMOKE）で確認済みです。

## 7. 実装していないもの

クリック処理そのもの、COM アクティベーターの登録、アクションボタン、通知履歴、Toast の置換・削除、ショートカットの自動登録、OS 登録の自動修復。

## 8. Windows App SDK への非依存と配布

Windows App SDK の通知コンポーネントには依存していません。次の 4 つを区別してください。

| 名前 | 関係 |
|---|---|
| Windows App SDK（NuGet、`AppNotificationManager`、ネイティブのブリッジ DLL） | 使わない（以前の Tracker で使っていたが撤去済み） |
| Windows App Runtime（OS 側のフレームワークパッケージ） | このライブラリは使わない。検証対象のアプリでは Windows App SDK の通知関連モジュールをロードしておらず、検証 VM の OS には Windows App Runtime が存在していた。その有無が結果に影響するかは未検証（入っていないマシンでの動作、すべての Windows 環境での非依存性は未確認） |
| Windows SDK .NET 投影（`Microsoft.Windows.SDK.NET.dll`） | 使う。利用側アプリに同梱される（PoC の self-contained 発行で約 25 MB） |
| `WinRT.Runtime.dll`（C#/WinRT） | 同じ投影パッケージの一部として使う。Windows App SDK ではない |

このライブラリを使うアプリは、`Microsoft.Windows.SDK.NET.dll` と `WinRT.Runtime.dll` を同梱することになります。これらの再配布条件、`ShellInterop.Notifications` 自体の公開条件、第三者通知の整備は **未確認で、公開前に決める必要があります**（法的な結論はここでは出しません）。詳しくは README.ja.md の「ライセンスと再配布の現状」を参照してください。
