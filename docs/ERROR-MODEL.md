# ERROR-MODEL

`JumpListApplyResult` = Status + FailurePhase + HResult + RequestedTaskCount + ShellState (+ Error / PrecedingError / ValidationErrors / Diagnostics / QueueWait / Elapsed)。
`VisuallyConfirmed` は常に false（ライブラリはタスクバーを観測できない）。

| Status（指示書の名称） | FailurePhase | ShellState | AbortList | 備考 |
|---|---|---|---|---|
| Success (SUCCESS) | None | Applied | 呼ばない | HRESULT 成功のみ。実画面は未確認 |
| ValidationFailed (VALIDATION_FAILED) | Validation / ProcessAppIdCheck | Unchanged | 呼ばない | COM 未呼び出し。HResult は null（UNKNOWN 表示。推測しない） |
| BeginFailed (BEGIN_FAILED) | CreateList / SetAppId / BeginList | Unchanged | 呼ばない | セッション未開始 |
| TaskCreationFailed (TASK_CREATION_FAILED) | CreateTask (+TaskIndex) | Unchanged | 1回呼ぶ | |
| AddTasksFailed (ADD_TASKS_FAILED) | AddUserTasks | Unchanged | 1回呼ぶ | |
| CommitFailed (COMMIT_FAILED) | CommitList | **Unknown** | **呼ばない** | Abort はロールバックではなく、コミット失敗後のセッション状態も不定 |
| AbortFailed (ABORT_FAILED) | AbortList | Unknown | 失敗 | `PrecedingError` に元の失敗 |
| DeleteFailed | CreateList / DeleteList | Unchanged / Unknown | — | |
| UnknownState (UNKNOWN_STATE) | Unexpected | Unknown / Unchanged | 状況による | 想定外例外。セッション開始後・コミット前なら Abort を試み、成功すれば Unchanged |
| Cancelled / PublisherDisposed | None | Unchanged | — | 実行前に終了 |

`Unchanged` は「コミットが発行されていない」ことを意味する。AbortList 成功後も既存リストが保持されることは Shell 側の仕様に依存し、実 Shell での確認は未実施。

## WPF 版との差
- `JumpItemsRejected`（拒否理由付き）に相当する情報は COM から自動では得られない → 本モデルでは **ローカル検証エラー**（コード付き）と **HRESULT** だけを返す。部分適用の疑いは検出できず、`Success` でも個々のタスクが表示されたかは不明。
- 利用側の判断前に実 Shell で「長すぎる/不正タスク時の Shell の挙動（黙って落とすか HRESULT で失敗するか）」を実測する必要がある。
