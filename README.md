# Crafting Hub

Craft from your whole house, make intermediates on the way, queue different recipes, and keep items stocked.

A workbench in Survival Log only takes materials from the storages linked to it, makes one recipe at a time, and won't help when you have the raw materials but not the planks or parts in between. Crafting Hub lets every workbench use every storage in your home, marks recipes you could make once their intermediates are crafted, and crafts a queue of recipes for you, intermediates included. Tell it to keep 10 bandages and it tops them back up whenever you open a workbench.

## Features

- **Every home storage counts**: a workbench counts and takes materials from every storage in your home, not only the ones linked to it. This applies to the materials list, the craftable check, the most you can craft at once, and laying out the materials.
- **A wider workbench screen**: the workbench's recipe list is replaced by a wider one in two columns, with a craft queue column beside it. The bench grid and its Craft button move into the bag's panel as a Workbench tab next to the bag's own tab, so the screen has room to breathe; they work as before. Picking a recipe, starting a craft, or dropping an item on the bench brings the Workbench tab up, and any of the bag panel's other tabs takes you back. While dragging an item, rest it on the Workbench tab or on the open bag tab for a moment to switch between them. The game's own list is still there, only hidden, and every click still goes through the game.
- **Search**: type part of a recipe's name, or of a material it uses, in the search box at the top. When a material matches, it is outlined on the card. While you type in it, the workbench treats your keys as it does for its own search box. The x empties it.
- **Filters**: All, Can make now, Needs parts first, Short, and Kept in stock (when keep stocked is on), each with how many recipes it holds. Below them, a row with one button per recipe group, with how many in that group you can make; press it again to show every group. When the row holds more than fits, scroll it sideways with the mouse wheel. Click a group's header in the list to fold it away; the game remembers that as it always has.
- **Recipe cards**: each card shows the recipe, how many one craft makes (when it makes more than one), and one line saying "Can make now", what gets made first, or what you are short of. Each material shows what you have and what the amount on the card needs: green when you have enough, amber when a recipe on the list makes it, red when you are short. Locked recipes are greyed out and come last in their group. Click a card's picture or name to lay that recipe on the bench, as a click on the game's own list does; the rest of the card, its amount and its buttons leave the bench alone. While the queue is crafting or paused, the queue keeps the bench and a click on a card lays nothing.
- **Craftable through intermediates**: a recipe you can't make yet, but could after crafting its intermediates (planks for a crate, say), shows in amber with what gets made first, for example "Makes first: Plank x2". The Needs parts first filter lists just these.
- **Add**: the - and + on a card set how many crafts, and Add puts them at the end of the queue. Adding the same recipe as the last order adds to that order instead of making a new one.
- **Craft with intermediates**: when the recipe on the bench is one of those, this button under the bench's Craft button makes the intermediates and then the recipe, as many times as the amount set on its card. It goes to the front of the queue and starts right away. If a craft is already in progress, the order goes second and is made as soon as that craft is done. While the queue is paused it is not taken ("the queue is paused and carries on here in a moment; press Stop first to craft this now"), and the queue is left as it was.
- **The craft queue**: each order shows how many crafts it has (and how many items that makes), and where it stands: Ready, what gets made first, or what it will be short of. Change its number with - and +, move it with the arrows or by dragging it by its handle, and remove it with its x. Removing the order a craft is being made for stops the run; removing any other order leaves the run going. The running order has a bar that follows the game's own craft progress.
- **Materials for the whole queue**: everything the queue uses, how many you have, and what is short. **Made on the way** lists the intermediates the queue will craft. Before you start, a warning names the first order that will come up short, since the queue stops there.
- **Craft queue**: crafts the queue in order, making intermediates as needed.
- **Stop**: shown while the queue runs or is paused. Nothing more is started; the queue is kept.
- **Clear**: empties the queue (shown when the queue is not running).
- **Closing the workbench pauses the queue**: the game cancels a craft when its window closes, so the queue waits instead. Open any workbench and, after about a second, it carries on there where it left off. You walk to the workbench yourself. Crafts the game finished before the close are counted, so they are not made twice.
- **Keep in stock**: Keep on a card keeps 10 of what that recipe makes in your home. Press it again to stop keeping it. The Keep in stock list under the queue shows every item this save keeps, how many your home holds against the count, - and + to change the count, and an x to stop keeping it.
- **Topping up**: each time you open a workbench, it counts what your home holds of each kept item and queues the crafts that bring it back up to its count. With `AutoStart` on and nothing else in the queue, the queue starts by itself.
- **Stamina**: the queue column shows how much stamina you have, and each card shows the stamina one craft of it takes. When you are out of stamina, a bar across the queue column says so and the count reads "(out)".
- **Status line**: the queue column says what is crafting now, what was topped up, and why a run stopped.
- **Log file**: every step and every stop is written to `BepInEx\crafting-hub.log`.

