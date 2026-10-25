# Obenseuer-QuickBottleRecycling

BepInEx 6 mod for **Obenseuer** that lets you insert **all empty compatible bottles** into the bottle recycling machine with a single key press (or a panel button).

## Features

- One click / one key press inserts every **empty** (no liquid inside) bottle from your backpack that the recycling machine accepts.
- Adds an **"Add Empty Bottles"** button to the recycling machine panel (cloned from the vanilla "Get coins" button).
- Hotkey works whenever the recycling machine panel is open.
- Skipped items: bottles that still contain liquid, items flagged `ItemOwnerPermission.ownerPermission`, and anything the machine does not accept.

## How it works

Vanilla flow: drag each bottle onto `BottleRecyclingDrop` → `BottleRecyclingPanel.HandleInsertedItem` (which calls `Allowed()` → `PutItem()` → `BottleAdd()`) → `SlotController.RemoveItem()`.

This mod automates that flow for every stack returned by `Inventory.GetPlayerItems()`:

1. **Compatibility check** — `BottleRecyclingPanel.Allowed(item, amount, owner, meta, out _)` reuses the machine's exact `acceptedCategories` + `acceptedItems` lists and the `ItemOwnerPermission` guard.
2. **Empty check** — a stack is considered empty when it has no `ItemLiquidData` meta or `GetLiquidAmount(itemAmount) <= 0`.
3. **Insert + pay** — `HandleInsertedItem(...)` adds the bottle worth to `currentBottleRecycling.bottleRecyclingMoney`.
4. **Remove from backpack** — `ItemStack.RemoveItemAmount(...)` then `Inventory.ForceCharacterItemsUpdate()` refresh the UI.

The panel button is created in a Harmony postfix on `BottleRecyclingPanel.Open`, cloning `BottleRecyclingPanelUI.GetCoinsGO` and rewiring its click handler.

## Installation

1. Install [BepInEx 6](https://github.com/BepInEx/BepInEx) into the game folder.
2. Drop `Obenseuer-QuickBottleRecycling.dll` into `BepInEx/plugins`.
3. Launch the game, open a recycling machine panel and press the hotkey (or click **Add Empty Bottles**).

## Configuration

| Config | Section | Default | Description |
| --- | --- | --- | --- |
| `QuickInsertKey` | General | `F8` | Hotkey to insert all empty compatible bottles. |
| `AddPanelButton` | General | `true` | Add the "Add Empty Bottles" button to the recycling panel. |

Config file: `BepInEx/config/JuanBerta.Obenseuer.QuickBottleRecycling.cfg`

## Build

```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Obenseuer-QuickBottleRecycling.csproj /p:Configuration=Release /p:Platform=x64
```

Output: `bin\Release\Obenseuer-QuickBottleRecycling.dll`
