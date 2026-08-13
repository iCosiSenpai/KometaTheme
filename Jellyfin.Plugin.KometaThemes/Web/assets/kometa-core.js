/* KometaThemes core — window.KT namespace.
   Shared by all plugin pages: API client (modern MediaBrowser auth),
   plugin config access, i18n, UI helpers (toast, confirm, sync poller,
   media preview player) and small DOM utilities. No toolchain, ES5-ish. */
(function () {
    'use strict';

    // Guard against double-loading the exact same version of core.
    // Bump this string on every meaningful change to kometa-core.js (keeps in sync with Directory.Build.props + HTML V=).
    var CURRENT_VERSION = '1.4.1.0';
    if (window.KT && window.KT.VERSION === CURRENT_VERSION) { return; }

    var KT = {
        VERSION: CURRENT_VERSION,
        GUID: '48c98707-45d1-43ac-94b8-f74d875ad29c',
        pages: (window.KT && window.KT.pages) || {}
    };

    /* ---------------- util ---------------- */

    var TRUSTED_REMOTE_SUFFIXES = ['animethemes.moe'];

    function isTrustedRemoteHost(hostname) {
        var host = String(hostname || '').toLowerCase().replace(/\.$/, '');
        return TRUSTED_REMOTE_SUFFIXES.some(function (suffix) {
            return host === suffix || host.slice(-(suffix.length + 1)) === '.' + suffix;
        });
    }

    var util = {
        esc: function (value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
        },
        el: function (tag, className, text) {
            var node = document.createElement(tag);
            if (className) { node.className = className; }
            if (text != null) { node.textContent = text; }
            return node;
        },
        /* Only same-origin HTTP(S) resources or HTTPS resources from the
           AnimeThemes domain family may be assigned to media/image attributes. */
        safeUrl: function (value) {
            if (!value) { return null; }
            try {
                var parsed = new URL(String(value), window.location.href);
                var sameOrigin = parsed.origin === window.location.origin;
                var protocolAllowed = parsed.protocol === 'https:' || (sameOrigin && parsed.protocol === 'http:');
                var hostAllowed = sameOrigin || isTrustedRemoteHost(parsed.hostname);
                if (protocolAllowed && hostAllowed && !parsed.username && !parsed.password) {
                    return parsed.href;
                }
            } catch (e) { /* invalid URL */ }
            return null;
        },
        setBackgroundImage: function (node, value) {
            var safe = util.safeUrl(value);
            if (!node || !safe) { return false; }
            node.style.backgroundImage = 'url("' + safe.replace(/"/g, '%22') + '")';
            return true;
        },
        clear: function (node) {
            while (node && node.firstChild) { node.removeChild(node.firstChild); }
        },
        /* Reads a query parameter from the dashboard hash (#/configurationpage?name=X&itemId=Y)
           with a fallback to the regular search string. */
        param: function (name) {
            try {
                var hash = window.location.hash || '';
                var qs = hash.indexOf('?') > -1 ? hash.slice(hash.indexOf('?') + 1) : '';
                var fromHash = new URLSearchParams(qs).get(name);
                if (fromHash) { return fromHash; }
                return new URLSearchParams(window.location.search).get(name);
            } catch (e) {
                return null;
            }
        },
        getItemId: function () { return util.param('itemId') || util.param('id'); },
        debounce: function (fn, ms) {
            var timer = null;
            return function () {
                var args = arguments, self = this;
                if (timer) { clearTimeout(timer); }
                timer = setTimeout(function () { timer = null; fn.apply(self, args); }, ms);
            };
        },
        formatDate: function (iso) {
            if (!iso) { return '-'; }
            try { return new Date(iso).toLocaleString(); } catch (e) { return String(iso); }
        },
        navigate: function (url) {
            try { Dashboard.navigate(url); } catch (e) { window.location.hash = '#/' + url; }
        },
        /* Returns a page-scoped element lookup. Every page module defined its own identical
           `function q(id) { return state.page.querySelector('#' + id); }`. */
        scoped: function (root) {
            return function (id) { return root ? root.querySelector('#' + id) : null; };
        },
        /* Applies a Jellyfin item's poster and backdrop to the standard hero nodes.
           search.js and item.js had byte-identical copies of this. */
        applyItemImages: function (item, posterNode, backdropNode) {
            if (item && posterNode && item.ImageTags && item.ImageTags.Primary) {
                var posterUrl = util.safeUrl(ApiClient.getScaledImageUrl(item.Id, {
                    type: 'Primary', maxWidth: 240, tag: item.ImageTags.Primary
                }));
                if (posterUrl) {
                    util.clear(posterNode);
                    var img = util.el('img');
                    img.alt = '';
                    img.setAttribute('aria-hidden', 'true');
                    img.src = posterUrl;
                    posterNode.appendChild(img);
                }
            }

            if (item && backdropNode && item.BackdropImageTags && item.BackdropImageTags.length) {
                var backdropUrl = ApiClient.getScaledImageUrl(item.Id, {
                    type: 'Backdrop', maxWidth: 1280, tag: item.BackdropImageTags[0]
                });
                backdropNode.style.display = util.setBackgroundImage(backdropNode, backdropUrl) ? '' : 'none';
            }
        }
    };

    /* ---------------- api ---------------- */

    function authHeader() {
        try {
            if (typeof ApiClient !== 'undefined' && typeof ApiClient.accessToken === 'function') {
                var token = ApiClient.accessToken();
                if (token) { return 'MediaBrowser Token="' + token + '"'; }
            }
        } catch (e) { /* fall through */ }
        return null;
    }

    function apiUrl(path) {
        try {
            if (typeof ApiClient !== 'undefined' && typeof ApiClient.getUrl === 'function') {
                return ApiClient.getUrl(path);
            }
        } catch (e) { /* fall through */ }
        return path;
    }

    function responseError(payload, status) {
        var message = payload && typeof payload === 'object' && (payload.error || payload.message || payload.title);
        if (!message && typeof payload === 'string' && payload.indexOf('<') === -1) { message = payload; }
        message = String(message || ('HTTP ' + status)).replace(/[\r\n\t]+/g, ' ').trim();
        if (message.length > 500) { message = message.slice(0, 497) + '...'; }
        var error = new Error(message);
        error.status = status;
        error.payload = payload;
        return error;
    }

    function request(method, path, body) {
        var headers = { Accept: 'application/json' };
        var auth = authHeader();
        if (auth) { headers.Authorization = auth; }
        var options = { method: method, credentials: 'same-origin', headers: headers };
        if (body !== undefined) {
            headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(body);
        }
        return fetch(apiUrl(path), options).then(function (response) {
            var isJson = (response.headers.get('Content-Type') || '').toLowerCase().indexOf('json') > -1;
            if (!response.ok) {
                return (isJson ? response.json() : response.text()).catch(function () { return null; })
                    .then(function (payload) { throw responseError(payload, response.status); });
            }
            if (response.status === 204 || !isJson) { return null; }
            return response.json();
        });
    }

    KT.api = {
        get: function (path) { return request('GET', path); },
        post: function (path, body) { return request('POST', path, body); },
        del: function (path) { return request('DELETE', path); },
        /* GET a binary endpoint and trigger a browser download. */
        download: function (path, filename) {
            var headers = {};
            var auth = authHeader();
            if (auth) { headers.Authorization = auth; }
            return fetch(apiUrl(path), { credentials: 'same-origin', headers: headers })
                .then(function (response) {
                    if (!response.ok) { throw new Error('HTTP ' + response.status); }
                    return response.blob();
                })
                .then(function (blob) {
                    var url = URL.createObjectURL(blob);
                    var link = util.el('a');
                    link.href = url;
                    link.download = filename;
                    document.body.appendChild(link);
                    link.click();
                    link.remove();
                    setTimeout(function () { URL.revokeObjectURL(url); }, 5000);
                });
        }
    };

    /* ---------------- plugin configuration ---------------- */

    KT.config = {
        load: function () {
            return ApiClient.getPluginConfiguration(KT.GUID);
        },
        save: function (config) {
            return ApiClient.updatePluginConfiguration(KT.GUID, config);
        }
    };

    /* ---------------- i18n ---------------- */

    var strings = {
        save: 'Save', discard: 'Discard', cancel: 'Cancel', confirm: 'Confirm',
        close: 'Close', refresh: 'Refresh', loading: 'Loading…', retry: 'Retry',
        saved: 'Settings saved', saveFailed: 'Save failed', error: 'Something went wrong',
        audio: 'Audio', video: 'Video', downloaded: 'Downloaded', missing: 'Missing',
        preview: 'Preview', delete: 'Delete', deleteAll: 'Delete all',
        syncNow: 'Sync now', dryRun: 'Dry run', syncRunning: 'Sync in progress…',
        syncDone: 'Sync finished', syncStartFailed: 'Could not start sync',
        phase: 'Phase', processed: 'Processed', resolved: 'Resolved',
        downloadedCount: 'Downloaded', failed: 'Failed', skipped: 'Skipped',
        idle: 'Idle', never: 'Never', unsavedChanges: 'Unsaved changes',
        confirmTitle: 'Are you sure?', themeFinder: 'Theme Finder',
        openThemeFinder: 'Open Theme Finder', settings: 'Settings', copy: 'Copy', copied: 'Copied',
        supportLabel: 'Enjoying KometaThemes? Support its development.', supportCoffee: 'Buy me a coffee', supportPayPal: 'Donate with PayPal', supportGitHub: 'Open on GitHub',
        invalidRemoteUrl: 'Blocked an invalid or insecure remote URL', openOnSource: 'Open on source',
        dotIdle: 'No sync running', dotRunning: 'Sync in progress', dotError: 'Last sync failed'
    };

    KT.i18n = {
        /* Each page module contributes its own strings on load. */
        extend: function (extra) {
            Object.keys(extra).forEach(function (key) { strings[key] = extra[key]; });
        },
        apply: function (root) {
            (root || document).querySelectorAll('[data-kt]').forEach(function (node) {
                node.textContent = KT.t(node.getAttribute('data-kt'));
            });
            (root || document).querySelectorAll('[data-kt-placeholder]').forEach(function (node) {
                node.placeholder = KT.t(node.getAttribute('data-kt-placeholder'));
            });
            (root || document).querySelectorAll('[data-kt-aria-label]').forEach(function (node) {
                node.setAttribute('aria-label', KT.t(node.getAttribute('data-kt-aria-label')));
            });
        }
    };

    KT.t = function (key, params) {
        /* An unknown key renders as itself, which is visible in the UI instead of blank. */
        var text = strings[key] != null ? strings[key] : key;
        if (params) {
            Object.keys(params).forEach(function (name) {
                /* Function replacement, not a string: $&, $`, $' and $1 are special in a
                   replacement string, so an anime title containing "$&" rendered corrupted. */
                var value = params[name];
                text = text.replace(new RegExp('\\{' + name + '\\}', 'g'), function () {
                    return value == null ? '' : String(value);
                });
            });
        }
        return text;
    };

    /* ---------------- ui ---------------- */

    var ui = {};

    ui.loading = function (show) {
        try {
            if (show) { Dashboard.showLoadingMsg(); } else { Dashboard.hideLoadingMsg(); }
        } catch (e) { /* dashboard global missing — pages render their own skeletons */ }
    };

    ui.toast = function (message, type, timeout) {
        var host = document.querySelector('.kt-toast-host');
        if (!host) {
            host = util.el('div', 'kt-toast-host');
            host.setAttribute('aria-live', 'polite');
            host.setAttribute('aria-atomic', 'false');
            document.body.appendChild(host);
        }
        var toast = util.el('div', 'kt-toast' + (type ? ' ' + type : ''), message);
        toast.setAttribute('role', type === 'error' ? 'alert' : 'status');
        host.appendChild(toast);
        setTimeout(function () {
            toast.classList.add('leaving');
            setTimeout(function () { toast.remove(); }, 220);
        }, timeout || 3200);
    };

    ui.confirm = function (message, title) {
        return new Promise(function (resolve) {
            var previouslyFocused = document.activeElement;
            var veil = util.el('div', 'kt-modal-veil');
            var modal = util.el('div', 'kt-modal kt-page');
            var titleId = 'kt-modal-title-' + Date.now();
            var heading = util.el('h3', null, title || KT.t('confirmTitle'));
            heading.id = titleId;
            modal.setAttribute('role', 'alertdialog');
            modal.setAttribute('aria-modal', 'true');
            modal.setAttribute('aria-labelledby', titleId);
            modal.tabIndex = -1;
            modal.appendChild(heading);
            if (typeof message === 'string') {
                modal.appendChild(util.el('p', null, message));
            } else if (Array.isArray(message)) {
                message.forEach(function (node) { modal.appendChild(node); });
            } else if (message && message.nodeType) {
                modal.appendChild(message);
            }
            var row = util.el('div', 'kt-row');
            var btnCancel = util.el('button', 'kt-btn kt-btn-ghost', KT.t('cancel'));
            var btnOk = util.el('button', 'kt-btn kt-btn-primary', KT.t('confirm'));
            btnCancel.type = 'button';
            btnOk.type = 'button';
            row.appendChild(btnCancel);
            row.appendChild(btnOk);
            modal.appendChild(row);
            veil.appendChild(modal);

            function onKeyDown(event) {
                if (event.key === 'Escape') {
                    event.preventDefault();
                    done(false);
                    return;
                }
                if (event.key !== 'Tab') { return; }
                var focusable = modal.querySelectorAll('button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href]');
                if (!focusable.length) { event.preventDefault(); modal.focus(); return; }
                var first = focusable[0];
                var last = focusable[focusable.length - 1];
                if (event.shiftKey && document.activeElement === first) {
                    event.preventDefault(); last.focus();
                } else if (!event.shiftKey && document.activeElement === last) {
                    event.preventDefault(); first.focus();
                }
            }

            function done(value) {
                document.removeEventListener('keydown', onKeyDown, true);
                veil.remove();
                if (previouslyFocused && previouslyFocused.isConnected && previouslyFocused.focus) {
                    previouslyFocused.focus();
                }
                resolve(value);
            }

            btnCancel.addEventListener('click', function () { done(false); });
            btnOk.addEventListener('click', function () { done(true); });
            veil.addEventListener('click', function (event) { if (event.target === veil) { done(false); } });
            document.addEventListener('keydown', onKeyDown, true);
            document.body.appendChild(veil);
            btnCancel.focus();
        });
    };

    /* Shared sync status poller. onUpdate(status) is called on every tick;
       polling stops automatically once status.isFinished is true. */
    ui.syncPoller = function (onUpdate, intervalMs) {
        var timer = null;
        var poller = {
            running: false,
            start: function () {
                if (poller.running) { return; }
                poller.running = true;
                tick();
                timer = setInterval(tick, intervalMs || 2000);
            },
            stop: function () {
                poller.running = false;
                if (timer) { clearInterval(timer); timer = null; }
            }
        };
        function tick() {
            KT.api.get('Plugins/KometaThemes/Sync/status').then(function (status) {
                onUpdate(status);
                if (status && status.isFinished) { poller.stop(); }
            }).catch(function () { /* keep polling */ });
        }
        return poller;
    };

    /* Live sync indicator dot for the page heroes. Polls slowly while idle and
       tightens up while a sync is running; pauses when the tab is hidden or the
       element left the DOM. Returns { stop }. */
    ui.attachSyncDot = function (el) {
        if (!el) { return { stop: function () {} }; }
        var IDLE_MS = 15000;
        var ACTIVE_MS = 2500;
        var timer = null;
        var stopped = false;

        function apply(status) {
            var cls = 'kt-live-dot ';
            var titleKey;
            if (status && status.phase === 'failed') {
                cls += 'is-error';
                titleKey = 'dotError';
            } else if (status && !status.isFinished && status.phase && status.phase !== 'idle') {
                cls += 'is-running';
                titleKey = 'dotRunning';
            } else {
                cls += 'is-idle';
                titleKey = 'dotIdle';
            }
            el.className = cls;
            el.title = KT.t(titleKey);
            return cls.indexOf('is-running') > -1;
        }

        function schedule(ms) {
            if (stopped) { return; }
            timer = setTimeout(tick, ms);
        }

        function tick() {
            if (stopped) { return; }
            if (document.hidden || !el.isConnected) {
                schedule(IDLE_MS);
                return;
            }
            KT.api.get('Plugins/KometaThemes/Sync/status').then(function (status) {
                schedule(apply(status) ? ACTIVE_MS : IDLE_MS);
            }).catch(function () {
                schedule(IDLE_MS);
            });
        }

        tick();
        return {
            stop: function () {
                stopped = true;
                if (timer) { clearTimeout(timer); timer = null; }
            }
        };
    };

    /* Appends a line to a .kt-log box, revealing it on first use.
       search.js called this logLine() and config.js called it log(); same body. */
    ui.logLine = function (box, message, level) {
        if (!box) { return; }
        box.style.display = '';
        box.appendChild(util.el('div', level || 'info', message));

        // Bound the box: these are aria-live regions, and an unbounded transcript is both a memory
        // leak and an ever-growing thing for a screen reader to sit inside.
        while (box.childElementCount > 400) {
            box.removeChild(box.firstChild);
        }

        box.scrollTop = box.scrollHeight;
    };

    /* Builds a two-state toggle button carrying aria-pressed, which the theme filters and the
       media toggles each reimplemented. onToggle receives the new state. */
    ui.toggleButton = function (label, pressed, onToggle, className) {
        var button = util.el('button', 'kt-btn kt-btn-sm ' + (className || ''), label);
        button.type = 'button';

        function apply(value) {
            button.classList.toggle('on', !!value);
            button.setAttribute('aria-pressed', value ? 'true' : 'false');
        }

        apply(pressed);
        button.addEventListener('click', function () {
            var next = button.getAttribute('aria-pressed') !== 'true';
            apply(next);
            if (onToggle) { onToggle(next); }
        });

        button.ktSetPressed = apply;
        return button;
    };

    /* Per-page teardown registry.
       Jellyfin caches page divs and never removes them, so a poller started on `pageshow`
       kept running for the rest of the session — attachSyncDot's 15s status poll and
       syncPoller's 2s progress poll were both started and their stop handles discarded, so
       leaving the page did not stop them. Pages register their disposables here and a single
       `viewbeforehide`/`pagehide` listener runs them when the page is navigated away from. */
    ui.lifecycle = function (page) {
        if (!page) { return { add: function () {}, dispose: function () {} }; }

        var registry = page.__ktDisposables;
        if (!registry) {
            registry = [];
            page.__ktDisposables = registry;

            var dispose = function () {
                var pending = registry.splice(0, registry.length);
                pending.forEach(function (fn) {
                    try { fn(); } catch (e) { /* teardown is best-effort */ }
                });
            };

            page.addEventListener('viewbeforehide', dispose);
            page.addEventListener('pagehide', dispose);
            window.addEventListener('beforeunload', dispose);
        }

        return {
            /* Accepts a function, or anything exposing stop()/close()/disconnect(). */
            add: function (disposable) {
                if (!disposable) { return disposable; }
                if (typeof disposable === 'function') {
                    registry.push(disposable);
                } else if (typeof disposable.stop === 'function') {
                    registry.push(function () { disposable.stop(); });
                } else if (typeof disposable.disconnect === 'function') {
                    registry.push(function () { disposable.disconnect(); });
                }

                return disposable;
            },
            timeout: function (fn, ms) {
                var id = setTimeout(fn, ms);
                registry.push(function () { clearTimeout(id); });
                return id;
            },
            dispose: function () {
                var pending = registry.splice(0, registry.length);
                pending.forEach(function (fn) {
                    try { fn(); } catch (e) { /* teardown is best-effort */ }
                });
            }
        };
    };

    /* Renders a sync status object into a .kt-progress block (fill, phase, counters). */
    ui.renderSyncStatus = function (root, status) {
        if (!root || !status) { return; }
        var fill = root.querySelector('.kt-progress-fill');
        if (fill) {
            fill.classList.toggle('indeterminate', !status.isFinished && status.totalItems === 0);
            fill.style.width = (status.isFinished ? 100 : (status.progressPercent || 0)) + '%';
        }
        var text = root.querySelector('[data-kt-sync-text]');
        if (text) {
            var label = status.isFinished ? KT.t('syncDone') : (status.message || status.phase || KT.t('idle'));
            if (!status.isFinished && status.totalItems > 0) {
                label += ' (' + status.processedItems + '/' + status.totalItems + ')';
            }
            text.textContent = label;
        }
        var counters = root.querySelector('[data-kt-sync-counters]');
        if (counters) {
            util.clear(counters);
            [[KT.t('phase'), status.phase || '-'],
             [KT.t('processed'), status.processedItems],
             [KT.t('resolved'), status.resolvedItems],
             [KT.t('downloadedCount'), status.downloadedItems],
             [KT.t('skipped'), status.skippedItems]].forEach(function (pair) {
                var span = util.el('span', null, pair[0] + ': ');
                span.appendChild(util.el('strong', null, String(pair[1] != null ? pair[1] : '-')));
                counters.appendChild(span);
            });
        }
    };

    /* Shared click-to-load media preview (one floating player at a time). */
    ui.player = (function () {
        var current = null;
        var lastVolume = 0.5;
        var keyHandler = null;

        /* One stable close(). The previous implementation reassigned the local `close`
           to a per-instance doClose() *after* binding the ✕ button to it, so the button
           kept calling the previous instance's closer and never unbound the current
           instance's capture-phase Escape listener. Every player open therefore left an
           orphan handler on document that swallowed one future Escape press elsewhere in
           the dashboard. Instance state now lives in module scope instead. */
        function close() {
            if (keyHandler) {
                document.removeEventListener('keydown', keyHandler, true);
                keyHandler = null;
            }

            if (current) {
                try {
                    var m = current.querySelector('audio,video');
                    if (m) {
                        lastVolume = m.volume;
                        m.pause();
                        m.removeAttribute('src');
                        m.load();
                    }
                } catch (e) { /* teardown is best-effort */ }
                current.remove();
                current = null;
            }
        }

        function open(url, mediaType, title, preferredVolume) {
            var safeUrl = util.safeUrl(url);
            if (!safeUrl) {
                ui.toast(KT.t('invalidRemoteUrl'), 'error');
                return;
            }
            url = safeUrl;
            close();

            var panel = util.el('div', 'kt-player kt-page');
            var head = util.el('div', 'kt-player-head');
            var badge = util.el('span', 'kt-badge ' + (mediaType === 'video' ? 'ed' : 'op'), mediaType === 'video' ? KT.t('video') : KT.t('audio'));
            badge.classList.add('kt-player-badge');
            var label = util.el('div', 'kt-player-title', title || KT.t('preview'));
            var btnClose = util.el('button', 'kt-player-close', '✕');
            btnClose.type = 'button';
            btnClose.setAttribute('aria-label', KT.t('close'));
            btnClose.addEventListener('click', close);

            var link = util.el('a', 'kt-player-link', '↗');
            link.href = url;
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            link.title = KT.t('openOnSource');
            link.addEventListener('click', function (ev) { ev.stopImmediatePropagation(); });

            head.appendChild(badge);
            head.appendChild(label);
            head.appendChild(link);
            head.appendChild(btnClose);
            panel.appendChild(head);

            var media = document.createElement(mediaType === 'video' ? 'video' : 'audio');
            media.controls = true;
            media.autoplay = true;
            media.preload = 'metadata';
            media.referrerPolicy = 'no-referrer';
            media.src = url;
            var vol = (typeof preferredVolume === 'number' && preferredVolume > 0) ? preferredVolume : lastVolume;
            media.volume = Math.min(1, Math.max(0, vol));
            media.addEventListener('volumechange', function () {
                lastVolume = media.volume;
            });
            media.addEventListener('error', function () {
                ui.toast(KT.t('error') + ' — ' + KT.t('previewFailedHint'), 'error');
            });

            keyHandler = function (ev) {
                if (ev.key === 'Escape') {
                    ev.preventDefault();
                    close();
                }
            };
            document.addEventListener('keydown', keyHandler, true);

            panel.appendChild(media);
            document.body.appendChild(panel);
            current = panel;
            panel.dataset.url = url;
            panel.dataset.mediaType = mediaType;
        }

        return {
            open: open,
            close: close,
            toggle: function (url, mediaType, title, preferredVolume) {
                if (current && current.dataset.url === url) { close(); return; }
                open(url, mediaType, title, preferredVolume);
            }
        };
    })();

    KT.util = util;
    KT.ui = ui;
    window.KT = KT;
})();
