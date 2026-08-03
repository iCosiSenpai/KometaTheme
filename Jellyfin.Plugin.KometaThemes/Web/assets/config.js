/* KometaThemes — configuration page logic (KT.pages.config).
   The form is generated from field descriptors and bound to the plugin
   configuration by dotted path; the sticky save bar appears on dirty state. */
(function () {
    'use strict';

    var KT = window.KT;
    var util = KT.util;

    KT.i18n.extend({
        configurationSections: 'Configuration sections',
        tabGeneral: 'General', tabThemes: 'Themes & Download', tabProviders: 'Providers & Matching', tabLibrary: 'Excluded', tabBindings: 'Bindings',
        statHealth: 'Status', statLastSync: 'Last sync', statCache: 'Cache hit rate', statSkipped: 'Skipped items',
        statSkippedSub: 'excluded from sync', healthOk: 'Ready', healthRunning: 'Syncing', healthDown: 'Unreachable',
        syncNow: 'Sync now', forceSync: 'Force sync', toggleLog: 'Activity log',
        confirmSyncNow: 'Run an incremental sync now (only missing or unsatisfied items)?',
        confirmForceSync: 'Force a full sync now? Outdated themes are removed and everything is re-checked.',
        syncTaskMissing: 'Sync task not found. Restart Jellyfin and try again.',
        libraryFilter: 'Library filter', libraryPattern: 'Library name pattern',
        libraryPatternDesc: 'Only libraries whose name contains this pattern are synced and show the KometaThemes UI (♪ button, item page). Empty = all libraries.',
        schedule: 'Schedule', syncInterval: 'Sync interval (hours)',
        syncIntervalDesc: '1–168 hours. Requires a Jellyfin restart to reschedule the timer.',
        automation: 'Automation',
        autoSyncNew: 'Auto-sync newly added items',
        autoSyncNewDesc: 'Fetch themes ~30s after an item is added to a matching library.',
        cleanupRemoved: 'Delete theme files when an item is removed',
        notifySync: 'Notify admins when a sync downloads new themes',
        seriesAudio: 'Series — audio themes', seriesVideo: 'Series — video themes',
        movieAudio: 'Movies — audio themes', movieVideo: 'Movies — video themes',
        fetchType: 'Fetch', volume: 'Volume (%)',
        fetchNone: 'None', fetchSingle: 'Best theme only', fetchAll: 'All themes', fetchAllPerSeason: 'All, per season',
        ignoreOverlapping: 'Skip themes that overlap with the episode',
        ignoreCredits: 'Skip themes with credits (prefer creditless)',
        ignoreOPs: 'Skip openings (OP)', ignoreEDs: 'Skip endings (ED)',
        seasonsTitle: 'Seasons & limits', seasonDetection: 'Season detection',
        seasonAuto: 'Auto', seasonByName: 'By season name', seasonByEpisodes: 'By episode range',
        maxPerSeason: 'Max themes per season',
        fallbackMode: 'When an item has no themes',
        fallbackNone: 'Do nothing special', fallbackAllOPs: 'Download all OPs (audio)',
        fallbackAllEDs: 'Download all EDs (audio)', fallbackAllVideos: 'Download all videos',
        fallbackEverything: 'Download everything',
        downloadTitle: 'Download engine', parallelism: 'Parallel downloads',
        timeout: 'ffmpeg timeout (seconds)',
        forceSyncOpt: 'Force sync on scheduled runs (remove outdated themes every time). "Sync now" button is always incremental.',
        dryRun: 'Dry-run mode (resolve only, never download)',
        dryRunDesc: 'Themes are looked up and logged but NOT downloaded. Useful for testing.',
        providersTitle: 'Provider priority',
        providersNote: 'ID lookups try these providers top to bottom, before the title fallback.',
        moveUp: 'Up', moveDown: 'Down',
        matchingTitle: 'Title matching',
        titleFallback: 'Enable title-based fallback search',
        titleThreshold: 'Match threshold (0–1)',
        rateLimit: 'AnimeThemes rate limit (req/min)',
        cacheTitle: 'Resolution cache',
        cachePositiveTtl: 'Positive entries TTL (days)', cacheNegativeTtl: 'Negative entries TTL (hours)',
        cacheEntries: 'Entries', cacheHits: 'Hits', cacheMisses: 'Misses', cacheHitRate: 'Hit rate',
        clearCache: 'Clear cache', cacheCleared: 'Cache cleared',
        confirmClearCache: 'Clear the whole resolution cache?',
        skippedTitle: 'Excluded anime (blacklist)',
        skippedNote: 'Anime you blacklisted: the sync skips them forever and never searches them again. Press Restore to bring one back.',
        skippedSearch: 'Filter by name…', skippedEmpty: 'No excluded items.',
        restore: 'Restore', restored: 'Item restored',
        colName: 'Name', colType: 'Type', colYear: 'Year', colReason: 'Reason', colWhen: 'When', colActions: '',
        ytTitle: 'YouTube import',
        ytNote: 'Lets you add openings and endings that animethemes.moe does not have, by pasting a YouTube link in the Theme Finder. Works out of the box: the extractor ships with the plugin.',
        ytEnable: 'Allow importing themes from YouTube links',
        ytEnableDesc: 'Off by default. Check the terms of use and the copyright rules that apply where you are before enabling this.',
        ytPath: 'yt-dlp path (optional)',
        ytPathDesc: 'Leave empty to use the bundled extractor. If yt-dlp is installed it is detected automatically and preferred, because it keeps up with YouTube changes faster than a bundled copy can. Set a full path only to point at a specific binary.',
        ytBackendBundled: 'Ready. Using the extractor bundled with the plugin — nothing to install.',
        ytBackendYtDlp: 'Using yt-dlp: {path}',
        ytBackendBroken: 'The configured yt-dlp path does not exist. Clear the field to use the bundled extractor.',
        playlistTitle: 'Themes playlist',
        playlistNote: 'Builds an M3U playlist containing every theme you have downloaded. Refresh rebuilds it; Export M3U saves it to a file.',
        enablePlaylist: 'Maintain a global themes playlist',
        playlistName: 'Playlist name', playlistRoot: 'Export root path (optional)',
        refreshPlaylist: 'Refresh playlist', exportM3u: 'Export M3U',
        playlistRefreshed: 'Playlist refreshed',
        displayHint: 'If themes still do not play, check Settings → Display → "Play theme songs" in your user profile: the Jellyfin 10.11 migration may have reset it.',
        tabFailed: 'Unresolved',
        failedTitle: 'Items that did not fetch',
        failedNote: 'These items could not be matched to an anime, or their download failed. Search & bind one manually, retry the automatic match, or blacklist the ones that are not on AnimeThemes so they are never searched again.',
        failedEmpty: 'Nothing here — every item fetched correctly.',
        failedReasonUnresolved: 'Not matched', failedReasonDownload: 'Download failed',
        failedSearch: 'Search & bind', failedRetry: 'Retry', failedBlacklist: 'Blacklist', failedDismiss: 'Dismiss',
        failedClearAll: 'Clear list',
        failedConfirmBlacklist: 'Blacklist "{name}"? It will never be searched again. You can restore it later from the Library tab.',
        failedConfirmClear: 'Clear the whole unresolved list? Items will reappear if they fail again on the next sync.',
        failedBlacklistReason: 'Not found on AnimeThemes',
        failedAttempts: 'Attempts', failedLastAttempt: 'Last attempt',
        failedRetryDone: 'Retry finished for "{name}"', failedRetryNothing: 'Still nothing found for "{name}"',
        failedBlacklisted: '"{name}" blacklisted',
        failedResolveManually: 'Manually resolved',
        failedResolved: '"{name}" marked as manually resolved',
        bindingsTitle: 'Manual bindings',
        bindingsNote: 'Items you manually bound through the Theme Finder. Automatic sync always uses these bindings first. Remove a binding to let the resolver try again, or unlock it to allow automatic matching without deleting files.',
        bindingsEmpty: 'No manual bindings yet.',
        bindingsSearch: 'Filter by name…',
        colAnime: 'Bound anime', colBound: 'Bound',
        removeBinding: 'Remove binding',
        unlockBinding: 'Unlock / recalculate',
        bindingRemoved: 'Binding removed',
        bindingUnlocked: 'Binding unlocked — next sync will use automatic resolution',
        confirmRemoveBinding: 'Remove the manual binding for "{name}"?',
        confirmUnlockBinding: 'Unlock "{name}" so the next sync re-matches it automatically? Downloaded files are kept.',
        confirmRemoveBindingDelete: 'Also delete downloaded theme files',
        logServer: 'Server', logSession: 'Session', logRefresh: 'Refresh',
        logEmpty: 'No plugin entries in the current server log.',
        logLoadFailed: 'Could not load the server log'
    });

    /* ---- enum helpers (backend accepts strings; values may arrive as numbers) ---- */

    var FETCH_TYPES = ['None', 'Single', 'All', 'AllPerSeason'];
    var FALLBACK_MODES = ['None', 'AllOPs', 'AllEDs', 'AllVideos', 'AllOPsEDsVideos'];

    function enumToString(value, names) {
        if (typeof value === 'number') { return names[value] || names[0]; }
        return names.indexOf(value) > -1 ? value : names[0];
    }

    /* ---- dotted-path access ---- */

    function getPath(obj, path) {
        return path.split('.').reduce(function (node, key) { return node == null ? node : node[key]; }, obj);
    }

    function setPath(obj, path, value) {
        var keys = path.split('.');
        var leaf = keys.pop();
        var node = keys.reduce(function (current, key) {
            if (current[key] == null) { current[key] = {}; }
            return current[key];
        }, obj);
        node[leaf] = value;
    }

    /* ---- field descriptors ---- */

    function fetchOptions() {
        return [
            { value: 'None', label: KT.t('fetchNone') },
            { value: 'Single', label: KT.t('fetchSingle') },
            { value: 'All', label: KT.t('fetchAll') },
            { value: 'AllPerSeason', label: KT.t('fetchAllPerSeason') }
        ];
    }

    function mediaGroup(titleKey, basePath) {
        return {
            title: titleKey,
            fields: [
                { path: basePath + '.FetchType', type: 'enum', names: FETCH_TYPES, label: 'fetchType', options: fetchOptions },
                { path: basePath + '.Volume', type: 'percent', label: 'volume', min: 0, max: 100, step: 5 },
                { path: basePath + '.IgnoreOverlapping', type: 'check', label: 'ignoreOverlapping' },
                { path: basePath + '.IgnoreThemesWithCredits', type: 'check', label: 'ignoreCredits' },
                { path: basePath + '.IgnoreOPs', type: 'check', label: 'ignoreOPs' },
                { path: basePath + '.IgnoreEDs', type: 'check', label: 'ignoreEDs' }
            ]
        };
    }

    var state = {
        page: null,
        config: null,
        providers: [],
        fields: [],
        skipped: [],
        failed: [],
        bindings: [],
        poller: null,
        life: null,
        scoped: null,
        syncStarting: false,
        saving: false,
        logTab: 'server',
        logFetchedAt: 0
    };

    function q(id) { return (state.scoped || util.scoped(state.page))(id); }

    /* ---- field rendering + binding ---- */

    function renderField(descriptor) {
        var wrap;
        if (descriptor.type === 'check') {
            wrap = util.el('div', 'kt-check');
            var label = util.el('label');
            var input = util.el('input', 'kt-checkbox');
            input.type = 'checkbox';
            var body = util.el('div');
            body.appendChild(util.el('span', null, KT.t(descriptor.label)));
            if (descriptor.desc) { body.appendChild(util.el('div', 'kt-field-desc', KT.t(descriptor.desc))); }
            label.appendChild(input);
            label.appendChild(body);
            wrap.appendChild(label);
            descriptor.input = input;
            descriptor.read = function () { return input.checked; };
            descriptor.write = function (value) { input.checked = !!value; };
        } else {
            wrap = util.el('div', 'kt-field');
            var fieldLabel = util.el('label', null, KT.t(descriptor.label));
            wrap.appendChild(fieldLabel);
            var control;
            if (descriptor.type === 'enum' || descriptor.type === 'select') {
                control = util.el('select', 'kt-select');
                (typeof descriptor.options === 'function' ? descriptor.options() : descriptor.options).forEach(function (option) {
                    var node = util.el('option', null, option.label);
                    node.value = option.value;
                    control.appendChild(node);
                });
                descriptor.read = function () { return control.value; };
                descriptor.write = function (value) {
                    control.value = descriptor.type === 'enum' ? enumToString(value, descriptor.names) : String(value);
                };
            } else {
                control = util.el('input', 'kt-input');
                control.type = descriptor.type === 'text' ? 'text' : 'number';
                if (descriptor.min != null) { control.min = descriptor.min; }
                if (descriptor.max != null) { control.max = descriptor.max; }
                if (descriptor.step != null) { control.step = descriptor.step; }
                if (descriptor.placeholder) { control.placeholder = descriptor.placeholder; }
                if (descriptor.type === 'percent') {
                    descriptor.read = function () {
                        var raw = parseFloat(control.value);
                        if (isNaN(raw)) { raw = 50; }
                        return Math.min(100, Math.max(0, raw)) / 100;
                    };
                    descriptor.write = function (value) { control.value = Math.round((value == null ? 0.5 : value) * 100); };
                } else if (descriptor.type === 'number') {
                    descriptor.read = function () {
                        var raw = parseFloat(control.value);
                        if (isNaN(raw)) { raw = descriptor.min != null ? descriptor.min : 0; }
                        if (descriptor.min != null) { raw = Math.max(descriptor.min, raw); }
                        if (descriptor.max != null) { raw = Math.min(descriptor.max, raw); }
                        return raw;
                    };
                    descriptor.write = function (value) { control.value = value != null ? value : ''; };
                } else {
                    descriptor.read = function () { return control.value; };
                    descriptor.write = function (value) { control.value = value != null ? value : ''; };
                }
            }
            var controlId = 'ktField-' + descriptor.path.replace(/[^A-Za-z0-9_-]/g, '-');
            control.id = controlId;
            fieldLabel.htmlFor = controlId;
            descriptor.input = control;
            wrap.appendChild(control);
            if (descriptor.desc) {
                var description = util.el('div', 'kt-field-desc', KT.t(descriptor.desc));
                description.id = controlId + '-description';
                control.setAttribute('aria-describedby', description.id);
                wrap.appendChild(description);
            }
        }
        descriptor.input.addEventListener('change', function () {
            updateDirty();
            if (descriptor.onChange) { descriptor.onChange(); }
        });
        descriptor.input.addEventListener('input', updateDirty);
        state.fields.push(descriptor);
        return wrap;
    }

    function card(titleKey, children, noteKey) {
        var node = util.el('div', 'kt-card kt-reveal');
        var stack = util.el('div', 'kt-stack');
        stack.appendChild(util.el('h3', 'kt-subtitle', KT.t(titleKey)));
        if (noteKey) { stack.appendChild(util.el('p', 'kt-note', KT.t(noteKey))); }
        children.forEach(function (child) { stack.appendChild(child); });
        node.appendChild(stack);
        return node;
    }

    function grid(children) {
        var node = util.el('div', 'kt-grid-2');
        children.forEach(function (child) { node.appendChild(child); });
        return node;
    }

    /* ---- panels ---- */

    function buildGeneralPanel(panel) {
        panel.appendChild(card('libraryFilter', [grid([
            renderField({ path: 'LibraryPattern', type: 'text', label: 'libraryPattern', desc: 'libraryPatternDesc', placeholder: 'Anime' })
        ])]));
        panel.appendChild(card('schedule', [grid([
            renderField({ path: 'SyncIntervalHours', type: 'number', label: 'syncInterval', desc: 'syncIntervalDesc', min: 1, max: 168 })
        ])]));
        panel.appendChild(card('automation', [
            renderField({ path: 'AutoSyncOnItemAdded', type: 'check', label: 'autoSyncNew', desc: 'autoSyncNewDesc' }),
            renderField({ path: 'CleanupThemesOnItemRemoved', type: 'check', label: 'cleanupRemoved' }),
            renderField({ path: 'NotifyOnSyncComplete', type: 'check', label: 'notifySync' })
        ]));
        var hint = util.el('p', 'kt-note', KT.t('displayHint'));
        hint.style.opacity = '0.8';
        panel.appendChild(hint);
    }

    /* Reports which backend import will use. The bundled extractor means this is normally a
       confirmation rather than a problem; the error case is a yt-dlp path set by hand that does
       not resolve, which is worth surfacing because the administrator asked for that binary. */
    function ytStatusRow() {
        var row = util.el('div', 'kt-state');
        row.id = 'ktYtDlpStatus';
        row.setAttribute('role', 'status');
        row.appendChild(document.createTextNode(KT.t('loading')));

        KT.api.get('Plugins/KometaThemes/YouTube/status').then(function (status) {
            util.clear(row);
            if (status && status.available && status.backend === 'yt-dlp') {
                row.className = 'kt-state success';
                row.appendChild(document.createTextNode(KT.t('ytBackendYtDlp', { path: status.executablePath })));
            } else if (status && status.available) {
                row.className = 'kt-state success';
                row.appendChild(document.createTextNode(KT.t('ytBackendBundled')));
            } else {
                row.className = 'kt-state error';
                row.appendChild(document.createTextNode((status && status.error) || KT.t('ytBackendBroken')));
            }
        }).catch(function () {
            util.clear(row);
            row.className = 'kt-state';
            row.appendChild(document.createTextNode(KT.t('ytBackendBundled')));
        });

        return row;
    }

    function buildThemesPanel(panel) {
        var groups = [
            mediaGroup('seriesAudio', 'AudioSettings'),
            mediaGroup('seriesVideo', 'VideoSettings'),
            mediaGroup('movieAudio', 'MovieSettings.AudioSettings'),
            mediaGroup('movieVideo', 'MovieSettings.VideoSettings')
        ];
        var wrap = util.el('div', 'kt-grid-2');
        groups.forEach(function (group) {
            wrap.appendChild(card(group.title, group.fields.map(renderField)));
        });
        panel.appendChild(wrap);

        panel.appendChild(card('seasonsTitle', [grid([
            renderField({ path: 'SeasonDetectionMode', type: 'select', label: 'seasonDetection', options: [
                { value: 'Auto', label: KT.t('seasonAuto') },
                { value: 'ByName', label: KT.t('seasonByName') },
                { value: 'ByEpisodeRange', label: KT.t('seasonByEpisodes') }
            ] }),
            renderField({ path: 'MaxThemesPerSeason', type: 'number', label: 'maxPerSeason', min: 1, max: 50 }),
            renderField({ path: 'MissingThemeFallbackMode', type: 'enum', names: FALLBACK_MODES, label: 'fallbackMode', options: function () {
                return [
                    { value: 'None', label: KT.t('fallbackNone') },
                    { value: 'AllOPs', label: KT.t('fallbackAllOPs') },
                    { value: 'AllEDs', label: KT.t('fallbackAllEDs') },
                    { value: 'AllVideos', label: KT.t('fallbackAllVideos') },
                    { value: 'AllOPsEDsVideos', label: KT.t('fallbackEverything') }
                ];
            } })
        ])]));

        panel.appendChild(card('downloadTitle', [
            grid([
                renderField({ path: 'DegreeOfParallelism', type: 'number', label: 'parallelism', min: 1, max: 8 }),
                renderField({ path: 'DownloadTimeoutSeconds', type: 'number', label: 'timeout', min: 15, max: 300 })
            ]),
            renderField({ path: 'ForceSync', type: 'check', label: 'forceSyncOpt' }),
            renderField({ path: 'DryRunMode', type: 'check', label: 'dryRun', desc: 'dryRunDesc' })
        ]));

        panel.appendChild(card('ytTitle', [
            renderField({ path: 'EnableYouTubeImport', type: 'check', label: 'ytEnable', desc: 'ytEnableDesc' }),
            renderField({ path: 'YtDlpPath', type: 'text', label: 'ytPath', desc: 'ytPathDesc' }),
            ytStatusRow()
        ], 'ytNote'));

        var playlistActions = util.el('div', 'kt-row');
        var btnRefresh = util.el('button', 'kt-btn', KT.t('refreshPlaylist'));
        btnRefresh.type = 'button';
        btnRefresh.addEventListener('click', refreshPlaylist);
        var btnExport = util.el('button', 'kt-btn kt-btn-ghost', KT.t('exportM3u'));
        btnExport.type = 'button';
        btnExport.addEventListener('click', exportPlaylist);
        playlistActions.appendChild(btnRefresh);
        playlistActions.appendChild(btnExport);
        panel.appendChild(card('playlistTitle', [
            renderField({ path: 'EnablePlaylist', type: 'check', label: 'enablePlaylist' }),
            grid([
                renderField({ path: 'PlaylistName', type: 'text', label: 'playlistName' }),
                renderField({ path: 'PlaylistExportRoot', type: 'text', label: 'playlistRoot' })
            ]),
            playlistActions
        ], 'playlistNote'));
    }

    function buildProvidersPanel(panel) {
        var list = util.el('div', 'kt-providers');
        list.id = 'ktProviderList';
        panel.appendChild(card('providersTitle', [list], 'providersNote'));

        panel.appendChild(card('matchingTitle', [
            renderField({ path: 'EnableTitleFallback', type: 'check', label: 'titleFallback' }),
            grid([
                renderField({ path: 'TitleMatchThreshold', type: 'number', label: 'titleThreshold', min: 0, max: 1, step: 0.05 }),
                renderField({ path: 'RateLimitPerMinute', type: 'number', label: 'rateLimit', min: 1, max: 600 })
            ])
        ]));

        var stats = util.el('div', 'kt-stats');
        stats.id = 'ktCacheStats';
        var actions = util.el('div', 'kt-row');
        var btnClear = util.el('button', 'kt-btn kt-btn-danger', KT.t('clearCache'));
        btnClear.type = 'button';
        btnClear.addEventListener('click', clearCache);
        actions.appendChild(btnClear);
        panel.appendChild(card('cacheTitle', [
            grid([
                renderField({ path: 'PositiveCacheTtlDays', type: 'number', label: 'cachePositiveTtl', min: 1, max: 365 }),
                renderField({ path: 'NegativeCacheTtlHours', type: 'number', label: 'cacheNegativeTtl', min: 1, max: 720 })
            ]),
            stats,
            actions
        ]));
    }

    function buildLibraryPanel(panel) {
        var search = util.el('input', 'kt-input kt-search-input');
        search.type = 'text';
        search.placeholder = KT.t('skippedSearch');
        search.addEventListener('input', util.debounce(function () { renderSkipped(search.value); }, 150));
        var tableWrap = util.el('div', 'kt-table-wrap');
        tableWrap.id = 'ktSkippedWrap';
        panel.appendChild(card('skippedTitle', [search, tableWrap], 'skippedNote'));
    }

    function buildFailedPanel(panel) {
        var actions = util.el('div', 'kt-row');
        var btnRefresh = util.el('button', 'kt-btn kt-btn-ghost kt-btn-sm', KT.t('refresh'));
        btnRefresh.type = 'button';
        btnRefresh.addEventListener('click', loadFailed);
        var btnClear = util.el('button', 'kt-btn kt-btn-danger kt-btn-sm', KT.t('failedClearAll'));
        btnClear.type = 'button';
        btnClear.addEventListener('click', clearFailed);
        actions.appendChild(btnRefresh);
        actions.appendChild(btnClear);
        var wrap = util.el('div', 'kt-table-wrap');
        wrap.id = 'ktFailedWrap';
        panel.appendChild(card('failedTitle', [actions, wrap], 'failedNote'));
    }

    function buildBindingsPanel(panel) {
        var search = util.el('input', 'kt-input kt-search-input');
        search.type = 'text';
        search.placeholder = KT.t('bindingsSearch');
        search.addEventListener('input', util.debounce(function () { renderBindings(search.value); }, 150));
        var tableWrap = util.el('div', 'kt-table-wrap');
        tableWrap.id = 'ktBindingsWrap';
        panel.appendChild(card('bindingsTitle', [search, tableWrap], 'bindingsNote'));
    }

    /* ---- failed / unresolved items ---- */

    function reasonChip(item) {
        var unresolved = item.reason === 'Unresolved';
        var chip = util.el('span', 'kt-chip ' + (unresolved ? 'kt-chip-warn' : 'kt-chip-danger'),
            KT.t(unresolved ? 'failedReasonUnresolved' : 'failedReasonDownload'));
        if (item.error) { chip.title = item.error; }
        return chip;
    }

    function renderFailed() {
        var wrap = q('ktFailedWrap');
        util.clear(wrap);
        if (!state.failed.length) {
            wrap.appendChild(util.el('p', 'kt-note', KT.t('failedEmpty'))).style.padding = '14px';
            return;
        }
        var table = util.el('table', 'kt-table');
        var thead = util.el('thead');
        var headRow = util.el('tr');
        ['colName', 'colYear', 'colReason', 'failedAttempts', 'failedLastAttempt', 'colActions'].forEach(function (key) {
            headRow.appendChild(util.el('th', null, KT.t(key)));
        });
        thead.appendChild(headRow);
        table.appendChild(thead);
        var tbody = util.el('tbody');
        state.failed.forEach(function (item) {
            var row = util.el('tr');
            row.appendChild(util.el('td', null, item.name || item.itemId));
            row.appendChild(util.el('td', null, item.productionYear != null ? String(item.productionYear) : '-'));
            var reasonCell = util.el('td');
            reasonCell.appendChild(reasonChip(item));
            row.appendChild(reasonCell);
            row.appendChild(util.el('td', null, String(item.attempts || 1)));
            row.appendChild(util.el('td', null, item.lastAttemptUtc ? new Date(item.lastAttemptUtc).toLocaleString() : '-'));
            var actionCell = util.el('td');
            var actionRow = util.el('div', 'kt-row');
            actionRow.appendChild(failedSearchButton(item));
            actionRow.appendChild(failedRetryButton(item));
            actionRow.appendChild(failedResolveManuallyButton(item));
            actionRow.appendChild(failedBlacklistButton(item));
            actionRow.appendChild(failedDismissButton(item));
            actionCell.appendChild(actionRow);
            row.appendChild(actionCell);
            tbody.appendChild(row);
        });
        table.appendChild(tbody);
        wrap.appendChild(table);
    }

    function openThemeFinder(itemId) {
        var url = 'configurationpage?name=KometaThemesSearch&itemId=' + encodeURIComponent(itemId);
        try { Dashboard.navigate(url); } catch (e) { window.location.hash = '#!/' + url; }
    }

    function failedSearchButton(item) {
        var btn = util.el('button', 'kt-btn kt-btn-sm kt-btn-primary', '🔍 ' + KT.t('failedSearch'));
        btn.type = 'button';
        btn.addEventListener('click', function () { openThemeFinder(item.itemId); });
        return btn;
    }

    function failedRetryButton(item) {
        var btn = util.el('button', 'kt-btn kt-btn-sm kt-btn-primary', KT.t('failedRetry'));
        btn.type = 'button';
        btn.addEventListener('click', function () {
            btn.disabled = true;
            btn.textContent = KT.t('loading');
            KT.api.post('Plugins/KometaThemes/Items/' + encodeURIComponent(item.itemId) + '/sync?force=true').then(function (data) {
                var found = data && data.downloaded;
                KT.ui.toast(KT.t(found ? 'failedRetryDone' : 'failedRetryNothing', { name: item.name }), found ? 'success' : 'error');
                loadFailed();
                loadCacheStats();
            }).catch(function (error) {
                KT.ui.toast(error.message || KT.t('error'), 'error');
                btn.disabled = false;
                btn.textContent = KT.t('failedRetry');
            });
        });
        return btn;
    }

    function failedResolveManuallyButton(item) {
        var btn = util.el('button', 'kt-btn kt-btn-sm kt-btn-ghost', KT.t('failedResolveManually'));
        btn.type = 'button';
        btn.addEventListener('click', function () {
            KT.api.post('Plugins/KometaThemes/Failed/items/' + encodeURIComponent(item.itemId) + '/resolve-manually').then(function () {
                KT.ui.toast(KT.t('failedResolved', { name: item.name }), 'success');
                loadFailed();
            }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
        });
        return btn;
    }

    function failedBlacklistButton(item) {
        var btn = util.el('button', 'kt-btn kt-btn-sm kt-btn-ghost', KT.t('failedBlacklist'));
        btn.type = 'button';
        btn.addEventListener('click', function () {
            KT.ui.confirm(KT.t('failedConfirmBlacklist', { name: item.name })).then(function (ok) {
                if (!ok) { return; }
                KT.api.post('Plugins/KometaThemes/Skipped/' + encodeURIComponent(item.itemId), {
                    reason: KT.t('failedBlacklistReason')
                }).then(function () {
                    KT.ui.toast(KT.t('failedBlacklisted', { name: item.name }), 'success');
                    loadFailed();
                    loadSkipped();
                }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
            });
        });
        return btn;
    }

    function failedDismissButton(item) {
        var btn = util.el('button', 'kt-btn kt-btn-sm kt-btn-ghost', '✕');
        btn.type = 'button';
        btn.title = KT.t('failedDismiss');
        btn.setAttribute('aria-label', KT.t('failedDismiss'));
        btn.addEventListener('click', function () {
            KT.api.del('Plugins/KometaThemes/Failed/items/' + encodeURIComponent(item.itemId)).then(function () {
                loadFailed();
            }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
        });
        return btn;
    }

    function clearFailed() {
        KT.ui.confirm(KT.t('failedConfirmClear')).then(function (ok) {
            if (!ok) { return; }
            KT.api.post('Plugins/KometaThemes/Failed/clear').then(function () {
                loadFailed();
            }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
        });
    }

    function loadFailed() {
        KT.api.get('Plugins/KometaThemes/Failed/items').then(function (items) {
            state.failed = items || [];
            var badge = q('ktFailedBadge');
            badge.textContent = String(state.failed.length);
            badge.style.display = state.failed.length ? '' : 'none';
            renderFailed();
        }).catch(function () { renderFailed(); });
    }

    /* ---- manual bindings ---- */

    function renderBindings(filter) {
        var wrap = q('ktBindingsWrap');
        util.clear(wrap);
        var items = state.bindings || [];
        if (filter) {
            var needle = filter.toLowerCase();
            items = items.filter(function (item) {
                return (item.itemName + ' ' + item.animeName).toLowerCase().indexOf(needle) > -1;
            });
        }
        if (!items.length) {
            wrap.appendChild(util.el('p', 'kt-note', KT.t('bindingsEmpty'))).style.padding = '14px';
            return;
        }

        var table = util.el('table', 'kt-table');
        var thead = util.el('thead');
        var headRow = util.el('tr');
        ['colName', 'colAnime', 'colBound', 'colActions'].forEach(function (key) {
            headRow.appendChild(util.el('th', null, KT.t(key)));
        });
        thead.appendChild(headRow);
        table.appendChild(thead);
        var tbody = util.el('tbody');
        items.forEach(function (item) {
            var row = util.el('tr');
            row.appendChild(util.el('td', null, item.itemName || item.itemId));
            row.appendChild(util.el('td', null, item.animeName || '-'));
            row.appendChild(util.el('td', null, item.boundAt ? new Date(item.boundAt).toLocaleString() : '-'));
            var actionCell = util.el('td');
            var actionRow = util.el('div', 'kt-row');

            var openFinder = util.el('button', 'kt-btn kt-btn-sm kt-btn-ghost', '🔍 ' + KT.t('failedSearch'));
            openFinder.type = 'button';
            openFinder.addEventListener('click', function () {
                util.navigate('configurationpage?name=KometaThemesSearch&itemId=' + encodeURIComponent(item.itemId));
            });
            actionRow.appendChild(openFinder);

            var unlock = util.el('button', 'kt-btn kt-btn-sm kt-btn-ghost', KT.t('unlockBinding'));
            unlock.type = 'button';
            unlock.addEventListener('click', function () {
                KT.ui.confirm(KT.t('confirmUnlockBinding', { name: item.itemName || item.itemId })).then(function (ok) {
                    if (!ok) { return; }
                    KT.api.post('Plugins/KometaThemes/Bindings/' + encodeURIComponent(item.itemId) + '/unlock').then(function () {
                        KT.ui.toast(KT.t('bindingUnlocked'), 'success');
                        loadBindings();
                    }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
                });
            });
            actionRow.appendChild(unlock);

            var remove = util.el('button', 'kt-btn kt-btn-sm kt-btn-danger', KT.t('removeBinding'));
            remove.type = 'button';
            remove.addEventListener('click', function () {
                var deleteCheckbox = util.el('input', 'kt-checkbox');
                deleteCheckbox.type = 'checkbox';
                deleteCheckbox.checked = true;
                var label = util.el('label', null, KT.t('confirmRemoveBindingDelete'));
                label.style.display = 'flex';
                label.style.gap = '8px';
                label.style.alignItems = 'center';
                label.insertBefore(deleteCheckbox, label.firstChild);

                KT.ui.confirm([
                    util.el('p', null, KT.t('confirmRemoveBinding', { name: item.itemName || item.itemId })),
                    label
                ]).then(function (ok) {
                    if (!ok) { return; }
                    var deleteFiles = deleteCheckbox.checked;
                    KT.api.del('Plugins/KometaThemes/Bindings/' + encodeURIComponent(item.itemId) + '?deleteFiles=' + deleteFiles).then(function () {
                        KT.ui.toast(KT.t('bindingRemoved'), 'success');
                        loadBindings();
                    }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
                });
            });
            actionRow.appendChild(remove);

            actionCell.appendChild(actionRow);
            row.appendChild(actionCell);
            tbody.appendChild(row);
        });
        table.appendChild(tbody);
        wrap.appendChild(table);
    }

    function loadBindings() {
        KT.api.get('Plugins/KometaThemes/Bindings').then(function (items) {
            state.bindings = items || [];
            var badge = q('ktBindingsBadge');
            badge.textContent = String(state.bindings.length);
            badge.style.display = state.bindings.length ? '' : 'none';
            renderBindings('');
        }).catch(function () { renderBindings(''); });
    }

    /* ---- provider priority list ---- */

    function renderProviders() {
        var list = q('ktProviderList');
        util.clear(list);
        state.providers.forEach(function (name, index) {
            var row = util.el('div', 'kt-provider');
            var meta = util.el('div', 'kt-row');
            meta.appendChild(util.el('span', 'kt-provider-rank', String(index + 1)));
            meta.appendChild(util.el('span', 'kt-provider-name', name));
            row.appendChild(meta);
            var controls = util.el('div', 'kt-row');
            var up = util.el('button', 'kt-btn kt-btn-ghost kt-btn-sm', '↑ ' + KT.t('moveUp'));
            var down = util.el('button', 'kt-btn kt-btn-ghost kt-btn-sm', '↓ ' + KT.t('moveDown'));
            up.type = 'button';
            down.type = 'button';
            up.disabled = index === 0;
            down.disabled = index === state.providers.length - 1;
            up.addEventListener('click', function () { moveProvider(index, -1); });
            down.addEventListener('click', function () { moveProvider(index, 1); });
            controls.appendChild(up);
            controls.appendChild(down);
            row.appendChild(controls);
            list.appendChild(row);
        });
    }

    function moveProvider(index, delta) {
        var target = index + delta;
        if (target < 0 || target >= state.providers.length) { return; }
        var moved = state.providers.splice(index, 1)[0];
        state.providers.splice(target, 0, moved);
        renderProviders();
        updateDirty();
    }

    /* ---- dirty tracking / save ---- */

    function snapshot() {
        return JSON.stringify({
            fields: state.fields.map(function (field) { return field.read(); }),
            providers: state.providers
        });
    }

    /* Debounced: this snapshots every field and JSON.stringifies the result, which ran on each
       individual `input` event. */
    function updateDirty() {
        var dirty = state.loadedSnapshot !== snapshot();
        q('ktSaveBar').style.display = dirty ? '' : 'none';
    }

    function applyConfigToForm() {
        state.fields.forEach(function (field) { field.write(getPath(state.config, field.path)); });
        state.providers = (state.config.ProviderPriority || []).slice();
        renderProviders();
        state.loadedSnapshot = snapshot();
        updateDirty();
    }

    function save() {
        if (state.saving) { return; }
        state.saving = true;
        q('ktBtnSave').disabled = true;
        q('ktBtnDiscard').disabled = true;
        KT.ui.loading(true);
        ApiClient.getPluginConfiguration(KT.GUID).then(function (config) {
            state.fields.forEach(function (field) { setPath(config, field.path, field.read()); });
            config.ProviderPriority = state.providers.slice();
            return KT.config.save(config).then(function () { return config; });
        }).then(function (config) {
            state.config = config;
            KT.ui.toast(KT.t('saved'), 'success');
            state.loadedSnapshot = snapshot();
            updateDirty();
        }).catch(function (error) {
            KT.ui.toast(KT.t('saveFailed') + ': ' + (error.message || ''), 'error');
        }).finally(function () {
            state.saving = false;
            q('ktBtnSave').disabled = false;
            q('ktBtnDiscard').disabled = false;
            KT.ui.loading(false);
        });
    }

    /* ---- hero: health / cache / skipped ---- */

    function loadHero() {
        KT.api.get('Plugins/KometaThemes/Health').then(function (health) {
            q('ktVersion').textContent = 'v' + (health.version || KT.VERSION);
            var stat = q('ktStatHealth');
            stat.textContent = health.isRunning ? KT.t('healthRunning') : KT.t('healthOk');
            stat.parentElement.className = 'kt-stat ' + (health.isRunning ? 'warn' : 'good');
            q('ktStatLastSync').textContent = health.lastFullSyncUtc
                ? new Date(health.lastFullSyncUtc).toLocaleDateString()
                : KT.t('never');
            q('ktStatLastSyncSub').textContent = health.lastSyncSummary || '';
            if (health.isRunning) { showSyncProgress(); }
        }).catch(function () {
            var stat = q('ktStatHealth');
            stat.textContent = KT.t('healthDown');
            stat.parentElement.className = 'kt-stat bad';
        });

        loadCacheStats();
        var skippedCount = (state.config.SkippedItems || []).length;
        q('ktStatSkipped').textContent = String(skippedCount);
    }

    function loadCacheStats() {
        KT.api.get('Plugins/KometaThemes/Cache/stats').then(function (stats) {
            q('ktStatCache').textContent = (stats.HitRatePercent != null ? stats.HitRatePercent : 0) + '%';
            q('ktStatCacheSub').textContent = stats.TotalEntries + ' ' + KT.t('cacheEntries').toLowerCase();
            var panelStats = q('ktCacheStats');
            util.clear(panelStats);
            [[KT.t('cacheEntries'), stats.TotalEntries],
             [KT.t('cacheHits'), stats.TotalHits],
             [KT.t('cacheMisses'), stats.TotalMisses],
             [KT.t('cacheHitRate'), stats.HitRatePercent + '%']].forEach(function (pair) {
                var tile = util.el('div', 'kt-stat');
                tile.appendChild(util.el('span', 'kt-stat-label', pair[0]));
                tile.appendChild(util.el('span', 'kt-stat-value', String(pair[1])));
                panelStats.appendChild(tile);
            });
        }).catch(function () { /* tiles stay at … */ });
    }

    function clearCache() {
        KT.ui.confirm(KT.t('confirmClearCache')).then(function (ok) {
            if (!ok) { return; }
            KT.api.post('Plugins/KometaThemes/Cache/clear').then(function () {
                KT.ui.toast(KT.t('cacheCleared'), 'success');
                loadCacheStats();
            }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
        });
    }

    /* ---- skipped items ---- */

    function renderSkipped(filter) {
        var wrap = q('ktSkippedWrap');
        util.clear(wrap);
        var items = state.skipped;
        if (filter) {
            var needle = filter.toLowerCase();
            items = items.filter(function (item) { return (item.itemId + ' ' + item.name).toLowerCase().indexOf(needle) > -1; });
        }
        if (!items.length) {
            wrap.appendChild(util.el('p', 'kt-note', KT.t('skippedEmpty'))).style.padding = '14px';
            return;
        }
        var table = util.el('table', 'kt-table');
        var thead = util.el('thead');
        var headRow = util.el('tr');
        ['colName', 'colType', 'colYear', 'colReason', 'colWhen', 'colActions'].forEach(function (key) {
            headRow.appendChild(util.el('th', null, KT.t(key)));
        });
        thead.appendChild(headRow);
        table.appendChild(thead);
        var tbody = util.el('tbody');
        items.forEach(function (item) {
            var row = util.el('tr');
            row.appendChild(util.el('td', null, item.name || item.itemId));
            row.appendChild(util.el('td', null, item.type || '-'));
            row.appendChild(util.el('td', null, item.productionYear != null ? String(item.productionYear) : '-'));
            row.appendChild(util.el('td', null, item.reason || '-'));
            row.appendChild(util.el('td', null, item.skippedUtc ? new Date(item.skippedUtc).toLocaleDateString() : '-'));
            var actionCell = util.el('td');
            var restore = util.el('button', 'kt-btn kt-btn-sm kt-btn-ghost', KT.t('restore'));
            restore.type = 'button';
            restore.addEventListener('click', function () {
                KT.api.post('Plugins/KometaThemes/Skipped/' + encodeURIComponent(item.itemId) + '/remove').then(function () {
                    KT.ui.toast(KT.t('restored'), 'success');
                    loadSkipped();
                }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
            });
            actionCell.appendChild(restore);
            row.appendChild(actionCell);
            tbody.appendChild(row);
        });
        table.appendChild(tbody);
        wrap.appendChild(table);
    }

    function loadSkipped() {
        KT.api.get('Plugins/KometaThemes/Skipped/items').then(function (items) {
            state.skipped = items || [];
            q('ktStatSkipped').textContent = String(state.skipped.length);
            renderSkipped('');
        }).catch(function () { renderSkipped(''); });
    }

    /* ---- activity log (server log + session messages) ---- */

    function buildLogPanel() {
        var box = q('ktLog');
        box.classList.add('kt-log-split');
        var head = util.el('div', 'kt-log-head');
        var tabs = util.el('div', 'kt-log-tabs');
        var tabServer = util.el('button', 'kt-log-tab active', KT.t('logServer'));
        var tabSession = util.el('button', 'kt-log-tab', KT.t('logSession'));
        tabServer.type = 'button';
        tabSession.type = 'button';
        var btnRefresh = util.el('button', 'kt-btn kt-btn-ghost kt-btn-sm', KT.t('logRefresh'));
        btnRefresh.type = 'button';
        tabs.appendChild(tabServer);
        tabs.appendChild(tabSession);
        head.appendChild(tabs);
        head.appendChild(btnRefresh);
        var server = util.el('div', 'kt-log-body');
        server.id = 'ktLogServer';
        var session = util.el('div', 'kt-log-body');
        session.id = 'ktLogSession';
        session.style.display = 'none';
        box.appendChild(head);
        box.appendChild(server);
        box.appendChild(session);

        function select(which) {
            state.logTab = which;
            tabServer.classList.toggle('active', which === 'server');
            tabSession.classList.toggle('active', which === 'session');
            server.style.display = which === 'server' ? '' : 'none';
            session.style.display = which === 'session' ? '' : 'none';
            if (which === 'server') { fetchServerLog(true); }
        }
        tabServer.addEventListener('click', function () { select('server'); });
        tabSession.addEventListener('click', function () { select('session'); });
        btnRefresh.addEventListener('click', function () { fetchServerLog(true); });
    }

    function fetchServerLog(force) {
        var now = Date.now();
        if (!force && now - state.logFetchedAt < 5000) { return; }
        state.logFetchedAt = now;
        KT.api.get('Plugins/KometaThemes/Logs?lines=200').then(function (data) {
            var box = q('ktLogServer');
            if (!box) { return; }
            util.clear(box);
            var entries = (data && data.entries) || [];
            if (!entries.length) {
                box.appendChild(util.el('div', 'kt-log-empty', KT.t('logEmpty')));
                return;
            }
            entries.forEach(function (entry) {
                var cls = entry.level === 'ERR' || entry.level === 'FTL' ? 'error'
                    : entry.level === 'WRN' ? 'warn' : 'info';
                var time = entry.timestamp ? new Date(entry.timestamp).toLocaleTimeString() : (entry.rawTimestamp || '');
                var category = (entry.category || '').split('.').pop();
                box.appendChild(util.el('div', cls, '[' + time + '] ' + entry.level + ' ' + category + ': ' + entry.message));
            });
            box.scrollTop = box.scrollHeight;
        }).catch(function () {
            var box = q('ktLogServer');
            if (!box) { return; }
            util.clear(box);
            box.appendChild(util.el('div', 'kt-log-empty', KT.t('logLoadFailed')));
        });
    }

    function logPanelOpen() {
        return q('ktLog').style.display !== 'none';
    }

    /* ---- sync actions ---- */

    function log(message, level) {
        // The panel wrapper and the session log are separate nodes here: reveal the wrapper, append
        // into the session box.
        q('ktLog').style.display = '';
        KT.ui.logLine(q('ktLogSession'), message, level);
    }

    function triggerSync(force) {
        if (state.syncStarting) { return; }
        var confirmKey = force ? 'confirmForceSync' : 'confirmSyncNow';
        KT.ui.confirm(KT.t(confirmKey)).then(function (ok) {
            if (!ok) { return; }

            // These two buttons had no disabled state, so repeated clicks fired repeated
            // start requests at the server.
            state.syncStarting = true;
            var buttons = [q('ktBtnSyncNow'), q('ktBtnForceSync')].filter(Boolean);
            buttons.forEach(function (button) { button.disabled = true; });

            var route = force ? 'Plugins/KometaThemes/Sync/force' : 'Plugins/KometaThemes/Sync/sync';
            KT.api.post(route).then(function () {
                log(KT.t('syncRunning'), 'info');
                showSyncProgress();
            }).catch(function (error) {
                log(error.message || KT.t('syncStartFailed'), 'error');
                KT.ui.toast(error.message || KT.t('syncStartFailed'), 'error');
            }).finally(function () {
                state.syncStarting = false;
                buttons.forEach(function (button) { button.disabled = false; });
            });
        });
    }

    /* Cancellable hide, registered with the page lifecycle: the bare setTimeout kept a handle on
       a node that may belong to a page the user already left. */
    function hideProgressLater(progress) {
        var hide = function () { progress.style.display = 'none'; };
        if (state.life) { state.life.timeout(hide, 6000); } else { setTimeout(hide, 6000); }
    }

    function showSyncProgress(onFinished) {
        var progress = q('ktSyncProgress');
        progress.style.display = '';
        if (state.poller) { state.poller.stop(); }
        state.poller = KT.ui.syncPoller(function (status) {
            KT.ui.renderSyncStatus(progress, status);
            if (logPanelOpen() && state.logTab === 'server') { fetchServerLog(false); }
            if (status.isFinished) {
                log(KT.t('syncDone'), 'success');
                if (onFinished) { onFinished(); }
                loadHero();
                loadFailed();
                hideProgressLater(progress);
            }
        });
        state.poller.start();
    }

    /* ---- playlist ---- */

    function refreshPlaylist() {
        KT.api.post('Plugins/KometaThemes/Playlist/refresh').then(function (data) {
            KT.ui.toast((data && data.message) || KT.t('playlistRefreshed'), 'success');
        }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
    }

    function exportPlaylist() {
        var name = (state.config && state.config.PlaylistName) || 'Anime Themes';
        KT.api.download('Plugins/KometaThemes/Playlist/export', name + '.m3u')
            .catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
    }

    /* ---- tabs ---- */

    function selectTab(name, focusTab) {
        if (state.tabs) { state.tabs.select(name, !!focusTab); }
    }

    function bindTabs() {
        state.tabs = KT.a11y.setupTabs(state.page, {
            tabList: q('ktTabs'),
            tabSelector: '.kt-tab',
            panelSelector: '.kt-panel',
            idPrefix: 'ktTab'
        });
    }

    function bindCacheTile() {
        var tile = q('ktStatCache').parentElement;
        tile.classList.add('kt-stat-link');
        tile.setAttribute('role', 'button');
        tile.tabIndex = 0;
        tile.title = KT.t('tabProviders');
        tile.addEventListener('click', function () { selectTab('providers'); });
        tile.addEventListener('keydown', function (event) {
            if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); selectTab('providers', true); }
        });
    }

    /* ---- ui (re)build — shared by first load and live language switch ---- */

    function renderUiText() {
        KT.i18n.apply(state.page);
        q('ktBtnSave').textContent = KT.t('save');
        q('ktBtnDiscard').textContent = KT.t('discard');
        q('ktBtnSyncNow').textContent = KT.t('syncNow');
        q('ktBtnForceSync').textContent = KT.t('forceSync');
        q('ktBtnToggleLog').textContent = KT.t('toggleLog');
        q('ktStatCache').parentElement.title = KT.t('tabProviders');
    }

    function buildAllPanels() {
        state.fields = [];
        [['ktPanelGeneral', buildGeneralPanel], ['ktPanelThemes', buildThemesPanel],
         ['ktPanelProviders', buildProvidersPanel], ['ktPanelLibrary', buildLibraryPanel],
         ['ktPanelBindings', buildBindingsPanel], ['ktPanelFailed', buildFailedPanel]].forEach(function (pair) {
            var host = q(pair[0]);
            util.clear(host);
            pair[1](host);
        });
        util.clear(q('ktLog'));
        buildLogPanel();
    }

    /* ---- entry point ---- */

    function show(page) {
        state.page = page;
        KT.ui.loading(true);
        KT.config.load().then(function (config) {
            state.config = config;

            if (!page.dataset.ktBound) {
                page.dataset.ktBound = '1';
                buildAllPanels();
                renderUiText();
                bindTabs();
                bindCacheTile();
                state.life = KT.ui.lifecycle(page);
                // Discarding these handles left the 15s status poll and the 2s progress poll
                // running for the rest of the dashboard session.
                state.life.add(KT.ui.attachSyncDot(q('ktLiveDot')));
                state.life.add(function () {
                    if (state.poller) { state.poller.stop(); state.poller = null; }
                });
                q('ktBtnSave').addEventListener('click', save);
                q('ktBtnDiscard').addEventListener('click', applyConfigToForm);
                q('ktBtnSyncNow').addEventListener('click', function () { triggerSync(false); });
                q('ktBtnForceSync').addEventListener('click', function () { triggerSync(true); });
                q('ktBtnToggleLog').addEventListener('click', function () {
                    var box = q('ktLog');
                    var opening = box.style.display === 'none';
                    box.style.display = opening ? '' : 'none';
                    if (opening && state.logTab === 'server') { fetchServerLog(true); }
                });
            }

            applyConfigToForm();
            loadHero();
            loadSkipped();
            loadFailed();
            loadBindings();
            KT.ui.loading(false);
        }).catch(function (error) {
            KT.ui.loading(false);
            KT.ui.toast(error.message || KT.t('error'), 'error');
        });
    }

    KT.pages.config = { show: show };
})();
