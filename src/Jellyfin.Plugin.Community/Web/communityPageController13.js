function normalizeCommunityJson(value) {
    if (Array.isArray(value)) {
        return value.map(normalizeCommunityJson);
    }
    if (!value || typeof value !== 'object' || value instanceof Date || value instanceof Blob) {
        return value;
    }

    const normalized = {};
    for (const [key, child] of Object.entries(value)) {
        const normalizedKey = key.length > 0 && key[0] >= 'A' && key[0] <= 'Z'
            ? key[0].toLowerCase() + key.slice(1)
            : key;
        normalized[normalizedKey] = normalizeCommunityJson(child);
    }
    return normalized;
}

function getResourceUrl(name) {
    if (window.Dashboard?.getConfigurationResourceUrl) {
        return window.Dashboard.getConfigurationResourceUrl(name);
    }
    return ApiClient.getUrl('web/ConfigurationPage', { name });
}

function installCommunityAjaxAdapter() {
    if (!window.ApiClient) {
        throw new Error('La API de Jellyfin todavía no está disponible.');
    }
    if (ApiClient.__communityAjaxAdapterInstalled) {
        return;
    }

    const originalAjax = ApiClient.ajax.bind(ApiClient);
    ApiClient.ajax = async function communityAwareAjax(request, includeAuthorization) {
        const isCommunityRequest = typeof request?.url === 'string'
            && request.url.includes('/Community/api/v1/');
        if (!isCommunityRequest) {
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

        let response;
        try {
            response = await fetch(request.url, {
                method: request.type || 'GET',
                headers,
                body: request.data ?? undefined,
                credentials: 'same-origin',
                cache: 'no-store'
            });
        } catch (error) {
            const networkError = new Error('No se pudo conectar con la API de Community en este servidor.');
            networkError.cause = error;
            throw networkError;
        }

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
}

let legacyModulePromise;
function getLegacyController() {
    if (!legacyModulePromise) {
        const url = `${getResourceUrl('CommunityPageControllerLegacy')}&v=1.3.0.0`;
        legacyModulePromise = import(url).then(module => {
            if (typeof module.default !== 'function') {
                throw new Error('El controlador principal de Community no es válido.');
            }
            return module.default;
        });
    }
    return legacyModulePromise;
}

export default class CommunityPageController13 {
    constructor(root, routeParams = {}) {
        if (!root || root.dataset.community13Loading === 'true') {
            return;
        }
        root.dataset.community13Loading = 'true';

        try {
            installCommunityAjaxAdapter();
        } catch (error) {
            root.dataset.community13Loading = 'false';
            throw error;
        }

        void getLegacyController()
            .then(Controller => {
                root.dataset.community13Loading = 'false';
                new Controller(root, routeParams);
            })
            .catch(error => {
                root.dataset.community13Loading = 'false';
                console.error('[Community] No se pudo iniciar la interfaz 1.3.', error);
                const banner = root.querySelector('#communityBanner');
                const main = root.querySelector('#communityMain');
                if (banner) {
                    banner.innerHTML = `<div class="community-alert community-error">${String(error?.message || error)}</div>`;
                }
                if (main) {
                    main.innerHTML = '<div class="community-empty community-card">Community no pudo iniciarse. Revise el registro del servidor.</div>';
                }
            });
    }
}

export { normalizeCommunityJson, installCommunityAjaxAdapter };
