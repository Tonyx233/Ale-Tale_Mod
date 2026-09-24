# Musket scope

- 原版槍 `Musket_fp` 加裝黃銅鏡環、鏡筒、鏡片及支架，跟隨 `MusketRoot/Musket` 動畫。
- 右鍵（原生 Aim/block 綁定；手把 L2）切換圓形鏡框、十字線與放大視野。預設 3×，以透視公式換算 FOV，並同步降低瞄準靈敏度。
- 開鏡時隱藏第一人稱手部與武器 renderer，關閉後還原原本的 enabled 狀態。
- 裝填、換槍、開背包／選單、死亡、失去視窗焦點、離開場景與停用元件時解除開鏡。
- `RaycastShot` 在同步呼叫期間使用原 FOV，finalizer 即使遇到例外也恢復放大 FOV；原生散布、傷害、彈藥與射速不變。
- 只修改本機第一人稱槍的外觀／視野，沒有新增物品或變更存檔；其他玩家的第三人稱槍模型不包含附加鏡筒。弩不受影響。
- 設定：既有 BepInEx plugin config 的 `[GunScope]`，`Enabled=true`、`Magnification=3`（1.5–6）。

## Validation

`tests/run-scope.ps1` 檢查透視倍率、靈敏度、原生 API、Harmony handler 與整包元件。
原生模型階層及 mesh 尺寸已從遊戲資產確認；build／靜態檢查不能替代實際遊戲驗證。

遊戲驗收：持槍確認鏡筒及換彈動畫；右鍵開關；開鏡射擊；装填、切物品、背包、設定、死亡、Alt-Tab 後 FOV／手部恢復；主客機各測一次；不同 FOV／解析度；弩維持原操作。
