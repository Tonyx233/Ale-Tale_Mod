# Tony Ale & Tale Mods 0.4.0

Ale and Tale Tavern 的整合 BepInEx 模組，血量面板、YouTube 點唱機與六人座小車共用一個 DLL。

## 功能

- 六人座小車：房主戶外 F6 放置空車，E 申請座位；1 位駕駛＋5 位乘客。房间人數仍需額外擴充。[操作與限制](TavernCart/README.md)

- 左側中央紅色血量面板，顯示自己與隊友的名字、HP；單人也可用。
- 打開原生點唱機介面後，畫面上方提供 YouTube 按鈕。
- 貼上影片網址後按 Play，於操作面板的 WebView2 子視窗顯示影片與聲音。
- 支援 watch、youtu.be、shorts、live 與 embed 網址。
- Close、關閉點唱機介面或退出遊戲會關閉播放器，不提供背景音訊播放。
- 第一版只有網址播放，不含關鍵字搜尋、佇列、下載或多人同步。

## 安裝

需要 Windows x64、BepInEx 5，以及 Microsoft Edge WebView2 Evergreen Runtime。
編譯後只複製 `bin/Tony.TeammateHealthBars.dll` 到：

`BepInEx/plugins/Tony.TeammateHealthBars/Tony.TeammateHealthBars.dll`

保留原檔名與 plugin GUID，以相容既有血量設定；不要同時放兩份 DLL。
DLL 內包含模組程式、播放器 helper 與 WebView2 SDK 元件。
首次開啟播放器會解出元件到 `BepInEx/cache/TonyAleTaleMods/0.2.0/`。
WebView2 Runtime 是額外系統依賴，瀏覽器資料位於 `%LOCALAPPDATA%/TonyAleTaleMods/WebView2/`。
請從 Microsoft 官方安裝 Runtime，本模組不會自動安裝系統元件。

[Microsoft WebView2](https://learn.microsoft.com/microsoft-edge/webview2/)

## 操作

1. 存檔、退出遊戲後更新 DLL，重新啟動。
2. 操作點唱機，按畫面上方 YouTube。
3. 貼網址並按 Play；若未自動播放，按影片內的播放按鈕。
4. 使用 YouTube 原生控制項調整音量、進度與暫停；Stop 清空播放器。
5. Close 返回原生介面。原生音樂需自行重新播放。

播放 YouTube 時停止本機該點唱機的原生音樂，不向其他玩家發送停止或播放命令。
禁止嵌入、地區／年齡限制、廣告、自動播放限制由 YouTube 決定，無法保證每支影片可播放。
保留 YouTube 原生播放器與一般廣告。
建議無邊框／視窗模式；獨佔全螢幕、DPI 與縮放相容性需實機驗證。
舊 YTJukebox 同樣修改點唱機，兩者同時使用可能衝突，本模組不會自動移除它。

## 編譯與驗證

```powershell
.\build.ps1
.\build.ps1 -GamePath 'D:\SteamLibrary\steamapps\common\Ale and Tale Tavern'
.\tests\run.ps1 -WebView
```

使用 Windows .NET Framework compiler，引用本機遊戲 assemblies。
首次編譯由 NuGet 下載固定版本 Microsoft.Web.WebView2 1.0.2903.40。
WebView2 元件與 helper 嵌入最終 DLL，遊戲 assemblies 不隨套件發布。

已驗證：編譯、17 個網址解析案例、WebView2 初始化與頁面 JavaScript、父視窗掛載與 STOP／EXIT 清理。
尚待實機驗證：遊戲內對位、輸入焦點、YouTube 影片實際播放、關閉及全螢幕切換。
血量面板多人同步亦需兩名玩家驗證。

[血量面板設定](TeammateHealthBars/README.md)
