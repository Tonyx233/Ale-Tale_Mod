## 0.8.0 YouTube 共用外放

- 關閉點唱機介面、播放器 X 或 Back to game 都保留播放，重開沿用進度；退出房間或遊戲時清理播放器。
- 每房間一首共用曲目，任何靠近聲源的玩家可換歌、暫停／繼續、跳轉或停止；晚加入者取得房主狀態。封包檢查來源、版本、曲目 ID、數值及操作距離。
- 沿用原生點唱機音量，3m 內全音量、30m 外靜音；可聽見且實際播放時沿用遊戲原本的背景音樂讓位機制。
- 每位玩家各自串流，廣告、網路緩衝、影片限制可能造成差異；校準容許約 2 秒差距，不承諾取樣級同步。直播／播放清單、跨多台點唱機不同歌曲與自動循環不在此版範圍。
- 驗證：328 項狀態／數值／距離測試、隱藏後播放、seek／pause／resume／volume／stop、兩個播放器延後加入與不同音量、既有輸入及馬匹測試。遊戲內雙人連線、BGM 讓位與全螢幕仍須實機確認。

## 0.7.1 水邊騎乘修正

- 移除水中／水面上方自動下馬，避免碰水或跳過水面時被強制踢下馬。
- 保留 E 主動下馬與安全落點檢查，以及死亡／斷線座位清理。此修正不新增游泳或水面浮力。

## 0.7.0 馬匹跳躍

- 駕駛使用原生跳躍鍵（預設 Space）帶馬匹與乘客跳躍，沿用遊戲落地判定、跳躍力度與重力，不增加空中連跳。
- 乘客不能獨立跳躍；馬匹在空中收腿，落地恢復步態，保留坐姿與騎乘武器功能。
- 空中暫停換座；下馬仍須找到安全落點，保留水邊自動下馬流程。駕駛死亡或斷線後，房主讓失去駕駛的馬落地，避免停在半空；其他場景的馬不受影響。
- `tests/run-horse-jump.ps1` 直接測試正式輸入攔截方法。遊戲內高度、碰撞、動畫和多人延遲仍須實機驗證。

## 0.6.0 騎乘坐姿與武器

- 前後座骨盆對準鞍座；依玩家實際腿骨長度求解膝蓋與腳部位置，取代沿錯誤骨軸扭轉的固定 Euler 角度。
- 僅調整下半身，保留原生上半身武器動畫；綁定第三人稱玩家 Animator，角色尚未就緒時重試。
- 騎乘時保留第一人稱手持物，允許原生攻擊、蓄力、瞄準／格擋輸入。跳躍、蹲下與衝刺技能等既有限制保持不變。
- 測試：`tests/run-horse-pose.ps1` 驗證腿長、不可達目標與退化輸入；仍須在遊戲內驗證男女角色、雙人坐姿、各種武器與下馬復原。幾何測試不代表已完成多人實測或完全消除所有攻擊動作穿模。

# Tony Ale & Tale Mods 0.8.0

Ale and Tale Tavern 的整合 BepInEx 模組，血量面板、YouTube 點唱機與雙人馬共用一個 DLL。

## 功能

- 雙人馬：商人購買，背包使用後在戶外放置；1 位駕駛＋1 位乘客，Ctrl+F1/F2 換位，附帶存檔。[操作與限制](Horse/README.md)

- 左側中央紅色血量面板，顯示自己與隊友的名字、HP；單人也可用。
- 打開原生點唱機介面後，畫面上方提供 YouTube 按鈕。
- 貼上影片網址後按 Play，於操作面板的 獨立 WebView2 視窗顯示影片與聲音。
- 支援 watch、youtu.be、shorts、live 與 embed 網址。
- Back to game、關閉視窗或關閉點唱機介面後繼續播放；退出房間／遊戲會停止。
- 支援一首房間共用曲目、晚加入進度校準及距離音量；不含搜尋、佇列或音訊下載。

## 安裝

需要 Windows x64、BepInEx 5，以及 Microsoft Edge WebView2 Evergreen Runtime。
編譯後只複製 `bin/Tony.TeammateHealthBars.dll` 到：

`BepInEx/plugins/TONY_BIG_SET.dll`

