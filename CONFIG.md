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

`MaxDepth` takes a whole number. A negative number counts as `0`. With `0`, recipes are never marked and the queue only crafts what the house can make directly.

With `ShowMarks` set to `false`, recipes get no mark and the "Craft with intermediates" button does not show. Queued orders still make their intermediates, up to `MaxDepth`.

## Targets

| Setting | Default | What it does |
| --- | --- | --- |
| `KeepStocked` | `true` | Show "Keep stocked" under the selected recipe. Opening a workbench that makes a kept item queues the crafts that bring your home back up to its count. |
| `AutoStart` | `true` | Start the queue by itself after topping up, when the queue had nothing else in it. `false` only queues the crafts; press Start queue yourself. |

With `KeepStocked` set to `false`, the keep row and the kept items are hidden and nothing is topped up. The counts you already set stay in `BepInEx\config\com.dev.survivallog.craftinghub.targets.txt` and come back when you turn it on again.

`AutoStart` never starts orders you queued yourself: when the queue already has something in it, top-ups are added to the end and wait for Start queue.
