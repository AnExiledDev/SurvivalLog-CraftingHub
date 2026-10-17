// Crafting Hub's half in the game's web UI. Runs in Root.html, the document every game page iframe sits in, and
// reaches into the workbench page (ToolTable.html, a same-origin iframe) to swap its recipe list for a searchable
// two-column one and to add a craft queue column beside it. The bench itself becomes a tab of the bag's panel, so the
// screen has one column fewer. The game's own list stays in the page, hidden, and the game keeps rebuilding it as
// before; the hub's list is redrawn right after each rebuild. Every craft it starts goes through the workbench page's
// own core.UnitySendEvent, exactly like a click. Talks to the plugin through
// vuplex.postMessage("slmod|craftinghub|<verb>|<args>") and is driven by window.__craftinghub.
(function () {
  if (window.__craftinghub) return;

  var PREFIX = 'slmod|craftinghub|';
  var BENCH_PAGE = /ToolTable\.html/;
  var POLL_MS = 500;
  // How many polls a frame may stay out of reach, or a loaded page stay without what the hub hooks into, before it
  // is reported: both happen for a moment while a frame loads.
  var SETTLE_POLLS = 4;
  // How many times in a row the hub tries to draw on a page before it waits for new data from the game or the plugin.
  var RENDER_TRIES = 2;

  var DEFAULT_KEEP = 10;
  var MAX_KEEP = 9999;
  var MAX_AMOUNT = 999;
  var PRESS_LIMIT_MS = 2000;
  var ALL_GROUPS = -1;
  var WHEEL_LINE_PX = 16;
  var HOVER_SWITCH_MS = 400;
  var BENCH_TAB = 'cc-tab-bench';
  var HUB_ROOTS = ['cc-mid', 'cc-queue', 'cc-panel', BENCH_TAB];

  // Built from char codes so the file stays ASCII.
  var GLYPH = {
    open: String.fromCharCode(0x25bc),
    closed: String.fromCharCode(0x25b6),
    up: String.fromCharCode(0x25b2),
    down: String.fromCharCode(0x25bc),
    handle: String.fromCharCode(0x2630),
    minus: String.fromCharCode(0x2212),
    times: String.fromCharCode(0xd7),
    cross: String.fromCharCode(0x2715)
  };

  // Each filter's label is the word under its key.
  var FILTERS = [
    { id: 'all', label: 'filterAll' },
    { id: 'now', label: 'filterNow' },
    { id: 'via', label: 'filterVia' },
    { id: 'short', label: 'filterShort' },
    { id: 'kept', label: 'filterKept' }
  ];

  // The panel's words, used until the plugin sends them in the game's language (window.__craftinghubWords) before
  // each call.
  var ENGLISH = {
    filterAll: 'All',
    filterNow: 'Can make now',
    filterVia: 'Needs parts first',
    filterShort: 'Short',
    filterKept: 'Kept in stock',
    oneLess: 'One less',
    oneMore: 'One more',
    locked: 'Locked',
    canMakeNow: 'Can make now',
    shortOf: 'Short: {0}',
    cannotMake: 'Cannot make now',
    clearSearch: 'Clear the search',
    search: 'Search a recipe or a material',
    recipes: 'Recipes',
    outOfStamina: 'Out of stamina: crafts that take stamina cannot start.',
    keepInStock: 'Keep in stock',
    keepInStockAside: 'topped up when you open a workbench',
    craftQueue: 'Craft queue',
    materials: 'Materials for the whole queue',
    materialsAside: 'need / you have',
    madeOnTheWay: 'Made on the way',
    countOf: '{0} of {1}',
    noMatch: 'No recipes match "{0}".',
    noRecipes: 'No recipes in this view.',
    allGroups: 'All groups',
    canMakeCount: '{0} / {1} can make',
    staminaCost: '{0} stamina',
    add: 'Add',
    addTip: 'Add this many crafts to the queue',
    keep: 'Keep',
    keepN: 'Keep {0}',
    keepTip: 'Keep {0} of this in your home; opening any workbench queues the crafts to top it back up',
    keepStopTip: 'Stop keeping this in stock',
    queueEmptyHint: 'The queue is empty. Use Add on any recipe.',
    idle: 'Idle',
    crafting: 'Crafting',
    paused: 'Paused',
    stamina: 'Stamina',
    staminaHave: 'Stamina {0}',
    staminaOut: '{0} (out)',
    dragTip: 'Drag to change the order',
    ready: 'Ready',
    moveUp: 'Move up',
    moveDown: 'Move down',
    removeTip: 'Remove from the queue',
    craftOne: '{0} craft',
    craftMany: '{0} crafts',
    orderOne: '{0} order',
    orderMany: '{0} orders',
    listSep: ', ',
    itemSep: ', ',
    workbench: 'Workbench',
    topUp: 'Top-up',
    makes: ', makes {0}',
    noMaterials: 'No materials to show.',
    nothingQueued: 'Nothing queued.',
    shortN: 'short {0}',
    enough: 'ok',
    noParts: 'No parts need making.',
    craftingNow: 'Crafting...',
    stop: 'Stop',
    stopTip: 'Stop after nothing more is started; the queue is kept',
    clear: 'Clear',
    clearTip: 'Empty the queue',
    queueIsEmpty: 'Queue is empty',
    craftQueueTip: 'Craft everything in the queue, making intermediates as needed',
    nothingKept: 'Nothing kept in stock. Use Keep on any recipe.',
    unkeepTip: 'Stop keeping {0} in stock',
    craftChain: 'Craft with intermediates',
    craftChainTip: 'Make the missing intermediates first, then this, using the amount set on its recipe card'
  };

  // The plugin's order states, as the colours the list uses for the same meaning.
  var STATE_LOOK = { ready: 'now', via: 'via', short: 'short', locked: 'short', unlisted: 'short' };

  var view = { marks: {}, queue: [], running: false, paused: false, activeId: 0, status: '', headsUp: '', totals: [], made: [], yields: {}, keep: { on: false, targets: [], byKey: {} } };
  var selectedKey = '';
  // The plugin's last view as it was sent, so the same view sent again draws nothing.
  var lastViewText = '';
  var hasSkippedRender = false;
  var isQueueActing = false;
  // Where the problems with the queue's own clicks are reported from.
  var actProblems = {};

  function post(message) {
    try {
      if (window.vuplex) window.vuplex.postMessage(PREFIX + message);
    } catch (e) {}
  }

  // Each problem is reported once per owner (a workbench page's document, a frame, the queue's clicks), so a broken
  // hook cannot flood the log, and a page loaded afresh that breaks again is still heard about.
  function diag(owner, id, text) {
    var reported = owner.__ccReported || (owner.__ccReported = {});

    if (reported[id]) return;

    reported[id] = true;
    post('diag|' + text);
  }

  // A problem that cleared up is reported again if it comes back.
  function forget(owner, id) {
    if (owner.__ccReported) delete owner.__ccReported[id];
  }

  function pageDiag(win, id, text) {
    try {
      diag(win.document, id, text);
    } catch (e) {}
  }

  function benchFrames() {
    return Array.prototype.filter.call(document.querySelectorAll('iframe'), function (frame) { return BENCH_PAGE.test(frame.src || ''); });
  }

  // The frame's workbench page; null while the frame still holds the empty page a new frame starts with, which already
  // counts as loaded. Throws when the browser will not let the root reach the page.
  function benchWindowOf(frame) {
    var win = frame.contentWindow;

    return win && win.document && win.location && BENCH_PAGE.test(String(win.location.href)) ? win : null;
  }

  function benchWindows() {
    var found = [];

    benchFrames().forEach(function (frame) {
      try {
        var win = benchWindowOf(frame);

        if (win) found.push(win);
      } catch (e) {}
    });

    return found;
  }

  // The page's top-level const and let values are not window properties, so they are read through its own eval.
  function pageValue(win, name) {
    try {
      return win.eval('typeof ' + name + ' !== "undefined" ? ' + name + ' : null');
    } catch (e) {
      pageDiag(win, 'read-' + name, 'cannot read ' + name + ' on the workbench page: ' + e);

      return null;
    }
  }

  function benchCore(win) {
    return pageValue(win, 'core');
  }

  // Sends what the page's own controls send, through its own core, so the game handles it exactly as theirs.
  function send(win, action, data) {
    var core = benchCore(win);

    if (core) core.UnitySendEvent(action, data);
  }

  // Tells the game a text box has focus, as the page's own search box does, so typing does not move the player.
  function sendFocus(win, isFocused) {
    try {
      send(win, 'SEARCH_FOCUS', { focused: isFocused });
    } catch (e) {}
  }

  // Whether the hub is on the page. A page still loading has not defined its functions yet, and the poll tries again,
  // so only a loaded page counts; the poll reports a loaded page that stays without them (noteBadShape).
  function install(win) {
    if (win.__ccInstalled) return true;

    if (win.document.readyState !== 'complete') return false;

    var core = benchCore(win);

    if (!core || typeof win.renderRecipes !== 'function' || !pageValue(win, 'NOTE_CAT_META')) return false;

    win.__ccInstalled = true;
    win.__ccUi = {
      filter: 'all',
      group: ALL_GROUPS,
      amounts: {},
      drag: null,
      view: 'bag',
      hover: null,
      isOverlayShown: false,
      isOverlayPending: false,
      isOwnCraft: true,
      isTabOff: false,
      dragCoord: null,
      renderFailures: 0,
      recipesSig: null,
      recipeVersion: 0,
      mats: {},
      icons: null
    };
    selectedKey = '';
    wrapRenderer(win);
    wrapSend(win, core);
    wrapGridHitTests(win);
    wireDragGuards(win);
    addStyle(win.document);

    return true;
  }

  // Counted on the page's own document, so a page loaded afresh is given its own few polls.
  function noteBadShape(win) {
    var doc = win.document;

    if (doc.readyState !== 'complete') return;

    doc.__ccShapeMisses = (doc.__ccShapeMisses || 0) + 1;

    if (doc.__ccShapeMisses < SETTLE_POLLS) return;

    var core = benchCore(win);

    pageDiag(win, 'shape', 'the workbench page changed shape (core ' + !!core + ', renderRecipes ' + typeof win.renderRecipes + '), the crafting hub is off');
  }

  // The game rebuilds its own, hidden, list on every recipe message. The hub's list is redrawn in the same turn, so
  // nothing flickers, and the search box is never part of what is redrawn, so it keeps its text and focus.
  function wrapRenderer(win) {
    var original = win.renderRecipes;

    win.renderRecipes = function () {
      var result = original.apply(this, arguments);

      noteRecipes(win);
      refresh();

      return result;
    };
  }

  // The game sends the same recipes again often. Only different ones count as new: what was worked out from the last
  // ones is dropped, and a hub that gave up drawing tries again.
  function noteRecipes(win) {
    var ui = win.__ccUi;
    var sig = recipesSignature(pageValue(win, 'lastRecipeData'));

    if (sig !== null && sig === ui.recipesSig) return;

    ui.recipesSig = sig;
    ui.recipeVersion += 1;
    ui.mats = {};
    ui.icons = null;
    ui.renderFailures = 0;
  }

  // The page marks its own recipe objects as it draws them (r._match); those marks are not the game's data. null when
  // the recipes cannot be written out, which counts as new every time.
  function recipesSignature(recipes) {
    try {
      return JSON.stringify(recipes, function (name, value) { return name.charAt(0) === '_' ? undefined : value; });
    } catch (e) {
      return null;
    }
  }

  // Watches what the page sends on its way to the game, which always gets it, whatever the hub makes of it.
  function wrapSend(win, core) {
    var original = core.UnitySendEvent;

    core.UnitySendEvent = function (action, data) {
      try {
        noteSend(win, action, data);
      } catch (e) {
        pageDiag(win, 'send', 'could not follow what the workbench page sent: ' + e);
      }

      return original.apply(this, arguments);
    };
  }

  // Which recipe the player (or the queue) lays on the bench, for the "Craft with intermediates" line, and whose craft
  // starts next. The player's own picks, and anything dropped on the bench grid, bring the bench tab up; the queue's
  // picks do not.
  function noteSend(win, action, data) {
    if (action === 'CRAFT') win.__ccUi.isOwnCraft = !isQueueActing;

    if (action === 'RECIPE_CLICK' && data && data.recipeKey) {
      selectedKey = data.recipeKey;

      if (!isQueueActing) showView(win, 'bench');

      refresh();
    }

    if (isDropOnBench(win, action, data)) showView(win, 'bench');
  }

  // The page sends ITEM_MOVE for a drag from one of its grids and GROUND_PICK_TO for one from the ground row.
  function isDropOnBench(win, action, data) {
    if (!data || (action !== 'ITEM_MOVE' && action !== 'GROUND_PICK_TO')) return false;

    var bench = pageValue(win, 'workbench');
    var owner = bench && typeof bench.getOwnerId === 'function' ? bench.getOwnerId() : 0;
    var target = action === 'ITEM_MOVE' ? data.toOwnerId : data.ownerId;

    return !!owner && target === owner;
  }

  // The page's drag keeps where each grid was, and the zoom, from the moment it starts. Switching the bag panel's view
  // mid-drag moves the grids, so their hit tests drop what was kept and look where the grids are now; the page looks
  // these up on the grid at every call.
  function wrapGridHitTests(win) {
    ['backpack', 'workbench'].forEach(function (name) {
      var grid = pageValue(win, name);

      if (!grid || typeof grid.isMouseOver !== 'function' || typeof grid.getGridCoordinates !== 'function') {
        pageDiag(win, 'grid-' + name, 'the workbench page\'s ' + name + ' grid changed shape; a drop may land where it was before a tab switch');

        return;
      }

      var isMouseOver = grid.isMouseOver;
      var getGridCoordinates = grid.getGridCoordinates;

      grid.isMouseOver = function (clientX, clientY) {
        return isMouseOver.call(this, clientX, clientY, null);
      };
      grid.getGridCoordinates = function (clientX, clientY, itemW, itemH) {
        return getGridCoordinates.call(this, clientX, clientY, itemW, itemH, null, null);
      };
    });
  }

  function addStyle(doc) {
    if (doc.getElementById('cc-style')) return;

    var style = doc.createElement('style');
    style.id = 'cc-style';
    style.textContent = [
      '.ui-container.cc-wide{align-items:flex-start}',
      '.combined-panel.cc-hub .notes-section{display:none!important}',
      '.combined-panel.cc-hub:not(.no-notes){min-height:820px}',
      '.combined-panel.cc-hub.no-notes #cc-mid{display:none}',
      '.combined-panel.cc-hub .workbench-section{border-right:1px solid rgba(255,193,7,.10)}',
      '.backpack-panel.cc-tabbed>.workbench-section{width:auto;align-self:stretch;border-right:none;border-radius:10px}',
      '.backpack-panel.cc-tabbed:not(.cc-bench-view)>.workbench-section{display:none}',
      '.backpack-panel.cc-tabbed>.workbench-section>h2{display:none}',
      '.backpack-panel.cc-tabbed #crafting-overlay{border-radius:10px}',
      '.backpack-panel.cc-bench-view>.weight-section,.backpack-panel.cc-bench-view>#bagToolbar{display:none!important}',
      // Out of sight rather than hidden: the page still measures the bag grid, and a drop can never land on it.
      '.backpack-panel.cc-bench-view>#backpack-grid,.backpack-panel.cc-bench-view>#rack-rows{position:absolute!important;left:-20000px!important;top:0!important;visibility:hidden!important}',
      '.backpack-panel.cc-bench-view .left-tab.active:not(#cc-tab-bench){color:var(--text-dim);background:rgba(8,8,10,.55);border-color:rgba(255,255,255,.10);box-shadow:none;cursor:pointer}',
      '.backpack-panel.cc-tabbed+.combined-panel #cc-mid{border-top-left-radius:14px;border-bottom-left-radius:14px}',
      '.backpack-panel.cc-tabbed+.combined-panel.no-notes #cc-queue{border-radius:14px}',
      '.cc-col{display:flex;flex-direction:column;min-height:0;box-sizing:border-box;padding:14px 16px;font-size:13px;color:#ddd;overflow:hidden}',
      '#cc-mid{width:660px;flex:none;border-right:1px solid rgba(255,193,7,.10);background:rgba(0,0,0,.18)}',
      '#cc-queue{width:400px;flex:none;background:rgba(0,0,0,.12);border-top-right-radius:14px;border-bottom-right-radius:14px}',
      '.cc-col button{font-family:inherit}',
      '.cc-title{display:flex;align-items:center;gap:8px;margin:0 0 10px;font-size:17px;font-weight:700;color:#ffe6a8;letter-spacing:.5px;flex:none}',
      '.cc-grow{flex:1}',
      '.cc-dim{color:#999;font-size:12px;font-weight:400}',
      '.cc-search{display:flex;align-items:center;gap:8px;background:rgba(0,0,0,.45);border:1px solid rgba(255,193,7,.35);border-radius:9px;padding:0 12px;height:40px;flex:none}',
      '.cc-search input{flex:1;min-width:0;background:transparent;border:0;outline:0;color:#fff;font-size:15px;font-family:inherit}',
      '.cc-search input::placeholder{color:#888}',
      '.cc-search-clear{border:0;background:transparent;color:#aaa;cursor:pointer;font-size:14px}',
      '.cc-chips{display:flex;flex-wrap:wrap;gap:6px;margin-top:8px;flex:none}',
      '.cc-chips.cc-slide{flex-wrap:nowrap;overflow-x:auto;overflow-y:hidden}',
      '.cc-slide::-webkit-scrollbar{height:3px}',
      '.cc-slide::-webkit-scrollbar-thumb{background:rgba(255,193,7,.3);border-radius:2px}',
      '.cc-fchip{display:inline-flex;align-items:center;gap:6px;padding:4px 10px;border-radius:16px;border:1px solid rgba(255,255,255,.14);background:rgba(255,255,255,.04);color:#ccc;cursor:pointer;font-size:12px;flex:none;white-space:nowrap}',
      '.cc-fchip .n{font-size:11px;color:#1b1406;background:#ffc107;border-radius:9px;padding:0 6px;font-weight:700}',
      '.cc-fchip .n.z{background:#444;color:#aaa}',
      '.cc-fchip.on{background:linear-gradient(180deg,rgba(107,87,38,.9),rgba(72,56,26,.9));border-color:rgba(255,193,7,.6);color:#fff3cf}',
      '.cc-list{flex:1;min-height:0;overflow-y:auto;margin-top:10px;padding-right:6px;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px 8px;align-content:start}',
      '.cc-scroll::-webkit-scrollbar{width:6px}',
      '.cc-scroll::-webkit-scrollbar-thumb{background:rgba(255,193,7,.3);border-radius:3px}',
      '.cc-ghead{grid-column:1/-1;display:flex;align-items:center;gap:8px;padding:5px 10px;border-radius:6px;background:linear-gradient(90deg,#6b5726,#48381a);border-left:3px solid #ffc107;color:#fff3cf;font-weight:700;cursor:pointer;font-size:13px}',
      '.cc-arrow{font-size:10px;opacity:.8;width:12px}',
      '.cc-gcount{margin-left:auto;font-size:12px;font-weight:600;color:#ffe6a8}',
      '.cc-card{display:grid;grid-template-columns:40px minmax(0,1fr);gap:5px 8px;align-items:start;align-content:space-between;padding:6px 8px;background:rgba(8,8,10,.55);border:1px solid rgba(255,255,255,.07);border-left:3px solid #7ce087;border-radius:8px;min-width:0}',
      '.cc-card.cc-k-via{border-left-color:#e0b45a}',
      '.cc-card.cc-k-short{border-left-color:#e08b6a}',
      '.cc-card.cc-k-locked{border-left-color:#5f5f66;opacity:.7}',
      '.cc-card-ic,.cc-card-name{cursor:pointer}',
      '.cc-card.cc-k-locked .cc-card-ic,.cc-card.cc-k-locked .cc-card-name{cursor:default}',
      '.cc-card.cc-sel{border-color:rgba(255,193,7,.6)}',
      '.cc-card-ic{position:relative;width:38px;height:38px;background:linear-gradient(180deg,#131316,#1d1d20);border-radius:6px;display:flex;align-items:center;justify-content:center;border:1px solid #000}',
      '.cc-card-ic img{width:32px;height:32px;object-fit:contain}',
      '.cc-yield{position:absolute;right:-4px;bottom:-4px;background:#000;color:#ffe6a8;font-size:11px;font-weight:700;border-radius:4px;padding:0 4px;border:1px solid rgba(255,193,7,.4)}',
      '.cc-card-head{min-width:0}',
      '.cc-card-name{font-weight:700;font-size:14px;line-height:18px;color:#fff;overflow-wrap:break-word}',
      '.cc-new{display:inline-block;font-size:10px;font-weight:700;color:#fff;background:linear-gradient(180deg,#ff6a3d,#e5382b);border-radius:4px;padding:1px 5px;margin-left:6px;vertical-align:2px}',
      '.cc-card-sub{display:flex;align-items:center;gap:6px;min-height:20px;margin-top:2px}',
      '.cc-card-st{flex:1;min-width:0;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
      '.cc-st-now{color:#7ce087}.cc-st-via{color:#e0b45a}.cc-st-short{color:#e08b6a}.cc-st-locked{color:#8a8a92}',
      '.cc-cost{flex:none;font-size:11px;color:#9fc7e8;white-space:nowrap}',
      '.cc-mats{display:flex;flex-wrap:wrap;gap:4px}',
      '.cc-mat{display:inline-flex;align-items:center;gap:3px;background:rgba(255,255,255,.05);border-radius:5px;padding:1px 6px 1px 2px;font-size:12px;font-weight:600}',
      '.cc-mat img{width:20px;height:20px;object-fit:contain}',
      '.cc-mat.ok{color:#7ce087}.cc-mat.via{color:#e0b45a}.cc-mat.no{color:#e08b6a}',
      '.cc-mat.hit{box-shadow:0 0 0 1px #ffc107}',
      '.cc-card-foot{grid-column:1/-1;display:flex;align-items:center;gap:6px;min-width:0}',
      '.cc-card-foot .cc-mats{flex:1;min-width:0}',
      '.cc-pin{border:1px solid rgba(255,255,255,.15);background:rgba(255,255,255,.04);color:#aaa;border-radius:6px;font-size:11px;padding:2px 7px;cursor:pointer;flex:none;white-space:nowrap}',
      '.cc-pin.on{border-color:rgba(255,193,7,.6);color:#ffd36b;background:rgba(255,193,7,.12)}',
      '.cc-add{background:linear-gradient(180deg,rgba(168,118,28,.94),rgba(110,76,16,.96));border:1px solid rgba(255,214,120,.5);color:#fff4d6;font-weight:700;border-radius:7px;padding:4px 12px;cursor:pointer;font-size:12px}',
      '.cc-add[disabled],.cc-pin[disabled]{filter:grayscale(1);opacity:.4;cursor:default}',
      '.cc-stepper{display:inline-flex;align-items:center;border:1px solid rgba(255,193,7,.35);border-radius:7px;overflow:hidden;background:rgba(0,0,0,.35);flex:none}',
      '.cc-stepper button{width:22px;height:22px;border:0;background:rgba(255,255,255,.06);color:#ffd36b;font-size:14px;cursor:pointer;line-height:1;padding:0}',
      '.cc-stepper button:hover{background:rgba(255,193,7,.18)}',
      '.cc-val{min-width:28px;text-align:center;font-weight:700;color:#fff;font-size:12px}',
      '.cc-empty{grid-column:1/-1;text-align:center;color:#888;padding:30px}',
      '.cc-pill{font-size:12px;padding:2px 10px;border-radius:20px;font-weight:600}',
      '.cc-pill.idle{background:rgba(255,255,255,.08);color:#bbb}',
      '.cc-pill.running{background:rgba(124,224,135,.16);color:#7ce087;border:1px solid rgba(124,224,135,.35)}',
      '.cc-pill.paused{background:rgba(224,180,90,.16);color:#e0b45a;border:1px solid rgba(224,180,90,.4)}',
      '.cc-banner{background:rgba(224,180,90,.12);border:1px solid rgba(224,180,90,.4);color:#f1d79b;border-radius:8px;padding:7px 10px;font-size:12px;margin-bottom:8px}',
      '.cc-banner.bad{background:rgba(224,139,106,.12);border-color:rgba(224,139,106,.4);color:#f2c0ad}',
      '.cc-outbar{flex:none;font-size:13px;font-weight:700}',
      '.cc-stamina{font-size:12px;font-weight:600;color:#9fc7e8;white-space:nowrap}',
      '.cc-stamina:empty{display:none}',
      '.cc-stamina.cc-warn{color:#ffd0c2;background:rgba(224,139,106,.25);border:1px solid #e08b6a;border-radius:6px;padding:1px 8px}',
      '.cc-hint{font-size:12px;color:#888;margin:0 0 8px}',
      '.cc-notes{flex:none}',
      '.cc-qlist{flex:1 1 auto;min-height:110px;overflow-y:auto;display:flex;flex-direction:column;gap:5px;padding-right:4px}',
      '.cc-qrow{display:flex;align-items:center;gap:6px;background:rgba(8,8,10,.55);border:1px solid rgba(255,255,255,.07);border-radius:8px;padding:6px 7px;flex:none}',
      '.cc-qrow.topup{border-left:2px dashed #e0b45a}',
      '.cc-qrow.active{border-color:rgba(124,224,135,.55)}',
      '.cc-qrow.short{border-color:rgba(224,139,106,.45)}',
      '.cc-qrow.drag{opacity:.4}',
      '.cc-qrow.drop{box-shadow:0 -2px 0 #ffc107}',
      '.cc-num{width:16px;text-align:center;color:#888;font-size:12px}',
      '.cc-handle{cursor:grab;color:#777;font-size:15px;padding:0 2px}',
      '.cc-qic{width:28px;height:28px;object-fit:contain;flex:none}',
      '.cc-qmain{flex:1;min-width:0}',
      '.cc-qname{font-weight:700;color:#fff;font-size:13px;display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:2;overflow:hidden;overflow-wrap:break-word}',
      '.cc-tag{display:inline-block;font-size:10px;font-weight:700;color:#1b1406;background:#e0b45a;border-radius:4px;padding:1px 6px;margin-left:6px;vertical-align:1px}',
      '.cc-qsub{font-size:12px;color:#aaa}',
      '.cc-qst{font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
      '.cc-prog{height:5px;background:rgba(255,255,255,.08);border-radius:3px;overflow:hidden;margin-top:3px}',
      '.cc-prog i{display:block;height:100%;width:100%;transform-origin:0 50%;transform:scaleX(0);transition:transform .5s linear;background:linear-gradient(90deg,#c08a1e,#ffc107)}',
      '.cc-prog.paused i{background:linear-gradient(90deg,#7a6a44,#a99560)}',
      '.cc-qmove{display:flex;flex-direction:column;gap:2px}',
      '.cc-qmove button,.cc-x{width:22px;height:17px;border:1px solid rgba(255,255,255,.12);background:rgba(255,255,255,.05);color:#ccc;border-radius:4px;cursor:pointer;font-size:9px;line-height:1;padding:0}',
      '.cc-qmove button[disabled]{opacity:.3;cursor:default}',
      '.cc-x{height:24px;width:24px;font-size:12px;color:#e8a593;flex:none}',
      '.cc-sect{margin-top:10px;border-top:1px solid rgba(255,255,255,.08);padding-top:8px;flex:none}',
      '.cc-sect h4{margin:0 0 5px;font-size:13px;color:#ffe6a8;display:flex;align-items:center;gap:6px;font-weight:700}',
      '.cc-totwrap{max-height:150px;overflow-y:auto}',
      '.cc-tot{width:100%;border-collapse:collapse;font-size:12px}',
      '.cc-tot td{padding:2px 4px;border-bottom:1px solid rgba(255,255,255,.04)}',
      '.cc-tot td.r{text-align:right;font-weight:700;width:56px}',
      '.cc-tot tr.no td{color:#e08b6a}',
      '.cc-tot img{width:18px;height:18px;vertical-align:-4px;object-fit:contain;margin-right:4px}',
      '.cc-made{display:flex;flex-wrap:wrap;gap:5px;max-height:60px;overflow-y:auto}',
      '.cc-made-item{display:inline-flex;align-items:center;gap:4px;font-size:12px;background:rgba(224,180,90,.1);border:1px dashed rgba(224,180,90,.45);color:#f1d79b;border-radius:6px;padding:1px 7px 1px 2px}',
      '.cc-made-item img{width:18px;height:18px;object-fit:contain}',
      '.cc-actions{display:flex;align-items:center;gap:10px;margin-top:10px;flex:none}',
      '.cc-btn{background:linear-gradient(180deg,rgba(58,58,60,.9),rgba(38,38,40,.92));border:1px solid rgba(255,255,255,.14);color:#e6e6e6;border-radius:8px;padding:6px 12px;cursor:pointer;font-size:13px}',
      '.cc-btn[disabled]{opacity:.4;cursor:default}',
      '.cc-go{background:linear-gradient(180deg,rgba(168,118,28,.94),rgba(110,76,16,.96));border:1px solid rgba(255,214,120,.55);color:#fff4d6;font-size:15px;font-weight:700;padding:10px 24px;border-radius:10px;cursor:pointer;letter-spacing:1px}',
      '.cc-go[disabled]{filter:grayscale(.8);opacity:.5;cursor:default}',
      '.cc-stop{background:linear-gradient(180deg,rgba(120,44,36,.94),rgba(78,28,22,.96));border:1px solid rgba(255,140,120,.45);color:#ffe2dc;font-size:15px;font-weight:700;padding:10px 22px;border-radius:10px;cursor:pointer}',
      '.cc-keeps{display:flex;flex-direction:column;gap:4px;max-height:140px;overflow-y:auto}',
      '.cc-keep{display:flex;align-items:center;gap:8px;background:rgba(8,8,10,.45);border-radius:7px;padding:3px 8px;flex:none}',
      '.cc-keep img{width:24px;height:24px;object-fit:contain}',
      '.cc-kname{font-size:12px;font-weight:600;color:#fff;width:100px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
      '.cc-kbar{flex:1;height:7px;background:rgba(255,255,255,.08);border-radius:4px;overflow:hidden}',
      '.cc-kbar i{display:block;height:100%;background:#7ce087}',
      '.cc-kbar i.low{background:#e0b45a}',
      '.cc-kval{font-size:12px;color:#ccc;width:56px;text-align:right}',
      '.cc-panel{display:flex;flex-direction:column;gap:8px;margin-top:12px;font-size:12px;width:100%}',
      '.cc-panel:empty{display:none}',
      '.cc-row{display:flex;flex-wrap:wrap;align-items:center;justify-content:center;gap:8px}',
      '.cc-plan{color:#e0b45a;text-align:center}',
      '.cc-panel .btn{padding:5px 14px;font-size:12px}'
    ].join('');
    doc.head.appendChild(style);
  }

  function el(doc, tag, className, text) {
    var node = doc.createElement(tag);

    if (className) node.className = className;
    if (text != null) node.textContent = text;

    return node;
  }

  function button(doc, className, text, title, onClick, isDisabled) {
    var node = el(doc, 'button', className, text);
    node.type = 'button';
    node.title = title;
    node.disabled = !!isDisabled;
    node.addEventListener('click', function (e) {
      e.stopPropagation();
      onClick();
    });

    return node;
  }

  function picture(doc, src, className) {
    if (!src) return el(doc, 'span', className);

    var node = el(doc, 'img', className);
    node.src = src;
    node.alt = '';
    node.onerror = function () { node.style.visibility = 'hidden'; };

    return node;
  }

  function stepper(doc, value, onStep) {
    var box = el(doc, 'span', 'cc-stepper');

    box.appendChild(button(doc, 'cc-less', GLYPH.minus, say('oneLess'), function () { onStep(-1); }));
    box.appendChild(el(doc, 'span', 'cc-val', String(value)));
    box.appendChild(button(doc, 'cc-more', '+', say('oneMore'), function () { onStep(1); }));

    return box;
  }

  // Changes the page only where it differs, so a redraw of the same thing costs the page nothing.
  function setText(node, text) {
    if (node.textContent !== text) node.textContent = text;
  }

  function setClass(node, className) {
    if (node.className !== className) node.className = className;
  }

  // Fills a part of the hub again only when what it shows has changed. inputs is everything fill reads that can change
  // under it; the key is kept on the box itself, so a box built afresh is always filled.
  function redraw(box, inputs, fill) {
    // A word of the current language goes in the key, so a language change draws everything again.
    var key = say('recipes') + JSON.stringify(inputs);

    if (!box || box.__ccKey === key) return;

    fill();
    box.__ccKey = key;
  }

  function hasOwn(map, key) {
    return !!map && Object.prototype.hasOwnProperty.call(map, key);
  }

  function lower(text) {
    return String(text || '').toLowerCase();
  }

  function clamp(value, low, high) {
    return Math.max(low, Math.min(high, value));
  }

  function words() {
    return window.__craftinghubWords || ENGLISH;
  }

  // say('keepN', 3) fills {0} with 3; a key the plugin's words lack falls back to English.
  function say(key) {
    var args = arguments;
    var text = words()[key] || ENGLISH[key] || key;

    return text.replace(/\{(\d)\}/g, function (all, index) { return String(args[Number(index) + 1]); });
  }

  function counted(count, one, many) {
    return say(count === 1 ? one : many, count);
  }

  // The plugin's lines use the multiplication sign; the page shows a plain x.
  function plain(text) {
    return String(text || '').split(GLYPH.times).join('x');
  }

  function keptFor(key) {
    return hasOwn(view.keep && view.keep.byKey, key) ? view.keep.byKey[key] : 0;
  }

  function yieldOf(key) {
    return hasOwn(view.yields, key) ? view.yields[key] : 1;
  }

  // The plugin's reading of the player's stamina: have, out, and cost, the stamina one craft takes by recipe key. null
  // when the plugin could not read it, and then nothing about stamina is shown.
  function staminaOf() {
    var stamina = view.stamina;

    return stamina && typeof stamina === 'object' ? stamina : null;
  }

  function staminaCosts() {
    var stamina = staminaOf();

    return stamina && stamina.cost && typeof stamina.cost === 'object' ? stamina.cost : null;
  }

  function staminaCost(key) {
    var costs = staminaCosts();
    var cost = hasOwn(costs, key) ? costs[key] : null;

    return typeof cost === 'number' ? cost : null;
  }

  // The recipes the page last drew its list from, as the game sent them.
  function recipesOf(win) {
    var recipes = pageValue(win, 'lastRecipeData');

    return (recipes || []).filter(function (r) { return r && r.recipeKey; });
  }

  // Read once per recipe for as long as the game keeps sending the same recipes.
  function materialsOf(win, r) {
    var cache = win.__ccUi.mats;

    if (!hasOwn(cache, r.recipeKey)) cache[r.recipeKey] = parseMaterials(r);

    return cache[r.recipeKey];
  }

  // What the page itself reads from each recipe (getMaterials): per material its icon, name, need and have for one
  // craft, hasEnough, and sub, the key of the recipe that makes it.
  function parseMaterials(r) {
    try {
      var mats = r.materialsJson ? JSON.parse(r.materialsJson) : [];

      return Array.isArray(mats) ? mats : [];
    } catch (e) {
      return [];
    }
  }

  function kindOf(r) {
    if (r.craftLocked) return 'locked';

    if (r.canCraft) return 'now';

    if (hasOwn(view.marks, r.recipeKey)) return 'via';

    return 'short';
  }

  // The page's own grouping: categories 1 to 7, anything else in 0 ("other").
  function groupOf(r) {
    return r.noteCategory >= 1 && r.noteCategory <= 7 ? r.noteCategory : 0;
  }

  // As the page's own search (matchRecipe): a hit on the name or a dye colour lists no materials, a hit on materials
  // lists the names that matched, and a miss is null.
  function searchHits(r, mats, query) {
    if (!query) return [];

    if (lower(r.name).indexOf(query) >= 0 || lower(r.dyeSearchText).indexOf(query) >= 0) return [];

    var hits = mats.filter(function (m) { return lower(m.name).indexOf(query) >= 0; }).map(function (m) { return m.name; });

    return hits.length ? hits : null;
  }

  function entriesOf(win, query) {
    return recipesOf(win).map(function (r) {
      var mats = materialsOf(win, r);

      return { recipe: r, kind: kindOf(r), group: groupOf(r), mats: mats, hits: searchHits(r, mats, query) };
    });
  }

  function inFilter(filter, entry) {
    if (filter === 'kept') return keptFor(entry.recipe.recipeKey) > 0;

    return filter === 'all' || filter === entry.kind;
  }

  function countGroups(entries) {
    var groups = {};

    entries.forEach(function (entry) {
      var group = groups[entry.group] || (groups[entry.group] = { size: 0, canMake: 0 });

      group.size += 1;
      if (entry.kind === 'now' || entry.kind === 'via') group.canMake += 1;
    });

    return groups;
  }

  function lockedLast(entries) {
    var open = entries.filter(function (entry) { return entry.kind !== 'locked'; });
    var locked = entries.filter(function (entry) { return entry.kind === 'locked'; });

    return open.concat(locked);
  }

  function groupName(win, meta) {
    try {
      if (typeof win.noteCatDisplayName === 'function') return win.noteCatDisplayName(meta);
    } catch (e) {}

    return meta.name;
  }

  function statusLine(entry) {
    var r = entry.recipe;

    if (entry.kind === 'locked') return r.craftLockText || say('locked');

    if (entry.kind === 'now') return say('canMakeNow');

    if (entry.kind === 'via') return plain(view.marks[r.recipeKey]);

    var missing = entry.mats.filter(function (m) { return !m.hasEnough && (m.need || 0) > (m.have || 0); }).map(function (m) { return m.name + ' ' + (m.need - (m.have || 0)); });

    return missing.length ? say('shortOf', missing.join(say('itemSep'))) : say('cannotMake');
  }

  // Green when you own enough for the amount set, amber when the bench lists a recipe that makes it, red otherwise.
  function materialLook(m, amount, byKey) {
    if ((m.have || 0) >= (m.need || 0) * amount) return 'ok';

    var maker = m.sub ? byKey[m.sub] : null;

    return maker && !maker.craftLocked ? 'via' : 'no';
  }

  function amountFor(win, key) {
    return win.__ccUi.amounts[key] || 1;
  }

  // ---------------------------------------------------------------- layout

  // The hub's two columns go after the game's own list inside the bench panel, and the bench moves into the bag's
  // panel as a tab, so the bench grid and its Craft button keep working as before.
  function ensureLayout(win) {
    var doc = win.document;
    var panel = doc.querySelector('.combined-panel');

    if (!panel || !doc.querySelector('.workbench-section')) return false;

    ensureTabs(win);

    if (doc.getElementById('cc-mid') && doc.getElementById('cc-queue')) return true;

    removeById(doc, 'cc-mid');
    removeById(doc, 'cc-queue');
    panel.classList.add('cc-hub');
    if (panel.parentElement) panel.parentElement.classList.add('cc-wide');
    panel.appendChild(buildMid(win));
    panel.appendChild(buildQueue(win));

    return true;
  }

  function removeById(doc, id) {
    var node = doc.getElementById(id);

    if (node) node.remove();
  }

  // ---------------------------------------------------------------- the bag panel's Workbench tab

  // The Workbench tab sits right after the bag's own tab. It is the hub's view, not one of the game's tabs: the game's
  // own tab stays as it was, so the bag view goes back to whichever of the bag, the drawer or a cabinet was open. Once
  // the hub has failed to draw on a page, the bench stays where the game put it there, so a hub that keeps failing
  // cannot move it back and forth at every try.
  function ensureTabs(win) {
    var doc = win.document;
    var bag = doc.querySelector('.backpack-panel');
    var bagTab = doc.getElementById('tab-bag');
    var bench = doc.querySelector('.workbench-section');

    if (!bag || !bagTab || !bench || win.__ccUi.isTabOff) return;

    var tab = doc.getElementById(BENCH_TAB) || addBenchTab(doc, bagTab);
    var label = benchTitle(bench);

    if (tab.textContent !== label) tab.textContent = label;
    if (bench.parentElement !== bag) bag.appendChild(bench);
    if (!bag.__ccWired) wireTabs(win, bag);

    bag.classList.add('cc-tabbed');
    applyView(win);
  }

  function addBenchTab(doc, bagTab) {
    var tab = el(doc, 'button', 'left-tab cc-tab');

    tab.id = BENCH_TAB;
    tab.type = 'button';
    bagTab.parentNode.insertBefore(tab, bagTab.nextSibling);

    return tab;
  }

  // The bench's own heading, in the game's language; the tab names the bench, so the heading itself is hidden.
  function benchTitle(bench) {
    var heading = bench.querySelector('h2');
    var text = heading ? heading.textContent.trim() : '';

    return text || say('workbench');
  }

  // Wired once on the panel, which the page never rebuilds; the game's cabinet tabs are rebuilt, so their clicks are
  // caught as they bubble up.
  function wireTabs(win, bag) {
    bag.__ccWired = true;
    bag.addEventListener('click', function (e) {
      var tab = e.target && e.target.closest ? e.target.closest('.left-tab') : null;

      if (tab) showView(win, tab.id === BENCH_TAB ? 'bench' : 'bag');
    });
    win.document.addEventListener('mousemove', function (e) { hoverSwitch(win, e); }, true);
  }

  function showView(win, next) {
    var ui = win.__ccUi;

    if (!ui || ui.view === next) return;

    ui.view = next;
    applyView(win);
    refit(win);
  }

  function applyView(win) {
    var doc = win.document;
    var bag = doc.querySelector('.backpack-panel');
    var tab = doc.getElementById(BENCH_TAB);

    if (!bag || !tab) return;

    var isBench = win.__ccUi.view === 'bench';

    bag.classList.toggle('cc-bench-view', isBench);
    tab.classList.toggle('active', isBench);
  }

  // While the game drags an item, resting on the Workbench tab, or on the game's open tab, switches to it, so an item
  // can be carried between the bag and the bench. The tabs are found by where they are, as the page's own tab drop
  // does, since the dragged item is under the pointer. A new move starts the wait again only on another tab.
  function hoverSwitch(win, e) {
    var ui = win.__ccUi;
    var target = isGameDragging(win) ? viewUnder(win.document, e.clientX, e.clientY) : null;

    if (ui.hover && ui.hover.view === target) return;

    var hover = target && target !== ui.view ? { view: target } : null;
    ui.hover = hover;

    if (!hover) return;

    setTimeout(function () {
      if (ui.hover !== hover) return;

      ui.hover = null;

      if (isGameDragging(win)) showView(win, hover.view);
    }, HOVER_SWITCH_MS);
  }

  function isGameDragging(win) {
    var ui = win.__ccUi;

    if (!ui.dragCoord) ui.dragCoord = pageValue(win, 'dragCoord');

    try {
      return !!(ui.dragCoord && ui.dragCoord.isDragging());
    } catch (e) {
      return false;
    }
  }

  // Only the game's open tab leads back to the bag view. Its other tabs are left to the page's own drop-on-a-tab, and
  // opening one mid-drag would show it before the game has sent its items.
  function viewUnder(doc, x, y) {
    if (isUnder(doc.getElementById(BENCH_TAB), x, y)) return 'bench';

    var open = doc.querySelectorAll('.left-tab.active');

    for (var i = 0; i < open.length; i++) {
      if (open[i].id !== BENCH_TAB && isUnder(open[i], x, y)) return 'bag';
    }

    return null;
  }

  function isUnder(node, x, y) {
    var rect = node ? visibleRect(node) : null;

    return !!rect && x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;
  }

  // The game's tab strip scrolls sideways; the part of a tab scrolled out of it does not count, as for the page's own
  // drop on a tab. null when nothing of the tab shows.
  function visibleRect(node) {
    var rect = node.getBoundingClientRect();
    var strip = node.closest ? node.closest('.left-tabs-scroll') : null;

    if (rect.width <= 0) return null;

    if (!strip) return rect;

    var box = strip.getBoundingClientRect();
    var left = Math.max(rect.left, box.left);
    var right = Math.min(rect.right, box.right);
    var top = Math.max(rect.top, box.top);
    var bottom = Math.min(rect.bottom, box.bottom);

    if (right <= left || bottom <= top) return null;

    return { left: left, right: right, top: top, bottom: bottom };
  }

  // The game's craft progress and its result are drawn over the bench, so the bench comes up when they do, for a craft
  // the player started; the queue's crafts leave the view alone. Only their coming up counts: a player who goes back to
  // the bag meanwhile stays there. During a drag the switch waits for the drop, and is dropped if the overlay has gone.
  function followCraftOverlay(win) {
    var ui = win.__ccUi;
    var overlay = win.document.getElementById('crafting-overlay');
    var isShown = !!overlay && overlay.style.display === 'flex';

    if (isShown && !ui.isOverlayShown) ui.isOverlayPending = ui.isOwnCraft;
    if (!isShown) ui.isOverlayPending = false;

    ui.isOverlayShown = isShown;

    if (!ui.isOverlayPending || isGameDragging(win)) return;

    ui.isOverlayPending = false;
    showView(win, 'bench');
  }

  // Puts the bench back in the game's panel, ahead of the game's recipe list, where the page has it.
  function untab(doc, panel) {
    var bag = doc.querySelector('.backpack-panel');
    var bench = doc.querySelector('.workbench-section');

    if (bag) bag.classList.remove('cc-tabbed', 'cc-bench-view');

    if (panel && bench && bench.parentElement !== panel) panel.insertBefore(bench, panel.querySelector('.notes-section'));
  }

  function buildMid(win) {
    var doc = win.document;
    var mid = el(doc, 'div', 'cc-col');
    var title = el(doc, 'div', 'cc-title');
    var count = el(doc, 'span', 'cc-dim');
    var search = el(doc, 'div', 'cc-search');
    var input = el(doc, 'input', 'cc-search-input');
    var clear = button(doc, 'cc-search-clear', GLYPH.cross, say('clearSearch'), function () {
      input.value = '';
      drawList(win, true);
      input.focus();
    });
    var filters = el(doc, 'div', 'cc-chips');
    var groups = slidingChipRow(doc, 'cc-groups');
    var list = el(doc, 'div', 'cc-list cc-scroll');

    mid.id = 'cc-mid';
    count.id = 'cc-count';
    input.id = 'cc-search';
    filters.id = 'cc-filters';
    list.id = 'cc-list';
    input.type = 'text';
    input.placeholder = say('search');
    input.spellcheck = false;
    input.autocomplete = 'off';
    mid.addEventListener('mousedown', holdUntilReleased, true);
    input.addEventListener('focus', function () { sendFocus(win, true); });
    input.addEventListener('blur', function () { sendFocus(win, false); });
    input.addEventListener('input', function () { drawList(win, true); });
    input.addEventListener('keydown', function (e) {
      e.stopPropagation();

      if (e.key === 'Enter') input.blur();
    });
    title.appendChild(el(doc, 'span', null, say('recipes')));
    title.appendChild(count);
    search.appendChild(input);
    search.appendChild(clear);
    mid.appendChild(title);
    mid.appendChild(search);
    mid.appendChild(filters);
    mid.appendChild(groups);
    mid.appendChild(list);

    return mid;
  }

  // A row of chips that stays on one line, so however many groups there are they never push the list down; when it
  // holds more than fits, the mouse wheel over it scrolls it sideways, as the Quick Actions row does.
  function slidingChipRow(doc, id) {
    var row = el(doc, 'div', 'cc-chips cc-slide');

    row.id = id;
    row.addEventListener('wheel', function (e) { scrollSideways(row, e); }, { passive: false });

    return row;
  }

  // A row that fits leaves the wheel alone.
  function scrollSideways(row, e) {
    var room = row.scrollWidth - row.clientWidth;

    if (room <= 0) return;

    var delta = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;

    if (e.deltaMode === 1) delta *= WHEEL_LINE_PX;
    if (e.deltaMode === 2) delta *= row.clientWidth;

    e.preventDefault();
    row.scrollLeft = clamp(row.scrollLeft + delta, 0, room);
  }

  function buildQueue(win) {
    var doc = win.document;
    var column = el(doc, 'div', 'cc-col');
    var title = el(doc, 'div', 'cc-title');
    var pill = el(doc, 'span', 'cc-pill idle');
    var stamina = el(doc, 'span', 'cc-stamina');
    var outOfStamina = el(doc, 'div', 'cc-banner bad cc-outbar', say('outOfStamina'));
    var notes = el(doc, 'div', 'cc-notes');
    var list = el(doc, 'div', 'cc-qlist cc-scroll');
    var totals = el(doc, 'div', 'cc-totwrap cc-scroll');
    var made = el(doc, 'div', 'cc-made cc-scroll');
    var actions = el(doc, 'div', 'cc-actions');
    var keeps = el(doc, 'div', 'cc-keeps cc-scroll');
    var keepSection = section(doc, say('keepInStock'), say('keepInStockAside'), keeps);

    column.id = 'cc-queue';
    pill.id = 'cc-pill';
    stamina.id = 'cc-stamina';
    outOfStamina.id = 'cc-stamina-out';
    outOfStamina.style.display = 'none';
    notes.id = 'cc-notes';
    list.id = 'cc-qlist';
    totals.id = 'cc-totals';
    made.id = 'cc-made';
    actions.id = 'cc-actions';
    keeps.id = 'cc-keeps';
    keepSection.id = 'cc-keepsect';
    column.addEventListener('mousedown', holdUntilReleased, true);
    list.addEventListener('mousemove', function (e) { dragOver(win, e.target); });
    title.appendChild(el(doc, 'span', null, say('craftQueue')));
    title.appendChild(el(doc, 'span', 'cc-grow'));
    title.appendChild(stamina);
    title.appendChild(pill);
    column.appendChild(title);
    column.appendChild(outOfStamina);
    column.appendChild(notes);
    column.appendChild(list);
    column.appendChild(section(doc, say('materials'), say('materialsAside'), totals));
    column.appendChild(section(doc, say('madeOnTheWay'), '', made));
    column.appendChild(actions);
    column.appendChild(keepSection);

    return column;
  }

  function section(doc, heading, aside, body) {
    var box = el(doc, 'div', 'cc-sect');
    var title = el(doc, 'h4', null, heading);

    if (aside) {
      title.appendChild(el(doc, 'span', 'cc-grow'));
      title.appendChild(el(doc, 'span', 'cc-dim', aside));
    }

    box.appendChild(title);
    box.appendChild(body);

    return box;
  }

  // The page zooms itself to fit the window from the size of its panels. The hub changes that size when it is added,
  // when the bag panel switches between the bag and the bench, and when the game shows or hides its recipe list, so
  // the page is asked to fit again then, and only then. Never during a drag, which works out where it is from the zoom
  // it started with; the poll fits the page once the item is dropped.
  function refit(win) {
    var doc = win.document;
    var panel = doc.querySelector('.combined-panel');
    var bag = doc.querySelector('.backpack-panel');
    var layout = (panel ? panel.className : '') + '|' + (bag ? bag.className : '');

    if (layout === win.__ccLayout || isGameDragging(win)) return;

    win.__ccLayout = layout;

    try {
      if (typeof win.applyViewportFit === 'function') win.applyViewportFit();
    } catch (e) {
      pageDiag(win, 'fit', 'could not fit the workbench page to the window: ' + e);
    }
  }

  // ---------------------------------------------------------------- recipe list

  // isNewView: the search, filter or group changed, so the list starts again from the top; any other redraw keeps
  // where the player had scrolled to.
  function drawList(win, isNewView) {
    var doc = win.document;
    var list = doc.getElementById('cc-list');
    var input = doc.getElementById('cc-search');

    if (!list || !input) return;

    var ui = win.__ccUi;
    var typed = input.value.trim();
    var entries = entriesOf(win, lower(typed));
    var metas = pageValue(win, 'NOTE_CAT_META') || [];
    var groups = countGroups(entries);
    var scrollTop = isNewView ? 0 : list.scrollTop;

    if (ui.filter === 'kept' && !view.keep.on) ui.filter = 'all';
    if (ui.group !== ALL_GROUPS && !groups[ui.group]) ui.group = ALL_GROUPS;

    var shown = entries.filter(function (entry) {
      return entry.hits !== null && inFilter(ui.filter, entry) && (ui.group === ALL_GROUPS || entry.group === ui.group);
    });
    var isFlat = typed !== '' || ui.group !== ALL_GROUPS;

    drawFilters(win, entries);
    drawGroups(win, metas, groups);
    doc.getElementById('cc-count').textContent = say('countOf', shown.length, entries.length);
    list.innerHTML = '';

    if (shown.length) drawCards(win, list, metas, shown, groups, isFlat);
    else list.appendChild(el(doc, 'div', 'cc-empty', typed ? say('noMatch', typed) : say('noRecipes')));

    list.scrollTop = scrollTop;
    list.__ccKey = listKey(win);
  }

  // The game and the plugin redraw the list only when something it shows from them changed. The player's own search,
  // filter, group and amounts draw it straight away (drawList), and the recipe on the bench is marked in place.
  function drawListIfChanged(win) {
    var list = win.document.getElementById('cc-list');

    if (list && list.__ccKey !== listKey(win)) drawList(win, false);
  }

  function listKey(win) {
    var metas = pageValue(win, 'NOTE_CAT_META') || [];
    var collapsed = pageValue(win, 'collapsedCats');
    var groupNames = metas.map(function (meta) { return groupName(win, meta); });

    return JSON.stringify([
      win.__ccUi.recipeVersion,
      groupNames,
      collapsed ? Array.from(collapsed) : null,
      view.marks,
      view.keep.on,
      view.keep.byKey,
      view.yields,
      staminaCosts()
    ]);
  }

  function markSelected(win) {
    var list = win.document.getElementById('cc-list');
    var cards = list ? list.querySelectorAll('.cc-card') : [];

    Array.prototype.forEach.call(cards, function (card) {
      var isSelected = card.getAttribute('data-key') === selectedKey;

      if (card.classList.contains('cc-sel') !== isSelected) card.classList.toggle('cc-sel', isSelected);
    });
  }

  function drawFilters(win, entries) {
    var doc = win.document;
    var row = doc.getElementById('cc-filters');
    var ui = win.__ccUi;

    row.innerHTML = '';
    FILTERS.forEach(function (filter) {
      if (filter.id === 'kept' && !view.keep.on) return;

      var count = entries.filter(function (entry) { return inFilter(filter.id, entry); }).length;

      row.appendChild(chip(doc, say(filter.label), count, ui.filter === filter.id, function () {
        ui.filter = filter.id;
        drawList(win, true);
      }));
    });
  }

  // "All groups", then each of the game's groups that has recipes, with how many of them you can make.
  function drawGroups(win, metas, groups) {
    var doc = win.document;
    var row = doc.getElementById('cc-groups');
    var ui = win.__ccUi;

    refillRow(row, function () {
      row.appendChild(chip(doc, say('allGroups'), null, ui.group === ALL_GROUPS, function () {
        ui.group = ALL_GROUPS;
        drawList(win, true);
      }));
      metas.forEach(function (meta) {
        if (!groups[meta.id]) return;

        row.appendChild(chip(doc, groupName(win, meta), groups[meta.id].canMake, ui.group === meta.id, function () {
          ui.group = ui.group === meta.id ? ALL_GROUPS : meta.id;
          drawList(win, true);
        }));
      });
    });
  }

  // Draws a chip row again where the player had scrolled it to, so picking a chip near its far end does not throw the
  // row back to its start.
  function refillRow(row, fill) {
    var scrollLeft = row.scrollLeft;

    row.innerHTML = '';
    fill();
    row.scrollLeft = scrollLeft;
  }

  function chip(doc, label, count, isOn, onClick) {
    var node = button(doc, 'cc-fchip' + (isOn ? ' on' : ''), label + ' ', '', onClick);

    if (count !== null) node.appendChild(el(doc, 'span', 'n' + (count ? '' : ' z'), String(count)));

    return node;
  }

  // In the game's group order. Flat (no headers) while searching or showing one group, as the game's own search is.
  function drawCards(win, list, metas, shown, groups, isFlat) {
    var collapsed = isFlat ? null : pageValue(win, 'collapsedCats');
    var byKey = {};

    recipesOf(win).forEach(function (r) { byKey[r.recipeKey] = r; });
    metas.forEach(function (meta) {
      var members = shown.filter(function (entry) { return entry.group === meta.id; });
      var isOpen = !(collapsed && collapsed.has(meta.id));

      if (!members.length) return;

      if (!isFlat) list.appendChild(groupHeader(win, meta, groups[meta.id], isOpen));

      if (isOpen) lockedLast(members).forEach(function (entry) { list.appendChild(card(win, entry, byKey)); });
    });
  }

  function groupHeader(win, meta, counts, isOpen) {
    var doc = win.document;
    var header = el(doc, 'div', 'cc-ghead' + (isOpen ? ' open' : ''));

    header.setAttribute('data-group', meta.id);
    header.appendChild(el(doc, 'span', 'cc-arrow', isOpen ? GLYPH.open : GLYPH.closed));
    header.appendChild(el(doc, 'span', null, groupName(win, meta)));
    header.appendChild(el(doc, 'span', 'cc-gcount', say('canMakeCount', counts.canMake, counts.size)));
    header.addEventListener('click', function () { toggleGroup(win, meta.id); });

    return header;
  }

  // Collapses through the page's own set and sends what its own headers send, so the game saves the choice as before.
  function toggleGroup(win, id) {
    var collapsed = pageValue(win, 'collapsedCats');

    if (!collapsed) return;

    var isNowCollapsed = !collapsed.has(id);

    if (isNowCollapsed) collapsed.add(id);
    else collapsed.delete(id);

    send(win, 'NOTE_CAT_TOGGLE', { cat: id, collapsed: isNowCollapsed });
    drawList(win, false);
  }

  // The name has the line beside the icon to itself and wraps rather than being cut short. The status line and Keep
  // sit under it, and the materials, the amount and Add share one row across the bottom of the card.
  function card(win, entry, byKey) {
    var doc = win.document;
    var r = entry.recipe;
    var key = r.recipeKey;
    var amount = amountFor(win, key);
    var isLocked = entry.kind === 'locked';
    var node = el(doc, 'div', 'cc-card cc-k-' + entry.kind + (key === selectedKey ? ' cc-sel' : ''));
    var icon = el(doc, 'div', 'cc-card-ic');
    var head = el(doc, 'div', 'cc-card-head');
    var name = el(doc, 'div', 'cc-card-name', r.name || key);
    var sub = el(doc, 'div', 'cc-card-sub');
    var foot = el(doc, 'div', 'cc-card-foot');
    var mats = el(doc, 'div', 'cc-mats');
    var cost = staminaCost(key);

    node.setAttribute('data-key', key);
    icon.appendChild(picture(doc, r.icon));
    if (yieldOf(key) > 1) icon.appendChild(el(doc, 'span', 'cc-yield', 'x' + yieldOf(key)));
    if (r.isNew) name.appendChild(el(doc, 'span', 'cc-new', 'NEW'));
    sub.appendChild(el(doc, 'div', 'cc-card-st cc-st-' + entry.kind, statusLine(entry)));
    if (cost !== null) sub.appendChild(el(doc, 'span', 'cc-cost', say('staminaCost', cost)));
    if (view.keep.on) sub.appendChild(keepButton(doc, key, isLocked));
    head.appendChild(name);
    head.appendChild(sub);
    entry.mats.forEach(function (m) { mats.appendChild(materialChip(doc, m, amount, byKey, entry.hits)); });
    foot.appendChild(mats);
    foot.appendChild(stepper(doc, amount, function (step) {
      win.__ccUi.amounts[key] = clamp(amount + step, 1, MAX_AMOUNT);
      drawList(win, false);
    }));
    foot.appendChild(button(doc, 'cc-add', say('add'), say('addTip'), function () {
      post('add|' + encodeURIComponent(key) + '|' + amount);
    }, isLocked));
    node.appendChild(icon);
    node.appendChild(head);
    node.appendChild(foot);
    icon.addEventListener('click', function () { pickRecipe(win, key, isLocked); });
    name.addEventListener('click', function () { pickRecipe(win, key, isLocked); });

    return node;
  }

  // The card's picture and name lay its recipe on the bench, as a click on the game's own row does; the rest of the
  // card is for setting it up, so a missed click on the amount does not swap the bench. Never while a queue run holds
  // the bench: a recipe laid between the queue's own pick and its craft would be crafted in its place.
  function pickRecipe(win, key, isLocked) {
    if (isLocked || view.running) return;

    send(win, 'RECIPE_CLICK', { recipeKey: key });
  }

  function materialChip(doc, m, amount, byKey, hits) {
    var isHit = hits.indexOf(m.name) >= 0;
    var node = el(doc, 'span', 'cc-mat ' + materialLook(m, amount, byKey) + (isHit ? ' hit' : ''));

    node.title = m.name || '';
    node.appendChild(picture(doc, m.icon));
    node.appendChild(el(doc, 'span', null, (m.have || 0) + '/' + (m.need || 0) * amount));

    return node;
  }

  function keepButton(doc, key, isLocked) {
    var kept = keptFor(key);
    var title = kept ? say('keepStopTip') : say('keepTip', DEFAULT_KEEP);

    return button(doc, 'cc-pin' + (kept ? ' on' : ''), kept ? say('keepN', kept) : say('keep'), title, function () {
      post('keep|' + encodeURIComponent(key) + '|' + (kept ? 0 : DEFAULT_KEEP));
    }, isLocked);
  }

  // ---------------------------------------------------------------- queue column

  // Each part is filled again only when what it shows changed, so the plugin's frequent views cost the page little and
  // the queue keeps where it was scrolled to.
  function drawQueue(win) {
    var doc = win.document;
    var list = doc.getElementById('cc-qlist');

    if (!list) return;

    var version = win.__ccUi.recipeVersion;
    var icons = iconsOf(win);

    drawPill(doc);
    drawStamina(doc);
    redraw(doc.getElementById('cc-notes'), [view.running, view.paused, view.headsUp, view.status], function () { drawNotes(doc); });
    redraw(list, [view.queue, view.running, view.paused, view.activeId, view.yields, version], function () { drawOrders(win, list, icons); });
    redraw(doc.getElementById('cc-totals'), [view.totals, view.queue.length, version], function () { drawTotals(doc, icons); });
    redraw(doc.getElementById('cc-made'), [view.made, version], function () { drawMade(doc, icons); });
    redraw(doc.getElementById('cc-actions'), [view.running, view.queue], function () { drawActions(doc); });
    redraw(doc.getElementById('cc-keeps'), [view.keep, version], function () { drawKeeps(doc, icons); });
  }

  function drawOrders(win, list, icons) {
    var doc = win.document;
    var scrollTop = list.scrollTop;

    list.innerHTML = '';
    view.queue.forEach(function (order, index) { list.appendChild(queueRow(win, icons, order, index)); });

    if (!view.queue.length) list.appendChild(el(doc, 'div', 'cc-empty', say('queueEmptyHint')));

    list.scrollTop = scrollTop;
  }

  // Pictures for the queue column, from the recipe data the page already holds: a recipe's icon by its key, and an
  // item's icon by its name, since the totals name items rather than recipes. Gathered once per set of recipes.
  function iconsOf(win) {
    var ui = win.__ccUi;

    if (!ui.icons) ui.icons = gatherIcons(win);

    return ui.icons;
  }

  function gatherIcons(win) {
    var icons = { byKey: {}, byName: {} };

    recipesOf(win).forEach(function (r) {
      if (r.icon) icons.byKey[r.recipeKey] = r.icon;
      if (r.icon && !icons.byName[r.name]) icons.byName[r.name] = r.icon;
      materialsOf(win, r).forEach(function (m) {
        if (m.icon && !icons.byName[m.name]) icons.byName[m.name] = m.icon;
      });
    });

    return icons;
  }

  function drawPill(doc) {
    var pill = doc.getElementById('cc-pill');
    var state = view.paused ? 'paused' : view.running ? 'running' : 'idle';

    setClass(pill, 'cc-pill ' + state);
    setText(pill, say({ idle: 'idle', running: 'crafting', paused: 'paused' }[state]));
  }

  // The stamina the player has, beside the queue's state, and a bar across the column once it has run out; the
  // readout then turns into a warning too, in words as well as colour.
  function drawStamina(doc) {
    var readout = doc.getElementById('cc-stamina');
    var bar = doc.getElementById('cc-stamina-out');

    if (!readout || !bar) return;

    var stamina = staminaOf();
    var isOut = !!stamina && stamina.out === true;
    var have = stamina && typeof stamina.have === 'number' ? say('staminaHave', stamina.have) : '';
    var display = isOut ? '' : 'none';

    setText(readout, isOut ? say('staminaOut', have || say('stamina')) : have);
    setClass(readout, 'cc-stamina' + (isOut ? ' cc-warn' : ''));

    if (bar.style.display !== display) bar.style.display = display;
  }

  // While paused the status is the run's own word on where it carries on, or why a button was refused, so it is shown
  // as the banner rather than a fixed line that could contradict it.
  function drawNotes(doc) {
    var notes = doc.getElementById('cc-notes');

    notes.innerHTML = '';

    if (!view.running && view.headsUp) notes.appendChild(el(doc, 'div', 'cc-banner bad', view.headsUp));

    if (view.status) notes.appendChild(el(doc, 'div', view.paused ? 'cc-banner' : 'cc-hint', view.status));
  }

  function queueRow(win, icons, order, index) {
    var doc = win.document;
    var look = STATE_LOOK[order.state] || '';
    var isActive = view.running && order.id === view.activeId;
    var crafts = order.crafts;
    var isLast = index === view.queue.length - 1;
    var row = el(doc, 'div', 'cc-qrow' + (order.topUp ? ' topup' : '') + (isActive ? ' active' : '') + (look === 'short' ? ' short' : ''));
    var handle = el(doc, 'span', 'cc-handle', GLYPH.handle);
    var main = el(doc, 'div', 'cc-qmain');
    var name = el(doc, 'div', 'cc-qname', order.name);
    var sub = el(doc, 'div', 'cc-qsub', craftsLine(order));
    var moves = el(doc, 'div', 'cc-qmove');

    row.setAttribute('data-index', index);
    handle.title = say('dragTip');
    handle.addEventListener('mousedown', function (e) {
      e.preventDefault();
      startDrag(win, row, order.id, index);
    });
    if (order.topUp) sub.appendChild(el(doc, 'span', 'cc-tag', say('topUp')));
    main.appendChild(name);
    main.appendChild(sub);
    if (look) main.appendChild(el(doc, 'div', 'cc-qst cc-st-' + look, order.state === 'ready' ? say('ready') : order.note));
    if (isActive) main.appendChild(progressBar(win));
    moves.appendChild(button(doc, 'cc-up', GLYPH.up, say('moveUp'), function () { post('move|' + order.id + '|' + (index - 1)); }, index === 0));
    moves.appendChild(button(doc, 'cc-down', GLYPH.down, say('moveDown'), function () { post('move|' + order.id + '|' + (index + 1)); }, isLast));
    row.appendChild(handle);
    row.appendChild(el(doc, 'span', 'cc-num', String(index + 1)));
    row.appendChild(picture(doc, icons.byKey[order.key], 'cc-qic'));
    row.appendChild(main);
    // Counts from the last amount asked for, so a second click before the plugin's answer redraws the row still adds.
    row.appendChild(stepper(doc, crafts, function (step) {
      if (crafts + step < 1) return;

      crafts += step;
      post('amount|' + order.id + '|' + crafts);
      awaitAnswer(row.parentElement);
    }));
    row.appendChild(moves);
    row.appendChild(button(doc, 'cc-x', GLYPH.cross, say('removeTip'), function () { post('remove|' + order.id); }));

    return row;
  }

  // The rows count ahead of the view until the plugin answers. Its answer redraws them even when it turns the change
  // down and so sends the same view again, which puts the count back to what the queue holds.
  function awaitAnswer(rows) {
    lastViewText = '';

    if (rows) rows.__ccKey = null;
  }

  function craftsLine(order) {
    var each = yieldOf(order.key);

    return counted(order.crafts, 'craftOne', 'craftMany') + (each > 1 ? say('makes', order.crafts * each) : '');
  }

  // The running order's bar follows the game's own craft progress bar; the poll keeps it in step.
  function progressBar(win) {
    var doc = win.document;
    var bar = el(doc, 'div', 'cc-prog' + (view.paused ? ' paused' : ''));
    var fill = el(doc, 'i');

    fill.id = 'cc-prog';
    fill.style.transform = gameProgress(win);
    bar.appendChild(fill);

    return bar;
  }

  function gameProgress(win) {
    var game = win.document.getElementById('craft-progress');

    return game && game.style.transform ? game.style.transform : 'scaleX(0)';
  }

  function mirrorProgress(win) {
    var fill = win.document.getElementById('cc-prog');
    var progress = gameProgress(win);

    if (fill && fill.style.transform !== progress) fill.style.transform = progress;
  }

  function drawTotals(doc, icons) {
    var box = doc.getElementById('cc-totals');
    var table = el(doc, 'table', 'cc-tot');

    box.innerHTML = '';

    if (!view.totals.length) {
      box.appendChild(el(doc, 'div', 'cc-hint', view.queue.length ? say('noMaterials') : say('nothingQueued')));

      return;
    }

    view.totals.forEach(function (total) {
      var row = el(doc, 'tr', total.short ? 'no' : null);
      var name = el(doc, 'td');

      name.appendChild(picture(doc, icons.byName[total.name]));
      name.appendChild(el(doc, 'span', null, total.name));
      row.appendChild(name);
      row.appendChild(el(doc, 'td', 'r', String(total.need)));
      row.appendChild(el(doc, 'td', 'r', String(total.have)));
      row.appendChild(el(doc, 'td', total.short ? 'r' : 'r cc-st-now', total.short ? say('shortN', total.short) : say('enough')));
      table.appendChild(row);
    });
    box.appendChild(table);
  }

  function drawMade(doc, icons) {
    var box = doc.getElementById('cc-made');

    box.innerHTML = '';

    if (!view.made.length) box.appendChild(el(doc, 'span', 'cc-hint', say('noParts')));

    view.made.forEach(function (step) {
      var item = el(doc, 'span', 'cc-made-item');

      item.appendChild(picture(doc, icons.byKey[step.key]));
      item.appendChild(el(doc, 'span', null, step.name + ' x' + step.crafts));
      box.appendChild(item);
    });
  }

  function drawActions(doc) {
    var bar = doc.getElementById('cc-actions');
    var orders = view.queue.length;
    var crafts = view.queue.reduce(function (sum, order) { return sum + order.crafts; }, 0);

    bar.innerHTML = '';

    if (view.running) {
      bar.appendChild(el(doc, 'span', 'cc-dim', say('craftingNow')));
      bar.appendChild(el(doc, 'span', 'cc-grow'));
      bar.appendChild(button(doc, 'cc-stop', say('stop'), say('stopTip'), function () { post('stop'); }));

      return;
    }

    bar.appendChild(button(doc, 'cc-btn cc-clear', say('clear'), say('clearTip'), function () { post('clear'); }, !orders));
    bar.appendChild(el(doc, 'span', 'cc-grow'));
    bar.appendChild(el(doc, 'span', 'cc-dim', orders ? counted(orders, 'orderOne', 'orderMany') + say('listSep') + counted(crafts, 'craftOne', 'craftMany') : say('queueIsEmpty')));
    bar.appendChild(button(doc, 'cc-go', say('craftQueue'), say('craftQueueTip'), function () { post('start'); }, !orders));
  }

  function drawKeeps(doc, icons) {
    var section = doc.getElementById('cc-keepsect');
    var box = doc.getElementById('cc-keeps');
    var targets = view.keep.targets || [];

    section.style.display = view.keep.on ? '' : 'none';
    box.innerHTML = '';

    if (!targets.length) box.appendChild(el(doc, 'div', 'cc-hint', say('nothingKept')));

    targets.forEach(function (target) { box.appendChild(keepRow(doc, icons, target)); });
  }

  // have is null when the game's items could not be counted; the bar then stays empty rather than guess.
  function keepRow(doc, icons, target) {
    var row = el(doc, 'div', 'cc-keep');
    var bar = el(doc, 'div', 'cc-kbar');
    var isCounted = typeof target.have === 'number';
    var percent = isCounted ? Math.min(100, Math.round(target.have * 100 / Math.max(1, target.count))) : 0;
    var fill = el(doc, 'i', percent < 100 ? 'low' : null);

    fill.style.width = percent + '%';
    bar.appendChild(fill);
    row.setAttribute('data-item', target.item);
    row.appendChild(picture(doc, icons.byKey[target.key]));
    row.appendChild(el(doc, 'span', 'cc-kname', target.name));
    row.appendChild(bar);
    row.appendChild(el(doc, 'span', 'cc-kval', (isCounted ? target.have : '?') + ' / ' + target.count));
    row.appendChild(stepper(doc, target.count, function (step) {
      var count = clamp(target.count + step, 1, MAX_KEEP);

      if (count !== target.count) post('keep|' + encodeURIComponent(target.key) + '|' + count);
    }));
    row.appendChild(button(doc, 'cc-x', GLYPH.cross, say('unkeepTip', target.name), function () { post('unkeep|' + target.item); }));

    return row;
  }

  // ---------------------------------------------------------------- reordering by drag

  // Drags by mouse events rather than the browser's drag and drop, so the bag's own drag handling never sees them.
  function startDrag(win, row, id, from) {
    win.__ccUi.drag = { id: id, from: from, to: from };
    row.classList.add('drag');
    win.document.addEventListener('mouseup', function () { finishDrag(win); }, { capture: true, once: true });
  }

  function dragOver(win, target) {
    var drag = win.__ccUi.drag;
    var row = drag && target && target.closest ? target.closest('.cc-qrow') : null;

    if (!row) return;

    drag.to = parseInt(row.getAttribute('data-index'), 10);
    Array.prototype.forEach.call(row.parentElement.children, function (other) {
      other.classList.toggle('drop', other === row && drag.to !== drag.from);
    });
  }

  // The order is moved by its id, so a drop still moves the order that was picked up if the queue changed meanwhile.
  function finishDrag(win) {
    var drag = endDrag(win);

    if (drag && !isNaN(drag.to) && drag.to !== drag.from) post('move|' + drag.id + '|' + drag.to);
  }

  // Lets go of the drag without moving anything; returns the drag that was let go of.
  function endDrag(win) {
    var drag = win.__ccUi.drag;
    var list = win.document.getElementById('cc-qlist');

    win.__ccUi.drag = null;

    if (list) {
      Array.prototype.forEach.call(list.children, function (row) {
        row.classList.remove('drag');
        row.classList.remove('drop');
      });
    }

    return drag;
  }

  // A mouse let go of outside the page is never heard here, so leaving the page, or the page losing focus, ends the
  // drag; otherwise the next release anywhere on the page would drop the order there.
  function wireDragGuards(win) {
    win.addEventListener('blur', function () { endDrag(win); });
    win.document.addEventListener('mouseout', function (e) {
      if (!e.relatedTarget) endDrag(win);
    });
  }

  // ---------------------------------------------------------------- the line under the bench's Craft button

  function drawChain(win) {
    var doc = win.document;
    var controls = doc.querySelector('.craft-controls');

    if (!controls) return;

    var panel = doc.getElementById('cc-panel');

    if (!panel || panel.previousElementSibling !== controls) {
      if (panel) panel.remove();

      panel = el(doc, 'div', 'cc-panel');
      panel.id = 'cc-panel';
      panel.addEventListener('mousedown', holdUntilReleased, true);
      controls.parentNode.insertBefore(panel, controls.nextSibling);
    }

    var key = selectedKey;
    var plan = key && view.marks[key];

    redraw(panel, [key, plan || ''], function () { fillChain(win, panel, key, plan); });
  }

  // The amount is the one set on the recipe's card, read at the click. The bench's own quantity box cannot carry it:
  // the page holds that to what the bag's parts make now, which is 1 for a recipe whose parts are still to be made.
  function fillChain(win, panel, key, plan) {
    var doc = win.document;

    panel.innerHTML = '';

    if (!plan) return;

    var actions = el(doc, 'div', 'cc-row');

    actions.appendChild(button(doc, 'btn', say('craftChain'), say('craftChainTip'), function () {
      post('chain|' + encodeURIComponent(key) + '|' + amountFor(win, key));
    }));
    panel.appendChild(el(doc, 'div', 'cc-plan', plain(plan)));
    panel.appendChild(actions);
  }

  // ---------------------------------------------------------------- drawing

  // A page the hub has failed to draw on RENDER_TRIES times in a row is left alone until the game or the plugin sends
  // something new, so a failure that happens every time is not retried at every poll.
  function renderWindow(win) {
    if (!install(win)) return;

    var ui = win.__ccUi;

    if (ui.renderFailures >= RENDER_TRIES) return;

    try {
      if (!ensureLayout(win)) return;

      followCraftOverlay(win);
      drawListIfChanged(win);
      markSelected(win);
      drawQueue(win);
      drawChain(win);
      refit(win);
      ui.renderFailures = 0;
    } catch (e) {
      ui.renderFailures += 1;
      ui.isTabOff = true;
      removeHub(win);
      throw e;
    }
  }

  function renderOne(win) {
    try {
      renderWindow(win);
    } catch (e) {
      pageDiag(win, 'render', 'could not draw on the workbench page: ' + e);
    }
  }

  // A hub that could not draw is taken off whole, so the game's own list shows again; the poll tries once more later,
  // with the bench left in the game's panel from then on. The search box is let go of first, so the game is told its
  // keys are its own again.
  function removeHub(win) {
    var doc = win.document;
    var panel = doc.querySelector('.combined-panel');
    var input = doc.getElementById('cc-search');

    if (input && doc.activeElement === input) input.blur();

    HUB_ROOTS.forEach(function (id) { removeById(doc, id); });
    untab(doc, panel);

    if (panel) panel.classList.remove('cc-hub');
    if (panel && panel.parentElement) panel.parentElement.classList.remove('cc-wide');

    refit(win);
  }

  function renderAll() {
    benchWindows().forEach(renderOne);
  }

  // The plugin sends its view often, mostly unchanged; the same view again draws nothing. A different one also lets a
  // hub that gave up drawing try again.
  function setData(next) {
    var text = next ? JSON.stringify(next) : '';

    if (!next || text === lastViewText) return;

    lastViewText = text;
    view = next;
    benchWindows().forEach(function (win) {
      if (win.__ccUi) win.__ccUi.renderFailures = 0;
    });
    refresh();
  }

  // Redraws now, unless the mouse is down on the hub: a redraw then would replace the button being pressed. The
  // skipped redraw happens once the press is over.
  function refresh() {
    if (isHolding()) {
      hasSkippedRender = true;

      return;
    }

    hasSkippedRender = false;
    renderAll();
  }

  function renderSkipped() {
    if (!hasSkippedRender || isHolding()) return;

    hasSkippedRender = false;
    renderAll();
  }

  // Asked of the live pages each time rather than kept in a flag, so a page that goes away mid-press cannot leave
  // redraws switched off.
  function isHolding() {
    return benchWindows().some(function (win) {
      return HUB_ROOTS.some(function (id) {
        var root = win.document.getElementById(id);

        return !!(root && root.__ccPressed);
      });
    });
  }

  // A hub root's mousedown, in the capture phase so it runs before anything inside reacts. The release clears it
  // after a turn, once the click that follows it has run; a release the page never sees clears it after
  // PRESS_LIMIT_MS.
  function holdUntilReleased() {
    var root = this;
    var doc = root.ownerDocument;
    var press = {};

    // Only this press's own release counts, so an earlier press's timer cannot end a later one.
    function release() {
      if (root.__ccPressed !== press) return;

      root.__ccPressed = null;
      renderSkipped();
    }

    root.__ccPressed = press;
    doc.addEventListener('mouseup', function () { setTimeout(release, 0); }, { capture: true, once: true });
    setTimeout(release, PRESS_LIMIT_MS);
  }

  // The queue's clicks, made the way the page makes them: picking a recipe sends what a row click sends, crafting
  // sets the quantity box and presses the page's craft button (so its own lock still applies), and confirming runs
  // the page's own result-close, which also resets the page's progress overlay.
  var ACTIONS = {
    recipe: function (win, core, key) { core.UnitySendEvent('RECIPE_CLICK', { recipeKey: key }); },
    craft: function (win, core, qty) {
      win.eval('craftQty = clampQty(' + Math.max(1, Math.floor(qty)) + '); renderQtyRow();');
      win.document.getElementById('btn-craft').click();
    },
    confirm: function (win, core) {
      if (typeof win.closeCrafting === 'function') win.closeCrafting();
      else core.UnitySendEvent('RESULT_CONFIRM');
    }
  };

  // The plugin retries a click that failed, so each kind of failure is reported once, and again only after that kind
  // of click has worked in between.
  function act(kind, arg) {
    var win = actingWindow();
    var core = win ? benchCore(win) : null;

    if (!core || !ACTIONS[kind]) {
      diag(actProblems, 'none-' + kind, 'no workbench page for ' + kind);

      return;
    }

    forget(actProblems, 'none-' + kind);
    isQueueActing = true;

    try {
      ACTIONS[kind](win, core, arg);
      forget(actProblems, 'fail-' + kind);
    } catch (e) {
      diag(actProblems, 'fail-' + kind, kind + ' failed on the workbench page: ' + e);
    } finally {
      isQueueActing = false;
    }
  }

  // The page the hub is on, ahead of one that is still loading or that the hub could not hook into.
  function actingWindow() {
    var wins = benchWindows();
    var installed = wins.filter(function (win) { return win.__ccInstalled; });

    return installed[0] || wins[0] || null;
  }

  // Installs on a newly opened bench, puts the hub back if the page lost it, shows the bench when a craft comes up on
  // it, keeps the running order's bar in step with the game's, and refits when the game shows or hides its recipe list.
  // With nothing changed it reads the page and writes nothing.
  setInterval(function () {
    benchFrames().forEach(function (frame) {
      try {
        pollFrame(frame);
      } catch (e) {
        diag(frame, 'poll', 'the crafting hub could not keep up with the workbench page: ' + e);
      }
    });
  }, POLL_MS);

  function pollFrame(frame) {
    var win = reachFrame(frame);

    if (!win) return;

    if (!install(win)) {
      noteBadShape(win);

      return;
    }

    var doc = win.document;

    if (!doc.getElementById('cc-mid') || !doc.getElementById('cc-panel')) renderOne(win);

    followCraftOverlay(win);
    mirrorProgress(win);
    refit(win);
  }

  // A frame the root cannot reach for a moment while it loads is normal; one that stays out of reach is reported.
  function reachFrame(frame) {
    try {
      var win = benchWindowOf(frame);

      frame.__ccDenied = 0;

      return win;
    } catch (e) {
      frame.__ccDenied = (frame.__ccDenied || 0) + 1;

      if (frame.__ccDenied >= SETTLE_POLLS) diag(frame, 'access', 'cannot reach the workbench page: ' + e);

      return null;
    }
  }

  window.__craftinghub = { setData: setData, act: act };
  post('ready');
})();
