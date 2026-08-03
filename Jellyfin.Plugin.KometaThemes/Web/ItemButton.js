(function () {
    'use strict';

    var STATE_KEY = '__KometaThemesItemButton';
    var BTN_ID = 'kometaThemesBtn';
    var BTN_CLASS = 'button-flat btnKometaThemes detailButton';
    var PAGE_NAME = 'KometaThemesSearch';
    var MENU_PAGE = 'KometaThemesItem'; // the main-menu drawer entry (Plugin.GetPages)
    var DRAWER_STYLE_ID = 'kometathemes-drawer-style';
    var ASSET_VERSION = '1.4.0.0';
    var ICON_URL = 'configurationpage?name=KometaThemesLogoSvg&v=' + ASSET_VERSION;
    var FALLBACK_INTERVAL_MS = 10000;
    var MAX_CACHE_ENTRIES = 500;

    if (window[STATE_KEY] && typeof window[STATE_KEY].destroy === 'function') {
        window[STATE_KEY].destroy();
    }

    var state = {
        observer: null,
        injectTimer: null,
        retryTimer: null,
        fallbackInterval: null,
        currentItemId: null,
        pending: null,
        itemTypeCache: {},
        listeners: [],
        destroy: destroy
    };
    window[STATE_KEY] = state;

    // Jellyfin web routes vary across setups: hash ('#/details?id=…'),
    // legacy hashbang ('#!/details?id=…') and path-based routing.
    function getRouteCandidates() {
        var candidates = [];
        var hash = window.location.hash || '';
        if (hash) {
            candidates.push(hash.replace(/^#!?/, ''));
        }

        candidates.push((window.location.pathname || '') + (window.location.search || ''));
        return candidates;
    }

    function isDetailPage() {
        var candidates = getRouteCandidates();
        for (var i = 0; i < candidates.length; i++) {
            if (candidates[i].indexOf('/details') > -1 || candidates[i].indexOf('details?') === 0) {
                return true;
            }
        }

        return false;
    }

    function getItemId() {
        var candidates = getRouteCandidates();
        for (var i = 0; i < candidates.length; i++) {
            try {
                var qs = candidates[i].split('?')[1] || '';
                var id = new URLSearchParams(qs).get('id');
                if (id) return id;
            } catch (e) {
                // try the next candidate
            }
        }

        return null;
    }

    function getVisibleDetailButtons() {
        var containers = document.querySelectorAll('.mainDetailButtons');
        // Newest page is appended last — iterate in reverse so a stale cached
        // page earlier in the DOM never wins over the page being shown.
        for (var i = containers.length - 1; i >= 0; i--) {
            var container = containers[i];
            var page = container.closest('.page, [data-role="page"]');
            if (!page || isVisible(page)) {
                return container;
            }
        }

        return null;
    }

    function isVisible(element) {
        return !!(element.offsetWidth || element.offsetHeight || element.getClientRects().length);
    }

    function getCurrentUserId() {
        try {
            if (typeof ApiClient !== 'undefined' && typeof ApiClient.getCurrentUserId === 'function') {
                return ApiClient.getCurrentUserId();
            }
        } catch (e) {
            return null;
        }

        return null;
    }

    function fetchItem(itemId) {
        if (!itemId || typeof ApiClient === 'undefined') {
            return Promise.resolve(null);
        }

        var userId = getCurrentUserId();
        if (typeof ApiClient.getItem === 'function' && userId) {
            return ApiClient.getItem(userId, itemId);
        }

        if (typeof ApiClient.getUrl === 'function') {
            var path = userId ?
                'Users/' + encodeURIComponent(userId) + '/Items/' + encodeURIComponent(itemId) :
                'Items/' + encodeURIComponent(itemId);
            var headers = { Accept: 'application/json' };
            if (typeof ApiClient.accessToken === 'function') {
                var token = ApiClient.accessToken();
                // Modern auth scheme — X-Emby-Token is removed in Jellyfin 10.13.
                if (token) headers.Authorization = 'MediaBrowser Token="' + token + '"';
            }

            return fetch(ApiClient.getUrl(path), { credentials: 'same-origin', headers: headers })
                .then(function (response) { return response.ok ? response.json() : null; });
        }

        return Promise.resolve(null);
    }

    var isAdminPromise = null;
    function isAdmin() {
        if (!isAdminPromise) {
            isAdminPromise = (typeof ApiClient !== 'undefined' && typeof ApiClient.getCurrentUser === 'function'
                ? ApiClient.getCurrentUser()
                : Promise.reject(new Error('ApiClient unavailable'))
            ).then(function (user) {
                if (!user) {
                    // No user object — treat as transient, allow a retry later.
                    isAdminPromise = null;
                    return false;
                }

                return !!(user.Policy && user.Policy.IsAdministrator);
            }).catch(function () {
                // Transient failure (startup, network) — don't memoize false forever.
                isAdminPromise = null;
                return false;
            });
        }

        return isAdminPromise;
    }

    function isSupportedItemType(item) {
        return item && (item.Type === 'Series' || item.Type === 'Movie');
    }

    function fetchEligible(itemId) {
        if (!itemId) return Promise.resolve({ eligible: false });
        var path = 'Plugins/KometaThemes/Items/' + encodeURIComponent(itemId) + '/eligible';
        var headers = { Accept: 'application/json' };
        if (typeof ApiClient !== 'undefined' && typeof ApiClient.accessToken === 'function') {
            var token = ApiClient.accessToken();
            if (token) headers.Authorization = 'MediaBrowser Token="' + token + '"';
        }
        return fetch(ApiClient.getUrl ? ApiClient.getUrl(path) : path, { credentials: 'same-origin', headers: headers })
            .then(function (response) { return response.ok ? response.json() : { eligible: false }; })
            .catch(function () { return { eligible: false }; });
    }

    function removeButton() {
        var old = document.getElementById(BTN_ID);
        if (old) old.remove();
    }

    function createButton(itemId) {
        var btn = document.createElement('button');
        btn.id = BTN_ID;
        btn.dataset.itemId = itemId;
        btn.setAttribute('is', 'emby-button');
        btn.type = 'button';
        btn.className = BTN_CLASS;
        btn.title = 'KometaThemes';
        btn.setAttribute('aria-label', 'KometaThemes Theme Finder');

        var div = document.createElement('div');
        div.className = 'detailButton-content';

        var icon = document.createElement('img');
        icon.className = 'detailButton-icon';
        icon.src = ICON_URL;
        icon.alt = '';
        icon.setAttribute('aria-hidden', 'true');
        icon.width = 28;
        icon.height = 28;
        icon.style.objectFit = 'contain';

        div.appendChild(icon);
        btn.appendChild(div);

        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var currentItemId = btn.dataset.itemId || getItemId();
            if (!currentItemId) return;

            var url = buildThemeFinderUrl(currentItemId);
            try {
                Dashboard.navigate(url);
            } catch (ex) {
                window.location.hash = '#!/' + url;
            }
        });

        return btn;
    }

    function buildThemeFinderUrl(itemId) {
        return 'configurationpage?name=' + PAGE_NAME + '&itemId=' + encodeURIComponent(itemId);
    }

    function injectButton(itemId) {
        var container = getVisibleDetailButtons();
        if (!container || getItemId() !== itemId) {
            return;
        }

        var old = document.getElementById(BTN_ID);
        if (old && old.dataset.itemId === itemId && old.parentElement === container) {
            return;
        }

        removeButton();
        var moreBtn = container.querySelector('.btnMoreCommands');
        var btn = createButton(itemId);
        if (moreBtn) {
            container.insertBefore(btn, moreBtn);
        } else {
            container.appendChild(btn);
        }
    }

    // Idempotent: safe to call from the observer, the fallback interval and
    // navigation events. Never tears down in-flight work for the same item —
    // a lookup slower than the 1.5s fallback tick must still land the button.
    function ensureButton() {
        // The drawer entry exists on every page, independent of the detail-page
        // button logic below, so decorate it before any early return.
        decorateDrawer();

        if (!isDetailPage()) {
            removeButton();
            state.currentItemId = null;
            return;
        }

        var itemId = getItemId();
        if (!itemId) {
            // Transient hash state during navigation — keep whatever is there.
            return;
        }

        if (itemId !== state.currentItemId) {
            state.currentItemId = itemId;
            removeButton();
            if (state.pending && state.pending.itemId !== itemId) {
                state.pending = null;
            }
        }

        // Fast path: the button is already in place for this item, so there is nothing to do and —
        // crucially — no request to make. Without this, every observer tick and every fallback tick
        // re-ran an uncached /eligible fetch, so simply sitting on a detail page issued tens of
        // authenticated requests per minute per open tab, indefinitely.
        var existing = document.getElementById(BTN_ID);
        if (existing && existing.dataset.itemId === itemId && existing.isConnected) {
            return;
        }

        var cached = state.itemTypeCache[itemId];
        if (cached === 'unsupported') {
            return;
        }

        if (cached === 'supported') {
            // Eligibility was resolved and cached with the item type; it is per-item static data,
            // so re-asking on every tick was pure overhead. The admin promise is memoized too, so
            // this resolves without touching the network.
            isAdmin().then(function (admin) {
                if (admin && isDetailPage() && getItemId() === itemId) {
                    injectButton(itemId);
                }
            });
            return;
        }

        if (state.pending && state.pending.itemId === itemId) {
            return;
        }

        var pending = { itemId: itemId };
        state.pending = pending;
        pending.promise = Promise.all([fetchItem(itemId), isAdmin(), fetchEligible(itemId)]).then(function (results) {
            var item = results[0];
            var admin = results[1];
            var eligibility = results[2] || { eligible: false };
            if (state.pending === pending) {
                state.pending = null;
            }

            var isSupported = isSupportedItemType(item) && eligibility.eligible;
            if (item) {
                // Bound the cache: a long dashboard session can visit a lot of items.
                var keys = Object.keys(state.itemTypeCache);
                if (keys.length > MAX_CACHE_ENTRIES) {
                    for (var k = 0; k < keys.length - MAX_CACHE_ENTRIES; k++) {
                        delete state.itemTypeCache[keys[k]];
                    }
                }

                // Only cache real lookups; network failures stay uncached so
                // the next tick retries. Admin is evaluated per attempt — a
                // transient auth failure must not poison the type cache.
                state.itemTypeCache[itemId] = isSupported ? 'supported' : 'unsupported';
            }

            if (admin && state.itemTypeCache[itemId] === 'supported' && isDetailPage()) {
                injectButton(itemId);
            }
        }).catch(function () {
            if (state.pending === pending) {
                state.pending = null;
            }
        });
    }

    // ---- Sidebar (dashboard drawer) brand icon -----------------------------
    // Jellyfin 10.11 renders EnableInMainMenu plugin pages with a hardcoded
    // Folder icon and ignores MenuIcon. A narrowly scoped rule hides that
    // Folder and displays the same versioned SVG mark used by the plugin pages.
    // The style survives React drawer re-renders without mutating menu nodes.
    function ensureDrawerStyle() {
        if (document.getElementById(DRAWER_STYLE_ID)) {
            return;
        }

        var sel = 'a[href*="name=' + MENU_PAGE + '"] .MuiListItemIcon-root';
        var style = document.createElement('style');
        style.id = DRAWER_STYLE_ID;
        style.textContent =
            sel + ' svg{display:none!important;}' +
            sel + '{position:relative;}' +
            sel + '::before{content:"";display:inline-block;flex:0 0 24px;' +
            'width:24px;height:24px;background:url("' + ICON_URL + '") center/contain no-repeat;' +
            'filter:drop-shadow(0 2px 4px rgba(40,93,210,.35));}';
        (document.head || document.documentElement).appendChild(style);
    }

    function decorateDrawer() {
        // The scoped CSS does the work; we just make sure it is present once.
        ensureDrawerStyle();
    }

    function scheduleEnsure(delay) {
        if (state.injectTimer) clearTimeout(state.injectTimer);
        state.injectTimer = setTimeout(function () {
            state.injectTimer = null;
            ensureButton();
        }, delay || 120);
    }

    function handleNavigation() {
        // ensureButton removes the button itself when the item really changed.
        scheduleEnsure(80);
    }

    function addWindowListener(name, handler) {
        window.addEventListener(name, handler);
        state.listeners.push({ name: name, handler: handler });
    }

    function startObserver() {
        if (!document.body) {
            state.retryTimer = setTimeout(startObserver, 100);
            return;
        }

        // Any mutation anywhere in the app schedules a check. That is acceptable only because
        // ensureButton() is now a no-op (no DOM writes, no network) once the button is in place.
        state.observer = new MutationObserver(function () {
            scheduleEnsure(250);
        });
        state.observer.observe(document.body, { childList: true, subtree: true });

        addWindowListener('hashchange', handleNavigation);
        addWindowListener('popstate', handleNavigation);
        addWindowListener('pageshow', function () { scheduleEnsure(60); });

        // Safety net for renders the observer misses. 1.5s was far too aggressive for what is only
        // a fallback; the observer plus the navigation events carry the normal case.
        state.fallbackInterval = setInterval(function () {
            ensureButton();
        }, FALLBACK_INTERVAL_MS);

        ensureButton();
    }

    function destroy() {
        if (state.injectTimer) clearTimeout(state.injectTimer);
        if (state.retryTimer) clearTimeout(state.retryTimer);
        if (state.fallbackInterval) clearInterval(state.fallbackInterval);
        if (state.observer) state.observer.disconnect();

        state.listeners.forEach(function (listener) {
            window.removeEventListener(listener.name, listener.handler);
        });
        state.listeners = [];
        state.pending = null;
        state.itemTypeCache = {};
        removeButton();

        var style = document.getElementById(DRAWER_STYLE_ID);
        if (style) style.remove();
    }

    startObserver();
})();
