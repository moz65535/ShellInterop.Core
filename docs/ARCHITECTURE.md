# ARCHITECTURE

## 責務（REPO-ISOLATION-01 で明文化）
- **実装済み**: JumpList Tasks 管理、Windows Shell COM 相互運用、AppUserModelID 関連処理、STA 処理境界、COM ライフサイクル、更新結果とエラー処理。
- **含めない**: WPF / Avalonia UI、設定画面、アプリ固有 IPC、更新サービス、インストーラー、タスクバーピン管理、ユーザー設定の永続化。
- **将来の候補（未実装）**: DWM サムネイル、DWM Peek、タスクバー関連の Win32 処理、その他の Shell 連携。実装済みとは扱わない。
- 公開 API と挙動の契約は `API-CONTRACT-REVIEW.md`、エラーの扱いは `ERROR-MODEL.md`、利用手順は `USAGE-AVALONIA.md`。

```
caller (Avalonia / WPF / console)  ──await──►  IJumpListPublisher (public)
                                                   │ validate on caller thread (pure)
                                                   ▼
                                     BlockingCollection queue (FIFO)
                                                   │
                                  dedicated STA worker thread (1 per publisher)
                                                   │ one transaction at a time
                                     IJumpListNativeFactory / IJumpListNative   (internal seam)
                                       ├─ ShellJumpListNative  → ICustomDestinationList (real COM)
                                       └─ FakeShell (tests only)
```

- **公開 API**: `IJumpListPublisher`（`ApplyAsync`, `DeleteListAsync`）、`JumpListDefinition`、`JumpTaskDefinition`、`JumpListApplyResult`、`JumpListPublisherOptions`、`JumpListValidator`、`ProcessAppIdentity`。COM 型は一切公開しない。
- **UI 非依存**: Dispatcher を受け取らない。呼び出し側は任意のスレッドから `await` するだけ。完了継続は `RunContinuationsAsynchronously`。
- **STA 境界**: ワーカーは `ApartmentState.STA` の専用バックグラウンドスレッド。COM オブジェクトは 1 リクエストの間だけ生存し、リクエスト終了時に同スレッドで解放する。スレッド間で COM オブジェクトを渡さない。
  ワーカーはメッセージポンプを持たないが、リクエスト間で COM オブジェクトを保持しないためクロスアパートメント呼び出しの待ちは発生しない設計。
- **直列化**: 同一 publisher の要求は常に 1 つずつ・投入順。複数 publisher を作ると並列になるため、同一プロセスでは 1 インスタンスを共有すること（README 注意事項）。
- **キャンセル**: 実行開始前（キュー待機中）のみ有効。COM 呼び出し中は中断しない（`Cancelled` を返す）。
- **終了（POC-02 で改訂）**: 状態は `Running → ShuttingDown → Stopped`。`ShutdownAsync(timeout)` が正式な終了手段: (1) 新規受付停止 (2) 未開始の要求は `PublisherDisposed` で確実に完了（Shellには届かない） (3) 実行中のCOMトランザクションは**最後まで待つ**（スレッド中断・COM中断・Cancellation による切断はしない。実行中のキャンセルは無視され実結果が返る） (4) COM解放 (5) STAワーカー終了を確認 (6) `Completed=true`。タイムアウト時は `Completed=false/TimedOut=true` で `ShuttingDown` のまま（成功を偽らず、再呼び出しで待機を再開できる）。同時呼び出し・多重呼び出しは安全。`Dispose()` は同期の best-effort（タイムアウト時はログ `DisposeTimedOut` を残して戻り、`Stopped` を主張しない。ワーカー自身からの呼び出しは自己Joinしない）。
- **スレッド属性**: ワーカーは `IsBackground=true` を維持。フォアグラウンドにすると固まったShell呼び出しがプロセス終了を無期限に妨げるため。安全性はスレッド属性ではなく「ホストは `ShutdownAsync` の完了を確認してから閉じる」契約で担保する（ホストは `HostShutdownCoordinator` が実施）。
- **AUMID の安全境界**: 暗黙 ID へのフォールバックなし。`AppIdFilter`（許可述語）、`RequireProcessAppIdMatch`（既定 true: プロセスの明示 AUMID が要求 ID と一致しなければ COM 呼び出し前に拒否）。
- **ログ**: `IJumpListLogSink`（日時, PID, スレッドID, ApartmentState, イベント, HRESULT）。例外は握りつぶされ更新に影響しない。
- **WPF JumpListService との適合**: `JumpListDefinition` は「AUMID + 順序付きタスク全置換」で、WPF の `JumpList.SetJumpList` と同じ粒度。
  相違点: WPF の `JumpItemsRejected` 相当は無い。入力検証エラー（ローカル）と HRESULT のみ。詳細は ERROR-MODEL.md。