此電腦沿用 TONY_BIG_SET.dll 檔名，plugin GUID 保持不變以相容既有血量設定；不要同時放兩份 DLL。
DLL 內包含模組程式、播放器 helper 與 WebView2 SDK 元件。
首次開啟播放器會解出元件到 `BepInEx/cache/TonyAleTaleMods/0.8.0/`。
WebView2 Runtime 是額外系統依賴，瀏覽器資料位於 `%LOCALAPPDATA%/TonyAleTaleMods/WebView2Speaker/`。
請從 Microsoft 官方安裝 Runtime，本模組不會自動安裝系統元件。

[Microsoft WebView2](https://learn.microsoft.com/microsoft-edge/webview2/)

## 操作

1. 存檔、退出遊戲後更新 DLL，重新啟動。
2. 操作點唱機，按畫面上方 YouTube。
3. 在 Windows 原生網址欄貼網址，按 Play 或 Enter；支援 Backspace、Delete、Ctrl+A、Ctrl+V，Clear 清空網址。若未自動播放，按影片內的播放按鈕。
4. 用點唱機原本的音量滑桿調音量；播放器 Pause／Resume／Seek (sec) 控制共用進度，Stop 或遊戲內 Stop YouTube 停止全員播放。控制音樂須在點唱機 8 公尺內。
5. 按 Back to game 或關閉視窗，再離開點唱機介面即可繼續聽；重新操作點唱機按 YouTube 可開回控制視窗。

房主統一維護曲目／暫停／進度，每位玩家自行載入同一支 YouTube 影片。全員需安裝 0.8.0；原生曲目會停止。3 公尺內保留點唱機音量，往外平滑衰減，30 公尺外靜音；音樂仍持續走時。這是距離衰減，不含方向聲像、遮蔽物或牆壁吸音。
禁止嵌入、地區／年齡限制、廣告、自動播放限制由 YouTube 決定，無法保證每支影片可播放。
保留 YouTube 原生播放器與一般廣告。
建議無邊框／視窗模式；獨佔全螢幕、DPI 與縮放相容性需實機驗證。
舊 YTJukebox 同樣修改點唱機，兩者同時使用可能衝突，本模組不會自動移除它。

## 編譯與驗證

```powershell
.\build.ps1
.\build.ps1 -GamePath 'D:\SteamLibrary\steamapps\common\Ale and Tale Tavern'
.\tests\run.ps1 -WebView -Speaker
.\tests\run.ps1 -Playback -VideoId ytQ3Hs3WjQ4
```

使用 Windows .NET Framework compiler，引用本機遊戲 assemblies。
首次編譯由 NuGet 下載固定版本 Microsoft.Web.WebView2 1.0.2903.40。
WebView2 元件與 helper 嵌入最終 DLL，遊戲 assemblies 不隨套件發布。

已驗證：編譯、17 個網址解析案例、WebView2 初始化與頁面 JavaScript、父視窗掛載與 STOP／EXIT 清理。
0.5.4 改用具有 HTTPS 來源的本機容器頁面，讓 iframe 自動帶入 Referer，並回報 PLAYER_READY、PLAYER_STATE、PLAYER_ERROR。已實測兩支 YouTube 影片到達 PLAYER_STATE 1；原生文字框 Backspace、Delete、選取刪除、Clear 及父視窗掛載／STOP／EXIT 通過測試。
尚待實機驗證：遊戲內開關、剪貼簿貼上及全螢幕切換。
血量面板多人同步亦需兩名玩家驗證。

[血量面板設定](TeammateHealthBars/README.md)

雙人馬已通過編譯、座位及步態測試與 API 靜態檢查；尚未做遊戲內雙人、購買、存檔實測。
[模型動畫預覽](Horse/Assets/preview.html)（下載後用瀏覽器開啟）。

## 0.5.8 點唱機操作修正

播放器改為一般可縮放視窗，不再用 SetParent 嵌入 Unity。工具列採明確的兩列佈局，提供網址欄、Paste、Play、Stop、Clear。支援 Ctrl+V、Enter；網址不合法會記錄 INVALID_URL。Unity 傳來的舊 RECT 指令忽略，保留玩家調整的視窗位置。

驗證：兩種視窗尺寸的工具列可見性與邊界、文字編輯、WebView2 初始化、YouTube PLAYER_STATE 1、146 個非點唱機方法與馬模型保留檢查通過。HostTests.exe 受 Windows 應用程式控制阻擋，生命週期整合測試未完成；仍需遊戲內操作驗證。
