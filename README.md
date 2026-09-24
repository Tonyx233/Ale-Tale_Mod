## 0.11.1 點唱機介面可讀性

- 播放器與待播清單按鈕明確指定深灰底、亮白字、邊框及滑鼠互動色；立即播放為藍色、停止為紅色。
- 按鈕、清單標題、循環狀態與主要提示改為繁體中文，區分清空網址與清空待播；使用 Microsoft JhengHei UI 10pt，增加按鈕高度及換行空間。
- 保留既有點歌、同步與待播清單邏輯。建置、837 項佇列／解析／房主控制檢查，以及 WebView 文字編輯、清單操作、視窗測試通過；預設尺寸離線繪圖已檢視，遊戲內顯示仍待實測。
- 此完整建置包含 main 既有的 0.11.0 堆疊整合；安裝時須與只更新播放器的需求區分。
## 0.11.0 所有物品堆疊 9999（已合併，未安裝）

將 ItemStackerFix／StackableFood 的功能整合進現有專案；裝備、家具、食物、飲料與特殊物品皆使用 9999 容量，並保留原始購買／掉落數量、單份消耗與物品狀態。原兩個 DLL 不改動；新版啟動時在記憶體接管其 patch。

已將 feat/all-items-stack-9999 合併至 main，保留 0.10.0 牛馬 X 收回提交 d3d286d。已完成 build 與離線測試，尚未完成遊戲內多人／存檔實測，也未安裝。[整合方式、測試與限制](ItemStacks/README.md)

## 0.10.0 牛馬 X 收回背包

- 牛馬與牛馬2 可像一般家具收回：無人騎乘時準星對準馬身，長按原生移除鍵（預設 **X**，手把 Y）約 1 秒，馬回到背包成為原物品 47920／47921，可再放置。
- 沿用遊戲原生的準星標題、「[X] - 收回背包」提示與長按進度條。馬身加一個 `NonCollidingInteractive` 層的互動碰撞盒：此層在原生互動射線 mask 內，且不與玩家碰撞；原本擋人的停放碰撞盒不變。
- 房主驗證：無人騎乘、玩家距馬身 3m 內（原生互動 2m 加延遲餘裕）、同一匹馬只收一次。房主開「禁止客人移除家具」時客人不能收馬，房主不受限，與家具相同。
- 背包滿時拒絕並保留馬匹，不像家具會把物品掉在地上。收回後所有玩家同步移除該馬，下次原生存檔時 sidecar 一併更新。
- Horse protocol 相容 0.9.0（新增 op 3，manifest 格式不變）；收回需要房主也是 0.10.0，舊版房主會忽略請求。
- 驗證：新增 `tests/run-horse-pickup.ps1`，以正式程式碼搭配替身跑 49 個情境（馬身距離、拒絕條件、提示可用性、op 3 閘門與頻率限制、背包滿／物品停用／重複收回），7 種刻意改壞的版本都會被抓到。既有座位／步態、坐姿、跳躍、上馬、長馬身、horse JSON、URL／佇列測試與 21 個 Harmony＋9 個收回 API 簽章檢查通過。`check-horse-api.ps1` 改用 bytes 載入 Mono.Cecil，避開下載檔 Zone.Identifier 造成的 `Add-Type` 失敗。
- 尚未做遊戲內實測：提示顯示、長按手感、多人收回、背包滿提示與存檔重載。

下列為先前版本的發布紀錄；其中「尚未安裝」等敘述只代表當時狀態。

## 0.9.0 牛馬2：五座十腿

