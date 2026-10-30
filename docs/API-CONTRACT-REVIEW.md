# API-CONTRACT-REVIEW

確認日: 2026-10-24。根拠は (1) Windows SDK 10.0.26100.0 のヘッダ実物、(2) Microsoft Learn 公式ページ。推測で記述した値はない。

## 1. 確認した定義（GUID・メソッド順序）

| 項目 | 値 | 根拠 |
|---|---|---|
| CLSID_DestinationList | 77f10cf0-3db5-4966-b520-b7c54fd35ed6 | ShObjIdl_core.h `DECLSPEC_UUID ... DestinationList` |
| CLSID_EnumerableObjectCollection | 2d3468c1-36a7-43b6-ac24-d3f02fd9607a | 同 |
| CLSID_ShellLink | 00021401-0000-0000-C000-000000000046 | 同 |
| IID_ICustomDestinationList | 6332debf-87b5-4670-90c0-5e57b408a49e | ShObjIdl_core.h |
| IID_IObjectArray | 92CA9DCD-5622-4bba-A805-5E9F541BD8C9 | ObjectArray.h |
| IID_IObjectCollection | 5632b1a4-e38a-400a-928a-d4cd63230295 | ObjectArray.h |
| IID_IShellLinkW | 000214F9-0000-0000-C000-000000000046 | ShObjIdl_core.h |
| IID_IPropertyStore | 886d8eeb-8cf2-4446-8d02-cdba1dbdcf99 | propsys.h |
| PKEY_Title | {F29F85E0-4FF9-1068-AB91-08002B27B3D9}, 2 | propkey.h |

vtable 順序（ヘッダ通り。テスト `VtableOrder_MatchesSdkHeaders` と、SDK がある環境でヘッダ本文を直接解析する `Interop_MatchesSdkHeaderText_WhenSdkIsInstalled` で自動検証）:

- ICustomDestinationList: SetAppID, BeginList, AppendCategory, AppendKnownCategory, AddUserTasks, CommitList, GetRemovedDestinations, DeleteList, AbortList
- IObjectArray: GetCount, GetAt / IObjectCollection: (基底2つ) + AddObject, AddFromArray, RemoveObjectAt, Clear
- IShellLinkW: GetPath, GetIDList, SetIDList, GetDescription, SetDescription, GetWorkingDirectory, SetWorkingDirectory, GetArguments, SetArguments, GetHotkey, SetHotkey, GetShowCmd, SetShowCmd, GetIconLocation, SetIconLocation, SetRelativePath, Resolve, SetPath
- IPropertyStore: GetCount, GetAt, GetValue, SetValue, Commit

**実装中に判明した重要点**: クラシック `[ComImport]` では派生インターフェースの vtable に基底メソッドは含まれない。
`IObjectCollection : IObjectArray` を継承で書くと `AddObject` が `GetCount` スロットを呼び AccessViolation になった（初回テストで実際に発生）。
現在は `GetCount`/`GetAt` を `IObjectCollection` に再宣言している。この種の誤りは 実COMオブジェクトを使う in-memory テストで検出できる。

## 2. 公式ドキュメントから確定した契約

- `BeginList` の前に、明示 AppUserModelID を使う場合は `SetAppID` を呼ぶ。`AddUserTasks` は `BeginList` 後。
- `BeginList` の `riid` は通常 IObjectArray（削除済み宛先の一覧）。受け取った参照は解放が必要。
- `AddUserTasks` に渡す各 IShellLink は **引数(SetArguments)必須**（空のセパレータのみ例外）、**パスまたは PIDL**、**アイコン位置**が必須。
  表示名は IShellLink の IPropertyStore の `System.Title`。説明(SetDescription)は任意。
- `AbortList`: 「コミットせずにセッションを中断する」。コミット済み変更を戻す機能ではない。
- Tasks は静的であることが推奨される（公式注記）。グループ切替での動的変更は技術的に禁止されていないが、アプリ側が最新性を保つ責任がある。
- AppUserModelID: 128 文字以内・空白不可・`CompanyName.ProductName[.SubProduct[.Version]]` 形式。
  `SetCurrentProcessExplicitAppUserModelID` は UI 表示・Jump List 操作・SHAddToRecentDocs より前の起動初期に呼ぶ。
  明示 ID を使う場合、Jump List 更新にも同じ ID を使う。

## 3. 公式資料で確定できなかった点（推測せず、実 Shell で実測が必要な項目（未実施））

| 項目 | 本実装の扱い |
|---|---|
| `AddUserTasks` に空の配列を渡した挙動 | 呼ばない。Tasks 空 = `BeginList`→`CommitList`（AddUserTasks 無し）。実画面で Tasks が消えるかは未確認 |
| `CommitList` 失敗後の Shell 状態 | 不定。`ShellState=Unknown`、AbortList は呼ばない |
| `DeleteList` と実画面の関係、既存ピンへの影響 | 実 Shell での確認は未実施 |
| STA 要否 | ドキュメントに明記なし。ただしレジストリ（読み取りのみ）で DestinationList / EnumerableObjectCollection の ThreadingModel=Apartment を確認（windows.storage.dll）。よって STA 必須として扱う |
| タスク数・各文字列長の Shell 側上限 | 公式値なし。ローカルの保守的上限（100件 / Title 256 / Arguments 2048）を設定。「local」と明記 |
| 同一内容の再 Apply の最適化可否 | 行わない（Shell が外部要因で変化している可能性があるため毎回適用） |

## 4. Interop 方式

classic `[ComImport]` + `[PreserveSig]`（全メソッド int 戻り）。`CoCreateInstance`(LibraryImport)で生成し HRESULT を取得、
`Marshal.GetObjectForIUnknown` で RCW 化。`[GeneratedComInterface]` は PROPVARIANT 等のマーシャリングを自作する必要があり、
実Shellを使わずに検証する以上、検証済みの in-memory テストで挙動を確認できる classic 方式を選択した。
トリミング/NativeAOT は対象外（Core は Windows デスクトップ用ライブラリ）。
