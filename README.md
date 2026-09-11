# Ale & Tale Mods

Ale and Tale Tavern 的個人 BepInEx 模組。

## Teammate Health Bars

- 頭頂紅色血條，顯示隊友 HP。
- 左側中央隊伍面板，包含自己與隊友的名字、HP，單人也會顯示。
- 可調整大小、距離及遮擋顯示。

[使用說明與設定](TeammateHealthBars/README.md)

## 編譯

```powershell
.\TeammateHealthBars\build.ps1
# 自訂遊戲安裝位置
.\TeammateHealthBars\build.ps1 -GamePath 'D:\SteamLibrary\steamapps\common\Ale and Tale Tavern'
```

需 Windows .NET Framework compiler、已安裝的遊戲與 BepInEx 5。
編譯產物位於 `TeammateHealthBars/bin/Tony.TeammateHealthBars.dll`。
將 DLL 放入遊戲的 `BepInEx/plugins/Tony.TeammateHealthBars/` 後重啟遊戲。

此 repository 僅包含本模組原始碼與文件；遊戲 assemblies 及第三方模組請由本機安裝提供。