- 保留原「牛馬」物品 47920、雙座四腿模型與舊存檔；另加商人物品「牛馬2」47921，預設 1 金，可在 `[Horse2] Price` 調整。
- 牛馬2使用原馬延伸模型：5 個鞍座、10 條腿，1 位駕駛與 4 位乘客。E 上下馬，Ctrl+F1～F5 選空座，Shift 加速，駕駛沿用原生跳躍鍵；不提高遊戲房間人數上限。
- 新馬可從後座旁上馬，下馬優先找座位左右的安全落點。放置檢查包含後段，騎乘駕駛對整段馬身做移動和轉彎障礙檢查；原牛馬維持既有碰撞方式。
- 馬匹 sidecar 升為 version 2，舊 version 1 自動視為原牛馬。新舊馬能共存、存檔及晚加入同步；全員需更新相同 DLL，不能混用舊版 horse protocol。
- 獨立模型為 `Horse/Assets/model2.json`，預覽為 `Horse/Assets/preview2.html`；原 `model.json` 與 `preview.html` 未變更。`build-model2.js` 可從原模型重建新模型與預覽。
- 驗證：38,926 項座位／步態斷言、12,804 項坐姿斷言、74 項跳躍輸入、24 項上馬遮擋、10 項長馬身移動／轉彎測試通過；舊存檔、五座封包、32 匹混合 manifest、21 個 Harmony API、雙模型幾何與瀏覽器步態預覽通過。碰撞測試使用替身，尚未完成遊戲內多人、坡地、窄路、購買及重載實測。
- 新模型：25 骨架節點、115 mesh parts、12,288 三角形；沒有逐腳貼地 IK，坡地仍可能出現腳部懸空。Build 沿用既有 Newtonsoft/netstandard CS1701 警告。

## 0.8.1 共用點歌佇列（當時已 build，尚未安裝）

- 點唱機 8m 內所有玩家都能點歌和管理同一份佇列；房主處理操作順序、同步曲目與自動接歌。全員需使用本版，protocol 為 v3。
- 網址欄支援單曲與歌單：`Add last` 加到最後、`Play next` 插在下一首、`Play now` 中斷目前歌曲並播放新選擇。Enter 預設加入最後。歌單帶 `v` 時匯入該影片起的可讀取歌曲，純歌單網址從第一首起；指定影片不在讀取結果內會明確拒絕。
- 右側 Up next 顯示待播歌曲、點歌者與順序；支援拖曳排序、Up／Down、Remove、Play selected 與 Clear pending。歌名透過 YouTube oEmbed 背景讀取及記憶體快取，失敗時顯示影片 ID，不妨礙播放或操作。
- 每筆點歌都有獨立 ID，同一支影片可重複加入；排序以歌曲 ID 和目標歌曲 ID 傳送，不依賴可能過期的列號。房主套用操作時目標已不存在則拒絕並提示，不移動另一首歌。
- 佇列 revision 與播放 revision 分開：新增、刪除、排序、標題更新都不強制 seek 或重載目前歌曲。
- 歌單用獨立 WebView 解析，匯入時目前歌曲繼續。房主依收到的順序處理匯入，最多 8 個等待／處理中的匯入；清空待播或停止會取消尚未完成的匯入，過期回傳不再插入歌曲。
- Stop 停止並保留目前曲目和待播清單，Resume 由開頭重新播放；Pause／Resume 暫停和延續進度；Clear pending 只清待播，不中斷目前歌曲。停止後加入歌曲不會自動重啟，按 Resume 或 Play now 才開始。
- Previous 從最多 50 筆播放紀錄返回，原本的目前歌曲放回待播第一首；待播已滿時拒絕，避免遺失歌曲。Next 手動跳下一首。Repeat mode 在不循環／單曲／全部之間切換，手動 Next 可跳出單曲循環。
- 最多 200 筆待播，另有目前歌曲；一次匯入最多 200 首，以 IFrame API 實際回傳結果為準，不保證涵蓋超長歌單全部內容。整批加入超過容量時全部拒絕，不默默截掉部分歌曲。歌單遠端更新與跨房間儲存不在此版範圍。
- 房主離開聲音範圍仍保留靜音播放器接收結束事件。其他玩家自己的 ended 不會推進全房佇列；track token 防止過期或重複結束事件造成跳歌。
- YouTube 100／101／150 最多連續跳過 2 首，第 3 次失敗停止且保留佇列。其他播放錯誤停止並提示；匯入錯誤不停止原曲。音量、距離衰減與 BGM 讓位沿用。

