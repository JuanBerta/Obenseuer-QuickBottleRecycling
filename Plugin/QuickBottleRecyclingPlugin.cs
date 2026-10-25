using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
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
                    Log.LogInfo("[QuickBottleRecycling] QuickInsert skipped: found null or empty stack in player inventory.");
                    continue;
                }
				Item item = stack.itemReference.Item;
				if (item == null)
				{
                    Log.LogInfo("[QuickBottleRecycling] QuickInsert skipped: found null item in player inventory.");
                    continue;
                }
				object[] meta = stack.Meta ?? new object[0];
				if (!IsEmptyBottle(stack, meta))
				{
					Log.LogInfo("[QuickBottleRecycling] Skipping non-empty bottle: " + item.Title);
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

				// Create the "Add Empty Bottles" button from scratch instead of cloning the
		// original "Get coins" button. Cloning was abandoned after multiple rounds of
		// runtime failures: the clone always carried some form of the vanilla persistent
		// onClick (m_PersistentCalls -> BottleRecyclingPanelUI.GiveCoins ->
		// BottleRecyclingPanel.RemoveMoney -> Money.instance.AddMoney), and even after
		// destroying the Button component (3a9e742) the workflow stayed fragile.
		// A brand new GameObject carries zero vanilla components/events, so there is no
		// possible path from a click to the vanilla GiveCoins payout.
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
				if (parent == null)
				{
					return;
				}

				// Reuse the button if it already exists on this panel (same panel/scene).
				Transform existing = parent.Find(ButtonName);
				if (existing != null)
				{
					if (existing.gameObject.activeInHierarchy)
					{
						return;
					}
					UnityEngine.Object.Destroy(existing.gameObject);
				}

				// Build from scratch: a brand-new GameObject. No Instantiate of the
				// vanilla button, no Button component, no persistent calls anywhere.
				GameObject button = new GameObject(ButtonName, typeof(RectTransform));
				button.transform.SetParent(parent, false);

				RectTransform templateRt = template.GetComponent<RectTransform>();
				RectTransform rt = button.GetComponent<RectTransform>();
				if (templateRt != null)
				{
					rt.sizeDelta = templateRt.sizeDelta;
					rt.anchorMin = templateRt.anchorMin;
					rt.anchorMax = templateRt.anchorMax;
					rt.pivot = templateRt.pivot;
					rt.localScale = templateRt.localScale;
					Vector3 pos = templateRt.localPosition;
					pos.y += 70f;
					rt.localPosition = pos;
				}

				// Visual: Image copied visually only (sprite/color/type), raycastTarget on.
				Image templateImg = template.GetComponent<Image>();
				Image img = button.AddComponent<Image>();
				img.raycastTarget = true;
				if (templateImg != null)
				{
					img.sprite = templateImg.sprite;
					img.color = templateImg.color;
					img.type = templateImg.type;
				}
				else
				{
					img.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
				}

				// Visual: label text (fresh TextMeshProUGUI, style copied from template label).
				TextMeshProUGUI templateLabel = template.GetComponentInChildren<TextMeshProUGUI>();
				GameObject labelGo = new GameObject("Label", typeof(RectTransform));
				labelGo.transform.SetParent(button.transform, false);
				TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
				label.text = "Add Empty Bottles";
				label.alignment = TextAlignmentOptions.Center;
				label.raycastTarget = false;
				if (templateLabel != null)
				{
					label.font = templateLabel.font;
					label.fontSize = templateLabel.fontSize;
					label.color = templateLabel.color;
				}
				RectTransform labelRt = labelGo.GetComponent<RectTransform>();
				labelRt.anchorMin = Vector2.zero;
				labelRt.anchorMax = Vector2.one;
				labelRt.offsetMin = Vector2.zero;
				labelRt.offsetMax = Vector2.zero;

				// Click handling: sole handler is ClickCatcher (IPointerClickHandler).
				// No Button component exists on this object, so EventSystem press/release
				// always resolve to ClickCatcher and the vanilla GiveCoins path is unreachable.
				button.AddComponent<ClickCatcher>();
				Log.LogInfo("[QuickBottleRecycling] AddEmptyBottles button built from scratch: no vanilla Button/event copied, ClickCatcher attached.");
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

