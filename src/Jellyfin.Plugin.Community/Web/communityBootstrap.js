(() => {
    'use strict';

    const VERSION = '1.5.0.0';
    const MARKER = 'data-jellyfin-community-menu';
    const currentScript = document.currentScript;
    const forumUrl = currentScript?.src
        ? new URL('../app?v=' + VERSION, currentScript.src).href
        : new URL('../Community/app?v=' + VERSION, document.baseURI).href;
    let scheduled = false;

    function isForumLink(anchor) {
        if (!(anchor instanceof HTMLAnchorElement)) return false;
        try {
            return new URL(anchor.href, location.href).pathname.toLowerCase().endsWith('/community/app');
        } catch (_) {
            return false;
        }
    }

    function openInCurrentWebView(event) {
        event.preventDefault();
        event.stopPropagation();
        location.assign(event.currentTarget.href);
    }

    function prepareLink(anchor) {
        if (anchor.hasAttribute(MARKER)) return;
        anchor.setAttribute(MARKER, VERSION);
        anchor.target = '_self';
        anchor.removeAttribute('rel');
        anchor.addEventListener('click', openInCurrentWebView, { capture: true });
    }

    function createFallback(container) {
        if (!(container instanceof HTMLElement)
            || [...container.querySelectorAll('a[href]')].some(isForumLink)) return;
        const anchor = document.createElement('a');
        anchor.className = 'navMenuOption lnkMediaFolder';
        anchor.href = forumUrl;
        anchor.dataset.jellyfinCommunityFallback = VERSION;

        const icon = document.createElement('span');
        icon.className = 'material-icons navMenuOptionIcon forum';
        icon.setAttribute('aria-hidden', 'true');
        anchor.appendChild(icon);

        const label = document.createElement('span');
        label.className = 'navMenuOptionText';
        label.textContent = 'Foro';
        anchor.appendChild(label);
        container.prepend(anchor);
        prepareLink(anchor);
    }

    function refreshMenu() {
        scheduled = false;
        const forumLinks = [...document.querySelectorAll('a[href]')].filter(isForumLink);
        const officialLink = forumLinks.find(anchor => !anchor.dataset.jellyfinCommunityFallback);
        if (officialLink) {
            forumLinks
                .filter(anchor => anchor.dataset.jellyfinCommunityFallback)
                .forEach(anchor => anchor.remove());
        }
        [...document.querySelectorAll('a[href]')].filter(isForumLink).forEach(prepareLink);
        document.querySelectorAll('.customMenuOptions').forEach(createFallback);
    }

    function scheduleRefresh() {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(refreshMenu);
    }

    const observer = new MutationObserver(scheduleRefresh);
    observer.observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('viewshow', scheduleRefresh);
    document.addEventListener('pageshow', scheduleRefresh);
    scheduleRefresh();

    window.JellyfinCommunityBootstrap = Object.freeze({ VERSION, forumUrl, refreshMenu });
})();
