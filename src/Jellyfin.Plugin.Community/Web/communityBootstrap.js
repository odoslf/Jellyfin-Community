(() => {
    'use strict';

    const VERSION = '1.1.0.0';
    const MENU_ATTRIBUTE = 'data-jellyfin-community-menu';
    const COMMUNITY_PAGE_ID = 'CommunityPage';
    const API_SEGMENT = '/Community/api/v1/';
    let mutationScheduled = false;

    function normalizeCommunityJson(value) {
        if (Array.isArray(value)) {
            return value.map(normalizeCommunityJson);
        }
        if (!value || typeof value !== 'object' || value instanceof Date || value instanceof Blob) {
            return value;
        }
        const normalized = {};
        for (const [key, child] of Object.entries(value)) {
            const normalizedKey = key.length && key[0] >= 'A' && key[0] <= 'Z'
                ? key[0].toLowerCase() + key.slice(1)
                : key;
            normalized[normalizedKey] = normalizeCommunityJson(child);
        }
        return normalized;
    }

    function installCommunityAjaxAdapter() {
        if (!window.ApiClient || ApiClient.__communityAjaxAdapterInstalled) {
            return;
        }

        const originalAjax = ApiClient.ajax.bind(ApiClient);
        ApiClient.ajax = async function communityAwareAjax(request, includeAuthorization) {
            if (!request?.url || !request.url.includes(API_SEGMENT)) {
                return originalAjax(request, includeAuthorization);
            }

            const headers = { ...(request.headers || {}) };
            if (includeAuthorization !== false && typeof ApiClient.setRequestHeaders === 'function') {
                ApiClient.setRequestHeaders(headers);
            }
            if (request.dataType === 'json') {
                headers.Accept = 'application/json';
            }
            if (request.contentType) {
                headers['Content-Type'] = request.contentType;
            }

            const response = await fetch(request.url, {
                method: request.type || 'GET',
                headers,
                body: request.data ?? undefined,
                credentials: 'same-origin'
            });

            if (!response.ok) {
                let message = response.statusText || `HTTP ${response.status}`;
                try {
                    const text = await response.text();
                    if (text) {
                        const body = JSON.parse(text);
                        message = body.Message || body.message || message;
                    }
                } catch (_) {
                    // Keep the HTTP status text when the body is not JSON.
                }
                const error = new Error(message);
                error.status = response.status;
                error.statusText = response.statusText;
                throw error;
            }

            if (response.status === 204 || response.status === 205) {
                return null;
            }

            const text = await response.text();
            if (!text) {
                return null;
            }

            const contentType = response.headers.get('content-type') || '';
            if (request.dataType === 'json' || contentType.includes('application/json')) {
                return normalizeCommunityJson(JSON.parse(text));
            }
            return text;
        };
        Object.defineProperty(ApiClient, '__communityAjaxAdapterInstalled', {
            value: true,
            enumerable: false,
            configurable: false,
            writable: false
        });
    }

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
        installCommunityAjaxAdapter();
        ensureMenuLinks();
        const observer = new MutationObserver(scheduleEnsureMenuLinks);
        observer.observe(document.documentElement, { childList: true, subtree: true });

        document.addEventListener('viewshow', event => updateSelectedState(event.target));
        document.addEventListener('pageshow', event => updateSelectedState(event.target));

        window.JellyfinCommunityBootstrap = Object.freeze({
            version: VERSION,
            ensureMenuLinks,
            navigateToCommunity,
            normalizeCommunityJson
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
