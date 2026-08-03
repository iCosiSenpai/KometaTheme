/* KometaThemes — item page logic (KT.pages.item). */
(function () {
    'use strict';

    var KT = window.KT;
    var util = KT.util;

    KT.i18n.extend({
        en: {
            itemThemes: 'Item themes',
            downloadedThemes: 'Downloaded themes',
            noThemes: 'No themes downloaded yet. Use Sync or the Theme Finder to fetch some.',
            noItemTitle: 'No item selected',
            noItemBody: 'Open this page from a series or movie: use the ♪ button on the item detail page, or run a library preset below.',
            libraryPresets: 'Library presets',
            libraryPresetsNote: 'Bulk download across every matching library. Runs in the background.',
            presetAllOP: 'All OP (audio)', presetAllED: 'All ED (audio)',
            presetOPED: 'OP + ED (audio)', presetOPEDVideo: 'OP + ED + video',
            regOk: '{songs} songs / {videos} videos registered in Jellyfin',
            regMismatch: '{disk} files on disk but only {reg} registered in Jellyfin — themes may not play',
            repairLinks: 'Repair links',
            repaired: 'Links repaired: {repaired} fixed, {songs} songs / {videos} videos now registered',
            repairNeedsScan: '{count} files are not in the library yet — run a library scan, then repair again',
            syncItem: 'Sync this item',
            manualBinding: 'Manual binding: {anime}',
            confirmDeleteAll: 'Delete ALL downloaded themes for this item?',
            confirmDelete: 'Delete {name}?',
            confirmPreset: 'Run "{name}" on all matching libraries now?',
            themesDeleted: 'Themes deleted',
            syncCompleted: 'Sync completed',
            season: 'Season',
            nonAnimeWarning: 'This item does not appear to be in an anime library matching your Library Pattern. KometaThemes features may be limited.'
        },
        it: {
            itemThemes: 'Temi elemento',
            downloadedThemes: 'Temi scaricati',
            noThemes: 'Nessun tema scaricato. Usa il Sync o il Theme Finder per scaricarne.',
            noItemTitle: 'Nessun elemento selezionato',
            noItemBody: 'Apri questa pagina da una serie o un film: usa il pulsante ♪ nella pagina dei dettagli, oppure esegui un preset libreria qui sotto.',
            libraryPresets: 'Preset libreria',
            libraryPresetsNote: 'Download massivo su tutte le librerie corrispondenti. Gira in background.',
            presetAllOP: 'Tutti gli OP (audio)', presetAllED: 'Tutti gli ED (audio)',
            presetOPED: 'OP + ED (audio)', presetOPEDVideo: 'OP + ED + video',
            regOk: '{songs} sigle / {videos} video registrati in Jellyfin',
            regMismatch: '{disk} file su disco ma solo {reg} registrati in Jellyfin — i temi potrebbero non riprodursi',
            repairLinks: 'Ripara collegamenti',
            repaired: 'Collegamenti riparati: {repaired} corretti, ora {songs} sigle / {videos} video registrati',
            repairNeedsScan: '{count} file non sono ancora in libreria — esegui una scansione, poi ripara di nuovo',
            syncItem: 'Sincronizza elemento',
            manualBinding: 'Binding manuale: {anime}',
            confirmDeleteAll: 'Eliminare TUTTI i temi scaricati per questo elemento?',
            nonAnimeWarning: 'Questo elemento non sembra appartenere a una libreria anime che corrisponde al tuo Library Pattern. Le funzionalità di KometaThemes potrebbero essere limitate.',
            confirmDelete: 'Eliminare {name}?',
            confirmPreset: 'Eseguire "{name}" su tutte le librerie corrispondenti?',
            themesDeleted: 'Temi eliminati',
            syncCompleted: 'Sincronizzazione completata',
            season: 'Stagione'
        }
    });

    var PRESETS = [
        { key: 'AllOPAudio', label: 'presetAllOP' },
        { key: 'AllEDAudio', label: 'presetAllED' },
        { key: 'AllOPEDAudio', label: 'presetOPED' },
        { key: 'AllOPEDAudioVideo', label: 'presetOPEDVideo' }
    ];

    var state = { page: null, itemId: null, poller: null, life: null, scoped: null, requestId: 0, busy: false };

    function q(id) { return (state.scoped || util.scoped(state.page))(id); }

    function setState(message, type) {
        var node = q('ktItemState');
        node.textContent = message || '';
        node.className = 'kt-state' + (type ? ' ' + type : '');
    }

    function setBusy(value) {
        state.busy = !!value;
        KT.a11y.setBusy(q('ktItemActions'), state.busy);
        ['ktBtnSyncItem', 'ktBtnRefresh', 'ktBtnDeleteAll'].forEach(function (id) {
            var button = q(id);
            if (button) { button.disabled = state.busy; }
        });
    }

    /* ---- item identity ---- */

    function loadIdentity() {
        var itemId = state.itemId;
        ApiClient.getItem(ApiClient.getCurrentUserId(), itemId).then(function (item) {
            if (itemId !== state.itemId) { return; }
            q('ktItemTitle').textContent = item.Name;
            var chips = q('ktItemChips');
            util.clear(chips);
            [item.Type, item.ProductionYear].forEach(function (value) {
                if (value) { chips.appendChild(util.el('span', 'kt-chip', String(value))); }
            });

            util.applyItemImages(item, q('ktItemPoster'), q('ktItemBackdrop'));
        }).catch(function () {
            if (itemId === state.itemId) { q('ktItemTitle').textContent = itemId; }
        });
    }

    function checkEligibility() {
        var itemId = state.itemId;
        KT.api.get('Plugins/KometaThemes/Items/' + encodeURIComponent(itemId) + '/eligible').then(function (res) {
            // Guard against a slow response for a previously selected item painting this one.
            if (itemId !== state.itemId) { return; }
            if (res && res.eligible === false) {
                var warning = q('ktEligibilityWarning');
                if (!warning) {
                    warning = util.el('div', 'kt-warning');
                    warning.id = 'ktEligibilityWarning';
                    warning.style.margin = '12px 0';
                    warning.style.padding = '10px';
                    warning.style.background = 'var(--kt-warn-bg, rgba(251,191,36,.1))';
                    warning.style.border = '1px solid var(--kt-warn, #fbbf24)';
                    warning.style.borderRadius = '6px';
                    var msg = KT.t && KT.t('nonAnimeWarning') || 'This item does not appear to be in an anime library matching your Library Pattern. KometaThemes features may be limited or not apply.';
                    warning.appendChild(util.el('span', null, msg));
                    var container = q('ktItemChips').parentNode;
                    container.insertBefore(warning, container.firstChild);
                }
            }
        }).catch(function () { /* ignore if endpoint fails */ });
    }

    /* ---- registration banner (Jellyfin 10.11.x link bug) ---- */

    function loadRegistration() {
        var itemId = state.itemId;
        var banner = q('ktRegBanner');
        util.clear(banner);
        KT.api.get('Plugins/KometaThemes/Items/' + encodeURIComponent(itemId) + '/info').then(function (info) {
            if (itemId !== state.itemId) { return; }
            var status = info && info.themeStatus;
            if (!status) { return; }
            var disk = status.songsOnDisk + status.videosOnDisk;
            var registered = status.registeredSongs + status.registeredVideos;
            if (disk === 0) { return; }

            if (registered >= disk) {
                banner.appendChild(util.el('span', 'kt-badge success',
                    KT.t('regOk', { songs: status.registeredSongs, videos: status.registeredVideos })));
                return;
            }

            var warn = util.el('div', 'kt-row');
            warn.appendChild(util.el('span', 'kt-badge warn',
                KT.t('regMismatch', { disk: disk, reg: registered })));
            var btn = util.el('button', 'kt-btn kt-btn-sm', KT.t('repairLinks'));
            btn.type = 'button';
            btn.addEventListener('click', repairLinks);
            warn.appendChild(btn);
            banner.appendChild(warn);
        }).catch(function () { /* banner is best-effort */ });
    }

    function repairLinks() {
        KT.ui.loading(true);
        KT.api.post('Plugins/KometaThemes/Items/' + encodeURIComponent(state.itemId) + '/repair').then(function (result) {
            KT.ui.loading(false);
            KT.ui.toast(KT.t('repaired', {
                repaired: result.repaired,
                songs: result.registeredSongs,
                videos: result.registeredVideos
            }), 'success', 5200);
            if (result.notScanned > 0) {
                KT.ui.toast(KT.t('repairNeedsScan', { count: result.notScanned }), 'error', 6500);
            }
            loadRegistration();
        }).catch(function (error) {
            KT.ui.loading(false);
            KT.ui.toast(error.message || KT.t('error'), 'error');
        });
    }

    function loadBinding() {
        var existing = state.page.querySelector('.kt-binding-banner');
        if (existing) { existing.remove(); }
        if (!state.itemId) { return; }
        var itemId = state.itemId;
        KT.api.get('Plugins/KometaThemes/Items/' + encodeURIComponent(itemId) + '/binding').then(function (data) {
            if (itemId !== state.itemId) { return; }
            if (!data || !data.hasBinding) { return; }
            var banner = util.el('div', 'kt-binding-banner');
            banner.appendChild(util.el('span', 'kt-badge success', KT.t('manualBinding', { anime: data.animeName })));
            var title = q('ktItemTitle');
            if (title && title.parentElement) {
                title.parentElement.insertBefore(banner, title.nextSibling);
            }
        }).catch(function () { /* optional */ });
    }

    /* ---- theme list ---- */

    function loadThemes() {
        var requestId = ++state.requestId;
        var itemId = state.itemId;
        var list = q('ktThemesList');
        util.clear(list);
        list.setAttribute('aria-busy', 'true');
        var skeletonRow = util.el('div', 'kt-theme');
        var skeleton = util.el('div', 'kt-skel');
        skeleton.style.minHeight = '30px';
        skeletonRow.appendChild(skeleton);
        list.appendChild(skeletonRow);

        KT.api.get('Plugins/KometaThemes/Items/' + encodeURIComponent(itemId) + '/themes').then(function (themes) {
            if (requestId !== state.requestId || itemId !== state.itemId) { return; }
            list.removeAttribute('aria-busy');
            util.clear(list);
            themes = themes || [];
            q('ktThemesCount').textContent = String(themes.length);
            if (!themes.length) {
                var empty = util.el('div', 'kt-theme');
                empty.appendChild(util.el('p', 'kt-note', KT.t('noThemes')));
                list.appendChild(empty);
                return;
            }

            themes.sort(function (a, b) {
                if (a.Type !== b.Type) { return a.Type === 'OP' ? -1 : 1; }
                return (a.Sequence || 0) - (b.Sequence || 0);
            });

            themes.forEach(function (theme) {
                var row = util.el('div', 'kt-theme');
                var type = (theme.Type || 'OP').toUpperCase();
                row.appendChild(util.el('span', 'kt-type-chip ' + type.toLowerCase(), type + (theme.Sequence || '')));

                var main = util.el('div', 'kt-theme-main');
                main.appendChild(util.el('div', 'kt-theme-title', theme.Slug || theme.FileName));
                main.appendChild(util.el('div', 'kt-theme-sub', theme.FileName));
                var chips = util.el('div', 'kt-theme-chips');
                if (theme.SeasonNumber) {
                    chips.appendChild(util.el('span', 'kt-chip', KT.t('season') + ' ' + theme.SeasonNumber));
                }
                chips.appendChild(util.el('span', 'kt-badge ' + (theme.Exists ? 'success' : 'danger'),
                    theme.Exists ? KT.t('downloaded') : KT.t('missing')));
                main.appendChild(chips);
                row.appendChild(main);

                var actions = util.el('div', 'kt-theme-actions');
                var del = util.el('button', 'kt-btn kt-btn-danger kt-btn-sm', KT.t('delete'));
                del.type = 'button';
                del.addEventListener('click', function () {
                    KT.ui.confirm(KT.t('confirmDelete', { name: theme.FileName })).then(function (ok) {
                        if (!ok) { return; }
                        del.disabled = true;
                        KT.api.del('Plugins/KometaThemes/Items/' + encodeURIComponent(state.itemId) + '/themes?fileName=' + encodeURIComponent(theme.FileName))
                            .then(function () {
                                KT.ui.toast(KT.t('themesDeleted'), 'success');
                                loadThemes();
                                loadRegistration();
                            })
                            .catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); })
                            .finally(function () { del.disabled = false; });
                    });
                });
                actions.appendChild(del);
                row.appendChild(actions);
                list.appendChild(row);
            });
        }).catch(function (error) {
            if (requestId !== state.requestId || itemId !== state.itemId) { return; }
            list.removeAttribute('aria-busy');
            util.clear(list);
            var fail = util.el('div', 'kt-theme');
            fail.appendChild(util.el('p', 'kt-state error', error.message || KT.t('error')));
            list.appendChild(fail);
        });
    }

    /* ---- actions ---- */

    function syncItem() {
        if (state.busy) { return; }
        setBusy(true);
        setState(KT.t('syncRunning'));
        KT.ui.loading(true);
        KT.api.post('Plugins/KometaThemes/Items/' + encodeURIComponent(state.itemId) + '/sync').then(function (data) {
            setState((data && data.message) || KT.t('syncCompleted'), 'success');
            loadThemes();
            loadRegistration();
        }).catch(function (error) {
            setState(error.message || KT.t('error'), 'error');
        }).finally(function () {
            setBusy(false);
            KT.ui.loading(false);
        });
    }

    function deleteAll() {
        if (state.busy) { return; }
        KT.ui.confirm(KT.t('confirmDeleteAll')).then(function (ok) {
            if (!ok) { return; }
            setBusy(true);
            KT.ui.loading(true);
            KT.api.del('Plugins/KometaThemes/Items/' + encodeURIComponent(state.itemId) + '/themes').then(function () {
                KT.ui.toast(KT.t('themesDeleted'), 'success');
                loadThemes();
                loadRegistration();
            }).catch(function (error) {
                KT.ui.toast(error.message || KT.t('error'), 'error');
            }).finally(function () {
                setBusy(false);
                KT.ui.loading(false);
            });
        });
    }

    function runPreset(preset, label) {
        KT.ui.confirm(KT.t('confirmPreset', { name: label })).then(function (ok) {
            if (!ok) { return; }
            KT.api.post('Plugins/KometaThemes/Sync/run', { preset: preset }).then(function () {
                showSyncProgress();
            }).catch(function (error) {
                KT.ui.toast(error.message || KT.t('syncStartFailed'), 'error');
            });
        });
    }

    function showSyncProgress() {
        var progress = q('ktSyncProgress');
        progress.style.display = '';
        if (state.poller) { state.poller.stop(); }
        state.poller = KT.ui.syncPoller(function (status) {
            KT.ui.renderSyncStatus(progress, status);
            if (status.isFinished) {
                var hide = function () { progress.style.display = 'none'; };
                if (state.life) { state.life.timeout(hide, 6000); } else { setTimeout(hide, 6000); }
                loadThemes();
                loadRegistration();
            }
        });
        state.poller.start();
    }

    function renderPresets() {
        var row = q('ktPresetRow');
        util.clear(row);
        PRESETS.forEach(function (preset) {
            var btn = util.el('button', 'kt-btn kt-btn-ghost', KT.t(preset.label));
            btn.type = 'button';
            btn.addEventListener('click', function () { runPreset(preset.key, KT.t(preset.label)); });
            row.appendChild(btn);
        });
    }

    /* ---- entry point (re-entrant: dashboard re-shows cached pages) ---- */

    function show(page) {
        state.page = page;
        state.scoped = util.scoped(page);
        var nextItemId = util.getItemId();
        var itemChanged = nextItemId !== state.itemId;
        state.itemId = nextItemId;
        if (itemChanged) {
            state.requestId++;
            setState('');
            var poster = q('ktItemPoster');
            util.clear(poster);
            poster.appendChild(util.el('div', 'kt-poster-ph', '♪'));
            var backdrop = q('ktItemBackdrop');
            backdrop.style.display = 'none';
            backdrop.style.backgroundImage = '';
            var warning = q('ktEligibilityWarning');
            if (warning) { warning.remove(); }
        }

        KT.config.load().catch(function () { return null; }).then(function () {
            KT.i18n.apply(page);
            q('ktBtnSyncItem').textContent = KT.t('syncItem');
            q('ktBtnFinder').textContent = KT.t('openThemeFinder');
            q('ktBtnRefresh').textContent = KT.t('refresh');
            q('ktBtnDeleteAll').textContent = KT.t('deleteAll');
            q('ktBtnGoSettings').textContent = KT.t('settings');
            renderPresets();

            var hasItem = !!state.itemId;
            q('ktNoItem').style.display = hasItem ? 'none' : '';
            q('ktThemesCard').style.display = hasItem ? '' : 'none';
            q('ktItemActions').style.display = hasItem ? '' : 'none';

            if (!page.dataset.ktBound) {
                page.dataset.ktBound = '1';
                state.life = KT.ui.lifecycle(page);
                state.life.add(KT.ui.attachSyncDot(q('ktLiveDot')));
                state.life.add(function () {
                    if (state.poller) { state.poller.stop(); state.poller = null; }
                });
                q('ktBtnSyncItem').addEventListener('click', syncItem);
                q('ktBtnRefresh').addEventListener('click', function () { loadThemes(); loadRegistration(); });
                q('ktBtnDeleteAll').addEventListener('click', deleteAll);
                q('ktBtnFinder').addEventListener('click', function () {
                    util.navigate('configurationpage?name=KometaThemesSearch&itemId=' + encodeURIComponent(state.itemId));
                });
                q('ktBtnGoSettings').addEventListener('click', function () {
                    util.navigate('configurationpage?name=KometaThemes');
                });
            }

            if (hasItem) {
                loadIdentity();
                loadRegistration();
                loadBinding();
                checkEligibility();
                loadThemes();
            } else {
                q('ktItemTitle').textContent = KT.t('noItemTitle');
            }

            KT.api.get('Plugins/KometaThemes/Sync/status').then(function (status) {
                if (status && !status.isFinished && (status.totalItems > 0 || status.processedItems > 0)) {
                    showSyncProgress();
                }
            }).catch(function () { /* optional */ });
        }).catch(function (error) {
            // Terminal handler: without it a failure inside the chain above became an unhandled
            // rejection and the page silently stayed on its loading skeleton.
            try {
                KT.ui.toast((error && error.message) || KT.t('error'), 'error');
            } catch (e) { /* toast host unavailable */ }
        });
    }

    KT.pages.item = { show: show };
})();
