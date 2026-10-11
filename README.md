> **AI-written from operator direction.** The intent is the operator's; the wording and scope are an agent's.
> `session: 127889d4-f8d8-4d17-8ded-f6f46da456fc | 2026-10-11 | source: op:2026-10-11-0818-c1e8`

# Crafting Hub

A BepInEx 6 (IL2CPP) plugin for Survival Log that makes the workbench craft from your whole house, makes intermediate materials for you, and queues crafts of different recipes.

## What it does

- **Every home storage counts.** A workbench counts and takes materials from every storage in your home, not only the ones linked to it.
- **Craftable through intermediates.** A recipe you can't make yet, but could after crafting its intermediates (planks for a crate), is marked in the recipe list with an amber dashed edge and a ⚒ badge. Select it and the panel under the craft button says what gets made first.
- **Craft with intermediates.** On a marked recipe, this button makes the intermediates and then the recipe, using the quantity box.
- **Queue.** "Add to queue" queues the selected recipe with the quantity box. "Start queue" crafts the queue in order, making intermediates as needed. Each order shows as a chip you can remove; "Stop" halts after the current step and keeps the queue.

Every craft goes through the workbench page's own clicks (pick recipe, craft, close result), so the game's own checks and locks still apply.

## Install

Copy `CraftingHub.dll` into `BepInEx/plugins`. Remove BetterCrafting: both change the workbench's storage list, and the log warns if both are loaded.

## Config

`BepInEx/config/com.dev.survivallog.craftinghub.cfg`

| Section | Key | Default | Meaning |
| --- | --- | --- | --- |
| Storage | UseAllHomeStorages | true | Count and take materials from every home storage |
| Chain | MaxDepth | 4 | Layers of intermediates allowed for one recipe; 0 turns them off |
| Chain | ShowMarks | true | Mark recipes craftable through intermediates |

The log is `BepInEx/crafting-hub.log`.

## Known limits

- Intermediates must be made at the same workbench as the recipe that needs them.
- The queue lives in memory: closing the workbench stops the run (Start carries on), and quitting the game empties it.
- A recipe's yield is read as one item per entry in the production table's product list. The log lists the recipes this reads as making several; check one in game.
- A failed craft is not retried as such. The next step is planned from what the house holds, so a failure that spent materials shows up as a shortage.
- Version 0.1.0 was built and unit tested but not yet run in game.

## Build

```
dotnet build src/CraftingHub -c Release
dotnet test tests/CraftingHub.Tests
```

The project expects the game at `D:\Downloads\Survival-Log-AnkerGames\Survival Log`; pass `-p:GameDir=<path>` otherwise.

## Provenance

> OMG yes being able to craft intermediate materials automatically would be exceptional, especially if we can get objects to show as craftable when you have all the materials to craft intermediate materials, but lack the intermediate materials, this would be a life saver!

> being able to queue up multple crafting types would be ideal, but may be dependent on the mod we have which pulls crafting material from any storage, otherwise we might be fighting the game a fair bit, granted we could batch it all into one mod.

## License

MIT
