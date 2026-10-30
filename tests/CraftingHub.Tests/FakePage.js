// Just enough of Root.html and the workbench page (ToolTable.html) for page.js to run in Jint: the root document with
// the bench's iframe, the vuplex bridge, a DOM with capture, bubbling, once-listeners and focus, a clock, and the
// workbench page's markup and the page-wide values the script reads. Test code only; nothing here ships.
var window = this;

// ---------------------------------------------------------------- clock

var now = 0;
var timers = [];
var intervals = [];

function setTimeout(callback, ms) {
  timers.push({ at: now + (ms || 0), callback: callback, seq: timers.length });

  return timers.length;
}

function setInterval(callback) {
  intervals.push(callback);

  return intervals.length;
}

// Moves the clock on, running every timer that falls due, earliest first.
function advance(ms) {
  var until = now + ms;

  for (;;) {
    var due = timers.filter(function (timer) { return timer.at <= until; }).sort(function (a, b) { return a.at - b.at || a.seq - b.seq; })[0];

    if (!due) break;

    timers.splice(timers.indexOf(due), 1);
    now = Math.max(now, due.at);
    due.callback();
  }

  now = until;
}

// One run of every interval the script set, which is how its poll runs.
function poll() {
  intervals.slice().forEach(function (callback) { callback(); });
}

// ---------------------------------------------------------------- DOM

// Every change made to any page's elements: a node added or taken away, or its text or classes set.
var domWrites = 0;

var FakeElement = class {
  constructor(doc, tagName) {
    var element = this;

    this.ownerDocument = doc;
    this.tagName = String(tagName).toUpperCase();
    this.id = '';
    this.parentElement = null;
    this.children = [];
    this.text = '';
    this.attrs = {};
    this.classes = [];
    this.style = {};
    this.listeners = [];
    this.scrollTop = 0;
    this.scrollLeft = 0;
    this.scrollWidth = 0;
    this.clientWidth = 0;
    this.disabled = false;
    this.value = '';
    // Where the element is drawn, in the page's client coordinates; nothing is laid out, so tests place what they need.
    this.rect = { left: 0, top: 0, right: 0, bottom: 0, width: 0, height: 0 };
    this.classList = {
      contains: function (name) { return element.classes.indexOf(name) >= 0; },
      add: function () {
        for (var i = 0; i < arguments.length; i++) {
          if (element.classes.indexOf(arguments[i]) < 0) element.classes.push(arguments[i]);
        }
      },
      remove: function () {
        var names = Array.prototype.slice.call(arguments);

        element.classes = element.classes.filter(function (name) { return names.indexOf(name) < 0; });
      },
      toggle: function (name, force) {
        var isOn = force === undefined ? !element.classList.contains(name) : !!force;

        if (isOn) element.classList.add(name);
        else element.classList.remove(name);

        return isOn;
      }
    };
  }

  get className() { return this.classes.join(' '); }

  set className(value) {
    domWrites += 1;
    this.classes = String(value).split(' ').filter(function (name) { return name; });
  }

  get parentNode() { return this.parentElement; }

  get isConnected() { return this.ownerDocument.contains(this); }

  get previousElementSibling() {
    var siblings = this.parentElement ? this.parentElement.children : [];
    var index = siblings.indexOf(this);

    return index > 0 ? siblings[index - 1] : null;
  }

  get nextSibling() {
    var siblings = this.parentElement ? this.parentElement.children : [];
    var index = siblings.indexOf(this);

    return index >= 0 && index + 1 < siblings.length ? siblings[index + 1] : null;
  }

  get textContent() { return this.text + this.children.map(function (child) { return child.textContent; }).join(''); }

  set textContent(value) {
    domWrites += 1;
    this.clear();
    this.text = value == null ? '' : String(value);
  }

  // Only ever set to '' by the script. Emptying a scrolled box scrolls it back to the top and the start, as in a
  // browser.
  set innerHTML(value) {
    if (value !== '') throw new Error('the fake page only clears innerHTML');

    this.clear();
    this.text = '';
    this.scrollTop = 0;
    this.scrollLeft = 0;
  }

  clear() {
    this.children.slice().forEach(function (child) { this.removeChild(child); }, this);
  }

  getBoundingClientRect() { return this.rect; }

  hasAttribute(name) { return Object.prototype.hasOwnProperty.call(this.attrs, name); }

  getAttribute(name) { return this.hasAttribute(name) ? this.attrs[name] : null; }

  setAttribute(name, value) { this.attrs[name] = String(value); }

  appendChild(child) {
    return this.insertBefore(child, null);
  }

  insertBefore(child, before) {
    if (child.parentElement) child.parentElement.removeChild(child);

    var index = before ? this.children.indexOf(before) : -1;

    domWrites += 1;
    child.parentElement = this;

    if (index < 0) this.children.push(child);
    else this.children.splice(index, 0, child);

    return child;
  }

  removeChild(child) {
    domWrites += 1;
    this.children = this.children.filter(function (c) { return c !== child; });
    child.parentElement = null;

    return child;
  }

  remove() {
    if (this.parentElement) this.parentElement.removeChild(this);
  }

  contains(other) {
    for (var element = other; element; element = element.parentElement) {
      if (element === this) return true;
    }

    return false;
  }

  matches(selector) {
    var parts = parseSelector(selector);

    if (parts.tag && parts.tag !== this.tagName) return false;

    if (parts.id && parts.id !== this.id) return false;

    var element = this;

    return parts.classes.every(function (name) { return element.classList.contains(name); });
  }

  closest(selector) {
    for (var element = this; element; element = element.parentElement) {
      if (element.matches(selector)) return element;
    }

    return null;
  }

  querySelectorAll(selector) {
    return descendantsOf(this).filter(function (element) { return element.matches(selector); });
  }

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] || null;
  }

  addEventListener(type, listener, options) {
    var capture = options === true || !!(options && options.capture);

    this.listeners.push({ type: type, listener: listener, capture: capture, once: !!(options && options.once) });
  }

  click() {
    fire(this, 'click');
  }

  focus() {
    var doc = this.ownerDocument;

    if (doc.activeElement === this) return;

    if (doc.activeElement) doc.activeElement.blur();

    doc.activeElement = this;
    fire(this, 'focus');
  }

  blur() {
    var doc = this.ownerDocument;

    if (doc.activeElement !== this) return;

    doc.activeElement = null;
    fire(this, 'blur');
  }
};

