(() => {
    'use strict';

    const VERSION = '1.3.0.0';
    const MENU_ATTRIBUTE = 'data-jellyfin-community-menu';
    const COMMUNITY_PAGE_ID = 'CommunityPage';
    const API_SEGMENT = '/Community/api/v1/';
    let mutationScheduled = false;
    let communityPage = null;
    let previousPageState = [];
    let openingPromise = null;

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
            return Boolean(window.ApiClient?.__communityAjaxAdapterInstalled);
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
                credentials: 'same-origin',
                cache: 'no-store'
            });

            if (!response.ok) {
                const text = await response.text().catch(() => '');
                let message = response.statusText || `HTTP ${response.status}`;
                if (text) {
                    try {
                        const body = normalizeCommunityJson(JSON.parse(text));
                        message = body.message || body.detail || body.title || message;
                    } catch (_) {
                        if (text.length <= 500) {
                            message = text;
                        }
                    }
                }
                const error = new Error(message);
                error.status = response.status;
                error.statusText = response.statusText;
                error.responseText = text;
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
        return true;
    }

    function getConfigurationResourceUrl(name) {
        if (window.Dashboard?.getConfigurationResourceUrl) {
            return window.Dashboard.getConfigurationResourceUrl(name);
        }
        return ApiClient.getUrl('web/ConfigurationPage', { name });
    }

    function closeDrawerIfOpen() {
        if (!document.querySelector('.mainDrawer.drawer-open')) {
            return;
        }
        const button = document.querySelector('.mainDrawerButton:not(.hide)');
        if (button instanceof HTMLElement) {
            button.click();
        }
    }

    function closeCommunity() {
        if (!communityPage) {
            updateSelectedState();
            return;
        }

        communityPage.remove();
        communityPage = null;

        for (const entry of previousPageState) {
            if (!entry.element.isConnected) {
                continue;
            }
            entry.element.classList.toggle('hide', entry.wasHidden);
        }
        previousPageState = [];
        document.documentElement.classList.remove('jellyfinCommunityOpen');
        updateSelectedState();
    }

    async function openCommunity(event) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();
        }

        if (!installCommunityAjaxAdapter()) {
            throw new Error('La API de Jellyfin todavía no está disponible. Inténtelo de nuevo en unos segundos.');
        }

        if (communityPage?.isConnected) {
            closeDrawerIfOpen();
            communityPage.classList.remove('hide');
            updateSelectedState(communityPage);
            return;
        }

        if (openingPromise) {
            return openingPromise;
        }

        openingPromise = (async () => {
            closeDrawerIfOpen();
            const container = document.querySelector('.mainAnimatedPages');
            if (!container) {
                throw new Error('Jellyfin Web no ha creado todavía el contenedor principal de páginas.');
            }

            const headers = {};
            if (typeof ApiClient.setRequestHeaders === 'function') {
                ApiClient.setRequestHeaders(headers);
            }

            const [pageResponse, controllerModule] = await Promise.all([
                fetch(`${getConfigurationResourceUrl('Community')}&v=${encodeURIComponent(VERSION)}`, {
                    headers,
                    credentials: 'same-origin',
                    cache: 'no-store'
                }),
                import(`${getConfigurationResourceUrl('CommunityPageController')}&v=${encodeURIComponent(VERSION)}`)
            ]);

            if (!pageResponse.ok) {
                throw new Error(`No se pudo cargar Comunidad (HTTP ${pageResponse.status}).`);
            }

            const html = await pageResponse.text();
            const wrapper = document.createElement('div');
            wrapper.innerHTML = html;
            const page = wrapper.querySelector(`div[data-role="page"]#${COMMUNITY_PAGE_ID}`);
            if (!(page instanceof HTMLElement)) {
                throw new Error('El recurso web de Community no contiene una página válida.');
            }

            const Controller = controllerModule.default;
            if (typeof Controller !== 'function') {
                throw new Error('El controlador web de Community no se pudo cargar.');
            }

            previousPageState = Array.from(container.children)
                .filter(element => element instanceof HTMLElement)
                .map(element => ({ element, wasHidden: element.classList.contains('hide') }));
            for (const entry of previousPageState) {
                entry.element.classList.add('hide');
            }

            page.removeAttribute('data-controller');
            page.classList.add('mainAnimatedPage', 'jellyfinCommunityStandalonePage');
            page.classList.remove('hide');
            container.appendChild(page);
            communityPage = page;
            document.documentElement.classList.add('jellyfinCommunityOpen');

            page.querySelector('#communityClose')?.addEventListener('click', closeCommunity);
            page.addEventListener('click', clickEvent => {
                const anchor = clickEvent.target.closest?.('a[href^="#!"],a[href^="#/"]');
                if (anchor) {
                    closeCommunity();
                }
            }, { capture: true });

            new Controller(page, {});
            updateSelectedState(page);
        })().catch(error => {
            closeCommunity();
            console.error('[Community] No se pudo abrir la interfaz del foro.', error);
            if (window.Dashboard?.alert) {
                window.Dashboard.alert(`No se pudo abrir Comunidad: ${error.message}`);
            }
            throw error;
        }).finally(() => {
            openingPromise = null;
        });

        return openingPromise;
    }

    function createMenuLink() {
        const link = document.createElement('a');
        link.setAttribute('is', 'emby-linkbutton');
        link.setAttribute(MENU_ATTRIBUTE, 'true');
        link.setAttribute('data-itemid', 'community');
        link.className = 'navMenuOption lnkMediaFolder jellyfinCommunityMenuOption';
        link.href = '#/community';
        link.addEventListener('click', openCommunity);

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
        const isCommunity = page?.id === COMMUNITY_PAGE_ID || communityPage?.isConnected === true;
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

        document.addEventListener('click', event => {
            if (!communityPage) {
                return;
            }
            const target = event.target;
            if (!(target instanceof Element)) {
                return;
            }
            const menuLink = target.closest('.navMenuOption');
            if (menuLink && !menuLink.hasAttribute(MENU_ATTRIBUTE)) {
                closeCommunity();
            }
        }, { capture: true });

        window.addEventListener('popstate', closeCommunity);
        window.addEventListener('hashchange', event => {
            if (location.hash === '#/community' || location.hash === '#!/community') {
                void openCommunity(event);
                return;
            }
            closeCommunity();
        });

        window.JellyfinCommunityBootstrap = Object.freeze({
            version: VERSION,
            ensureMenuLinks,
            openCommunity,
            closeCommunity,
            normalizeCommunityJson
        });

        if (location.hash === '#/community' || location.hash === '#!/community') {
            void openCommunity();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
