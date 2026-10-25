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
using UnityEngine.UI;

namespace QuickBottleRecyclingMod
{
	[BepInPlugin("JuanBerta.Obenseuer.QuickBottleRecycling", "Quick Bottle Recycling", "1.0.0")]
	public class QuickBottleRecyclingPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<KeyboardShortcut> QuickInsertKey;
		internal static ConfigEntry<bool> AddPanelButton;
		private static GameObject cachedButton;

		private void Awake()
		{
			QuickInsertKey = Config.Bind("General", "QuickInsertKey", new KeyboardShortcut(KeyCode.F8), "Quickly insert all empty compatible bottles from the backpack into the open recycling machine.");
			AddPanelButton = Config.Bind("General", "AddPanelButton", true, "Add an 'Add Empty Bottles' button to the bottle recycling machine panel.");
			Harmony.CreateAndPatchAll(typeof(BottleRecyclingPanel_Open_Patch));
			Logger.LogInfo("Quick Bottle Recycling loaded.");
		}

		private void Update()
		{
			if (QuickInsertKey.Value.IsDown())
			{
				int n = QuickInsert();
				if (n > 0)
				{
					Logger.LogInfo("Quick recycle: inserted " + n + " bottle stack(s).");
				}
			}
		}

		// Core: scan the player inventory, take every stack that the machine accepts and
		// that is empty (no liquid inside), add its worth to the machine and remove it from the backpack.
		public static int QuickInsert()
		{
			BottleRecyclingPanel panel = BottleRecyclingPanel.instance;
			if (panel == null || !panel.open || panel.currentBottleRecycling == null)
			{
				return 0;
			}
			if (Inventory.instance == null)
			{
				return 0;
			}
			List<ItemStack> stacks = Inventory.instance.GetPlayerItems();
			if (stacks == null)
			{
				return 0;
			}
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
				if (!panel.Allowed(item, stack.itemAmount, stack.ownerId, meta, out _))
				{
					continue;
				}
				if (panel.HandleInsertedItem(item, stack.itemAmount, stack.ownerId, meta))
				{
					stack.RemoveItemAmount(stack.itemAmount);
					inserted++;
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
				if (cachedButton != null && cachedButton.activeInHierarchy)
				{
					return;
				}
				if (cachedButton != null)
				{
					UnityEngine.Object.Destroy(cachedButton);
				}
				cachedButton = UnityEngine.Object.Instantiate(template, template.transform.parent);
				cachedButton.name = "QuickBottleRecycling_AddEmptyBottles";
				RectTransform rt = cachedButton.GetComponent<RectTransform>();
				if (rt != null)
				{
					Vector3 pos = rt.localPosition;
					pos.y += 70f;
					rt.localPosition = pos;
				}
				TextMeshProUGUI label = cachedButton.GetComponentInChildren<TextMeshProUGUI>();
				if (label != null)
				{
					label.text = "Add Empty Bottles";
				}
				Button btn = cachedButton.GetComponent<Button>();
				if (btn != null)
				{
					// onClick is accessed via reflection to stay compatible with the exact UnityEngine.UI build shipped with the game.
					object clickEvent = Traverse.Create(btn).Property("onClick").GetValue();
					if (clickEvent != null)
					{
						Traverse.Create(clickEvent).Method("RemoveAllListeners").GetValue();
						Type actionType = Type.GetType("UnityEngine.Events.UnityAction, UnityEngine.CoreModule");
						if (actionType != null)
						{
							Delegate handler = Delegate.CreateDelegate(actionType, typeof(QuickBottleRecyclingPlugin), "OnAddEmptyBottlesClick");
							Traverse.Create(clickEvent).Method("AddListener", handler).GetValue();
						}
					}
				}
			}
		}

		public static void OnAddEmptyBottlesClick()
		{
			QuickInsert();
		}
	}
}
