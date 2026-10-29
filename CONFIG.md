# Crafting Hub settings

Settings live in `BepInEx\config\com.dev.survivallog.craftinghub.cfg`, created the first time the game starts with the mod. Change them in game on the Mod Manager's Mods screen, where they take effect at once, or edit the file while the game is closed. Delete the file to get the defaults back. Without Exiled's Toolkit installed, changes made on the Mods screen take effect at the next game start instead.

## Storage

| Setting | Default | What it does |
| --- | --- | --- |
| `UseAllHomeStorages` | `true` | Workbenches use materials from every storage in your home, not only the storages linked to them. |

With `false`, workbenches use only their linked storages, as in the unmodded game. Intermediates and the queue still work, from those storages only.

## Chain

| Setting | Default | What it does |
| --- | --- | --- |
| `MaxDepth` | `4` | How many layers of intermediates can be crafted for one recipe. Making planks for a crate is one layer, and `0` turns intermediates off. |
| `ShowMarks` | `true` | Mark recipes you can make once their intermediates are crafted: they show "Makes first" in amber and get a "Craft with intermediates" button. |

`MaxDepth` takes a whole number. A negative number counts as `0`. With `0`, recipes are never marked and the queue only crafts what the house can make directly. Intermediates come only from recipes the workbench lists. Every workbench lists the same recipes, so it does not matter which one you use.

With `ShowMarks` set to `false`, recipes get no mark, so they show as Short rather than under Needs parts first, and the "Craft with intermediates" button does not show. Queued orders still make their intermediates, up to `MaxDepth`.

## Targets

| Setting | Default | What it does |
| --- | --- | --- |
| `KeepStocked` | `true` | Adds a Keep button to every recipe and a "Keep in stock" list under the queue. Each time you open a workbench, crafts are queued to bring kept items back up to their counts. |
| `AutoStart` | `true` | After kept items are topped up, start the queue by itself if nothing else was in it. `false` only queues the crafts, and you press Craft queue yourself. |

With `KeepStocked` set to `false`, the Keep buttons, the Kept in stock filter and the Keep in stock list are hidden and nothing is topped up. The counts you already set stay in `BepInEx\config\com.dev.survivallog.craftinghub.targets.txt` and come back when you turn it on again.

`AutoStart` never starts orders you queued yourself: when the queue already has something in it, top-ups are added to the end and wait for Craft queue.

## Queue

| Setting | Default | What it does |
| --- | --- | --- |
| `ResumeOnReopen` | `true` | Closing a workbench pauses a running queue, and it carries on when you open a workbench again. `false` stops the queue when the workbench closes, and you press Craft queue to carry on. |
| `OtherBenches` | `true` | A paused queue carries on at whichever workbench you open next. `false` waits for the workbench it was running on. Only matters when `ResumeOnReopen` is `true`. |

The game cancels a craft when the workbench window closes, so the queue cannot keep crafting with the window shut. With `true`, it waits and carries on about a second after you open a workbench again, planning from what your house holds then. You walk to the workbench and open it yourself; the mod never moves your character.

`OtherBenches` decides which workbench that can be. The game lists the same recipes at every workbench, so with `true` the queue carries on at whichever one you open, the same one or another. With `UseAllHomeStorages` set to `false`, another workbench plans from its own linked storages and can come up short; the queue then stops and says what it is short of. With `false`, the queue carries on only at the workbench it was running on: opening a different one leaves it waiting, and Stop, then Craft queue, runs it at the one you have open. `OtherBenches` does nothing when `ResumeOnReopen` is `false`.

Either way, crafts the game finished before the close count against their order and are not made again. The one exception is a single craft that finishes in the same moment you close the workbench, which can be made once more.

## Interface

| Setting | Default | What it does |
| --- | --- | --- |
| `UiScale` | `0` | How big Crafting Hub's own windows, panels and messages are drawn, in percent, from 75 to 200. At `0` they follow the UI size set in Exiled's Toolkit (its `UiScale`). Needs Exiled's Toolkit 0.11.0 or newer. |
