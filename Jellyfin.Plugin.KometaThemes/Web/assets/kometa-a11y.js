/* KometaThemes accessibility primitives shared by page modules. */
(function (global) {
    'use strict';

    var KT = global.KT;
    if (!KT) { throw new Error('KometaThemes core must load before accessibility helpers'); }

    function setupTabs(scope, options) {
        options = options || {};
        var tabList = options.tabList || scope;
        var selector = options.tabSelector || '[role="tab"]';
        var tabs = Array.prototype.slice.call(tabList.querySelectorAll(selector));
        var panelSelector = options.panelSelector || '[role="tabpanel"]';
        var panels = Array.prototype.slice.call(scope.querySelectorAll(panelSelector));
        var prefix = options.idPrefix || 'kt-tab';

        tabList.setAttribute('aria-orientation', options.orientation || 'horizontal');

        function select(name, focus, notify) {
            tabs.forEach(function (tab) {
                var selected = tab.dataset.panel === name;
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
                tab.tabIndex = selected ? 0 : -1;
                if (selected && focus) { tab.focus(); }
            });
            panels.forEach(function (panel) {
                var selected = panel.dataset.panel === name;
                panel.classList.toggle('active', selected);
                panel.hidden = !selected;
            });
            if (notify !== false && typeof options.onChange === 'function') { options.onChange(name); }
        }

        tabs.forEach(function (tab, index) {
            var panel = panels.filter(function (candidate) {
                return candidate.dataset.panel === tab.dataset.panel;
            })[0];
            tab.id = tab.id || (prefix + '-' + tab.dataset.panel);
            if (panel) {
                panel.setAttribute('role', 'tabpanel');
                panel.setAttribute('aria-labelledby', tab.id);
                tab.setAttribute('aria-controls', panel.id);
            }
            tab.addEventListener('click', function () { select(tab.dataset.panel, false); });
            tab.addEventListener('keydown', function (event) {
                var next = index;
                if (event.key === 'ArrowRight' || event.key === 'ArrowDown') { next = (index + 1) % tabs.length; }
                else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') { next = (index - 1 + tabs.length) % tabs.length; }
                else if (event.key === 'Home') { next = 0; }
                else if (event.key === 'End') { next = tabs.length - 1; }
                else { return; }
                event.preventDefault();
                select(tabs[next].dataset.panel, true);
            });
        });

        var active = tabs.filter(function (tab) { return tab.getAttribute('aria-selected') === 'true'; })[0];
        if (tabs.length) { select(active ? active.dataset.panel : tabs[0].dataset.panel, false, false); }
        return { select: select };
    }

    function setBusy(node, busy) {
        if (!node) { return; }
        node.setAttribute('aria-busy', busy ? 'true' : 'false');
    }

    function announce(root, message, urgent) {
        if (!root) { return; }
        var region = root.querySelector('[data-kt-announcer]');
        if (!region) {
            region = document.createElement('div');
            region.className = 'kt-sr-only';
            region.setAttribute('data-kt-announcer', '');
            region.setAttribute('aria-live', urgent ? 'assertive' : 'polite');
            region.setAttribute('aria-atomic', 'true');
            root.appendChild(region);
        }
        region.setAttribute('aria-live', urgent ? 'assertive' : 'polite');
        region.textContent = '';
        setTimeout(function () { region.textContent = message || ''; }, 0);
    }

    /* Keyboard behaviour for a single-select listbox of option elements.
       The Theme Finder had two ~95% identical copies of this, differing only in the container id
       and which state field held the active index. */
    function setupListbox(container, options) {
        if (!container || container.dataset.ktKeys === '1') { return null; }
        container.dataset.ktKeys = '1';

        options = options || {};
        var itemSelector = options.itemSelector || '[role="option"]';

        container.setAttribute('role', 'listbox');
        container.setAttribute('aria-multiselectable', 'false');
        if (options.label) { container.setAttribute('aria-label', options.label); }

        // One ARIA model, not two: focus stays on the container and the active option is named by
        // aria-activedescendant. The previous code did both — it set aria-activedescendant *and*
        // called focus() on the option, which immediately moved focus off the container that the
        // arrow-key handler was bound to.
        container.setAttribute('tabindex', '0');

        function items() {
            return Array.prototype.slice.call(container.querySelectorAll(itemSelector));
        }

        function activate(index) {
            if (typeof options.onActivate === 'function') { options.onActivate(index); }
        }

        container.addEventListener('keydown', function (event) {
            var list = items();
            if (!list.length) { return; }

            var current = typeof options.getIndex === 'function' ? options.getIndex() : -1;
            var next = null;

            if (event.key === 'ArrowDown') { next = current < 0 ? 0 : (current + 1) % list.length; }
            else if (event.key === 'ArrowUp') { next = current <= 0 ? list.length - 1 : current - 1; }
            else if (event.key === 'Home') { next = 0; }
            else if (event.key === 'End') { next = list.length - 1; }
            else if (event.key === 'Escape') { next = -1; }
            else if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                if (current >= 0 && list[current] && typeof options.onChoose === 'function') {
                    options.onChoose(list[current], current);
                }

                return;
            } else {
                return;
            }

            event.preventDefault();
            activate(next);
        });

        container.addEventListener('focus', function () {
            if (typeof options.getIndex === 'function' && options.getIndex() < 0 && items().length) {
                activate(0);
            }
        });

        // Clicking an option keeps the active index in step, so mixing mouse and keyboard works.
        container.addEventListener('click', function (event) {
            var option = event.target.closest(itemSelector);
            if (!option) { return; }
            var index = items().indexOf(option);
            if (index >= 0) { activate(index); }
        }, true);

        return { items: items };
    }

    KT.a11y = { setupTabs: setupTabs, setupListbox: setupListbox, setBusy: setBusy, announce: announce };
})(window);