驗證結果：

- 808 項佇列 ID、交錯操作、播放時間、容量、播放紀錄、循環及距離檢查；14 項歌單網址／解析檢查通過。
- 15 項正式 JukeboxSpeaker 測試通過，使用遊戲／網路替身驗證客戶端權限、房主遠距離接歌、排序不 seek、晚加入、取消匯入及 200 首 JSON 往返。這不是實際 Unity Netcode 雙人連線測試。
- 前一 build 的 1100x650／1000x600 版面、文字輸入與 WebView 初始化通過。真實 YouTube 雙播放器測試匯入範例歌單 200 首，原曲持續播放，排序後由 ended 事件從 `pEdxU1F-FE8` 接到 `cfS4YBuKgEw`，雙方均 playing；當次探測進度差約 0.18 秒，不是同步精度保證。
- 最後增加佇列 UI 操作自測後，重編的播放器 helper 被 Windows Application Control 阻擋，最後一輪 UI／播放回歸未能啟動；既有 UrlTests.exe 也被阻擋。未調整或繞過安全設定。837 項核心檢查在最後 build 後仍通過。
- 遊戲安裝的 TONY_BIG_SET.dll 保持 0.8.0；本版未安裝。遊戲內雙人、拖曳手感、全螢幕、距離與 BGM 行為，及目標機器是否允許執行 helper，仍待使用者允許更新後驗證。

可重跑 `powershell -File tests/run.ps1 -QueueOnly`；此選項只選取佇列相關測試，不執行遭阻擋的既有 URL／馬匹測試。加 `-Playlist` 測真實歌單匯入及雙播放器接歌，加 `-Speaker` 測既有音量／暫停／隱藏播放。`-WebView` 包含 UI 和既有 HostTests；若被 Windows 阻擋會失敗，不視為通過。

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

# Tony Ale & Tale Mods 0.8.1

Ale and Tale Tavern 的整合 BepInEx 模組，血量面板、YouTube 點唱機與雙人馬共用一個 DLL。

## 功能

- 雙人馬：商人購買，背包使用後在戶外放置；1 位駕駛＋1 位乘客，Ctrl+F1/F2 換位，附帶存檔；無人騎乘時對準馬長按 X 收回背包。[操作與限制](Horse/README.md)

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
首次開啟播放器會解出元件到 `BepInEx/cache/TonyAleTaleMods/0.8.1/`。
WebView2 Runtime 是額外系統依賴，瀏覽器資料位於 `%LOCALAPPDATA%/TonyAleTaleMods/WebView2Speaker/`。
請從 Microsoft 官方安裝 Runtime，本模組不會自動安裝系統元件。

[Microsoft WebView2](https://learn.microsoft.com/microsoft-edge/webview2/)

## 操作

1. 存檔、退出遊戲後更新 DLL，重新啟動。
2. 操作點唱機，按畫面上方 YouTube。
3. 在 Windows 原生網址欄貼影片或歌單網址，按 Add last 或 Enter 加到共用待播佇列，也可用 Play next 插歌或 Play now 立即播放；支援 Backspace、Delete、Ctrl+A、Ctrl+V，Clear 清空網址。若未自動播放，按影片內的播放按鈕。
4. 用點唱機原本的音量滑桿調音量；播放器 Pause／Resume／Seek (sec) 控制共用進度，Stop 或遊戲內 Stop YouTube 停止全員播放。控制音樂須在點唱機 8 公尺內。
5. 按 Back to game 或關閉視窗，再離開點唱機介面即可繼續聽；重新操作點唱機按 YouTube 可開回控制視窗。

房主統一維護歌單／曲目／暫停／進度，每位玩家自行載入同一支 YouTube 影片。全員需安裝 0.8.1；原生曲目會停止。3 公尺內保留點唱機音量，往外平滑衰減，30 公尺外靜音；音樂仍持續走時。這是距離衰減，不含方向聲像、遮蔽物或牆壁吸音。
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
