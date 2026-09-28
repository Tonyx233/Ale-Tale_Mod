using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace TonyMods
{
    public sealed class TideHuntHome : MonoBehaviour
    {
        internal HelperHouse House { get; private set; }
        internal ContainerNet Cargo { get { return House.container; } }
        internal bool Active { get { return House != null && House.isHelperActive.Value; } }
        internal TideCreature Body;
        private double nextSpawn, nextCheck;
        private bool closed;
        private TextMeshPro label;
        private bool? lastActive;
        private Vector3 delivery;
        private bool hasDelivery;
        private double nextDiagnostic;
        private Vector3 diagnosticPosition;
        private void DiagnoseMovement()
        {
            if (Body == null || TideSummons.Now < nextDiagnostic) return;
            nextDiagnostic = TideSummons.Now + 10;
            var agent = Body.GetComponent<NavMeshAgent>();
            bool onMesh = agent != null && agent.enabled && agent.isOnNavMesh;
            Vector3 position = Body.transform.position;
            string nav = !onMesh ? "onMesh=false" : "onMesh=true, stopped=" + agent.isStopped +
                ", pending=" + agent.pathPending + ", hasPath=" + agent.hasPath + ", path=" + agent.pathStatus +
                ", remaining=" + agent.remainingDistance + ", destination=" + agent.destination +
                ", velocity=" + agent.velocity + ", desired=" + agent.desiredVelocity + ", speed=" + agent.speed +
                ", updatePosition=" + agent.updatePosition + ", nextPosition=" + agent.nextPosition;
            Debug.Log("Tide hunting navigation: base=" + transform.position + ", worker=" + position +
                ", moved=" + Vector3.Distance(position, diagnosticPosition) + ", action=" + Body.State.action +
                ", active=" + Active + ", cargo=" + Count + ", delivery=" + hasDelivery + "; " + nav + "; " + Body.HuntingDiagnostics());
            diagnosticPosition = position;
        }
        private string status = "準備出勤";
        internal void Report(string value)
        {
            if (status == value) return;
            status = value;
            Debug.Log("Tide hunting base " + transform.position + ": " + value);
        }
        internal int Count
        {
            get { return Cargo.orderedItems == null ? 0 : Cargo.orderedItems.Sum(i => (int)i.amount); }
        }
        internal void Initialize(HelperHouse house)
        {
            House = house;
            house.container.ignoreInteraction = true;
            house.interactive.IsPressAvailable = false;
            house.interactive.onInteract += Interact;
            house.interactive.ObjectTitle = "TonyTideHuntName";
            if (house.IsServer)
            {
                house.isHelperActive.Value = SaveManager.Instance == null || !SaveManager.Instance.isLoadingGame ||
                    house.savedHelperHouse == null || house.savedHelperHouse.isHelperActive;
                house.container.contName.Value = TideHuntBuilding.Marker;
            }
            // A small local sign identifies the independent variant without changing network prefab components.
            var sign = new GameObject("TideHuntSign"); sign.transform.SetParent(transform, false);
            sign.transform.localPosition = new Vector3(0, 3.2f, 0);
            label = sign.AddComponent<TextMeshPro>(); label.fontSize = 2;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(4, 1);
            label.color = new Color(.4f, 1, .85f);
        }
        private void Interact(Interactive.Event action, ushort type, uint id)
        {
            if ((int)action == 4 && House != null) AccessTools.Method(typeof(HelperHouse), "ToggleHelperActiveServerRpc").Invoke(House, null);
        }
        private void Update()
        {
            if (closed || House == null || !House.IsSpawned) return;
            if (label != null)
            {
                label.text = "十魚架(狩獵)\n" + Count + "/30  " + (House.IsServer ? status : (Active ? "啟用" : "返回／待命"));
                if (Camera.main != null) label.transform.rotation = Camera.main.transform.rotation;
            }
            if (lastActive != Active)
            {
                lastActive = Active;
                House.interactive.SetInteractParams((Interactive.Event)4, true, Active ? "停止狩獵並返回交貨" : "開始狩獵", 0);
            }
            if (!House.IsServer || Time.timeScale <= 0 || TideSummons.Now < nextCheck) return;
            nextCheck = TideSummons.Now + 1;
            // isLoadingGame describes how this session started and stays true throughout a loaded game.
            // Native PlayerManager marks the host ready only after its local player has spawned.
            if (Master.Instance == null || !Master.Instance.isHostReady) { Report("等待房主角色就緒"); return; }
            if (Master.Instance != null && Master.Instance.HasConnectingClients()) { Report("等待玩家連線完成"); return; }
            try
            {
                // Cargo has no public inventory UI; native container serialization preserves every Item field.
                if (Cargo.size.Value.x * Cargo.size.Value.y < 256) Cargo.Resize(new Vector2Int(16, 16));
                GetDelivery(out delivery);
                DiagnoseMovement();
                House.GetComponent<Furniture>().isRemoveAvailable.Value = Count == 0;
                if (Body != null && Body.GetComponent<Vulnerable>().hp.Value == 0)
                {
                    // Death leaves actual ground loot at the corpse; no second copy remains in the saved cargo.
                    DropAt(Body.transform.position); House.isHelperActive.Value = false;
                    nextSpawn = TideSummons.Now + 10;
                }
                if (Body != null) Report(!hasDelivery ? "已生成，等待酒館交貨點" : Active ? "出勤中" : "返回／待命");
                else if (!Active && Count == 0) Report("已停工，長按開始狩獵");
                // The worker must be visible even when delivery-point discovery has not succeeded yet.
                if (Body == null && (Active || Count > 0) && TideSummons.Now >= nextSpawn)
                {
                    nextSpawn = TideSummons.Now + 5;
                    NavMeshHit nav;
                    if (NavMesh.SamplePosition(transform.position + transform.forward * 2, out nav, 4, NavMesh.AllAreas))
                        Body = TideSummons.SpawnHunter(this, nav.position);
                    else Report("基地附近無可行走地面，請移到戶外");
                }
            }
            catch (Exception ex) { House.isHelperActive.Value = false; Report("生成或工作失敗，請查看 log"); Debug.LogError("Tide hunting paused, cargo retained: " + ex); }
        }
        internal bool GetDelivery(out Vector3 point)
        {
            if (hasDelivery) { point = delivery; return true; }
            // The named tavern sign identifies the FRONT, not whichever player/house happens to be closest.
            TavernSign sign = UnityEngine.Object.FindObjectOfType<TavernSign>();
            if (sign != null && Game.Instance != null)
            {
                Vector3 outward = sign.transform.position - Game.tavernCenter; outward.y = 0;
                if (outward.sqrMagnitude > .1f)
                {
                    Vector3 desired = sign.transform.position + outward.normalized * 3;
                    // Use the sign's actual elevation; tavernCenter.y is a fixed world constant, not terrain height.
                    NavMeshHit nav;
                    // Native ground loot inside tavernRadius also survives reload without auto-selling.
                    if (NavMesh.SamplePosition(desired, out nav, 6, NavMesh.AllAreas) &&
                        Vector3.Distance(nav.position, Game.tavernCenter) + 2 < Game.tavernRadius)
                    { delivery = nav.position; hasDelivery = true; }
                }
            }
            point = delivery; return hasDelivery;
        }
        internal void DropAt(Vector3 point)
        {
            if (!House.IsServer || CollectibleManager.Instance == null || Master.Instance != null && Master.Instance.HasConnectingClients()) return;
            // Keep each original item (quality, durability and charge). Removing after successful Spawn makes
            // retries safe when the manager rejects an item; no random loot is rolled again on delivery.
            int offset = 0;
            foreach (Item carried in Cargo.orderedItems.Where(i => i.amount > 0).ToArray())
            {
                Item drop = carried;
                drop.id = 0; drop.contId = 0; drop.order = 0;
                Vector3 pos = point + new Vector3((offset % 5 - 2) * .3f, .3f, (offset / 5 % 5) * .3f);
                uint deliveryId = (uint)AccessTools.Method(typeof(CollectibleManager), "GenId").Invoke(CollectibleManager.Instance, null);
                CollectibleNet spawned;
                try { spawned = CollectibleManager.Instance.Spawn(drop, pos, Quaternion.identity, deliveryId, true); }
                catch
                {
                    // A native callback can throw AFTER spawning. Resolve our known ID before retrying, so
                    // already visible loot is committed once; roll back an unspawned partial object instead.
                    if (!CollectibleManager.Instance.GetCollectible(deliveryId, out spawned, false)) throw;
                    if (!spawned.IsSpawned)
                    {
                        CollectibleManager.Instance.RemoveCollectible(deliveryId);
                        UnityEngine.Object.Destroy(spawned.gameObject);
                        throw;
                    }
                }
                if (spawned == null) return;
                if (!Cargo.RemoveItemById(carried.id)) throw new InvalidOperationException("Cargo changed during delivery");
                offset++;
            }
        }
        internal void Close()
        {
            if (closed) return; closed = true;
            if (House != null) House.interactive.onInteract -= Interact;
            if (Body != null) TideSummons.RemoveHunter(Body);
            Body = null;
        }
        private void OnDestroy() { Close(); }
    }
}
