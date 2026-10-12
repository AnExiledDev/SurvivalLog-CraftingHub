# Crafting Hub

Craft from your whole house, make intermediates on the way, queue different recipes, and keep items stocked.

A workbench in Survival Log only takes materials from the storages linked to it, makes one recipe at a time, and won't help when you have the raw materials but not the planks or parts in between. Crafting Hub lets every workbench use every storage in your home, marks recipes you could make once their intermediates are crafted, and crafts a queue of recipes for you, intermediates included. Tell it to keep 10 bandages and it tops them back up whenever you open a workbench that makes them.

## Features

- **Every home storage counts**: a workbench counts and takes materials from every storage in your home, not only the ones linked to it. This applies to the materials list, the craftable check, the most you can craft at once, and laying out the materials.
- **Craftable through intermediates**: a recipe you can't make yet, but could after crafting its intermediates (planks for a crate, say), is marked in the recipe list with a dashed amber left edge and a small hammer badge on its icon. Select it and the panel under the craft button says what gets made first, for example "Makes first: Plank x2".
- **Craft with intermediates**: on a marked recipe, this button makes the intermediates and then the recipe, using the number in the quantity box. It goes to the front of the queue and starts right away. If a craft is already in progress, the order goes second and is made as soon as that craft is done. While the queue is paused it is not taken ("the queue is paused and carries on at the workbench it was running on; press Stop first to craft this now"), and the queue is left as it was.
- **Add to queue**: queues the selected recipe with the number in the quantity box. Adding the same recipe as the last order adds to that order instead of making a new one.
- **Start queue**: crafts the queue in order, making intermediates as needed.
- **Stop**: shown while the queue runs or is paused. Nothing more is started; the queue is kept.
- **Closing the workbench pauses the queue**: the game cancels a craft when its window closes, so the queue waits instead. Open the same workbench again and, after about a second, it carries on where it left off. Crafts the game finished before the close are counted, so they are not made twice.
- **Queue chips**: each order shows as "Name xN" with a small x to remove it. Removing the order that is running stops the run.
- **Clear**: empties the queue (shown when the queue is not running).
- **Keep in stock**: under the selected recipe, a box with - and + buttons sets how many of its item to keep in your home (10 to start with), and **Keep stocked** saves that count. Once a count is kept the row shows "(keeping N)". Set 0 and press Keep stocked to stop keeping it.
- **Keep stocked chips**: every item this save keeps shows as "Name N" with a small x to stop keeping it.
- **Topping up**: each time you open a workbench that makes a kept item, it counts what your home holds and queues the crafts that bring it back up to its count. With `AutoStart` on and nothing else in the queue, the queue starts by itself.
- **Status line**: the panel says what is crafting now, what was topped up, and why a run stopped.
- **Log file**: every step and every stop is written to `BepInEx\crafting-hub.log`.

Every craft goes through the workbench page's own clicks (pick the recipe, set the quantity, craft, close the result), so the game's own checks, locks and timings still apply.

