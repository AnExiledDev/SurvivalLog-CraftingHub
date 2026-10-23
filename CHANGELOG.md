# Changelog

## 0.9.5

- Point at a card's picture or name, or at an order in the queue, for a tooltip of the item it makes (the full item tooltip with Info Pack 0.8.5 or newer).
- Made for Survival Log 1.1.19131.

## 0.9.4

- Fixed: the stamina meter, the queue's stamina need and the stamina cost on cards never showed.
- Point at a material on a card to see its name, and at the bell, the amount or its - and + to see what they do. With Info Pack 0.8.5 or newer installed, these use its tooltip, and a material gets the full item tooltip.
- Made for Survival Log 1.1.19131.

## 0.9.3

The workbench screen has a new look in the gold style of the other hubs.

- Recipe cards sit two to a row, with materials on their own line as need/have and the stamina a craft takes beside Add.
- The bell on a card keeps that item in stock and shows the kept count. Kept items have their own tab beside the queue.
- The queue column shows how much stamina the queued orders take against what you have. Each order shows its own stamina cost. Intermediates made on the way are not counted in this figure.
- The materials list folds to a line of what is short; click it to see the whole list.
- The up and down arrows on queue orders are gone; drag an order by its handle to move it.
- Shorter filter names: Can make, Parts first, Kept.
- Made for Survival Log 1.1.19131.

## 0.9.2

An order in the craft queue counts down by one as each craft of a batch finishes, and leaves the queue with its last craft. Before, it kept its full count until the whole batch was done, so "Materials for the whole queue" showed you short of materials the batch had already used. Crafts finished before you press Stop now come off the order too.

Your stamina shows beside the craft queue again, each recipe shows what a craft costs, and a bar says so when the queue cannot go on for lack of stamina. The game had been answering the stamina question with nothing, so none of this showed.

- After a game update, a hook that can no longer be placed is logged as an error, so the Mod Manager can mark the mod as maybe needing an update.
- Made for Survival Log 1.1.19131.

## 0.9.1

The bag panel beside the crafting columns is as wide as the tab shown. Before, it kept the width of your widest storage (a large rack), which squeezed the recipe list and queue even while the bag was open.

## 0.9.0

Every setting can be changed on the Mod Manager's Mods screen without a restart. The Mods screen shows the mod's name, description, category and links, lists Stacking as a mod it works with and BetterCrafting as one it does not work with. While BetterCrafting is installed, Crafting Hub does not load. The Mods screen links this changelog, and `AutoStart` stays hidden there while `KeepStocked` is off. Exiled's Toolkit 0.4.0 or newer is optional. With it, settings changed on the Mods screen take effect without a restart; without it, at the next game start.

## 0.8.0

Crafting is a lot smoother. The workbench rebuilt its whole recipe list after every item it moved, so pulling materials and finishing a craft froze the game for a moment each time. Those rebuilds now run once, and the check for what you can make reads your storages once per rebuild instead of once per recipe. Intermediate parts a recipe needs go into the queue as their own orders, ahead of the item that needs them, so the "Made on the way" list is gone. Cards say "Can make now" whenever you own enough of every material. Before, a material held in one stack bigger than the recipe needs (with the Stacking mod) made the card say "Cannot make now", even though the queue could take part of that stack. When the queue stops because your stamina is too low, a bar over the queue says so. Keep on a recipe card starts at 1 instead of 10. No setting changed.

## 0.7.1

First public release. A wider, searchable workbench screen with a craft queue. Workbenches use every storage in your home, make intermediate materials for you, and craft a queue of different recipes.
