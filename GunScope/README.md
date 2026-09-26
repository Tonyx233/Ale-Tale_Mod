# Musket scope

- 原版槍 `Musket_fp` 加裝黃銅鏡環、鏡筒、鏡片及支架，跟隨 `MusketRoot/Musket` 動畫。
- 以原生 mesh `MusketRoot/Musket/Musket1_2_1` 辨識火槍：手持物件是 Instantiate 出的 `Musket_fp(Clone)`，不能用名稱比對；`Crossbow_fp` 也有 `MusketRoot/Musket`，但沒有這個 mesh。
- 右鍵（原生 Aim/block 綁定；手把 L2）依序切換 3× → 6× → 收起；圓形鏡框顯示目前倍率。兩個倍率皆依原始 FOV 計算，不會疊乘成 18×，並同步降低瞄準靈敏度。
- 開鏡時隱藏第一人稱手部與武器 renderer，關閉後還原原本的 enabled 狀態。
- 裝填、換槍、開背包／選單、死亡、失去視窗焦點、離開場景與停用元件時解除開鏡，下次右鍵重新從 3× 開始。
- `RaycastShot` 在同步呼叫期間使用原 FOV，finalizer 即使遇到例外也恢復放大 FOV；原生散布、傷害、彈藥與射速不變。
- 0.14.0 起與 M4A1 共用：M4 由 `Musket_fp` 生成，同樣有火槍 mesh，所以先以 `M4Armory.IsM4` 判斷；M4 不加黃銅鏡（ACOG 是模型的一部分），右鍵單段 4× 開關，準星改為紅色 chevron 與落點刻度，開鏡時 M4 散布縮小。詳見 [M4 README](../M4/README.md)。
- 只修改本機第一人稱槍的外觀／視野，沒有新增物品或變更存檔；其他玩家的第三人稱槍模型不包含附加鏡筒。弩不受影響。
- 設定：既有 BepInEx plugin config 的 `[GunScope]`，`Enabled=true`。倍率固定循環 3×／6×；舊版 `Magnification` 設定不再使用。

## Validation

`tests/run-scope.ps1` 檢查透視倍率、靈敏度、原生 API、Harmony handler、火槍 mesh 過濾（禁止名稱過濾）與整包元件。
原生模型階層及 mesh 尺寸已從遊戲資產確認；build／靜態檢查不能替代實際遊戲驗證。

遊戲驗收：持槍確認鏡筒及換彈動畫；右鍵開關；開鏡射擊；装填、切物品、背包、設定、死亡、Alt-Tab 後 FOV／手部恢復；主客機各測一次；不同 FOV／解析度；弩維持原操作。
