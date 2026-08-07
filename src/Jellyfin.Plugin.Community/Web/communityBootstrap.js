(() => {
    'use strict';

    const VERSION = '1.1.0.0';
    const MENU_ATTRIBUTE = 'data-jellyfin-community-menu';
    const COMMUNITY_PAGE_ID = 'CommunityPage';
    let mutationScheduled = false;

    function navigateToCommunity(event) {
        if (event) {
            event.preventDefault();
        }

        if (window.Dashboard?.navigate && window.Dashboard?.getPluginUrl) {
            window.Dashboard.navigate(window.Dashboard.getPluginUrl('Community'));
            return;
        }

        window.location.hash = '#/configurationpage?name=Community';
    }

    function createMenuLink() {
        const link = document.createElement('a');
        link.setAttribute('is', 'emby-linkbutton');
        link.setAttribute(MENU_ATTRIBUTE, 'true');
        link.setAttribute('data-itemid', 'community');
        link.className = 'navMenuOption lnkMediaFolder jellyfinCommunityMenuOption';
        link.href = '#/configurationpage?name=Community';
        link.addEventListener('click', navigateToCommunity);

        const icon = document.createElement('span');
        icon.className = 'material-icons navMenuOptionIcon forum';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = 'forum';
        link.appendChild(icon);

        const label = document.createElement('span');
        label.className = 'navMenuOptionText';
        label.textContent = 'Comunidad';
        link.appendChild(label);
        return link;
    }

    function ensureMenuLinks() {
        document.querySelectorAll('.customMenuOptions').forEach(container => {
            if (!container.querySelector(`[${MENU_ATTRIBUTE}]`)) {
                container.appendChild(createMenuLink());
            }
        });
        updateSelectedState();
    }

    function updateSelectedState(page) {
        const isCommunity = page?.id === COMMUNITY_PAGE_ID
            || document.querySelector(`#${COMMUNITY_PAGE_ID}:not(.hide)`) !== null;
        document.querySelectorAll(`[${MENU_ATTRIBUTE}]`).forEach(link => {
            link.classList.toggle('navMenuOption-selected', isCommunity);
        });
    }

    function scheduleEnsureMenuLinks() {
        if (mutationScheduled) {
            return;
        }

        mutationScheduled = true;
        queueMicrotask(() => {
            mutationScheduled = false;
            ensureMenuLinks();
        });
    }

    function start() {
        ensureMenuLinks();
        const observer = new MutationObserver(scheduleEnsureMenuLinks);
        observer.observe(document.documentElement, { childList: true, subtree: true });

        document.addEventListener('viewshow', event => updateSelectedState(event.target));
        document.addEventListener('pageshow', event => updateSelectedState(event.target));

        window.JellyfinCommunityBootstrap = Object.freeze({
            version: VERSION,
            ensureMenuLinks,
            navigateToCommunity
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
