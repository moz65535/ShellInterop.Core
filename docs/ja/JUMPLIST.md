# ShellInterop.JumpList 利用ガイド（日本語）

対応する英語文書: [../JUMPLIST-PACKAGE-README.md](../JUMPLIST-PACKAGE-README.md)（パッケージ用 README）と、ルートの [README.md](../../README.md)。
この文書は同じ内容を日本語で読めるようにしたもので、新しい仕様や保証を加えるものではありません。食い違いがあれば、英語版・ソース・README の Verification status を確認してください。
基本的な使い方（Avalonia のサンプル、終了処理）は [../USAGE-AVALONIA.md](../USAGE-AVALONIA.md) にあります。

`ShellInterop.JumpList` は Windows 専用（`net10.0-windows`）のジャンプリスト公開ライブラリで、`ShellInterop.Core` に依存します。分割版は未公開で、古い Core 0.1.0-preview.1 のパッケージを置き換えてはいけません。名前空間は `ShellInterop.Core.JumpLists` のままですが、バイナリ互換は保証しません。

## 1. できること・できないこと

- 実装済み: Tasks、区切り線、1 つ以上のカスタムカテゴリ（削除済み宛先の追跡付き）、適用・削除の結果、公開側の安全な終了。
- 未実装: Recent / Frequent、UI、設定画面、アプリ固有の IPC、更新サービス、インストーラー、タスクバーのピン管理。

## 2. 区切り線を含む Tasks

従来の `new JumpListDefinition(appId, tasks)` はそのまま使えます。タスクと区切り線を混ぜるには `FromEntries` を使います。

```csharp
var definition = JumpListDefinition.FromEntries(appId,
[
    JumpListEntry.ForTask(firstTask),
    JumpListEntry.Separator(),
    JumpListEntry.ForTask(secondTask),
]);
await publisher.ApplyAsync(definition);
```

- `Entries` が順序付きの正式な入力で、`Tasks` はタスクだけを取り出した見え方です。入力はコピーされます。
- このファクトリを使った場合、エラーの `TaskIndex` はエントリ位置（0 始まり）を指します。従来のタスクだけの呼び出しでは、これまでのインデックスのままです。
- `RequestedTaskCount` とローカルの 100 件上限には、区切り線は含まれません。
- Tasks が空の場合は従来どおりの動作です。区切り線だけの入力は Shell にそのまま渡されます。
- ライブラリは先頭・末尾・連続する区切り線を正規化しません。Windows が先頭・末尾を無視し、連続する区切り線の一方を抑止します。
- `BeginList` の最小スロット数は、区切り線とヘッダーの合計が 4 件までなら数に含まれません。それを超えると、表示される宛先に影響することがあります。ライブラリはその数に合わせた切り詰めをしません。
- 実 Shell: `TasksFallback` のブロック内の区切り線は、このライブラリで期待どおり表示されました（SHELL-JL-07、Windows 11）。このライブラリの公開 API を呼ぶスタンドアロンのテストアプリでも、区切り線の表示を確認しました（SHELL-JL-02）。それ以外の配置は未検証です。

## 3. カスタムカテゴリと削除済み宛先の追跡

**実 Shell での状態。** 以下の動作はフェイクテストで確認しています。実 Shell では、単一カテゴリ（SHELL-JL-05）、複数カテゴリ・削除済み宛先の追跡・プライバシー設定による `E_ACCESSDENIED` のフォールバック（SHELL-JL-07、条件付き PASS）を確認しました。SHELL-JL-08（条件付き PASS）では、Explorer 再起動後の削除済み宛先と、トランザクション中にプライバシー設定を切り替えて起こした後続カテゴリの `E_ACCESSDENIED` を確認しました。未検証: 後続カテゴリで自然に起きる `E_ACCESSDENIED`、空 Tasks によるクリア、OS 再起動、他の Windows ビルド。フェイクテストの結果は実 Shell の結果ではありません。

登録の成功は、Shell がすべての宛先を表示することを意味しません。Shell は表示件数を制限し、`JumpListItems_Maximum` レジストリ値がその件数を変えます。重要な宛先は最初のカテゴリに置いてください。観測された動作とその限界は、[README.ja.md](../../README.ja.md) の「JumpList の既知の表示制限」を参照してください。

`CommitList` は、Tasks だけの Apply でも Shell の削除済み宛先リストを破棄します。ユーザーの「このリストから削除」が黙って失われないよう、`JumpListDefinition.FromEntries(appId, entries, category, tracking)` を使います。

