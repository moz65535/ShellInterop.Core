# DOCS-MAP / 文書の対応表

English: which English and Japanese documents correspond, and what to update together when a specification or verification status changes. There is no automatic synchronization; the LICENSE is never translated.

日本語: 日英の文書の対応と、仕様・検証状態が変わったときに一緒に更新する文書の一覧です。自動同期の仕組みはありません。LICENSE は翻訳しません。

## 対応表

| 内容 | 英語 | 日本語 |
|---|---|---|
| 概要・導入・使用例・検証状況 | [README.md](../README.md) | [README.ja.md](../README.ja.md) |
| JumpList の詳細 | [JUMPLIST-PACKAGE-README.md](JUMPLIST-PACKAGE-README.md)（パッケージ用）、README の該当章 | [ja/JUMPLIST.md](ja/JUMPLIST.md) |
| Notifications の詳細 | README の「Notifications」章 | [ja/NOTIFICATIONS.md](ja/NOTIFICATIONS.md) |
| Core のパッケージ README | [PACKAGE-README.md](PACKAGE-README.md) | （日本語版なし。README.ja.md で代替） |
| Avalonia での使い方 | （英語版なし。コードは英語） | [USAGE-AVALONIA.md](USAGE-AVALONIA.md) |
| 構造・結果モデル・COM 宣言の確認 | （英語版なし） | [ARCHITECTURE.md](ARCHITECTURE.md)、[ERROR-MODEL.md](ERROR-MODEL.md)、[API-CONTRACT-REVIEW.md](API-CONTRACT-REVIEW.md) |
| ライセンス | [LICENSE](../LICENSE)（正式文面は英語のみ） | README.ja.md に現状を記載 |

## 一緒に更新するもの

- **検証状態（SHELL-* の結果）が変わったとき:** README.md の Verification status → README.ja.md の「検証状況」→ ja/JUMPLIST.md または ja/NOTIFICATIONS.md の該当節 → JUMPLIST-PACKAGE-README.md / PACKAGE-README.md の要約文。USAGE-AVALONIA.md の 0 章も確認する。
- **公開 API が変わったとき:** README.md と README.ja.md の使用例（コードは両方で同一にする）、ARCHITECTURE.md の公開 API の一覧、ja/* の該当節。
- **ジャンプリストの表示制限・`JumpListItems_Maximum` の観測が変わったとき:** README.md の「Displayed destination count」→ README.ja.md の 6 章 → ja/JUMPLIST.md。
- **Windows App SDK / SDK .NET 投影 / ライセンスの状況が変わったとき:** README.md の Notifications 章と License 章 → README.ja.md の 7・9 章 → ja/NOTIFICATIONS.md の 8 章。ライセンスの未確定事項を、確認なしに確定済みへ書き換えない。

## 運用方針

- 重要な公開仕様は日英で矛盾させない。API 名とコード例は両言語で共通にする。
- 日本語版は自然な文章として保守する。翻訳によって新しい仕様や保証を作らない。
- 内部の検証ログ・調査証跡（`outputs` 側）は原文のままでよい。ARCHITECTURE.md などの日本語の既存文書は、英語版を新設せず維持する。
- ARCHITECTURE.md / ERROR-MODEL.md / API-CONTRACT-REVIEW.md は初期の調査記録を含む。古くなった「未実施」の記述は、README の Verification status を優先して読む（各文書の冒頭に注記あり）。