// Compound selectors of one tag, one id and classes ("div.cc-qrow", "h2", "#cc-list", ".a.b"); no combinators.
function parseSelector(selector) {
  var match = /^([a-zA-Z][a-zA-Z0-9]*)?((?:[.#][\w-]+)*)$/.exec(selector);

  if (!match) throw new Error('the fake page does not know the selector "' + selector + '"');

  var parts = { tag: match[1] ? match[1].toUpperCase() : '', id: '', classes: [] };

  (match[2].match(/[.#][\w-]+/g) || []).forEach(function (step) {
    if (step.charAt(0) === '#') parts.id = step.slice(1);
    else parts.classes.push(step.slice(1));
  });

  return parts;
}

function descendantsOf(root) {
  var found = [];

  root.children.forEach(function walk(child) {
    found.push(child);
    child.children.forEach(walk);
  });

  return found;
}

var FakeDocument = class {
  constructor() {
    this.readyState = 'complete';
    this.activeElement = null;
    this.listeners = [];
    this.head = new FakeElement(this, 'head');
    this.body = new FakeElement(this, 'body');
  }

  createElement(tagName) {
    return new FakeElement(this, tagName);
  }

  contains(element) {
    return this.head.contains(element) || this.body.contains(element);
  }

  all() {
    return [this.head].concat(descendantsOf(this.head), [this.body], descendantsOf(this.body));
  }

  getElementById(id) {
    return this.all().filter(function (element) { return element.id === id; })[0] || null;
  }

  querySelectorAll(selector) {
    return this.all().filter(function (element) { return element.matches(selector); });
  }

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] || null;
  }

  addEventListener(type, listener, options) {
    FakeElement.prototype.addEventListener.call(this, type, listener, options);
  }
};

var NON_BUBBLING = ['focus', 'blur'];

// Dispatches as a browser does: capture listeners from the document down, the target's own, then bubbling listeners
// back up, with this as the element listening. A disabled button gets no click.
function fire(target, type, init) {
  if (type === 'click' && target.disabled) return null;

  var path = [];
  var event = {
    type: type,
    target: target,
    key: init && init.key,
    deltaX: (init && init.deltaX) || 0,
    deltaY: (init && init.deltaY) || 0,
    deltaMode: (init && init.deltaMode) || 0,
    clientX: (init && init.clientX) || 0,
    clientY: (init && init.clientY) || 0,
    relatedTarget: (init && init.relatedTarget) || null,
    defaultPrevented: false,
    isStopped: false,
    stopPropagation: function () { event.isStopped = true; },
    preventDefault: function () { event.defaultPrevented = true; }
  };

  for (var element = target; element; element = element.parentElement) path.unshift(element);

  path.unshift(target.ownerDocument);

  for (var i = 0; i < path.length - 1 && !event.isStopped; i++) notify(path[i], event, true);

  if (!event.isStopped) notify(target, event, null);

  if (NON_BUBBLING.indexOf(type) >= 0) return event;

  for (var j = path.length - 2; j >= 0 && !event.isStopped; j--) notify(path[j], event, false);

  return event;
}

// capture: true for capture listeners only, false for bubbling ones only, null for both (the target itself).
function notify(node, event, capture) {
  node.listeners.slice().forEach(function (entry) {
    if (entry.type !== event.type || (capture !== null && entry.capture !== capture)) return;

    if (entry.once) node.listeners.splice(node.listeners.indexOf(entry), 1);

    entry.listener.call(node, event);
  });
}

// A whole click as the mouse makes it: press, release, click, and the turn after it.
function press(target) {
  fire(target, 'mousedown');
  fire(target, 'mouseup');
  fire(target, 'click');
  advance(1);
}

// Typing into a text box: the new value, then its input event.
function typeInto(input, value) {
  input.value = value;
  fire(input, 'input');
}

// Root.html's own document, which holds the page iframes.
var document = new FakeDocument();

// The vuplex bridge: what the script posts to the plugin.
var posted = [];

window.vuplex = {
  postMessage: function (message) { posted.push(message); }
};

// Exiled's Toolkit (ui.js) as far as the hub uses it, once a test loads it: what was handed to it, and the zoom it reads
// off the style of a node and of each element around it.
var handed = [];

function fakeToolkit() {
  window.ExiledUi = {
    version: 1,
    scale: 1,
    size: function (el, options) { handed.push({ el: el, options: options }); },
    zoomAt: function (el) {
      var zoom = 1;

      for (var node = el; node; node = node.parentElement) {
        if (node.style && node.style.zoom) zoom *= Number(node.style.zoom);
      }

      return zoom;
    }
  };
}

function handedId(id) {
  return handed.filter(function (entry) { return entry.el.id === id; });
}

// ---------------------------------------------------------------- the workbench page

// The page's groups in its own order (NOTE_CAT_META, ToolTable.html:2946): 1 to 7, then 0 for everything else.
// The names are the fake's own.
var GROUPS = [1, 2, 3, 4, 5, 6, 7, 0].map(function (id) { return { id: id, name: id ? 'Group ' + id : 'Other' }; });

function node(doc, parent, tagName, className, id) {
  var element = parent.appendChild(doc.createElement(tagName));

  if (className) element.className = className;
  if (id) element.id = id;

  return element;
}

// The workbench page's markup (ToolTable.html:1946-2087), down to what the script reaches for.
function benchMarkup(doc) {
  var container = node(doc, doc.body, 'div', 'ui-container');
  var bag;
  var tabs;
  var panel;
  var bench;
  var controls;
  var overlay;
  var notes;

  bag = node(doc, container, 'div', 'backpack-panel');
  tabs = node(doc, bag, 'div', 'left-tabs');
  node(doc, node(doc, tabs, 'div', 'left-tab-pinned'), 'button', 'left-tab active', 'tab-bag');
  node(doc, node(doc, tabs, 'div', 'left-tabs-scroll', 'leftTabsScroll'), 'button', 'left-tab', 'tab-drawer');
  node(doc, bag, 'div', 'weight-section');
  node(doc, bag, 'div', 'grid-container', 'backpack-grid');
  node(doc, bag, 'div', 'rack-rows', 'rack-rows');
  node(doc, bag, 'div', 'toolbar', 'bagToolbar');
  panel = node(doc, container, 'div', 'combined-panel no-notes');
  bench = node(doc, panel, 'div', 'workbench-section');
  node(doc, bench, 'h2').textContent = 'Bench';
  node(doc, bench, 'div', 'grid-container', 'workbench-grid');
  node(doc, bench, 'div', 'qty-row');
  controls = node(doc, bench, 'div', 'craft-controls');
  node(doc, controls, 'button', 'btn', 'btn-clear-workbench');
  node(doc, controls, 'button', 'btn', 'btn-craft');
  node(doc, controls, 'span', 'cost-label');
  node(doc, bench, 'div', '', 'recentBlock');
  overlay = node(doc, bench, 'div', '', 'crafting-overlay');
  node(doc, node(doc, overlay, 'div', 'crafting-box', 'crafting-progress-box'), 'div', 'crafting-bar-fill', 'craft-progress');
  node(doc, overlay, 'div', '', 'crafting-result-box');
  notes = node(doc, panel, 'div', 'notes-section');
  node(doc, notes, 'div', 'notes-head');
  node(doc, notes, 'div', '', 'notes-search-bar');
  node(doc, notes, 'div', 'recipe-list', 'recipe-list');
}

// One of the page's grids (createGridBag, webui-bag.js:237-264). The page's drag passes the place and zoom it kept
// when the drag started; without them the grid looks where its container is now, at the page's zoom of 1.
function gridBag(doc, containerId) {
  var SLOT_PX = 40;

  function where(cachedRect) {
    return cachedRect || doc.getElementById(containerId).getBoundingClientRect();
  }

  return {
    ownerId: 0,
    getOwnerId: function () { return this.ownerId; },
    isMouseOver: function (clientX, clientY, cachedRect) {
      var rect = where(cachedRect);

      return clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom;
    },
    getGridCoordinates: function (clientX, clientY, itemW, itemH, cachedRect, zoomOpt) {
      var rect = where(cachedRect);
      var zoom = zoomOpt || 1;

      return { row: Math.floor((clientY - rect.top) / zoom / SLOT_PX), col: Math.floor((clientX - rect.left) / zoom / SLOT_PX) };
    }
  };
}

// The page's top-level const and let values, which the script can only read through the page's eval.
function benchWindow(doc) {
  var win = { document: doc, location: { href: BENCH_URL }, listeners: [], sent: [], renders: 0, fits: 0, closes: 0, barStarts: 0 };
  var values = {
    core: { UnitySendEvent: function (action, data) { win.sent.push({ action: action, data: data }); } },
    NOTE_CAT_META: GROUPS,
    lastRecipeData: null,
    collapsedCats: new Set(),
    craftQty: 1,
    // How many of the laid recipe the bag's parts make; the page shows its quantity box only from 2 up.
    maxCraftCount: 999,
    // The page's own craft bar (ToolTable.html): whether it saw the craft start, its speed, progress and animation.
    lastCraftingState: false,
    currentCraftSpeed: 0,
    currentProgress: 0,
    craftAnimationId: null,
    // The page's drag of its grid items (createBagDragCoordinator) and its bench grid (createGridBag).
    dragCoord: { dragging: false, isDragging: function () { return this.dragging; } },
    backpack: gridBag(doc, 'backpack-grid'),
    workbench: gridBag(doc, 'workbench-grid')
  };

  win.values = values;
  // Only the three statement shapes the script uses; anything else is a test failure.
  win.eval = function (code) {
    var read = /^typeof (\w+) !== "undefined" \? (\w+) : null$/.exec(code);
    var qty = /^craftQty = clampQty\((\d+)\); renderQtyRow\(\);$/.exec(code);
    var bar = /^currentCraftSpeed = ([0-9.e+-]+); if \(!lastCraftingState\) \{ lastCraftingState = true; document\.getElementById\("crafting-overlay"\)\.style\.display = "flex"; \} if \(currentProgress < ([0-9.]+)\) currentProgress = \2; if \(!craftAnimationId\) \{ lastFrameTime = performance\.now\(\); startCraftAnimation\(\); \}$/.exec(code);

    if (read && read[1] === read[2]) return Object.prototype.hasOwnProperty.call(values, read[1]) ? values[read[1]] : null;

    // clampQty and renderQtyRow (ToolTable.html): the quantity is held to what the parts make, and with fewer than 2
    // makeable the row is hidden and the quantity forced back to 1.
    if (qty) {
      var asked = Math.max(1, parseInt(qty[1], 10));

      values.craftQty = values.maxCraftCount >= 2 ? Math.min(asked, values.maxCraftCount) : 1;

      return undefined;
    }

    // The game's bar picks up the speed, shows its overlay, and startCraftAnimation runs only when none is running.
    if (bar) {
      values.currentCraftSpeed = parseFloat(bar[1]);

      if (!values.lastCraftingState) {
        values.lastCraftingState = true;
        doc.getElementById('crafting-overlay').style.display = 'flex';
      }

      values.currentProgress = Math.max(values.currentProgress, parseFloat(bar[2]));

      if (!values.craftAnimationId) {
        values.craftAnimationId = 1;
        win.barStarts += 1;
      }

      return undefined;
    }

    throw new Error('the fake page cannot eval: ' + code);
  };
  win.addEventListener = function (type, listener, options) {
    FakeElement.prototype.addEventListener.call(win, type, listener, options);
  };
  // renderRecipes (ToolTable.html:3038): keeps the data, then draws its own list again from nothing.
  win.renderRecipes = function (recipes) {
    var list = doc.getElementById('recipe-list');

    win.renders += 1;
    values.lastRecipeData = recipes;
    list.innerHTML = '';
    recipes.forEach(function (recipe) {
      var row = node(doc, list, 'div', 'recipe-item');

      row.setAttribute('data-key', recipe.recipeKey);
      row.textContent = recipe.name;
    });
  };
  win.noteCatDisplayName = function (meta) { return meta.name; };
  win.applyViewportFit = function () { win.fits += 1; };
  win.closeCrafting = function () { win.closes += 1; };
  // The bench's Craft button (ToolTable.html:3724) sends the quantity box's value.
  doc.getElementById('btn-craft').addEventListener('click', function () {
    values.core.UnitySendEvent('CRAFT', { qty: values.craftQty });
  });

  return win;
}

var BENCH_URL = 'https://game.local/UI/ToolTable/ToolTable.html';

// A fresh iframe for the workbench as the browser first makes it: the src already set, but still holding an empty,
// finished about:blank document until the page loads.
function openBlankFrame() {
  var frame = document.body.appendChild(document.createElement('iframe'));

  frame.src = BENCH_URL;
  frame.contentWindow = {
    document: new FakeDocument(),
    location: { href: 'about:blank' },
    listeners: [],
    addEventListener: function () {},
    eval: function () { return null; }
  };

  return frame;
}

// The workbench page loading into a frame.
function loadBench(frame) {
  var doc = new FakeDocument();

  benchMarkup(doc);
  frame.contentWindow = benchWindow(doc);

  return frame.contentWindow;
}

// Opens a workbench: its page in a fresh iframe, as the game loads it.
function openBench() {
  return loadBench(openBlankFrame());
}

// A frame whose page the root cannot reach, as the browser refuses a page from another origin.
function denyFrame(frame) {
  Object.defineProperty(frame, 'contentWindow', {
    configurable: true,
    get: function () { throw new Error('SecurityError: Blocked a frame from accessing a cross-origin frame.'); }
  });
}

// The browser letting the root reach the frame's page again.
function allowFrame(frame, win) {
  Object.defineProperty(frame, 'contentWindow', { configurable: true, writable: true, value: win });
}

// The workbench page's window losing focus, as when the mouse is let go over another part of the game.
function blurBench() {
  notify(bench(), { type: 'blur', target: bench(), stopPropagation: function () {}, preventDefault: function () {} }, null);
}

// The mouse leaving the workbench page altogether.
function leavePage() {
  fire(benchDoc().body, 'mouseout', { relatedTarget: null });
}

function bench() {
  return document.querySelector('iframe').contentWindow;
}

function benchDoc() {
  return bench().document;
}

function $(id) {
  return benchDoc().getElementById(id);
}

// The declarations of the hub's style rule for exactly this selector, as its style sheet writes them; '' when none.
function cssRule(selector) {
  var sheet = '}' + benchDoc().getElementById('cc-style').textContent;
  var start = sheet.indexOf('}' + selector + '{');

  if (start < 0) return '';

  var body = start + selector.length + 2;

  return sheet.slice(body, sheet.indexOf('}', body));
}

// A recipe message (RecipeMsg, ToolTable.html:2922): shows or hides the recipe list, and draws it when shown, through
// the page-wide renderRecipes as the page's own call does.
function recipeMsg(isShown, recipes) {
  var doc = benchDoc();
  var panel = doc.querySelector('.combined-panel');

  if (isShown) {
    doc.querySelector('.notes-section').style.display = 'flex';
    panel.classList.remove('no-notes');
    bench().renderRecipes(recipes);
  } else {
    doc.querySelector('.notes-section').style.display = 'none';
    panel.classList.add('no-notes');
  }
}

// The hub's recipe cards in the order shown, by key.
function cardKeys() {
  return benchDoc().querySelectorAll('.cc-card').map(function (card) { return card.getAttribute('data-key'); }).join(',');
}

function card(key) {
  return benchDoc().querySelectorAll('.cc-card').filter(function (card) { return card.getAttribute('data-key') === key; })[0] || null;
}

function cardPart(key, selector) {
  return card(key).querySelector(selector);
}

// A color dot on a dyeable card, by the colored recipe's key.
function dyeDot(key, colorKey) {
  return card(key).querySelectorAll('.cc-dye').filter(function (dot) { return dot.getAttribute('data-key') === colorKey; })[0] || null;
}

function filterChip(label) {
  return $('cc-filters').children.filter(function (chip) { return chip.textContent.indexOf(label + ' ') === 0; })[0] || null;
}

function groupChip(label) {
  return $('cc-groups').children.filter(function (chip) { return chip.textContent.indexOf(label + ' ') === 0; })[0] || null;
}

function queueRow(index) {
  return $('cc-qlist').querySelectorAll('.cc-qrow')[index];
}

// Puts an element on screen at a spot, as the browser would have laid it out.
function placeAt(element, left, top, width, height) {
  element.rect = { left: left, top: top, right: left + width, bottom: top + height, width: width, height: height };
}

// The page's own drag of an item from one of its grids, begun or ended.
function gameDrag(isDragging) {
  bench().values.dragCoord.dragging = isDragging;
}

// The mouse moving to a point on the workbench page.
function moveMouse(x, y) {
  fire(benchDoc().body, 'mousemove', { clientX: x, clientY: y });
}

// Whether the bag panel shows the bench rather than the game's own tab.
function isBenchShown() {
  return benchDoc().querySelector('.backpack-panel').classList.contains('cc-bench-view');
}

function sentActions(action) {
  return bench().sent.filter(function (entry) { return entry.action === action; });
}