- `JumpRemovedDestinationTracking`（オプトイン。カテゴリとは独立）: `ExcludedDestinationKeys` と `MatchOnly`（認識はするが表示しない識別情報。非表示のグループや過去に登録した項目など）。追跡を有効にすると、カテゴリの有無にかかわらず Apply のたびに削除済みリストを読みます。
- `JumpCategoryDefinition`: `Name`、`Destinations`（`JumpDestinationDefinition`: 安定した `Key` とリンク）、任意の `TasksFallback`。**追跡のないカテゴリは検証エラー（`Tracking.Required`）です。** 一度でもカテゴリを使ったら、以降の Tasks だけの Apply でも追跡（空のカテゴリまたは `MatchOnly`）を渡し続けないと、削除が気づかれずに消費されます。
- 照合に使うのは Arguments（Ordinal）とパス（OrdinalIgnoreCase）だけで、表示名や順序は使いません。Arguments と Key は、宛先と `MatchOnly` の間で一意でなければなりません。Arguments が一致してパスが違うリンクは `Ambiguous`（Apply を止める）ですが、定義が `PathPolicy = AllowArgumentsOnly` を指定した場合は例外です。これは、Arguments がアプリ固有の安定した識別子であると呼び出し側が保証する指定です。ライブラリは引数の形式を固定していません。
- `RemovedDestinations` の分類: `Matched`、`Foreign` / `ForeignItem`（報告するが止めない）、`Unreadable` / `Ambiguous`（止める）。何にも一致しない読み取り可能なリンクは `Foreign` になるので、保護したい過去の項目は `MatchOnly` に登録します。

流れ（「検出 → 中断 → 呼び出し側が保存 → 再適用」）:

1. `Matched` の削除があり、それが `ExcludedDestinationKeys` にない場合、ライブラリは `AbortList` を呼んで `RemovedDestinationsPending` を返します。**何もコミットされません**（`ShellState = Unchanged`、`Succeeded = false`、`Error = null`）。Pending は成功ではありません。
2. **キーの保存は呼び出し側の責任です**（ライブラリは何も保存しません）。`AlreadyExcluded == false` の `Matched` のキーをすべて保存し、そのあとでだけ `ExcludedDestinationKeys` に追加します。保存していないキーを渡してはいけません。
3. 保存したら **もう一度 Apply** します。除外した宛先は二度と追加されません。
4. `RemovedDestinationUnidentifiable`: 要素が読めない・曖昧だった（または一覧を読めなかった）場合。中断し、何もコミットせず、削除済みリストは保持されます。

`AppendCategory` では、`E_ACCESSDENIED`（プライバシー設定やポリシー）だけが許容されます。`TasksFallback` がなければそのカテゴリは省略され（`CategorySkipped`）、あれば呼び出し側が明示した静的なエントリが Tasks に追加されます（`TasksFallback`）。宛先を自動で変換することはなく、キー付きのフォールバック項目（`JumpListEntry.ForKeyedTask`）は除外中なら落とされ、通常のタスクと重複するものは拒否され、ブロック内の区切り線は整理・統合されます（通常のタスクの後に区切り線が 1 つ入ります）。フォールバックに履歴由来の項目を入れないでください。Windows のプライバシー設定を変更することはありません。それ以外の HRESULT の失敗は中断します（`CategoryAppendFailed`）。Apply のたびに `AppendCategory` をやり直します。

結果のフィールド: `EffectiveDisplayMode`（成功時のみ設定。`TasksOnly`、`CategoryAndTasks`、`CategoryEmpty`、`TasksFallback`、`CategorySkipped`）、`FallbackReason`、`RemovedDestinations`。これらは COM の結果を表すもので、Explorer が何を表示しているかではありません。

`JumpListEntry` は API の入力モデルで、永続化用の形式ではありません。アプリのデータは別に保存し、ファクトリで再構築してください。

## 4. 利用者側の責任（まとめ）

- AUMID を最初のウィンドウ生成前に設定する。実際のジャンプリストに書き込むため、いつ・どの AUMID に対して書くかを管理する。開発用には別の AUMID を使う。
- 削除済み宛先のキーを保存してから、除外リストに加えて再適用する。
- アプリ終了前に `ShutdownAsync` の完了（`Completed == true`）を待つ。
- `Succeeded` と画面表示を同一視しない。表示件数の制限を考慮して、重要な宛先を先頭のカテゴリに置く。
