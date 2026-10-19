# Changelog

## 0.9.0

Every setting can be changed on the Mod Manager's Mods screen without a restart. The Mods screen shows the mod's name, description, category and links, lists Stacking as a mod it works with and BetterCrafting as one it does not work with. While BetterCrafting is installed, Crafting Hub does not load. Needs Exiled's Toolkit 0.4.0 or newer.

## 0.8.0

Crafting is a lot smoother. The workbench rebuilt its whole recipe list after every item it moved, so pulling materials and finishing a craft froze the game for a moment each time. Those rebuilds now run once, and the check for what you can make reads your storages once per rebuild instead of once per recipe. Intermediate parts a recipe needs go into the queue as their own orders, ahead of the item that needs them, so the "Made on the way" list is gone. Cards say "Can make now" whenever you own enough of every material. Before, a material held in one stack bigger than the recipe needs (with the Stacking mod) made the card say "Cannot make now", even though the queue could take part of that stack. When the queue stops because your stamina is too low, a bar over the queue says so. Keep on a recipe card starts at 1 instead of 10. No setting changed.

## 0.7.1

First public release. A wider, searchable workbench screen with a craft queue. Workbenches use every storage in your home, make intermediate materials for you, and craft a queue of different recipes.
