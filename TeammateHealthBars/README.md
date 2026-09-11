# Teammate Health Bars 1.0.1

適用於本機目前版本的 Ale and Tale Tavern、BepInEx 5.4.23.5。

- 血條固定紅色；頭頂名字下方顯示 HP 血條與數字；預設 50 公尺內，遮擋時隱藏。
- 隊伍面板固定在畫面最左側中央，預設距左側 8 個 UI 單位。
- 左側面板包含自己與隊友，自己排在第一列，單人遊玩也會顯示自己的名字與 HP；頭頂血條僅顯示隊友。血量每 0.1 秒讀取一次，位置每個繪製 frame 更新。
- 原生同步資料無法讀取或尚未就緒時顯示 `-- / --`。
- 使用本機 IMGUI 疊圖，不新增 RPC、NetworkObject 或修改存檔。

## 安裝

DLL 位於 `BepInEx/plugins/Tony.TeammateHealthBars/Tony.TeammateHealthBars.dll`。
透過原本能載入 BepInEx 的方式啟動遊戲；已啟動則需退出後重新啟動。
載入後 log 應有 `Teammate Health Bars 1.0.1 loaded (read-only client UI, panel includes self).`。
首次載入自動產生 `BepInEx/config/Tony.TeammateHealthBars.cfg`。

## 設定

| Key | 預設 | 用途 |
|---|---|---|
| TeamPanel | true | 左側中央面板 |
| OverheadBars | true | 頭頂血條 |
| HideBehindWalls | true | 遮擋時隱藏頭頂血條 |
| MaxDistance | 50 | 頭頂血條最大距離 |
| UIScale | 1 | UI 比例 |
| LeftMargin | 8 | 面板左邊距 |

修改設定後重新啟動遊戲。頭頂標籤不會攔截滑鼠；面板為純顯示。

## 編譯

在此目錄執行 `./build.ps1`，或使用 `./build.ps1 -GamePath 'D:\SteamLibrary\steamapps\common\Ale and Tale Tavern'` 指定遊戲路徑。使用 Windows 內建 .NET Framework C# compiler，
引用本機遊戲 assemblies，不需要下載 NuGet 套件。
輸出至 `bin/Tony.TeammateHealthBars.dll`，編譯腳本不會自行覆蓋已安裝 DLL。

## 驗證狀態與待測

已完成：針對本機遊戲 assemblies 編譯。
尚未完成：遊戲內載入、畫面及多人連線測試。

先以單人遊玩確認左側中央顯示自己的名字與 HP，再以兩名玩家驗證：
1. 非房主安裝模組加入未安裝模組的房主，確認雙方血量資訊可讀。
2. 隊友受傷、回血、增加最大 HP、死亡及重生後，數值與原生 HP 一致。
3. 隊友離線、重連、回主選單再進房後，不殘留舊列。
4. 頭頂血條在鏡頭後方、遠距離與牆後隱藏；面板維持左側中央。
5. 中文名稱、不同解析度、UI 縮放及現有模組共同載入。

血量只讀不代表所有版本均允許非房主讀取，需完成上述實測才能確認 client-only 相容性。
若 log 沒有本模組載入訊息，
需先確認既有 BepInEx 啟動方式。本模組不安裝或變更 loader。

移除時在遊戲關閉後移走本模組子目錄即可，無存檔資料需要還原。
