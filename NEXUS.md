Name: Crafting Hub
Summary: A wider, searchable workbench screen with a craft queue. Workbenches use every storage in your home, make intermediate materials for you, and craft a queue of different recipes. 更宽、可搜索的工作台界面，带制作队列。工作台可以使用家里所有储物家具里的材料，替你制作中间材料，并能依次制作一整列不同的配方。

Description (BBCode):
A workbench normally only uses its linked storages and makes one recipe at a time. Crafting Hub lets every workbench use every storage in your home, makes the planks and parts in between for you, and crafts a whole queue of recipes in order.

[size=5]Main features[/size]
[list]
[*][b]Every home storage counts[/b]: workbenches count and take materials from every storage in your home.[/*]
[*][b]A wider workbench screen[/b]: the recipe list becomes a wider one in two columns, with a craft queue column beside it. The bench grid and its Craft button become a Workbench tab in the bag's panel, so the screen has more room, and the tab comes up by itself when you pick a recipe or start a craft.[/*]
[*][b]Search and filters[/b]: search by recipe name or by a material it uses. Filter to what you can make now, what needs parts first, what you are short of, or what you keep in stock, and pick one recipe group at a time.[/*]
[*][b]Craftable through intermediates[/b]: recipes you could make after crafting their intermediates show in amber with what gets made first.[/*]
[*][b]Craft with intermediates[/b]: one button under the bench's Craft button makes the intermediates and then the recipe, using the quantity box.[/*]
[*][b]Queue[/b]: set an amount on any recipe and press Add, then "Craft queue" crafts different recipes one after another, intermediates included. Change an order's amount, move it with the arrows or by dragging, remove it with its x, or press Clear. Each order says whether it is ready, what it makes first, or what it is short of.[/*]
[*][b]Materials for the whole queue[/b]: what the queue uses in total, what you have, and what is short. A warning names the first order that will come up short before you start.[/*]
[*][b]Stamina[/b]: the queue column shows your stamina and each recipe shows the stamina a craft takes. A "Stamina too low to craft" bar comes up while you cannot afford the next craft, and a run that runs out says so with the numbers.[/*]
[*][b]Stop[/b]: nothing more is started and the queue is kept.[/*]
[*][b]Closing pauses the queue[/b]: close the workbench mid-run and the queue waits. Open any workbench (you walk there yourself) and it carries on by itself. Crafts finished before the close are counted, so they are not made twice.[/*]
[*][b]Keep stocked[/b]: press Keep on a recipe to keep 1 of it in your home, and raise the count in the Keep in stock list (say 20 bandages). Each time you open a workbench, the crafts that bring your home back up to that count are queued, and started if the queue was empty. The list shows how many your home holds, with an x to stop keeping an item.[/*]
[*][b]Game's own clicks[/b]: every craft goes through the workbench's own buttons, so the game's checks and timings apply.[/*]
[/list]

The mod adds no data of its own to your save (kept counts go in their own file in the BepInEx config folder), but the crafts it runs are real: they use materials from any storage in your home. You can remove it at any time; workbenches go back to their linked storages.

Works in English and Chinese.

[size=5]Known issues[/size]
[list]
[*]Do not use it together with BetterCrafting. Remove BetterCrafting first.[/*]
[*]The queue only crafts at the workbench whose window is open. The mod never moves your character: when the queue pauses, you walk to a workbench and open it yourself.[/*]
[*]The game cancels a craft when the workbench closes; that craft is made again when the queue carries on. Quitting the game empties the queue.[/*]
[*]A craft still going when you press Stop is not counted, so starting again can make that batch twice.[/*]
[*]Closing the workbench in the same moment a single craft finishes can make that craft once more.[/*]
[*]Craft with intermediates is not taken while the queue is paused; press Stop first.[/*]
[*]Every storage in your home is used, including ones you meant to keep for something else.[/*]
[*]A kept item is only topped up when you open a workbench. Recipes that make several at once can end a little over the count.[/*]
[*]Kept counts follow the save file. A save the game starts fresh from an old one (rolling back, restarting from a snapshot) begins with none.[/*]
[/list]

[size=5]Requirements[/size]
[list]
[*]Survival Log 1.1.18902. A game update can break it until the mod is updated.[/*]
[*][url=https://www.nexusmods.com/survivallog/mods/12]BepInEx for Survival Log[/url]. Install it first.[/*]
[*]Optional: [url=https://github.com/AnExiledDev/SurvivalLog-ExiledToolkit]Exiled's Toolkit[/url] 0.4.0 or newer. With it, settings changed on the Mod Manager's Mods screen take effect without restarting the game. Without it, they take effect at the next game start.[/*]
[*]Not together with BetterCrafting.[/*]
[/list]

[size=5]Installation[/size]
[list=1]
[*]Install [url=https://www.nexusmods.com/survivallog/mods/12]BepInEx[/url] [i](if no other mods were installed before, start the game once so BepInEx finishes setup, then quit)[/i].[/*]
[*]Optional, to change settings without a restart: install Exiled's Toolkit the same way. Download its zip and extract it into the game folder.[/*]
[*]Download the zip from the Files tab.[/*]
[*]Extract it into the game folder (the folder with [font=Courier New]SurvivalLog.exe[/font]). The zip holds a BepInEx folder, so the DLL lands in the right place. When asked, merge the folders. The result should be:
[code]C:\Program Files (x86)\Steam\steamapps\common\Survival Log\BepInEx\plugins\CraftingHub.dll[/code][/*]
[*]If you use BetterCrafting, delete its DLL from the same folder.[/*]
[/list]

[size=5]Uninstall[/size]
Delete [font=Courier New]CraftingHub.dll[/font] from the [font=Courier New]BepInEx\plugins[/font] folder.

[size=5]Configuration[/size]
To turn off the whole-house storage, change how deep intermediates go, hide the marks, turn off keep stocked or its auto start, have closing the workbench stop the queue instead of pausing it, or keep a paused queue to the workbench it was running on, edit [font=Courier New]BepInEx\config\com.dev.survivallog.craftinghub.cfg[/font] while the game is closed, or do it in game on the Mod Manager's Mods screen, where changes take effect at once (see [url=https://github.com/AnExiledDev/SurvivalLog-CraftingHub/blob/main/CONFIG.md]CONFIG.md[/url]; the cfg file explains each setting in English and Chinese). Without Exiled's Toolkit installed, changes made on the Mods screen take effect at the next game start instead.

[size=5]Feedback[/size]
Found a bug or have an idea? Come say hi on the [url=https://discord.gg/trTsyxUFqg]Core Directive Discord[/url], or post in this page's Posts tab. For bugs, please include [font=Courier New]BepInEx\LogOutput.log[/font] and [font=Courier New]BepInEx\crafting-hub.log[/font].
To reach me directly, message [b]exiled.dev[/b] on Discord; you can find me in the [url=https://discord.gg/MZGg4gQAUe]official Survival Log Discord[/url]. I'm also on X as [url=https://x.com/AnExiledDev]@AnExiledDev[/url].
Coming soon: a Survival Log wiki with detailed documentation for all of my mods.

[size=5]Source and license[/size]
Open source under MIT: [url=https://github.com/AnExiledDev/SurvivalLog-CraftingHub]github.com/AnExiledDev/SurvivalLog-CraftingHub[/url]

[line]
[size=5]简体中文[/size]

工作台平时只能用接入它的储物家具，一次也只能做一个配方。Crafting Hub 让每个工作台都能用家里所有储物家具里的材料，替你先做好中间的木板和零件，并按顺序制作一整列配方。

[size=5]主要功能[/size]
[list]
[*][b]家里所有储物家具都算[/b]：工作台会清点并取用家里每个储物家具里的材料。[/*]
[*][b]更宽的工作台界面[/b]：配方列表变成更宽的两列，旁边多出一列制作队列。工作台格子和它的「手工制作」按钮变成背包面板里的一个工作台标签页，界面更宽敞；选配方或开始制作时，这个标签页会自动切出来。[/*]
[*][b]搜索和筛选[/b]：可以按配方名称或它用到的材料搜索。可以只看现在能做的、需先做材料的、材料不足的或保持库存的配方，也可以一次只看一个配方分组。[/*]
[*][b]通过中间材料可做[/b]：先做中间材料就能做的配方会以琥珀色显示，并写明要先做什么。[/*]
[*][b]连同中间材料一起制作[/b]：工作台「手工制作」按钮下方多一个按钮，按数量框里的数量先做中间材料，再做这个配方。[/*]
[*][b]队列[/b]：在任意配方上设定数量并点「添加」，然后点「制作队列」，就会一个接一个地制作不同的配方，中间材料也包括在内。可以修改订单的数量，用箭头或拖动调整顺序，点它的 x 移除，或点「清空」。每个订单都会写明是否就绪、要先做什么或缺少什么。[/*]
[*][b]整个队列所需材料[/b]：队列一共要用多少材料、你有多少、缺多少。开始之前，会有提示指出第一个会缺材料的订单。[/*]
[*][b]精力[/b]：队列一栏显示你的精力，每个配方显示制作一次需要的精力。精力不够做下一次时会出现「精力不足」提示条；因精力不足而停下的运行会写明具体数字。[/*]
[*][b]停止[/b]：不再开始新的制作，队列会保留。[/*]
[*][b]关掉工作台会暂停队列[/b]：运行中关掉工作台，队列会等待。打开任意工作台（要你自己走过去）后它会自动继续。关闭前已完成的制作都会计数，不会重复制作。[/*]
[*][b]保持库存[/b]：在配方上点「保持」，家里会保持 1 个这种物品，可以在「保持库存」列表里调高数量（比如 20 个绷带）。每次打开工作台，都会把补回这个数量所需的制作加入队列；如果队列原本是空的，就会自动开始。列表会显示家里现有的数量，点 x 就不再保持这种物品。[/*]
[*][b]走游戏自己的点击[/b]：每次制作都通过工作台自己的按钮，所以游戏自己的检查和时间都照常生效。[/*]
[/list]

模组不会在你的存档里加入自己的数据（保持库存的数量保存在 BepInEx 配置文件夹里的单独文件中），但它执行的制作是真实的：会用掉家里任意储物家具里的材料。你可以随时移除它，工作台会恢复为只使用接入的储物家具。

[size=5]已知问题[/size]
[list]
[*]不要和 BetterCrafting 一起使用。请先移除 BetterCrafting。[/*]
[*]队列只在窗口打开着的那个工作台制作。模组从不移动你的角色：队列暂停时，要你自己走到工作台前打开它。[/*]
[*]工作台关闭时游戏会取消正在进行的制作；队列继续时会重新制作这一次。退出游戏会清空队列。[/*]
[*]点「停止」时还在进行的制作不会计数，所以再次开始可能会把这一批做两次。[/*]
[*]在单次制作完成的同一瞬间关掉工作台，可能会多做一次。[/*]
[*]队列暂停时不接受「连同中间材料一起制作」；请先点「停止」。[/*]
[*]家里所有储物家具都会被使用，包括你打算留作别用的那些。[/*]
[*]保持库存的物品只在你打开工作台时补充。一次产出多个的配方，最后可能会比设定数量多一点。[/*]
[*]保持库存的数量跟着存档文件走。游戏从旧存档重新开始的存档（回档、从快照重新开始）不会带有这些数量。[/*]
[/list]

[size=5]前置要求[/size]
[list]
[*]Survival Log 1.1.18902。游戏更新后模组可能暂时失效，直到模组更新为止。[/*]
[*][url=https://www.nexusmods.com/survivallog/mods/12]BepInEx for Survival Log[/url]，请先安装。[/*]
[*]可选：[url=https://github.com/AnExiledDev/SurvivalLog-ExiledToolkit]Exiled's Toolkit[/url] 0.4.0 或更新版本。装了它，在 Mod Manager 的「模组」界面改动的设置无需重启游戏即可生效；没装的话，改动在下次启动游戏时生效。[/*]
[/list]

[size=5]安装[/size]
[list=1]
[*]安装 [url=https://www.nexusmods.com/survivallog/mods/12]BepInEx[/url] [i]（如果之前没有装过其他模组，先启动一次游戏，让 BepInEx 完成初始化，然后退出）[/i]。[/*]
[*]可选（改动设置无需重启游戏）：用同样的方式安装 Exiled's Toolkit，下载它的压缩包并解压到游戏目录。[/*]
[*]在「文件」页下载压缩包。[/*]
[*]把压缩包解压到游戏目录（[font=Courier New]SurvivalLog.exe[/font] 所在的文件夹）。压缩包里带有 BepInEx 文件夹，DLL 会自动放到正确位置。如果提示合并文件夹，选择合并。解压后应为：
[code]C:\Program Files (x86)\Steam\steamapps\common\Survival Log\BepInEx\plugins\CraftingHub.dll[/code][/*]
[/list]

[size=5]卸载[/size]
从 [font=Courier New]BepInEx\plugins[/font] 文件夹中删除 [font=Courier New]CraftingHub.dll[/font]。工作台会恢复为只使用接入它的储物家具。

[size=5]配置[/size]
如需关闭全屋储物、修改中间材料的层数、隐藏标记、关闭保持库存或它的自动开始、让关掉工作台时停止队列而不是暂停，或让暂停的队列只在原来的工作台继续，可以在游戏内 Mod Manager 的「模组」界面修改（立即生效），也可以在游戏关闭时编辑 [font=Courier New]BepInEx\config\com.dev.survivallog.craftinghub.cfg[/font]。配置文件中每项设置都有中文说明，完整说明见 [url=https://github.com/AnExiledDev/SurvivalLog-CraftingHub/blob/main/CONFIG.md]CONFIG.md[/url]（英文）。没装 Exiled's Toolkit 的话，在「模组」界面的改动改为在下次启动游戏时生效。

[size=5]反馈[/size]
发现 bug 或有好点子？欢迎来 [url=https://discord.gg/trTsyxUFqg]Core Directive Discord[/url] 打个招呼，或在本页的「Posts」标签里发帖。报告 bug 时，请附上 [font=Courier New]BepInEx\LogOutput.log[/font] 和 [font=Courier New]BepInEx\crafting-hub.log[/font]。
想直接联系我，可以在 Discord 上给 [b]exiled.dev[/b] 发消息，我在 [url=https://discord.gg/MZGg4gQAUe]Survival Log 官方 Discord[/url] 里。也可以在 X 上找我：[url=https://x.com/AnExiledDev]@AnExiledDev[/url]。
即将推出：Survival Log 维基，收录我所有模组的详细说明。

[size=5]源代码与许可[/size]
基于 MIT 许可开源：[url=https://github.com/AnExiledDev/SurvivalLog-CraftingHub]github.com/AnExiledDev/SurvivalLog-CraftingHub[/url]
