# Changelog

## 0.10.5

- Clearer wording in the settings, on the workbench screen and in the queue's messages.
- Made for Survival Log 1.1.20719.

## 0.10.4

- Now needs Exiled's Toolkit 0.19.0 or newer, because it uses the Toolkit's shared code. Update the Toolkit first.
- Made for Survival Log 1.1.20719.

## 0.10.3

- **Warning:** needs Exiled's Toolkit 0.11.0 or newer. Update the Toolkit first.
- Removed a harmless warning the mod logged when the game started.
- Made for Survival Log 1.1.20151.

## 0.10.2

- Fixed: the first queued craft after you open a workbench showed an empty progress bar until it finished. It now fills like the rest.
- Made for Survival Log 1.1.20151.

## 0.10.1

- While the workbench is closed, the queue now tells you with a pop-up from Exiled's Toolkit when it finishes, stops or pauses. The pop-ups also go to the Notifications history.
- Exiled's Toolkit 0.11.0 or newer is now required.
- Made for Survival Log 1.1.19611.

## 0.10.0

- New: `UiScale` under Interface sets the size of Crafting Hub's windows, from 75% to 200%, separately from Exiled's Toolkit's UI size. At 0, the default, they follow the Toolkit's size. Needs Exiled's Toolkit 0.11.0 or newer.
- Made for Survival Log 1.1.19611.

## 0.9.11

- Fixed: a stutter the first time in a session that you open a workbench.
- Fixed: short stutters while a workbench works out which recipes you can make through intermediates, mostly when your storages hold a lot of items.
- Made for Survival Log 1.1.19611.

## 0.9.10

- The recipe list and the craft queue on the workbench can be drawn bigger or smaller with the UI size setting of Exiled's Toolkit 0.10.0 or newer. Changes show at once. They only grow as long as the whole workbench still fits in the game's window, so the game's panels keep their size.
- Their tooltips follow the same size and show next to the pointer.
- Fixed: when the game window was too small for the whole workbench and the game drew it smaller, Crafting Hub's tooltips showed away from the pointer.
- Made for Survival Log 1.1.19611.

## 0.9.9

- Fixed: small stutters while you play, mostly with the workbench closed.
- The workbench recipe list redraws only the cards whose counts or marks changed, so it stays quick with many recipes.
- Made for Survival Log 1.1.19611.

## 0.9.8

- Works with the workbench in game version 1.1.19611. Recipes in the queue, on cards and in messages show their names again instead of numbers.
- Fixed: materials in your home storages showed as missing on game version 1.1.19611.
- Made for Survival Log 1.1.19611.

## 0.9.7

- Colors: a card for something that can be dyed has a row of color dots under its materials, one for each color the game's dye bar offers, with how much of that dye you have.
- Pick a color, and Add, the bell and clicking the card's picture or name all use it. The striped dot is the plain version.
- Made for Survival Log 1.1.19131.

## 0.9.6

- Workbenches no longer take materials from the neighbor's basket, and what you put there for her no longer counts toward Keep stocked.
- Made for Survival Log 1.1.19131.

## 0.9.5

- Point at a card's picture or name, or at an order in the queue, for a tooltip of the item it makes (the full item tooltip with Info Pack 0.8.5 or newer).
- Made for Survival Log 1.1.19131.

## 0.9.4

- Fixed: the stamina meter, the queue's stamina need and the stamina cost on cards never showed.
- Point at a material on a card to see its name, and at the bell, the amount or its - and + to see what they do. With Info Pack 0.8.5 or newer installed, these use its tooltip, and a material gets the full item tooltip.
- Made for Survival Log 1.1.19131.

## 0.9.3

- The workbench screen has a new look in the gold style of the other hubs.
- Recipe cards sit two to a row. Each material is on its own line with how much you need and how much you have, and the stamina a craft takes sits beside Add.
- The bell on a card keeps that item in stock and shows the kept count. Kept items have their own tab beside the queue.
- The queue column shows how much stamina the queued orders take against what you have. Each order shows its own stamina cost. Intermediates made on the way are not counted in this figure.
- The materials list folds to a line of what is short. Click it to see the whole list.
- The up and down arrows on queue orders are gone. Drag an order by its handle to move it.
- Shorter filter names: Can make, Parts first, Kept.
- Made for Survival Log 1.1.19131.

## 0.9.2

- "Materials for the whole queue" no longer shows you short of materials a batch has already used. An order in the craft queue counts down by one as each craft finishes, and leaves the queue with its last craft.
- Crafts finished before you press Stop come off the order.
- Your stamina shows beside the craft queue again, each recipe shows what a craft costs, and a bar says so when the queue cannot go on for lack of stamina.
- After a game update, Mod Manager can tell you if this mod may need an update.
- Made for Survival Log 1.1.19131.

## 0.9.1

- The recipe list and queue are no longer squeezed while the bag is open. The bag panel beside them is now only as wide as the tab it shows, instead of as wide as your widest storage.

## 0.9.0

- Mod Manager's Mods screen shows the mod's name, description, category and links, and links to this changelog.
- The Mods screen lists Stacking as a mod it works with, and BetterCrafting as one it does not work with. While BetterCrafting is installed, Crafting Hub does not load.
- `AutoStart` stays hidden on the Mods screen while `KeepStocked` is off.
- Exiled's Toolkit 0.4.0 or newer is optional. With it, settings you change on the Mods screen take effect right away. Without it, they take effect the next time you start the game.

## 0.8.0

- Crafting is a lot smoother: pulling materials and finishing a craft no longer freeze the game for a moment each time.
- Intermediate parts a recipe needs go into the queue as their own orders, ahead of the item that needs them, so the "Made on the way" list is gone.
- Cards say "Can make now" whenever you own enough of every material. Before, with the Stacking mod, a material held in one stack bigger than the recipe needs made the card say "Cannot make now".
- When the queue stops because your stamina is too low, a bar over the queue says so.
- Keep on a recipe card starts at 1 instead of 10.

## 0.7.1

- First release.
- A wider workbench screen with a search box and a craft queue.
- Workbenches use every storage in your home.
- Intermediate materials are made for you.
- The queue crafts different recipes one after another.
