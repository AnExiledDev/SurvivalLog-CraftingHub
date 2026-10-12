# Crafting Hub settings

Settings live in `BepInEx\config\com.dev.survivallog.craftinghub.cfg`, created the first time the game starts with the mod. Edit it while the game is closed; changes apply at the next game start. Delete the file to get the defaults back.

## Storage

| Setting | Default | What it does |
| --- | --- | --- |
| `UseAllHomeStorages` | `true` | Workbenches count and take materials from every storage in your home, not only the ones linked to them. |

With `false`, workbenches use only their linked storages, as in the unmodded game. Intermediates and the queue still work, from those storages only.

## Chain

| Setting | Default | What it does |
| --- | --- | --- |
| `MaxDepth` | `4` | How many layers of intermediates may be made for one recipe (planks for a crate is one layer). `0` turns intermediates off. |
| `ShowMarks` | `true` | Mark recipes you can make once their intermediates are crafted, and offer "Craft with intermediates" on them. |

`MaxDepth` takes a whole number. A negative number counts as `0`. With `0`, recipes are never marked and the queue only crafts what the house can make directly. Intermediates come only from recipes the workbench lists. Every workbench lists the same recipes, so it does not matter which one you use.

With `ShowMarks` set to `false`, recipes get no mark and the "Craft with intermediates" button does not show. Queued orders still make their intermediates, up to `MaxDepth`.

## Targets

| Setting | Default | What it does |
| --- | --- | --- |
| `KeepStocked` | `true` | Show "Keep stocked" under the selected recipe. Opening a workbench that makes a kept item queues the crafts that bring your home back up to its count. |
| `AutoStart` | `true` | Start the queue by itself after topping up, when the queue had nothing else in it. `false` only queues the crafts; press Start queue yourself. |

With `KeepStocked` set to `false`, the keep row and the kept items are hidden and nothing is topped up. The counts you already set stay in `BepInEx\config\com.dev.survivallog.craftinghub.targets.txt` and come back when you turn it on again.

`AutoStart` never starts orders you queued yourself: when the queue already has something in it, top-ups are added to the end and wait for Start queue.

## Queue

| Setting | Default | What it does |
| --- | --- | --- |
| `ResumeOnReopen` | `true` | Closing a workbench pauses a running queue, and it carries on when you open that workbench again. `false` stops the queue when the workbench closes; press Start queue to carry on. |
| `OtherBenches` | `true` | A paused queue carries on at whichever workbench you open next. `false` waits for the workbench it was running on, as in 0.3.0. |

The game cancels a craft when the workbench window closes, so the queue cannot keep crafting with the window shut. With `true`, it waits and carries on about a second after you open a workbench again, planning from what your house holds then. You walk to the workbench and open it yourself; the mod never moves your character.

`OtherBenches` decides which workbench that can be. The game lists the same recipes at every workbench, so with `true` the queue carries on at whichever one you open, the same one or another. With `UseAllHomeStorages` set to `false`, another workbench plans from its own linked storages and can come up short; the queue then stops and says what it is short of. With `false`, the queue carries on only at the workbench it was running on, exactly as in 0.3.0: opening a different one leaves it waiting, and Stop, then Start queue, runs it at the one you have open. `OtherBenches` does nothing when `ResumeOnReopen` is `false`.

Either way, crafts the game finished before the close count against their order and are not made again. The one exception is a single craft that finishes in the same moment you close the workbench, which can be made once more.
