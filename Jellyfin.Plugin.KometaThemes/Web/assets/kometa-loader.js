/* KometaThemes asset loader — shared by the three Jellyfin page shells. */
(function (global) {
    'use strict';

    var VERSION = '1.3.0.0';

    function asset(name) {
        return 'configurationpage?name=' + encodeURIComponent(name) + '&v=' + VERSION;
    }

    function report(page, message) {
        var host = page && page.querySelector('.kt-shell');
        if (!host || host.querySelector('[data-kt-loader-error]')) { return; }
        var alert = document.createElement('div');
        alert.className = 'kt-state error';
        alert.setAttribute('role', 'alert');
        alert.setAttribute('data-kt-loader-error', '');
        alert.textContent = message;
        host.insertBefore(alert, host.firstChild);
    }

    function ensureCss(done, fail) {
        var id = 'kt-css-' + VERSION;
        var existing = document.getElementById(id);
        if (existing) {
            if (existing.dataset.loaded === '1' || existing.sheet) { done(); }
            else if (existing.dataset.failed === '1') { fail('KometaThemesCss'); }
            else {
                existing.addEventListener('load', done, { once: true });
                existing.addEventListener('error', function () { fail('KometaThemesCss'); }, { once: true });
            }
            return;
        }

        var link = document.createElement('link');
        link.id = id;
        link.rel = 'stylesheet';
        link.href = asset('KometaThemesCss');
        link.addEventListener('load', function () {
            link.dataset.loaded = '1';
            done();
        }, { once: true });
        link.addEventListener('error', function () {
            link.dataset.failed = '1';
            fail('KometaThemesCss');
        }, { once: true });
        document.head.appendChild(link);
    }

    function ensureScript(name, done, fail) {
        var id = 'kt-js-' + name + '-' + VERSION;
        var existing = document.getElementById(id);
        if (existing) {
            if (existing.dataset.loaded === '1') { done(); }
            else if (existing.dataset.failed === '1') { fail(name); }
            else {
                existing.addEventListener('load', done, { once: true });
                existing.addEventListener('error', function () { fail(name); }, { once: true });
            }
            return;
        }

        var script = document.createElement('script');
        script.id = id;
        script.src = asset(name);
        script.addEventListener('load', function () {
            script.dataset.loaded = '1';
            done();
        }, { once: true });
        script.addEventListener('error', function () {
            script.dataset.failed = '1';
            fail(name);
        }, { once: true });
        document.head.appendChild(script);
    }

    function load(page, scripts, ready) {
        var index = 0;
        var failed = false;

        function fail(name) {
            if (failed) { return; }
            failed = true;
            report(page, 'KometaThemes could not load ' + name + '. Refresh the dashboard and try again.');
        }

        function next() {
            if (failed) { return; }
            if (index >= scripts.length) {
                try { ready(); } catch (error) { fail(error && error.message ? error.message : 'page module'); }
                return;
            }
            ensureScript(scripts[index++], next, fail);
        }

        ensureCss(next, fail);
    }

    global.KTLoader = { VERSION: VERSION, load: load };
})(window);