Crafting Hub adds no data of its own to your save. Keep-stocked counts are kept in a separate file (see [Keep stocked](#keep-stocked)). The crafts it runs are real crafts: they use up materials from any storage in your home and add the results, exactly as if you had clicked them yourself. You can remove it at any time; workbenches go back to their linked storages, and nothing already crafted changes.

Recipe and item names come from the game, so they show in whatever language the game uses. The mod's own buttons and messages are in English.

## How it works

The intermediates for a recipe come only from recipes the same workbench lists. A recipe that is still locked is never marked and never used as an intermediate. `MaxDepth` limits how many layers of intermediates one recipe may need.

Before each step the run plans again from what your house holds right now, so materials you add or use while it runs are taken into account. Each batch is capped at the most the game allows with what is laid out.

Crafting only happens while the workbench window is open: the game cancels a craft in progress when its window closes, before any materials are used. So when you close the workbench during a run, the queue pauses ("the workbench closed; the queue carries on when you open it again"). Crafts the game finished before the close, a whole batch or part of one, count against their order. The craft that was cancelled is made again when the queue carries on.

A paused queue carries on at the workbench it was running on, about a second after you open it, and plans again from what your house holds then. Opening a different workbench leaves it waiting ("the queue carries on at the workbench it was running on; press Stop, then Start, to run it here"). Press Stop to end the pause; Start then runs the queue at the workbench you have open. Nothing is topped up while a queue is paused. With `ResumeOnReopen` off, closing the workbench stops the run instead and Start carries on.

As a safety net, if the workbench ever shows a batch in progress with nothing being made, the queue waits 3 seconds, says so on the status line, and carries on with the next craft. If the workbench shows a craft in progress with no change for 30 seconds, the status line says it is still waiting; the queue keeps waiting and does nothing else.

A run stops with a message in the status line when:

- the workbench window closes and `ResumeOnReopen` is off ("the workbench closed; the queue is kept, press Start to carry on");
- a recipe is not at this workbench or not unlocked yet;
- your house is short of something ("can't make ...: short of ...");
- the game does not lay out the materials within 3 seconds, a craft does not start within 4 seconds, or the result screen does not close within 4 seconds;
- 30 intermediate crafts have been made without finishing the recipe they were for.

## Keep stocked

A kept item is topped up about a second and a half after you open a workbench that makes it, and again right after you change its count. It is never checked on a timer or after a run ends, so a craft that keeps failing does not start another round by itself. If the workbench is busy when the check comes due, that check is skipped; close and open the workbench once it is free.

What your home holds is counted the way the game counts what you can craft from: your own inventory plus every storage in your home, hidden ones too. What is already in the queue counts as well, so opening the workbench twice does not queue the same crafts twice.

Crafts are only queued when the workbench is free (no craft running, no result showing, the queue not running) and the materials are in the house. Each order is capped at what the materials allow, after the orders already in the queue have taken theirs; if nothing can be made, the status line says which items are short and nothing is queued. Intermediates are made as for any other order, up to `MaxDepth`.

Topping up one item never uses what you keep of another. Keep 4 planks and 1 crate, and the crate's planks are made fresh rather than taken from the 4 you keep. Only what is above a kept count is used. Orders you queue yourself can still use anything in the house.

A recipe that makes several at once is crafted in whole crafts, so you can end up a little over the count (keep 5 planks with a recipe that makes 2, and you get 6).

Kept counts belong to the save you are playing. They are stored in `BepInEx\config\com.dev.survivallog.craftinghub.targets.txt`, one line per item, next to the name of the game's save file. Other saves' lines are never touched. If the file is there but cannot be read (another program holds it open, for example), no count can be changed until it reads again, so the save's other counts are never written over; the status line says so and the log has the reason.

## Known issues and limits

This mod uses materials from every storage in your home and crafts many times on its own. Back up your save before trying it for the first time.

- Intermediates are only made at the workbench you are using. The game only crafts at a workbench whose window is open, and it uses your character for that, so the mod cannot craft at another workbench for you. When an intermediate comes from another workbench, make it there first.
- The queue lives in memory only: quitting the game or loading another save empties it.
- When the queue carries on after the workbench reopens, it starts crafting right away. Press Stop first if you opened the workbench to do something else.
- A craft that is still going when you press Stop is not counted against its order, so starting the queue again can make that batch a second time. Check the queue chips before you press Start again.
- If you close the workbench in the same moment a single craft finishes, that craft can be made once more when the queue carries on, using the materials for one more craft.
- A failed craft still counts against its order, because the game spent the materials. It is not retried; the next step is planned from what the house holds.
- A recipe's yield is read as one item per entry in the game's product list. The log lists the recipes this reads as making more than one; if one of them looks wrong in game, please report it.
- With `UseAllHomeStorages` on, the game's own way of excluding a storage from crafting still applies, but every other storage in your home is used, including ones you may have meant to keep for something else.
- A kept item is only topped up at a workbench that makes it, with the recipe you set the count on.
- Spoiled food counts toward a kept count, as the game counts it, but the game does not craft with it.
- Keep-stocked counts are tied to the save file's name. When the game starts a new file from an old one (rolling back or restarting from a snapshot), the new file starts with no kept counts.
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

Delete `CraftingHub.dll` from the `BepInEx\plugins` folder. For a clean slate, also delete `BepInEx\config\com.dev.survivallog.craftinghub.cfg`, `BepInEx\config\com.dev.survivallog.craftinghub.targets.txt` and `BepInEx\crafting-hub.log`.

## Configuration

Settings live in `BepInEx\config\com.dev.survivallog.craftinghub.cfg`, created the first time the game starts with the mod. Edit it while the game is closed; changes apply at the next game start. See [CONFIG.md](CONFIG.md) for every setting.

## Troubleshooting

Tested on Survival Log 1.1.18317 with BepInEx `6.0.0-be.788`.

Check `BepInEx\LogOutput.log` for `===== Crafting Hub 0.3.0 started: all home storages on, intermediates up to 4 deep, keep stocked on, resume on reopen on` (the words change with your settings). The same line, plus every step of every run and why it stopped, is in `BepInEx\crafting-hub.log`, which survives restarts. When that file passes 1 MB, it is cut down to its newest part at the next game start.

If workbenches still only see their linked storages, look in the logs for "BetterCrafting is also loaded".

## Feedback and bug reports

Found a bug, have an idea, or something feels off? Come say so on the Core Directive Discord: https://discord.gg/trTsyxUFqg . You can also leave a comment on the Nexus page or [open an issue](https://github.com/AnExiledDev/SurvivalLog-CraftingHub/issues). When reporting a bug, attach `BepInEx\LogOutput.log` and `BepInEx\crafting-hub.log`.

## Build

This is a BepInEx 6 IL2CPP plugin. It compiles against BepInEx and the game's IL2CPP interop assemblies, so a game install with BepInEx set up and started once is required. Those assemblies come from the game and are not part of this repo. The .NET 8 SDK is required.

```
dotnet build src/CraftingHub -c Release -p:GameDir="C:\path\to\Survival Log"
```

`GameDir` is the game folder. The default in the project file is a local path you will almost certainly need to override. The output DLL is at `src\CraftingHub\bin\Release\CraftingHub.dll`.

The unit tests cover the pure rules (planning intermediates, chain marks, the queue and the run, topping up kept items) and need nothing from the game:

```
dotnet test tests/CraftingHub.Tests
```

## License

MIT. Copyright (c) 2026 AnExiledDev. See [LICENSE](LICENSE).
