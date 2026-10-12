// Crafting Hub's half in the game's web UI. Runs in Root.html, the document every game page iframe sits in, and
// reaches into the workbench page (ToolTable.html, a same-origin iframe) to mark recipes that become craftable by
// making their intermediates first, and to add a queue panel under the craft button. Every craft it starts goes
// through the workbench page's own core.UnitySendEvent, exactly like a click. Talks to the plugin through
// vuplex.postMessage("slmod|craftinghub|<verb>|<args>") and is driven by window.__craftinghub.
(function () {
  if (window.__craftinghub) return;

  var PREFIX = 'slmod|craftinghub|';
  var BENCH_PAGE = /ToolTable\.html/;
  var POLL_MS = 500;

  var DEFAULT_KEEP = 10;
  var MAX_KEEP = 9999;
  var PRESS_LIMIT_MS = 2000;

  var view = { marks: {}, queue: [], running: false, status: '', keep: { on: false, targets: [], byKey: {} } };
  var selectedKey = '';
  var reported = {};
  // The keep count being typed, for the recipe it was typed on. While its box has focus, or the mouse is down on the
  // panel, the panel is not rebuilt (see isHoldingPanel); the skipped redraw happens once both are over.
  var keepDraft = { key: '', count: DEFAULT_KEEP };
  var hasSkippedRender = false;

  function post(message) {
    try {
      if (window.vuplex) window.vuplex.postMessage(PREFIX + message);
    } catch (e) {}
  }

  // Each problem is reported once per session, so a broken hook cannot flood the log.
  function diag(id, text) {
    if (reported[id]) return;

    reported[id] = true;
    post('diag|' + text);
  }

  function benchWindows() {
    var found = [];
    var frames = document.querySelectorAll('iframe');

    for (var i = 0; i < frames.length; i++) {
      if (!BENCH_PAGE.test(frames[i].src || '')) continue;

      try {
        if (frames[i].contentWindow && frames[i].contentWindow.document) found.push(frames[i].contentWindow);
      } catch (e) {
        diag('access', 'cannot reach the workbench page: ' + e);
      }
    }

    return found;
  }

  // `core` is a top-level const in the bench page, so it is reachable only through that page's own eval.
  function benchCore(win) {
    try {
      return win.eval('typeof core !== "undefined" ? core : null');
    } catch (e) {
      diag('core', 'cannot read the workbench page core: ' + e);

      return null;
    }
  }

  function install(win) {
    if (win.__ccInstalled) return;

    // A page still loading has not defined its functions yet; the poll tries again, so only a loaded page counts.
    if (win.document.readyState !== 'complete') return;

    var core = benchCore(win);

    if (!core || typeof win.buildRecipeItem !== 'function') {
      diag('shape', 'the workbench page changed shape (core ' + !!core + ', buildRecipeItem ' + typeof win.buildRecipeItem + '), recipe marks are off');

      return;
    }

    win.__ccInstalled = true;
    selectedKey = '';
    wrapRecipeBuilder(win);
    wrapSend(core);
    addStyle(win.document);

    // Rows built before the wrap carry no key; one rebuild through the page's own renderer tags them all.
    try {
      win.eval('if (typeof lastRecipeData !== "undefined" && lastRecipeData) renderRecipes(lastRecipeData)');
    } catch (e) {
      diag('rerender', 'could not redraw the recipe list: ' + e);
    }
  }

  // Tags every recipe row with its key as the page builds it, so marks can be applied without rebuilding the list.
  function wrapRecipeBuilder(win) {
    var original = win.buildRecipeItem;

    win.buildRecipeItem = function (r) {
      var frag = original.apply(this, arguments);

      try {
        var item = frag && frag.querySelector ? frag.querySelector('.recipe-item') : null;

        if (item && r && r.recipeKey) {
          item.setAttribute('data-cc-key', r.recipeKey);
          decorate(item);
        }
      } catch (e) {
        diag('decorate', 'could not mark a recipe row: ' + e);
      }

      return frag;
    };
  }

  // Watches which recipe the player (or the queue) picks, for the panel's "Craft with intermediates" button.
  function wrapSend(core) {
    var original = core.UnitySendEvent;

    core.UnitySendEvent = function (action, data) {
      if (action === 'RECIPE_CLICK' && data && data.recipeKey) {
        selectedKey = data.recipeKey;
        setData();
      }

      return original.apply(this, arguments);
    };
  }

  function decorate(item) {
    var key = item.getAttribute('data-cc-key');
    var isChain = Object.prototype.hasOwnProperty.call(view.marks, key);

    item.classList.toggle('cc-chain', isChain && !item.classList.contains('can-craft'));
  }

  function addStyle(doc) {
    if (doc.getElementById('cc-style')) return;

    var style = doc.createElement('style');
    style.id = 'cc-style';
    style.textContent = [
      '.recipe-item.cc-chain{color:var(--accent);border-left-color:var(--accent);border-left-style:dashed}',
      '.recipe-item.cc-chain .recipe-icon{position:relative}',
      ".recipe-item.cc-chain .recipe-icon::before{content:'\\2692';position:absolute;left:-4px;top:-5px;font-size:11px;line-height:1;color:#1a1a1a;background:rgba(255,193,7,.92);border-radius:3px;padding:1px 2px;pointer-events:none}",
      '.cc-panel{display:flex;flex-direction:column;gap:8px;margin-top:12px;padding-top:10px;border-top:1px solid rgba(255,255,255,.08);font-size:12px;color:var(--text-dim,#999);width:100%}',
      '.cc-row{display:flex;flex-wrap:wrap;align-items:center;justify-content:center;gap:8px}',
      '.cc-plan{color:var(--accent);text-align:center}',
      '.cc-status{text-align:center;min-height:14px}',
      '.cc-panel .btn{padding:5px 14px;font-size:12px}',
      '.cc-chip{display:inline-flex;align-items:center;gap:6px;padding:3px 8px;border-radius:4px;background:rgba(8,8,10,.55);border:1px solid rgba(255,255,255,.08);color:#ddd}',
      '.cc-chip-x{cursor:pointer;color:#888}.cc-chip-x:hover{color:#fff}',
      '.cc-title{letter-spacing:.5px;text-transform:uppercase;color:#777;font-size:11px}',
      '.cc-kept{color:var(--accent)}'
    ].join('');
    doc.head.appendChild(style);
  }

  function el(doc, tag, className, text) {
    var node = doc.createElement(tag);

    if (className) node.className = className;
    if (text != null) node.textContent = text;

    return node;
  }

  function button(doc, text, title, onClick) {
    var node = el(doc, 'button', 'btn', text);
    node.type = 'button';
    node.title = title;
    node.addEventListener('click', function (e) {
      e.stopPropagation();
      onClick();
    });

    return node;
  }

  function craftQty(win) {
    try {
      var qty = win.eval('typeof craftQty !== "undefined" ? craftQty : 1');

      return qty > 0 ? Math.floor(qty) : 1;
    } catch (e) {
      return 1;
    }
  }

  function renderPanel(win) {
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

    panel.innerHTML = '';

    var plan = selectedKey && view.marks[selectedKey];

    if (plan) panel.appendChild(el(doc, 'div', 'cc-plan', plan));

    var actions = el(doc, 'div', 'cc-row');

    if (plan) {
      actions.appendChild(button(doc, 'Craft with intermediates', 'Make the missing intermediates first, then this, using the quantity above', function () {
        post('chain|' + encodeURIComponent(selectedKey) + '|' + craftQty(win));
      }));
    }

    if (selectedKey) {
      actions.appendChild(button(doc, 'Add to queue', 'Queue the selected recipe with the quantity above', function () {
        post('add|' + encodeURIComponent(selectedKey) + '|' + craftQty(win));
      }));
    }

    if (view.running) {
      actions.appendChild(button(doc, 'Stop', 'Stop after nothing more is started; the queue is kept', function () { post('stop'); }));
    } else if (view.queue.length) {
      actions.appendChild(button(doc, 'Start queue', 'Craft everything in the queue, making intermediates as needed', function () { post('start'); }));
    }

    if (actions.childNodes.length) panel.appendChild(actions);

    if (view.queue.length) {
      var chips = el(doc, 'div', 'cc-row');
      chips.appendChild(el(doc, 'span', 'cc-title', 'Queue'));

      view.queue.forEach(function (entry, index) {
        var chip = el(doc, 'span', 'cc-chip', entry.name + ' ×' + entry.crafts);
        var remove = el(doc, 'span', 'cc-chip-x', '✕');
        remove.title = 'Remove';
        remove.addEventListener('click', function (e) {
          e.stopPropagation();
          post('remove|' + index);
        });
        chip.appendChild(remove);
        chips.appendChild(chip);
      });

      if (!view.running) chips.appendChild(button(doc, 'Clear', 'Empty the queue', function () { post('clear'); }));

      panel.appendChild(chips);
    }

    renderKeep(win, panel);

    if (view.status) panel.appendChild(el(doc, 'div', 'cc-status', view.status));
  }

  function keptFor(key) {
    var byKey = view.keep && view.keep.byKey;

    return byKey && Object.prototype.hasOwnProperty.call(byKey, key) ? byKey[key] : 0;
  }

  function clampKeep(value) {
    var n = parseInt(value, 10);

    return isNaN(n) ? 0 : Math.max(0, Math.min(MAX_KEEP, n));
  }

  // Tells the game a text box has focus, as the page's own quantity box does, so typing does not trigger hotkeys.
  function sendFocus(win, isFocused) {
    var core = benchCore(win);

    try {
      if (core) core.UnitySendEvent('SEARCH_FOCUS', { focused: isFocused });
    } catch (e) {}
  }

  function stepButton(doc, text, title, onClick) {
    var node = el(doc, 'button', null, text);
    node.type = 'button';
    node.title = title;
    node.addEventListener('click', function (e) {
      e.stopPropagation();
      onClick();
    });

    return node;
  }

  // The selected recipe's keep-stocked row, then every target this save keeps, each with a remove cross.
  function renderKeep(win, panel) {
    var doc = win.document;
    var keep = view.keep;

    if (!keep || !keep.on) return;

    if (selectedKey) panel.appendChild(keepRow(win, doc));

    if (!keep.targets || !keep.targets.length) return;

    var chips = el(doc, 'div', 'cc-row');
    chips.appendChild(el(doc, 'span', 'cc-title', 'Keep stocked'));

    keep.targets.forEach(function (target) {
      var chip = el(doc, 'span', 'cc-chip', target.name + ' ' + target.count);
      var remove = el(doc, 'span', 'cc-chip-x', '\u2715');
      remove.title = 'Stop keeping ' + target.name + ' stocked';
      remove.addEventListener('click', function (e) {
        e.stopPropagation();
        post('unkeep|' + target.item);
      });
      chip.appendChild(remove);
      chips.appendChild(chip);
    });

    panel.appendChild(chips);
  }

  function keepRow(win, doc) {
    var kept = keptFor(selectedKey);

    if (keepDraft.key !== selectedKey) keepDraft = { key: selectedKey, count: kept || DEFAULT_KEEP };

    var row = el(doc, 'div', 'cc-row');
    var stepper = el(doc, 'span', 'qty-step');
    var input = el(doc, 'input', 'cc-keep-input');

    function setDraft(value) {
      keepDraft.count = clampKeep(value);
      input.value = String(keepDraft.count);
    }

    input.type = 'text';
    input.inputMode = 'numeric';
    input.value = String(keepDraft.count);
    input.title = 'How many to keep in your home';
    input.addEventListener('focus', function () {
      input.select();
      sendFocus(win, true);
    });
    // A press on one of the panel's buttons is what usually blurs the box; the redraw then waits for the release, so
    // the button is still there when its click lands.
    input.addEventListener('blur', function () {
      setDraft(input.value);
      sendFocus(win, false);
      renderSkipped();
    });
    input.addEventListener('keydown', function (e) {
      e.stopPropagation();

      if (e.key === 'Enter') input.blur();
    });

    stepper.appendChild(stepButton(doc, '\u2212', 'One less', function () { setDraft(keepDraft.count - 1); }));
    stepper.appendChild(input);
    stepper.appendChild(stepButton(doc, '+', 'One more', function () { setDraft(keepDraft.count + 1); }));

    row.appendChild(el(doc, 'span', 'cc-title', 'Keep in stock'));
    row.appendChild(stepper);
    row.appendChild(button(doc, 'Keep stocked', 'Keep this many in your home: opening any workbench queues the crafts to top it back up. 0 stops keeping it.', function () {
      post('keep|' + encodeURIComponent(selectedKey) + '|' + clampKeep(input.value));
    }));

    if (kept) row.appendChild(el(doc, 'span', 'cc-kept', '(keeping ' + kept + ')'));

    return row;
  }

  function renderWindow(win) {
    install(win);

    if (!win.__ccInstalled) return;

    var items = win.document.querySelectorAll('.recipe-item[data-cc-key]');

    for (var i = 0; i < items.length; i++) decorate(items[i]);

    renderPanel(win);
  }

  function renderAll() {
    var wins = benchWindows();

    for (var i = 0; i < wins.length; i++) {
      try {
        renderWindow(wins[i]);
      } catch (e) {
        diag('render', 'could not draw on the workbench page: ' + e);
      }
    }
  }

  function setData(next) {
    if (next) view = next;

    if (isHoldingPanel()) {
      hasSkippedRender = true;

      return;
    }

    hasSkippedRender = false;
    renderAll();
  }

  function renderSkipped() {
    if (!hasSkippedRender || isHoldingPanel()) return;

    hasSkippedRender = false;
    renderAll();
  }

  // True while the keep box has focus or the mouse is down on a panel: a redraw then would throw away the number being
  // typed or replace the button being pressed. Asked of the live pages each time rather than kept in a flag, so a page
  // that goes away mid-typing or mid-press cannot leave redraws switched off.
  function isHoldingPanel() {
    var wins = benchWindows();

    for (var i = 0; i < wins.length; i++) {
      try {
        var doc = wins[i].document;
        var panel = doc.getElementById('cc-panel');
        var active = doc.activeElement;

        if (panel && panel.__ccPressed) return true;

        if (active && active.isConnected && active.classList && active.classList.contains('cc-keep-input')) return true;
      } catch (e) {}
    }

    return false;
  }

  // The panel's mousedown, in the capture phase so it runs before the keep box blurs. The release clears it after a
  // turn, once the click that follows it has run; a release the page never sees clears it after PRESS_LIMIT_MS.
  function holdUntilReleased() {
    var panel = this;
    var doc = panel.ownerDocument;
    var press = {};

    // Only this press's own release counts, so an earlier press's timer cannot end a later one.
    function release() {
      if (panel.__ccPressed !== press) return;

      panel.__ccPressed = null;
      renderSkipped();
    }

    panel.__ccPressed = press;
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

  function act(kind, arg) {
    var wins = benchWindows();
    var core = wins.length ? benchCore(wins[0]) : null;

    if (!core || !ACTIONS[kind]) {
      post('diag|no workbench page for ' + kind);

      return;
    }

    try {
      ACTIONS[kind](wins[0], core, arg);
    } catch (e) {
      post('diag|' + kind + ' failed on the workbench page: ' + e);
    }
  }

  // The panel sits after the craft controls, which the page can rebuild; checking keeps it there.
  setInterval(function () {
    var wins = benchWindows();

    for (var i = 0; i < wins.length; i++) {
      try {
        install(wins[i]);

        if (wins[i].__ccInstalled && !wins[i].document.getElementById('cc-panel')) renderWindow(wins[i]);
      } catch (e) {}
    }
  }, POLL_MS);

  window.__craftinghub = { setData: setData, act: act };
  post('ready');
})();