Every craft goes through the workbench page's own clicks (pick the recipe, set the quantity, craft, close the result), so the game's own checks, locks and timings still apply.

Crafting Hub adds no data of its own to your save. Keep-stocked counts are kept in a separate file (see [Keep stocked](#keep-stocked)). The crafts it runs are real crafts: they use up materials from any storage in your home and add the results, exactly as if you had clicked them yourself. You can remove it at any time; workbenches go back to their linked storages, and nothing already crafted changes.

The panel and every message follow the game's language: English or Simplified Chinese. Recipe and item names come from the game as it words them.

## How it works

The intermediates for a recipe come only from recipes the workbench lists. Every workbench lists the same recipes (the game picks them by your level and what you have unlocked, not by which workbench it is), so any workbench can make every step. A recipe that is still locked is never marked and never used as an intermediate. `MaxDepth` limits how many layers of intermediates one recipe may need.

Before each step the run plans again from what your house holds right now, so materials you add or use while it runs are taken into account. Each batch is capped at the most the game allows with what is laid out.

Crafting only happens while the workbench window is open: the game cancels a craft in progress when its window closes, before any materials are used. So when you close the workbench during a run, the queue pauses ("the workbench closed; the queue carries on when you open any workbench"). Crafts the game finished before the close, a whole batch or part of one, count against their order. The craft that was cancelled is made again when the queue carries on.

A paused queue carries on at the next workbench you open, the same one or another, about a second after you open it, and plans again from what your house holds then. The mod never moves your character: you walk to a workbench and open it yourself. With `UseAllHomeStorages` off, another workbench plans from its own linked storages, so it can come up short where the first one did not; the run then stops and says what it is short of. Press Stop to end the pause. Nothing is topped up while a queue is paused. With `OtherBenches` off, the queue carries on only at the workbench it was running on; opening a different one leaves it waiting ("the queue carries on at the workbench it was running on; press Stop, then Craft queue, to run it here"), and Stop, then Craft queue, runs it at the one you have open. With `ResumeOnReopen` off, closing the workbench stops the run instead and Craft queue carries on.

As a safety net, if the workbench ever shows a batch in progress with nothing being made, the queue waits 3 seconds, says so on the status line, and carries on with the next craft. If the workbench shows a craft in progress with no change for 30 seconds, the status line says it is still waiting; the queue keeps waiting and does nothing else.

A run stops with a message in the status line when:

- the workbench window closes and `ResumeOnReopen` is off ("the workbench closed; the queue is kept, press Craft queue to carry on");
- a recipe is not in your recipe list yet or not unlocked yet;
- your house is short of something ("can't make ...: short of ...");
- you are out of stamina ("Out of stamina: Repair Parts needs 8 per craft, you have 3"); the Workbench tab also shows your stamina, the stamina each craft takes, and an "Out of stamina" bar while you cannot afford the next craft;
- the game locks the recipe (the game's own lock reason is shown), or the workbench holds more than one craft's worth of materials;
- the workbench is stuck after a craft and stays stuck (close it and open it again; the queue carries on);
- the workbench cannot be read for 3 seconds, the game makes nothing from a craft twice in a row, or 20 errors happen in a row (details in `BepInEx\crafting-hub.log`);
- the game does not lay out the materials within 6 seconds, a craft does not start within 4 seconds, or the result screen does not close within 4 seconds;
- 30 intermediate crafts have been made without finishing the recipe they were for.

## Keep stocked

A kept item is topped up about a second and a half after you open a workbench, and again right after you change its count. It is never checked on a timer or after a run ends, so a craft that keeps failing does not start another round by itself. If the workbench is busy when the check comes due, that check is skipped; close and open the workbench once it is free.

What your home holds is counted the way the game counts what you can craft from: your own inventory plus every storage in your home, hidden ones too. What is already in the queue counts as well, so opening the workbench twice does not queue the same crafts twice.

Crafts are only queued when the workbench is free (no craft running, no result showing, the queue not running) and the materials are in the house. Each order is capped at what the materials allow, after the orders already in the queue have taken theirs; if nothing can be made, the status line says which items are short and nothing is queued. Intermediates are made as for any other order, up to `MaxDepth`.

Topping up one item never uses what you keep of another. Keep 4 planks and 1 crate, and the crate's planks are made fresh rather than taken from the 4 you keep. Only what is above a kept count is used. Orders you queue yourself can still use anything in the house.

A recipe that makes several at once is crafted in whole crafts, so you can end up a little over the count (keep 5 planks with a recipe that makes 2, and you get 6).

Kept counts belong to the save you are playing. They are stored in `BepInEx\config\com.dev.survivallog.craftinghub.targets.txt`, one line per item, next to the name of the game's save file. Other saves' lines are never touched. If the file is there but cannot be read (another program holds it open, for example), no count can be changed until it reads again, so the save's other counts are never written over; the status line says so and the log has the reason.

## Known issues and limits

This mod uses materials from every storage in your home and crafts many times on its own. Back up your save before trying it for the first time.

- The queue only crafts at the workbench whose window is open, because the game uses your character for that. The mod never moves your character: when the queue pauses, you walk to a workbench and open it yourself. Anything made somewhere other than a workbench is not made for you; the run stops and says what it is short of.
- The queue lives in memory only: quitting the game or loading another save empties it.
- When the queue carries on after you open a workbench, it starts crafting right away. Press Stop first if you opened the workbench to do something else.
- A craft that is still going when you press Stop is not counted against its order, so starting the queue again can make that batch a second time. Check the queue before you press Craft queue again.
- If you close the workbench in the same moment a single craft finishes, that craft can be made once more when the queue carries on, using the materials for one more craft.
- A failed craft still counts against its order, because the game spent the materials. It is not retried; the next step is planned from what the house holds.
- A recipe's yield is read as one item per entry in the game's product list. The log lists the recipes this reads as making more than one; if one of them looks wrong in game, please report it.
- With `UseAllHomeStorages` on, the game's own way of excluding a storage from crafting still applies, but every other storage in your home is used, including ones you may have meant to keep for something else.
- A kept item is only topped up with the recipe you set the count on.
- Spoiled food counts toward a kept count, as the game counts it, but the game does not craft with it.
- Keep-stocked counts are tied to the save file's name. When the game starts a new file from an old one (rolling back or restarting from a snapshot), the new file starts with no kept counts.
- The recipe cards do not show the game's hover box for a recipe. Click a card to lay the recipe on the bench and see it there. The queue does not show how long a craft takes; the running order's bar follows the game's own progress bar instead.
- Right-clicking or shift-clicking an item while the bag tab is showing moves it to the workbench grid, which is hidden behind the Workbench tab; the view does not switch to it. Click the Workbench tab to see it.
- BetterCrafting also changes the workbench's storage list. Do not use both (see [Requirements](#requirements)).

If a craft uses the wrong materials, makes the wrong amount, or a run stops for no clear reason, please tell me (see [Feedback](#feedback-and-bug-reports)).

## Requirements

- Survival Log 1.1.18902. Built and tested on this version; a game update can break it until the mod is updated.
- The [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12), the BepInEx 6 (IL2CPP) build for the game.
- Not together with BetterCrafting. Both change which storages a workbench uses; remove BetterCrafting before installing this. If both are loaded, the log says so.

## Install

1. Install the [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12) (if no other mods were installed before, start the game once so BepInEx finishes setup, then quit).
2. Download `CraftingHub.dll` from the [latest release](https://github.com/AnExiledDev/SurvivalLog-CraftingHub/releases/latest) and put it in the `BepInEx\plugins` folder inside the game folder (the folder with the game .exe). Full path example: `C:\Program Files (x86)\Steam\steamapps\common\Survival Log\BepInEx\plugins\CraftingHub.dll`
3. If you use BetterCrafting, delete its DLL from `BepInEx\plugins`.

## Uninstall

Delete `CraftingHub.dll` from the `BepInEx\plugins` folder. For a clean slate, also delete `BepInEx\config\com.dev.survivallog.craftinghub.cfg`, `BepInEx\config\com.dev.survivallog.craftinghub.targets.txt` and `BepInEx\crafting-hub.log`.

## Configuration

Settings live in `BepInEx\config\com.dev.survivallog.craftinghub.cfg`, created the first time the game starts with the mod. Edit it while the game is closed; changes apply at the next game start. Each setting in the cfg file is explained in English and Simplified Chinese. See [CONFIG.md](CONFIG.md) for every setting.

## Troubleshooting

Tested on Survival Log 1.1.18902 with BepInEx `6.0.0-be.788`.

Check `BepInEx\LogOutput.log` for `===== Crafting Hub 0.7.1 started: all home storages on, intermediates up to 4 deep, keep stocked on, resume on reopen on, other workbenches on` (the words change with your settings). The same line, plus every step of every run and why it stopped, is in `BepInEx\crafting-hub.log`, which survives restarts. When that file passes 1 MB, it is cut down to its newest part at the next game start.

If workbenches still only see their linked storages, look in the logs for "BetterCrafting is also loaded".

## Changelog

- 0.7.1: The panel and every message follow the game's language, English or Simplified Chinese. Settings are explained in both. No setting changed.
- 0.7.0: Reliability and speed. A queued amount is really made: items crafted one at a time, like Repair Parts, are counted craft by craft instead of the whole amount being credited after one. Stamina shows on the Workbench tab with the cost per craft, and a run that runs out says so with the numbers. The tab redraws only what changed, so it is much lighter. Keep stocked counts items that do not stack, refills a kept part before using it in something else, and respects every order in the queue. The queue waits out a busy or briefly unreadable workbench instead of stopping, checks stock again right after a batch before calling an item short, and asks you to close and reopen the bench if the game gets stuck after a craft. Clicking a card only lays out the recipe from its picture or name, and never while the queue runs. "Craft with intermediates" uses the amount set on the card. No setting changed.
- 0.6.1: Fixes. When the game misses the queue's click on a recipe, the queue clicks it again, and it waits up to 6 seconds for the materials. A run that stops because the game allows no craft with the materials laid out now says you may be out of stamina. The bench no longer jumps up for crafts the queue starts or while you drag an item; a craft you start during a drag brings it up after the drop. Drop highlights follow you when a drag switches tabs, and the screen is no longer resized in the middle of a drag. If the Workbench tab cannot be drawn, the bench stays where the game puts it. No setting changed.
- 0.6.0: A roomier workbench screen. The bench grid and its Craft button are now a Workbench tab in the bag's panel, next to the bag, the drawer and the cabinets, instead of a column of their own. The bench comes up by itself when you pick a recipe, start a craft or drop an item on it, and resting a dragged item on a tab switches to it. No setting changed.
- 0.5.2: Plainer setting descriptions in the config file, each with a Simplified Chinese line under it. No setting or default changed.
- 0.5.1: A roomier recipe list. Recipe names are no longer cut off, the cards are shorter so more recipes fit on screen, and the group buttons sit in one row that you scroll sideways with the mouse wheel. The recipe list is a little wider and the queue a little narrower. Long names in the queue now get two lines, and the Top-up tag sits under the name, so neither is cut off.

## Feedback and bug reports

Found a bug, have an idea, or something feels off? Come say so on the Core Directive Discord: https://discord.gg/trTsyxUFqg . You can also leave a comment on the Nexus page or [open an issue](https://github.com/AnExiledDev/SurvivalLog-CraftingHub/issues). When reporting a bug, attach `BepInEx\LogOutput.log` and `BepInEx\crafting-hub.log`.

## Build

This is a BepInEx 6 IL2CPP plugin. It compiles against BepInEx and the game's IL2CPP interop assemblies, so a game install with BepInEx set up and started once is required. Those assemblies come from the game and are not part of this repo. The .NET 8 SDK is required.

```
dotnet build src/CraftingHub -c Release -p:GameDir="C:\path\to\Survival Log"
```

`GameDir` is the game folder. The default in the project file is a local path you will almost certainly need to override. The output DLL is at `src\CraftingHub\bin\Release\CraftingHub.dll`.

The unit tests cover the pure rules (planning intermediates, chain marks, the queue and the run, costing the whole queue, topping up kept items) and the workbench screen's script, run against a stand-in workbench page. They need nothing from the game:

```
dotnet test tests/CraftingHub.Tests
```

## License

MIT. Copyright (c) 2026 AnExiledDev. See [LICENSE](LICENSE).
