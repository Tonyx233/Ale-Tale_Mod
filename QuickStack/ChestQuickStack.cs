using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace TonyMods
{
    // Chest quick stack: Q or the button beside "Take all" moves backpack items
    // (hotbar 1-0 excluded) whose kind the open chest already holds. Client-only:
    // it sends the vanilla double-click RPC, so the host needs no mod.
    public sealed class ChestQuickStack : MonoBehaviour
    {
        private const string ButtonName = "TonyQuickStack";
        private const float Cooldown = 0.3f;
        private const float Gap = 20f;
        private const int MoveSound = 162; // SoundEvent played by vanilla double-click move
        private ConfigEntry<bool> enabledSetting;
        private ConfigEntry<KeyCode> key;
        private ManualLogSource log;
        private InventoryUI screen;
        private Button button;
        private TextMeshProUGUI label;
        private string labelLocale;
        private KeyCode labelKey;
        private float nextUse;
        private bool failed, reportedMissing;

        public void Initialize(ConfigFile config, ManualLogSource logger)
        {
            log = logger;
            enabledSetting = config.Bind("QuickStack", "Enabled", true, "Chest quick stack: move backpack items (not hotbar 1-0) whose kind the open chest already holds.");
            key = config.Bind("QuickStack", "Key", KeyCode.Q, "Quick stack key while a chest is open.");
            log.LogInfo("Chest quick stack ready: " + key.Value + " or the button beside Take all; hotbar 1-0 kept.");
        }

        private void Update()
        {
            if (failed) return;
            try { Tick(); }
            catch (Exception ex)
            {
                failed = true;
                if (button != null) button.gameObject.SetActive(false);
                log.LogError("Chest quick stack disabled: " + ex);
            }
        }

        private void Tick()
        {
            PlayerInventory player = PlayerInventory.Instance;
            ContainerNet chest = player != null && player.state == PlayerInventory.State.Storage ? player.extContainer : null;
            if (chest == null || !enabledSetting.Value)
            {
                if (button != null) button.gameObject.SetActive(false);
                return;
            }
            bool usable = Usable(player, chest);
            EnsureButton(chest);
            if (button != null)
            {
                if (button.gameObject.activeSelf != usable) button.gameObject.SetActive(usable);
                if (usable) RefreshLabel();
            }
            if (usable && Input.GetKeyDown(key.Value) && !Typing()) Deposit(player, chest);
        }

        private static bool Usable(PlayerInventory player, ContainerNet chest)
        {
            return chest.IsSpawned && player.inventory != null && player.inventory.orderedItems != null && chest.orderedItems != null && ItemManager.Instance != null &&
                QuickStackRules.CanTarget(chest.id.Value, chest.isShop, chest.isPet);
        }

        private void OnClick()
        {
            PlayerInventory player = PlayerInventory.Instance;
            if (player == null || player.state != PlayerInventory.State.Storage) return;
            ContainerNet chest = player.extContainer;
            if (chest != null && Usable(player, chest)) Deposit(player, chest);
        }

        // orderedItems, not the NetworkList: csc 4 rejects NetworkList<T>'s unmanaged
        // constraint, and every client rebuilds orderedItems on each list change.
        private void Deposit(PlayerInventory player, ContainerNet chest)
        {
            if (Time.unscaledTime < nextUse) return;
            nextUse = Time.unscaledTime + Cooldown;
            ItemManager manager = ItemManager.Instance;
            List<Item> moves = QuickStackRules.Select(player.inventory.orderedItems, chest.orderedItems, chest.isGeneric, id =>
            {
                ItemData data;
                return manager.GetItemData(id, out data) ? data : null;
            });
            ushort target = chest.id.Value;
            // Same RPC and argument order as InventoryUI.OnItemDoubleClickBP. The host
            // ignores ids that already moved, so a stale repeat cannot duplicate items.
            foreach (Item item in moves) manager.MoveItemToContServerRpc(item.id, item.contId, target);
            if (moves.Count > 0 && SoundManager.Instance != null) SoundManager.Instance.Play((SoundEvent)MoveSound);
        }

        private static bool Typing()
        {
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null) return false;
            TMP_InputField field = selected.GetComponent<TMP_InputField>();
            if (field != null && field.isFocused) return true;
            InputField legacy = selected.GetComponent<InputField>();
            return legacy != null && legacy.isFocused;
        }

        private void EnsureButton(ContainerNet chest)
        {
            if (button != null && screen != null && screen.invExt != null && screen.invExt.container == chest) return;
            screen = null;
            foreach (InventoryUI ui in FindObjectsOfType<InventoryUI>())
                if (ui.invExt != null && ui.invExt.container == chest) { screen = ui; break; }
            if (screen == null) return;
            Transform panel = screen.invExt.transform.parent;
            Transform existing = panel.Find(ButtonName);
            if (existing != null) { Bind(existing.gameObject); return; }
            Button takeAll = FindNative(panel, "TakeAll");
            if (takeAll == null)
            {
                if (!reportedMissing) log.LogWarning("Chest quick stack: Take all button not found; Q still works.");
                reportedMissing = true;
                return;
            }
            // Clone under an inactive holder so the copied LocalizeStringEvent never
            // enables and later overwrites our label with "Take all".
            GameObject holder = new GameObject("TonyQuickStackHolder");
            holder.SetActive(false);
            GameObject copy = Instantiate(takeAll.gameObject, holder.transform);
            copy.name = ButtonName;
            foreach (LocalizeStringEvent localize in copy.GetComponentsInChildren<LocalizeStringEvent>(true)) DestroyImmediate(localize);
            copy.transform.SetParent(panel, false);
            Destroy(holder);
            Layout(FindNative(panel, "Sort"), takeAll, (RectTransform)copy.transform);
            Bind(copy);
            log.LogInfo("Chest quick stack button added beside Take all.");
        }

        private void Bind(GameObject copy)
        {
            button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(OnClick);
            label = copy.GetComponentInChildren<TextMeshProUGUI>(true);
            labelLocale = null;
            if (label != null)
            {
                // Narrower padding than the native 25px so "一鍵放入 (Q)" keeps a readable size.
                RectTransform text = label.rectTransform;
                text.sizeDelta = new Vector2(Mathf.Max(text.sizeDelta.x, -20f), text.sizeDelta.y);
            }
        }

        private Button FindNative(Transform panel, string method)
        {
            foreach (Button candidate in panel.GetComponentsInChildren<Button>(true))
                for (int i = 0; i < candidate.onClick.GetPersistentEventCount(); i++)
                    if (candidate.onClick.GetPersistentMethodName(i) == method && candidate.onClick.GetPersistentTarget(i) == screen.invExt)
                        return candidate;
            return null;
        }

        // Native Sort/Take all sit at x=-180/+180 (200 wide) with a 160px gap: too small
        // for a third button. Split their outer span into Sort | Quick stack | Take all.
        private static void Layout(Button sort, Button takeAll, RectTransform added)
        {
            RectTransform right = (RectTransform)takeAll.transform;
            RectTransform left = sort != null ? (RectTransform)sort.transform : null;
            if (left == null || left.parent != right.parent || Mathf.Abs(left.anchoredPosition.y - right.anchoredPosition.y) > 1f)
            {
                Place(added, LeftEdge(right) - Gap - right.sizeDelta.x, right.sizeDelta.x);
                return;
            }
            float start = LeftEdge(left);
            float end = LeftEdge(right) + right.sizeDelta.x;
            float width = (end - start - 2 * Gap) / 3;
            Place(left, start, width);
            Place(added, start + width + Gap, width);
            Place(right, end - width, width);
            foreach (RectTransform rect in new[] { left, right, added }) FitLabel(rect);
        }

        private static float LeftEdge(RectTransform rect)
        {
            return rect.anchoredPosition.x - rect.pivot.x * rect.sizeDelta.x;
        }

        private static void Place(RectTransform rect, float leftEdge, float width)
        {
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(leftEdge + rect.pivot.x * width, rect.anchoredPosition.y);
        }

        // Shrink-only autosize: text that already fits keeps its native size.
        private static void FitLabel(RectTransform rect)
        {
            TextMeshProUGUI text = rect.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text == null || text.enableAutoSizing) return;
            float size = text.fontSize;
            text.fontSizeMax = size;
            text.fontSizeMin = Mathf.Max(10f, size * 0.5f);
            text.enableAutoSizing = true;
        }

        private void RefreshLabel()
        {
            if (label == null) return;
            string locale = LocalizationSettings.SelectedLocale != null ? LocalizationSettings.SelectedLocale.Identifier.Code : "";
            if (locale == labelLocale && key.Value == labelKey) return;
            labelLocale = locale;
            labelKey = key.Value;
            string text = "Quick stack";
            if (locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                const string zh = "一鍵放入";
                uint[] missing;
                if (label.font == null || label.font.HasCharacters(zh, out missing, true, true)) text = zh;
            }
            label.text = text + " (" + key.Value + ")";
        }
    }
}
