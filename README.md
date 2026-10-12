# Crafting Hub

Craft from your whole house, make intermediates on the way, and queue different recipes.

A workbench in Survival Log only takes materials from the storages linked to it, makes one recipe at a time, and won't help when you have the raw materials but not the planks or parts in between. Crafting Hub lets every workbench use every storage in your home, marks recipes you could make once their intermediates are crafted, and crafts a queue of recipes for you, intermediates included.

## Features

- **Every home storage counts**: a workbench counts and takes materials from every storage in your home, not only the ones linked to it. This applies to the materials list, the craftable check, the most you can craft at once, and laying out the materials.
- **Craftable through intermediates**: a recipe you can't make yet, but could after crafting its intermediates (planks for a crate, say), is marked in the recipe list with a dashed amber left edge and a small hammer badge on its icon. Select it and the panel under the craft button says what gets made first, for example "Makes first: Plank x2".
- **Craft with intermediates**: on a marked recipe, this button makes the intermediates and then the recipe, using the number in the quantity box. It goes to the front of the queue and starts right away.
- **Add to queue**: queues the selected recipe with the number in the quantity box. Adding the same recipe as the last order adds to that order instead of making a new one.
- **Start queue**: crafts the queue in order, making intermediates as needed.
- **Stop**: shown while the queue runs. Nothing more is started; the queue is kept.
- **Queue chips**: each order shows as "Name xN" with a small x to remove it. Removing the order that is running stops the run.
- **Clear**: empties the queue (shown when the queue is not running).
- **Status line**: the panel says what is crafting now and why a run stopped.
- **Log file**: every step and every stop is written to `BepInEx\crafting-hub.log`.

Every craft goes through the workbench page's own clicks (pick the recipe, set the quantity, craft, close the result), so the game's own checks, locks and timings still apply.

Crafting Hub adds no data of its own to your save. The crafts it runs are real crafts: they use up materials from any storage in your home and add the results, exactly as if you had clicked them yourself. You can remove it at any time; workbenches go back to their linked storages, and nothing already crafted changes.

Recipe and item names come from the game, so they show in whatever language the game uses. The mod's own buttons and messages are in English.

## How it works

The intermediates for a recipe come only from recipes the same workbench lists. A recipe that is still locked is never marked and never used as an intermediate. `MaxDepth` limits how many layers of intermediates one recipe may need.

Before each step the run plans again from what your house holds right now, so materials you add or use while it runs are taken into account. Each batch is capped at the most the game allows with what is laid out.

A run stops with a message in the status line when:

- the workbench window closes ("the workbench closed; the queue is kept, press Start to carry on");
- a recipe is not at this workbench or not unlocked yet;
- your house is short of something ("can't make ...: short of ...");
- the game does not lay out the materials within 3 seconds, a craft does not start within 4 seconds, or the result screen does not close within 4 seconds;
- 30 intermediate crafts have been made without finishing the recipe they were for.

## Known issues and limits

This mod uses materials from every storage in your home and crafts many times on its own. Back up your save before trying it for the first time.

- Intermediates must be made at the same workbench as the recipe that needs them.
- The queue lives in memory only: closing the workbench stops the run (Start carries on), and quitting the game empties it.
- A craft that is still going when you press Stop or close the workbench is not counted against its order, so starting the queue again can make that batch a second time. Check the queue chips before you press Start again.
- A failed craft still counts against its order, because the game spent the materials. It is not retried; the next step is planned from what the house holds.
- A recipe's yield is read as one item per entry in the game's product list. The log lists the recipes this reads as making more than one; if one of them looks wrong in game, please report it.
- With `UseAllHomeStorages` on, the game's own way of excluding a storage from crafting still applies, but every other storage in your home is used, including ones you may have meant to keep for something else.
- BetterCrafting also changes the workbench's storage list. Do not use both (see [Requirements](#requirements)).

If a craft uses the wrong materials, makes the wrong amount, or a run stops for no clear reason, please tell me (see [Feedback](#feedback-and-bug-reports)).

## Requirements

- Survival Log 1.1.18317. Built and tested on this version; a game update can break it until the mod is updated.
- The [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12), the BepInEx 6 (IL2CPP) build for the game.
- Not together with BetterCrafting. Both change which storages a workbench uses; remove BetterCrafting before installing this. If both are loaded, the log says so.

## Install

1. Install the [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12) (if no other mods were installed before, start the game once so BepInEx finishes setup, then quit).
2. Download `CraftingHub.dll` from the [latest release](https://github.com/AnExiledDev/SurvivalLog-CraftingHub/releases/latest) and put it in the `BepInEx\plugins` folder inside the game folder (the folder with the game .exe). Full path example: `C:\Program Files (x86)\Steam\steamapps\common\Survival Log\BepInEx\plugins\CraftingHub.dll`
3. If you use BetterCrafting, delete its DLL from `BepInEx\plugins`.

## Uninstall

Delete `CraftingHub.dll` from the `BepInEx\plugins` folder. For a clean slate, also delete `BepInEx\config\com.dev.survivallog.craftinghub.cfg` and `BepInEx\crafting-hub.log`.

## Configuration

Settings live in `BepInEx\config\com.dev.survivallog.craftinghub.cfg`, created the first time the game starts with the mod. Edit it while the game is closed; changes apply at the next game start. See [CONFIG.md](CONFIG.md) for every setting.

## Troubleshooting

Tested on Survival Log 1.1.18317 with BepInEx `6.0.0-be.788`.

Check `BepInEx\LogOutput.log` for `===== Crafting Hub 0.1.1 started: all home storages on, intermediates up to 4 deep` (the words change with your settings). The same line, plus every step of every run and why it stopped, is in `BepInEx\crafting-hub.log`, which survives restarts. When that file passes 1 MB, it is cut down to its newest part at the next game start.

If workbenches still only see their linked storages, look in the logs for "BetterCrafting is also loaded".

## Feedback and bug reports

Found a bug, have an idea, or something feels off? Come say so on the Core Directive Discord: https://discord.gg/trTsyxUFqg . You can also leave a comment on the Nexus page or [open an issue](https://github.com/AnExiledDev/SurvivalLog-CraftingHub/issues). When reporting a bug, attach `BepInEx\LogOutput.log` and `BepInEx\crafting-hub.log`.

## Build

This is a BepInEx 6 IL2CPP plugin. It compiles against BepInEx and the game's IL2CPP interop assemblies, so a game install with BepInEx set up and started once is required. Those assemblies come from the game and are not part of this repo. The .NET 8 SDK is required.

```
dotnet build src/CraftingHub -c Release -p:GameDir="C:\path\to\Survival Log"
```

`GameDir` is the game folder. The default in the project file is a local path you will almost certainly need to override. The output DLL is at `src\CraftingHub\bin\Release\CraftingHub.dll`.

The unit tests cover the pure rules (planning intermediates, chain marks, the queue and the run) and need nothing from the game:

```
dotnet test tests/CraftingHub.Tests
```

## License

MIT. Copyright (c) 2026 AnExiledDev. See [LICENSE](LICENSE).
