using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Configuration;
using HarmonyLib;
using OS.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QuickBottleRecyclingMod
{
	[BepInPlugin("JuanBerta.Obenseuer.QuickBottleRecycling", "Quick Bottle Recycling", "1.0.0")]
	public class QuickBottleRecyclingPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<KeyboardShortcut> QuickInsertKey;
		internal static ConfigEntry<bool> AddPanelButton;
		internal static BepInEx.Logging.ManualLogSource Log;

		private void Awake()
		{
			Log = Logger;
			QuickInsertKey = Config.Bind("General", "QuickInsertKey", new KeyboardShortcut(KeyCode.F8), "Quickly insert all empty compatible bottles from the backpack into the open recycling machine.");
			AddPanelButton = Config.Bind("General", "AddPanelButton", true, "Add an 'Add Empty Bottles' button to the bottle recycling machine panel.");
			Harmony.CreateAndPatchAll(typeof(BottleRecyclingPanel_Open_Patch));
			Log.LogInfo("Quick Bottle Recycling loaded.");
		}

		private void Update()
		{
			if (QuickInsertKey.Value.IsDown())
			{
				int n = QuickInsert();
				if (n > 0)
				{
					Log.LogInfo("Quick recycle: inserted " + n + " bottle stack(s).");
				}
			}
		}

		// Core: scan the player inventory, take every stack that the machine accepts and
		// that is empty (no liquid inside), add its worth to the machine and remove it from the backpack.
		public static int QuickInsert()
		{
			BottleRecyclingPanel panel = BottleRecyclingPanel.instance;
			if (panel == null)
			{
				Log.LogWarning("[QuickBottleRecycling] QuickInsert skipped: no BottleRecyclingPanel instance.");
				return 0;
			}
			if (!panel.open)
			{
				Log.LogWarning("[QuickBottleRecycling] QuickInsert skipped: panel is not open.");
				return 0;
			}
			if (panel.currentBottleRecycling == null)
			{
				Log.LogWarning("[QuickBottleRecycling] QuickInsert skipped: currentBottleRecycling is null.");
				return 0;
			}
			if (Inventory.instance == null)
			{
				Log.LogWarning("[QuickBottleRecycling] QuickInsert skipped: no Inventory instance.");
				return 0;
			}
			List<ItemStack> stacks = Inventory.instance.GetPlayerItems();
			if (stacks == null)
			{
				Log.LogWarning("[QuickBottleRecycling] QuickInsert skipped: GetPlayerItems() returned null.");
				return 0;
			}

			int emptyBottles = 0;
			int allowedBottles = 0;
			int inserted = 0;
			foreach (ItemStack stack in stacks)
			{
				if (stack == null || stack.IsEmpty())
				{
					continue;
				}
				Item item = stack.itemReference.Item;
				if (item == null)
				{
					continue;
				}
				object[] meta = stack.Meta ?? new object[0];
				if (!IsEmptyBottle(stack, meta))
				{
					continue;
				}
				emptyBottles++;
				if (!panel.Allowed(item, stack.itemAmount, stack.ownerId, meta, out _))
				{
					Log.LogInfo("[QuickBottleRecycling] Machine does not accept: " + item.Title);
					continue;
				}
				allowedBottles++;
				if (panel.HandleInsertedItem(item, stack.itemAmount, stack.ownerId, meta))
				{
					stack.RemoveItemAmount(stack.itemAmount);
					inserted++;
					Log.LogInfo("[QuickBottleRecycling] Inserted stack: " + item.Title + " x" + stack.itemAmount + " (machine money now " + panel.currentBottleRecycling.bottleRecyclingMoney + ").");
				}
				else
				{
					Log.LogWarning("[QuickBottleRecycling] HandleInsertedItem returned false for: " + item.Title);
				}
			}
			if (inserted > 0)
			{
				Inventory.instance.ForceCharacterItemsUpdate();
				if (panel.recyclingPanelUI != null && panel.recyclingPanelUI.bottleSumText != null)
				{
					panel.recyclingPanelUI.bottleSumText.text = panel.currentBottleRecycling.bottleRecyclingMoney.ToString("D6");
				}
			}
			Log.LogInfo("[QuickBottleRecycling] QuickInsert done: empty=" + emptyBottles + " allowed=" + allowedBottles + " inserted=" + inserted + ".");
			return inserted;
		}

		// "Empty" = the bottle has no liquid inside (no ItemLiquidData meta, or liquid amount <= 0).
		private static bool IsEmptyBottle(ItemStack stack, object[] meta)
		{
			ItemLiquidData liquid = stack.GetMetaOfType<ItemLiquidData>();
			if (liquid != null && liquid.GetLiquidAmount(stack.itemAmount) > 0f)
			{
				return false;
			}
			return true;
		}

		// Add a clone of the "Get coins" button onto the recycling panel.
		[HarmonyPatch(typeof(BottleRecyclingPanel), "Open")]
		internal static class BottleRecyclingPanel_Open_Patch
		{
			private const string ButtonName = "QuickBottleRecycling_AddEmptyBottles";

			internal static void Postfix(BottleRecyclingPanel __instance)
			{
				if (!AddPanelButton.Value)
				{
					return;
				}
				if (__instance == null || __instance.recyclingPanelUI == null)
				{
					return;
				}
				GameObject template = __instance.recyclingPanelUI.GetCoinsGO;
				if (template == null)
				{
					return;
				}
				Transform parent = template.transform.parent;

				// Find any previously created clone first. We do NOT keep a static reference
				// across panels/scenes: the panel UI may be destroyed or deactivated between
				// opens, and a stale static reference can point at a destroyed object.
				GameObject existing = null;
				if (parent != null)
				{
					Transform t = parent.Find(ButtonName);
					if (t != null)
					{
						existing = t.gameObject;
					}
				}
				if (existing != null && existing.activeInHierarchy)
				{
					return; // already visible on this panel
				}
				if (existing != null)
				{
					UnityEngine.Object.Destroy(existing);
				}

				GameObject button = UnityEngine.Object.Instantiate(template, parent);
				button.name = ButtonName;
				RectTransform rt = button.GetComponent<RectTransform>();
				if (rt != null)
				{
					Vector3 pos = rt.localPosition;
					pos.y += 70f;
					rt.localPosition = pos;
				}
				TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
				if (label != null)
				{
					label.text = "Add Empty Bottles";
				}

				// Make sure the clone is raycastable even if the template's Image had
				// raycastTarget disabled (e.g. it was only used as a visual template).
				Image img = button.GetComponent<Image>();
				if (img != null)
				{
					img.raycastTarget = true;
				}

				Button btn = button.GetComponent<Button>();
				if (btn != null)
				{
					// CRITICAL: the cloned "Get coins" button carries the original persistent onClick
					// call (m_PersistentCalls -> GiveCoins) which reflection cleanup proved unable to
					// fully neutralize at runtime. Previously we disabled the Button, but a disabled
					// Button STILL implements IPointerClickHandler, so ExecuteEvents.GetEventHandler
					// can still select it (it does not filter isActiveAndEnabled). That made the
					// EventSystem record the disabled Button as the press handler while the release
					// lookup picked the enabled ClickCatcher -> handler mismatch -> Unity logs
					// "handler was null" and the click is never delivered. If both lookups returned
					// the disabled Button, the click would be silently dropped by ShouldSendToComponent.
					// DETERMINISTIC FIX: destroy the original Button component entirely. Only the
					// Image (raycastTarget forced true) and our ClickCatcher remain on the object,
					// so the sole IPointerClickHandler is ClickCatcher: press and release always
					// resolve to the same handler, and the original GiveCoins persistent callback
					// (which lived on the destroyed Button) can never fire again.
					UnityEngine.Object.Destroy(btn);
					if (button.GetComponent<ClickCatcher>() == null)
					{
						button.AddComponent<ClickCatcher>();
					}
					Log.LogInfo("[QuickBottleRecycling] Clone button ready: original Button destroyed, Image raycastTarget on, ClickCatcher attached.");
				}
			}
		}

		// EventSystem click handler attached to the cloned button. Routes the click to QuickInsert()
		// without touching UnityEvent reflection.
		public class ClickCatcher : MonoBehaviour, IPointerClickHandler
		{
			public void OnPointerClick(PointerEventData eventData)
			{
				int n = QuickInsert();
				Log.LogInfo("[QuickBottleRecycling] Add Empty Bottles clicked; inserted " + n + " stack(s).");
			}
		}
	}
}

