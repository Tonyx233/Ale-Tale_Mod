# 馬匹外觀更新驗證（2026-09-13）

依使用者提供的栗色風格化馬匹參考，重做專用程序 mesh。70 部件、13 關節、7,752 三角面；原本 60 部件、7,200 三角面。保留雙座、骨架錨點與步態。非自動 image-to-3D，也未使用第三方模型。

- 改善胸腹連續輪廓、頸部收細、頭與口鼻、尖耳、白色額斑、鬃毛與尾巴分束、腿與平底馬蹄、雙人皮鞍、鞍墊、韁繩和中空馬鐙。
- `HorseModel` 讀取每部件的頂點與三角形，保留 flat normals，銷毀時釋放所有 mesh。
- 安裝前比對舊 DLL 與 Git build 的 IL，僅發現 JSON parser 與騎乘視角修補不同；兩者補回 source，避免重建遺失修補。
- 完整 build 通過；Newtonsoft.Json 引用 netstandard 2.0/2.1 產生 CS1701 相容性警告，遊戲內載入尚未验证。
- 17 URL cases、17,302 座位／步態 assertions、21 Harmony/API 檢查通過。
- 7,752 面均非退化、閉合、邊朝向一致、signed volume 向外；雙座、四蹄落點及 preview/model.json 一致性通過。
- 預覽 JavaScript 模擬 45 個待機／行走／奔跑 frame，頂點有限數及 draw count 通過。這不是實際 WebGL GPU 測試。
- `model-review.png` 直接從模型頂點離線渲染並檢視，修正鞍墊遮住馬鞍的問題。圖片非 Unity 遊戲截圖。
- 瀏覽器安全政策拒絕開啟本機 HTML，未繞過。WebGL preview 實際瀏覽器執行尚未驗證。

仍需遊戲內確認：Unity shader/光照、載入、雙人騎姿、上／下馬視角、跑步轉向與模型穿插、多人連線一致性。沒有宣稱完成這些實測。

重建：`node Horse/tools/create-horse-model.js Horse/Assets`，再執行 `build.ps1`。
幾何檢查：`node tests/validate-horse-geometry.js Horse/Assets/model.json`。
預覽計算檢查：`node tests/test-horse-preview.js Horse/Assets/preview.html`。
離線渲染需 Python、numpy、Pillow：`python Horse/tools/render-model.py Horse/Assets/model.json Horse/Assets/model-review.png`。
