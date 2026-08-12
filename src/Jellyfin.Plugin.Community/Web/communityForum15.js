(() => {
    'use strict';

    const VERSION = '1.5.0.0';
    const pathIndex = location.pathname.toLowerCase().lastIndexOf('/community/app');
    const serverPath = pathIndex >= 0 ? location.pathname.slice(0, pathIndex).replace(/\/$/, '') : '';
    const serverRoot = `${location.origin}${serverPath}`;
    const apiRoot = `${serverRoot}/Community/api/v1/`;
    const webRoot = `${serverRoot}/web/`;

    const elements = {
        back: document.querySelector('#backToJellyfin'),
        user: document.querySelector('#currentUser'),
        banner: document.querySelector('#forumBanner'),
        content: document.querySelector('#forumContent'),
        tabs: document.querySelector('#forumTabs'),
        moderationTab: document.querySelector('#moderationTab'),
        adminTab: document.querySelector('#adminTab'),
        searchForm: document.querySelector('#searchForm'),
        search: document.querySelector('#forumSearch'),
        newThread: document.querySelector('#newThreadButton'),
        modal: document.querySelector('#forumModal'),
        modalContent: document.querySelector('#modalContent'),
        modalClose: document.querySelector('#modalClose'),
        busy: document.querySelector('#busyIndicator')
    };

    const state = {
        auth: null,
        me: null,
        categories: [],
        view: 'threads',
        categoryId: null,
        page: 1,
        pageSize: 25,
        query: '',
        currentThread: null,
        postsById: new Map(),
        pendingRequests: 0
    };

    class ForumApiError extends Error {
        constructor(message, status = 0, code = 'network_error', requestId = '') {
            super(message);
            this.name = 'ForumApiError';
            this.status = status;
            this.code = code;
            this.requestId = requestId;
        }
    }

    function normalizeJson(value) {
        if (Array.isArray(value)) return value.map(normalizeJson);
        if (!value || typeof value !== 'object') return value;
        const result = {};
        for (const [key, child] of Object.entries(value)) {
            const normalizedKey = /^[A-Z]/.test(key) ? key[0].toLowerCase() + key.slice(1) : key;
            result[normalizedKey] = normalizeJson(child);
        }
        return result;
    }

    function escapeHtml(value) {
        return String(value ?? '').replace(/[&<>'"]/g, character => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
        })[character]);
    }

    function queryString(values) {
        const params = new URLSearchParams();
        for (const [key, value] of Object.entries(values)) {
            if (value !== null && value !== undefined && value !== '') params.set(key, String(value));
        }
        return params.toString();
    }

    function parseAddress(value) {
        if (typeof value !== 'string' || !value.trim()) return null;
        try {
            return new URL(value, location.origin);
        } catch (_) {
            return null;
        }
    }

    function findStoredAuthentication() {
        let credentials;
        try {
            credentials = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
        } catch (_) {
            credentials = {};
        }

        const servers = credentials.Servers || credentials.servers || [];
        const authenticated = servers.filter(server =>
            (server.AccessToken || server.accessToken) && (server.UserId || server.userId));
        if (!authenticated.length) return null;

        const expectedPath = serverPath.replace(/\/$/, '').toLowerCase();
        const score = server => {
            const addresses = [
                server.Address, server.address, server.ManualAddress, server.manualAddress,
                server.LocalAddress, server.localAddress, server.RemoteAddress, server.remoteAddress
            ].map(parseAddress).filter(Boolean);
            let value = 0;
            for (const address of addresses) {
                if (address.origin === location.origin) value += 10;
                const addressPath = address.pathname.replace(/\/$/, '').toLowerCase();
                if (addressPath === expectedPath) value += 6;
            }
            return value;
        };

        authenticated.sort((left, right) => score(right) - score(left));
        const selected = authenticated[0];
        return {
            token: selected.AccessToken || selected.accessToken,
            userId: selected.UserId || selected.userId,
            serverId: selected.Id || selected.id || ''
        };
    }

    function setBusy(active) {
        state.pendingRequests += active ? 1 : -1;
        state.pendingRequests = Math.max(0, state.pendingRequests);
        elements.busy.hidden = state.pendingRequests === 0;
    }

    function friendlyHttpMessage(status, fallback) {
        if (status === 401) return 'La sesión de Jellyfin ha caducado. Vuelve a Jellyfin, inicia sesión y abre de nuevo el Foro.';
        if (status === 403) return 'Tu usuario de Jellyfin no tiene permiso para realizar esta acción.';
        if (status === 404) return 'El recurso solicitado ya no existe o no está disponible.';
        if (status === 429) return 'Has realizado demasiadas acciones seguidas. Espera un momento y vuelve a intentarlo.';
        if (status >= 500) return fallback || 'El servidor no pudo completar la operación.';
        return fallback || `La operación no se pudo completar (HTTP ${status}).`;
    }

    async function request(path, options = {}) {
        if (!state.auth?.token) {
            throw new ForumApiError('No se encontró una sesión activa de Jellyfin.', 401, 'missing_session');
        }

        const headers = {
            Accept: 'application/json',
            'X-Emby-Token': state.auth.token,
            'X-Emby-Authorization': `MediaBrowser Client="Jellyfin Community", Device="Web", DeviceId="community-forum", Version="${VERSION}", Token="${state.auth.token}"`,
            ...(options.headers || {})
        };
        let body;
        if (options.body !== undefined) {
            headers['Content-Type'] = 'application/json';
            body = JSON.stringify(options.body);
        }

        setBusy(true);
        let response;
        try {
            response = await fetch(apiRoot + path.replace(/^\//, ''), {
                method: options.method || 'GET',
                headers,
                body,
                credentials: 'same-origin',
                cache: 'no-store'
            });
        } catch (error) {
            throw new ForumApiError(
                'No se pudo contactar con Community en este servidor. Comprueba el proxy inverso y que el plugin esté activado.',
                0,
                'network_error');
        } finally {
            setBusy(false);
        }

        const text = await response.text().catch(() => '');
        let payload = null;
        if (text) {
            try {
                payload = normalizeJson(JSON.parse(text));
            } catch (_) {
                payload = null;
            }
        }

        if (!response.ok) {
            const requestId = payload?.requestId || response.headers.get('x-request-id') || '';
            const code = payload?.error || payload?.code || `http_${response.status}`;
            const serverMessage = payload?.message || payload?.detail || payload?.title || '';
            throw new ForumApiError(friendlyHttpMessage(response.status, serverMessage), response.status, code, requestId);
        }

        if (response.status === 204 || response.status === 205 || !text) return null;
        if (payload !== null) return payload;
        return text;
    }

    function errorDetails(error) {
        const status = Number(error?.status) || 0;
        const code = error?.code || (status ? `http_${status}` : 'client_error');
        const reference = [status ? `HTTP ${status}` : '', code, error?.requestId ? `referencia ${error.requestId}` : '']
            .filter(Boolean)
            .join(' · ');
        return {
            message: error?.message || 'Se produjo un error inesperado en el Foro.',
            reference
        };
    }

    function showError(error, target = elements.banner) {
        const details = errorDetails(error);
        target.hidden = false;
        target.className = target === elements.banner ? 'banner error' : 'form-error';
        target.innerHTML = `<strong class="error-title">No se pudo completar la operación</strong>`
            + `<span>${escapeHtml(details.message)}</span>`
            + (details.reference ? `<div class="error-reference">${escapeHtml(details.reference)}</div>` : '');
        target.scrollIntoView({ block: 'nearest' });
    }

    function setBanner(message = '', type = '') {
        if (!message) {
            elements.banner.hidden = true;
            elements.banner.textContent = '';
            elements.banner.className = 'banner';
            return;
        }
        elements.banner.hidden = false;
        elements.banner.className = `banner ${type}`.trim();
        elements.banner.textContent = message;
    }

    function formatDate(value) {
        if (!value) return '';
        try {
            return new Intl.DateTimeFormat('es', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
        } catch (_) {
            return String(value);
        }
    }

    function formatBytes(bytes) {
        if (!Number.isFinite(bytes)) return '0 B';
        const units = ['B', 'KB', 'MB', 'GB', 'TB'];
        let value = bytes;
        let index = 0;
        while (value >= 1024 && index < units.length - 1) {
            value /= 1024;
            index++;
        }
        return `${value.toFixed(index ? 1 : 0)} ${units[index]}`;
    }

    function updateTabs() {
        elements.tabs.querySelectorAll('[data-view]').forEach(tab => {
            const active = tab.dataset.view === state.view;
            tab.classList.toggle('active', active);
            tab.setAttribute('aria-current', active ? 'page' : 'false');
        });
    }

    function openModal(html) {
        elements.modalContent.innerHTML = html;
        elements.modal.hidden = false;
        document.body.style.overflow = 'hidden';
        elements.modalContent.querySelector('input,select,textarea,button')?.focus();
    }

    function closeModal() {
        elements.modal.hidden = true;
        elements.modalContent.replaceChildren();
        document.body.style.overflow = '';
    }

    function formError(form) {
        let target = form.querySelector('.form-error');
        if (!target) {
            target = document.createElement('div');
            target.className = 'form-error';
            target.hidden = true;
            form.prepend(target);
        }
        return target;
    }

    async function perform(form, action) {
        const submit = form?.querySelector('button[type="submit"]');
        if (submit) submit.disabled = true;
        const target = form ? formError(form) : elements.banner;
        target.hidden = true;
        try {
            return await action();
        } catch (error) {
            showError(error, target);
            return null;
        } finally {
            if (submit) submit.disabled = false;
        }
    }

    function categorySidebar() {
        return `<aside class="card category-list" aria-label="Categorías"><h2>Categorías</h2>`
            + `<button class="category-button ${state.categoryId === null ? 'active' : ''}" type="button" data-category="">`
            + '<span>Todas</span></button>'
            + state.categories.map(category => `<button class="category-button ${state.categoryId === category.id ? 'active' : ''}" type="button" data-category="${category.id}">`
                + `<span>${escapeHtml(category.name)}</span><span class="category-count">${category.threadCount}</span></button>`).join('')
            + '</aside>';
    }

    function threadCard(thread) {
        const flags = [
            thread.isPinned ? '<span class="badge">fijado</span>' : '',
            thread.isLocked ? '<span class="badge warning">cerrado</span>' : ''
        ].filter(Boolean).join(' ');
        return `<article class="card"><button class="thread-button" type="button" data-thread="${thread.id}">`
            + `<h3 class="thread-title">${escapeHtml(thread.title)} ${flags}</h3>`
            + `<div class="meta"><span>${escapeHtml(thread.categoryName)}</span><span>${escapeHtml(thread.authorName)}</span>`
            + `<span>${formatDate(thread.lastActivityUtc)}</span><span>${thread.replyCount} respuestas</span><span>${thread.viewCount} vistas</span>`
            + (thread.itemName ? `<span>🎬 ${escapeHtml(thread.itemName)}</span>` : '') + '</div>'
            + `<div class="tags">${(thread.tags || []).map(tag => `<span class="tag">${escapeHtml(tag)}</span>`).join('')}</div>`
            + '</button></article>';
    }

    function pager(data) {
        const pages = Math.max(1, Math.ceil(data.total / data.pageSize));
        return `<nav class="pager" aria-label="Paginación">`
            + `<button class="button button-secondary button-small" type="button" data-page="${Math.max(1, data.page - 1)}" ${data.page <= 1 ? 'disabled' : ''}>Anterior</button>`
            + `<span>${data.page} / ${pages}</span>`
            + `<button class="button button-secondary button-small" type="button" data-page="${Math.min(pages, data.page + 1)}" ${data.page >= pages ? 'disabled' : ''}>Siguiente</button>`
            + '</nav>';
    }

    async function renderThreads() {
        elements.content.innerHTML = '<div class="loading-card">Cargando conversaciones…</div>';
        try {
            const data = await request('threads?' + queryString({
                categoryId: state.categoryId,
                q: state.query,
                sort: 'activity',
                page: state.page,
                pageSize: state.pageSize,
                followedOnly: state.view === 'followed'
            }));
            setBanner();
            elements.content.innerHTML = `<div class="forum-grid">${categorySidebar()}<section aria-label="Conversaciones">`
                + (data.items.length ? data.items.map(threadCard).join('') : '<div class="empty-card">No hay conversaciones en esta sección.</div>')
                + `${pager(data)}</section></div>`;
            elements.content.querySelectorAll('[data-category]').forEach(button => button.addEventListener('click', () => {
                state.categoryId = button.dataset.category ? Number(button.dataset.category) : null;
                state.page = 1;
                void renderThreads();
            }));
            elements.content.querySelectorAll('[data-thread]').forEach(button => button.addEventListener('click', () => {
                void openThread(Number(button.dataset.thread));
            }));
            elements.content.querySelectorAll('[data-page]').forEach(button => button.addEventListener('click', () => {
                state.page = Number(button.dataset.page);
                void renderThreads();
            }));
        } catch (error) {
            elements.content.innerHTML = '<div class="empty-card">No se pudieron cargar las conversaciones.</div>';
            showError(error);
        }
    }

    function renderPoll(poll) {
        if (!poll) return '';
        return `<form id="pollForm" class="card"><h3>${escapeHtml(poll.question)}</h3>`
            + poll.options.map(option => `<label class="poll-option"><input type="${poll.allowMultiple ? 'checkbox' : 'radio'}" name="pollOption" value="${option.id}" ${option.currentUserVoted ? 'checked' : ''}>`
                + `<span>${escapeHtml(option.text)} — ${option.voteCount}</span></label>`).join('')
            + '<div class="form-error" hidden></div><button class="button button-secondary button-small" type="submit">Votar</button></form>';
    }

    function postCard(post) {
        const body = post.containsSpoiler
            ? `<div class="spoiler ${post.spoilerUnlocked ? '' : 'locked'}"><button class="link-button spoiler-toggle" type="button">${post.spoilerUnlocked ? 'Ocultar' : 'Mostrar'} spoiler${post.spoilerLabel ? ` — ${escapeHtml(post.spoilerLabel)}` : ''}</button><div class="spoiler-content post-body">${post.bodyHtml}</div></div>`
            : `<div class="post-body">${post.bodyHtml}</div>`;
        return `<article class="card post" data-post="${post.id}"><div class="meta"><strong>${escapeHtml(post.authorName)}</strong>`
            + `<span>${formatDate(post.createdUtc)}</span>${post.isEdited ? '<span>editado</span>' : ''}${post.isHidden ? '<span class="badge warning">oculto</span>' : ''}</div>`
            + body
            + `<div class="actions">${['like', 'love', 'laugh', 'insightful'].map(reaction => `<button class="link-button" type="button" data-reaction="${reaction}">${reaction} ${(post.reactions && post.reactions[reaction]) || 0}</button>`).join('')}`
            + '<button class="link-button" type="button" data-quote>Responder</button><button class="link-button" type="button" data-report>Denunciar</button>'
            + (post.canEdit ? '<button class="link-button" type="button" data-edit>Editar</button>' : '')
            + (post.canModerate ? '<button class="link-button" type="button" data-hide>Ocultar</button>' : '')
            + '</div></article>';
    }

    function replyForm() {
        return '<form id="replyForm" class="card reply-form"><h3>Responder</h3><div class="form-error" hidden></div>'
            + '<label class="sr-only" for="replyBody">Respuesta</label><textarea id="replyBody" required maxlength="20000" placeholder="Escribe tu respuesta en Markdown"></textarea>'
            + '<input id="parentPostId" type="hidden"><label class="check-row"><input id="replySpoiler" type="checkbox"><span>Contiene spoilers</span></label>'
            + '<button class="button button-primary" type="submit">Publicar respuesta</button></form>';
    }

    async function openThread(threadId) {
        elements.content.innerHTML = '<div class="loading-card">Cargando conversación…</div>';
        try {
            const [thread, posts] = await Promise.all([
                request(`threads/${threadId}`),
                request(`threads/${threadId}/posts?page=1&pageSize=100`)
            ]);
            state.currentThread = thread;
            state.postsById = new Map(posts.items.map(post => [post.id, post]));
            setBanner();
            elements.content.innerHTML = '<button id="threadBack" class="button button-secondary button-small" type="button">← Conversaciones</button>'
                + `<article class="card"><div class="thread-heading"><div><h2>${escapeHtml(thread.thread.title)}</h2>`
                + `<div class="meta"><span>${escapeHtml(thread.thread.categoryName)}</span><span>${escapeHtml(thread.thread.authorName)}</span><span>${formatDate(thread.thread.createdUtc)}</span></div></div></div>`
                + `${renderPoll(thread.poll)}<div class="actions"><button id="followThread" class="link-button" type="button">${thread.thread.isFollowing ? 'Dejar de seguir' : 'Seguir'}</button>`
                + (thread.canModerate ? '<button id="moderateThread" class="link-button" type="button">Moderar</button>' : '') + '</div></article>'
                + `<section id="postList" aria-label="Mensajes">${posts.items.map(postCard).join('')}</section>`
                + (!thread.thread.isLocked && !state.me.isMuted ? replyForm() : '<div class="banner">La conversación no admite nuevas respuestas.</div>');

            elements.content.querySelector('#threadBack').addEventListener('click', () => void renderCurrentView());
            elements.content.querySelector('#followThread').addEventListener('click', async () => {
                try {
                    await request(`threads/${threadId}/follow`, { method: thread.thread.isFollowing ? 'DELETE' : 'POST' });
                    await openThread(threadId);
                } catch (error) {
                    showError(error);
                }
            });
            elements.content.querySelector('#moderateThread')?.addEventListener('click', () => openModerationDialog(thread.thread));
            wirePosts(threadId);
            wireReply(threadId);
            wirePoll(threadId);
            request(`threads/${threadId}/read`, { method: 'POST' }).catch(() => {});
        } catch (error) {
            elements.content.innerHTML = '<div class="empty-card">No se pudo abrir la conversación.</div>';
            showError(error);
        }
    }

    function wireReply(threadId) {
        const form = elements.content.querySelector('#replyForm');
        if (!form) return;
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const result = await perform(form, () => request(`threads/${threadId}/posts`, {
                method: 'POST',
                body: {
                    body: form.querySelector('#replyBody').value,
                    parentPostId: Number(form.querySelector('#parentPostId').value) || null,
                    containsSpoiler: form.querySelector('#replySpoiler').checked,
                    spoilerItemId: null,
                    spoilerLabel: null
                }
            }));
            if (result) await openThread(threadId);
        });
    }

    function wirePoll(threadId) {
        const form = elements.content.querySelector('#pollForm');
        if (!form) return;
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const optionIds = [...form.querySelectorAll('input:checked')].map(option => Number(option.value));
            const result = await perform(form, () => request(`threads/${threadId}/poll/vote`, { method: 'POST', body: { optionIds } }));
            if (result) await openThread(threadId);
        });
    }

    function wirePosts(threadId) {
        elements.content.querySelectorAll('.spoiler-toggle').forEach(button => button.addEventListener('click', () => {
            button.parentElement.classList.toggle('locked');
        }));
        elements.content.querySelectorAll('[data-post]').forEach(card => {
            const postId = Number(card.dataset.post);
            card.querySelectorAll('[data-reaction]').forEach(button => button.addEventListener('click', async () => {
                try {
                    await request(`posts/${postId}/reaction`, { method: 'PUT', body: { reaction: button.dataset.reaction } });
                    await openThread(threadId);
                } catch (error) {
                    showError(error);
                }
            }));
            card.querySelector('[data-quote]')?.addEventListener('click', () => {
                const parent = elements.content.querySelector('#parentPostId');
                const reply = elements.content.querySelector('#replyBody');
                if (!parent || !reply) {
                    setBanner('Esta conversación no admite respuestas en este momento.', 'error');
                    return;
                }
                parent.value = String(postId);
                reply.focus();
                reply.scrollIntoView({ block: 'center' });
            });
            card.querySelector('[data-report]')?.addEventListener('click', () => openReportDialog(postId));
            card.querySelector('[data-edit]')?.addEventListener('click', () => openEditDialog(postId, threadId));
            card.querySelector('[data-hide]')?.addEventListener('click', async () => {
                if (!confirm('¿Ocultar esta publicación?')) return;
                try {
                    await request(`posts/${postId}?reason=${encodeURIComponent('Ocultación por moderación')}`, { method: 'DELETE' });
                    await openThread(threadId);
                } catch (error) {
                    showError(error);
                }
            });
        });
    }

    function openNewThreadDialog() {
        const writable = state.categories.filter(category => !category.isArchived && (!category.isReadOnly || state.me.isModerator));
        if (!writable.length) {
            setBanner('No hay ninguna categoría disponible para publicar.', 'error');
            return;
        }
        openModal('<h2 id="modalTitle">Nueva conversación</h2><form id="newThreadForm"><div class="form-error" hidden></div>'
            + `<div class="field"><label for="newCategory">Categoría</label><select id="newCategory" required>${writable.map(category => `<option value="${category.id}" ${state.categoryId === category.id ? 'selected' : ''}>${escapeHtml(category.name)}</option>`).join('')}</select></div>`
            + '<div class="field"><label for="newKind">Tipo</label><select id="newKind"><option value="0">Debate</option><option value="1">Reseña</option><option value="2">Encuesta</option>'
            + (state.me.isModerator ? '<option value="3">Anuncio</option>' : '') + '</select></div>'
            + '<div class="field"><label for="newTitle">Título</label><input id="newTitle" required maxlength="200" autocomplete="off"></div>'
            + '<div class="field"><label for="newBody">Mensaje</label><textarea id="newBody" required maxlength="20000"></textarea><p class="field-description">Se admite Markdown.</p></div>'
            + '<div class="field"><label for="newTags">Etiquetas</label><input id="newTags" maxlength="300" placeholder="cine, recomendaciones"><p class="field-description">Separadas por comas.</p></div>'
            + '<label class="check-row"><input id="newSpoiler" type="checkbox"><span>Contiene spoilers</span></label>'
            + '<div id="newPollFields" hidden><div class="field"><label for="newPollQuestion">Pregunta</label><input id="newPollQuestion" maxlength="300"></div>'
            + '<div class="field"><label for="newPollOptions">Opciones</label><textarea id="newPollOptions" placeholder="Una opción por línea"></textarea></div>'
            + '<label class="check-row"><input id="newPollMultiple" type="checkbox"><span>Permitir varias opciones</span></label></div>'
            + '<div class="actions"><button class="button button-primary" type="submit">Crear tema</button><button class="button button-secondary" type="button" data-close>Cancelar</button></div></form>');

        const form = elements.modalContent.querySelector('#newThreadForm');
        const kind = form.querySelector('#newKind');
        const pollFields = form.querySelector('#newPollFields');
        kind.addEventListener('change', () => { pollFields.hidden = kind.value !== '2'; });
        form.querySelector('[data-close]').addEventListener('click', closeModal);
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const isPoll = kind.value === '2';
            const result = await perform(form, () => request('threads', {
                method: 'POST',
                body: {
                    categoryId: Number(form.querySelector('#newCategory').value),
                    kind: Number(kind.value),
                    title: form.querySelector('#newTitle').value,
                    body: form.querySelector('#newBody').value,
                    itemId: null,
                    itemName: null,
                    tags: form.querySelector('#newTags').value.split(',').map(value => value.trim()).filter(Boolean),
                    containsSpoiler: form.querySelector('#newSpoiler').checked,
                    spoilerItemId: null,
                    spoilerLabel: null,
                    poll: isPoll ? {
                        question: form.querySelector('#newPollQuestion').value,
                        allowMultiple: form.querySelector('#newPollMultiple').checked,
                        closesUtc: null,
                        options: form.querySelector('#newPollOptions').value.split(/\r?\n/).map(value => value.trim()).filter(Boolean)
                    } : null
                }
            }));
            if (result?.thread?.id) {
                closeModal();
                await openThread(result.thread.id);
            }
        });
    }

    function openReportDialog(postId) {
        openModal('<h2 id="modalTitle">Denunciar publicación</h2><form id="reportForm"><div class="form-error" hidden></div>'
            + '<div class="field"><label for="reportReason">Motivo</label><input id="reportReason" required maxlength="100"></div>'
            + '<div class="field"><label for="reportComment">Comentario</label><textarea id="reportComment" maxlength="2000"></textarea></div>'
            + '<div class="actions"><button class="button button-primary" type="submit">Enviar denuncia</button><button class="button button-secondary" type="button" data-close>Cancelar</button></div></form>');
        const form = elements.modalContent.querySelector('#reportForm');
        form.querySelector('[data-close]').addEventListener('click', closeModal);
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const result = await perform(form, () => request(`posts/${postId}/report`, {
                method: 'POST',
                body: { reason: form.querySelector('#reportReason').value, comment: form.querySelector('#reportComment').value }
            }));
            if (result) {
                closeModal();
                setBanner('La denuncia se ha registrado.', 'success');
            }
        });
    }

    function openEditDialog(postId, threadId) {
        const post = state.postsById.get(postId);
        if (!post) return;
        openModal('<h2 id="modalTitle">Editar publicación</h2><form id="editPostForm"><div class="form-error" hidden></div>'
            + `<div class="field"><label for="editBody">Mensaje</label><textarea id="editBody" required maxlength="20000">${escapeHtml(post.bodyMarkdown)}</textarea></div>`
            + `<label class="check-row"><input id="editSpoiler" type="checkbox" ${post.containsSpoiler ? 'checked' : ''}><span>Contiene spoilers</span></label>`
            + '<div class="field"><label for="editReason">Motivo de la edición</label><input id="editReason" maxlength="300"></div>'
            + '<div class="actions"><button class="button button-primary" type="submit">Guardar</button><button class="button button-secondary" type="button" data-close>Cancelar</button></div></form>');
        const form = elements.modalContent.querySelector('#editPostForm');
        form.querySelector('[data-close]').addEventListener('click', closeModal);
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const result = await perform(form, () => request(`posts/${postId}`, {
                method: 'PUT',
                body: {
                    body: form.querySelector('#editBody').value,
                    containsSpoiler: form.querySelector('#editSpoiler').checked,
                    spoilerItemId: post.spoilerItemId,
                    spoilerLabel: post.spoilerLabel,
                    editReason: form.querySelector('#editReason').value
                }
            }));
            if (result) {
                closeModal();
                await openThread(threadId);
            }
        });
    }

    function openModerationDialog(thread) {
        openModal('<h2 id="modalTitle">Moderar conversación</h2><form id="moderateThreadForm"><div class="form-error" hidden></div>'
            + `<label class="check-row"><input id="modPinned" type="checkbox" ${thread.isPinned ? 'checked' : ''}><span>Fijada</span></label>`
            + `<label class="check-row"><input id="modLocked" type="checkbox" ${thread.isLocked ? 'checked' : ''}><span>Cerrada</span></label>`
            + `<label class="check-row"><input id="modArchived" type="checkbox" ${thread.isArchived ? 'checked' : ''}><span>Archivada</span></label>`
            + `<label class="check-row"><input id="modHidden" type="checkbox" ${thread.isHidden ? 'checked' : ''}><span>Oculta</span></label>`
            + `<div class="field"><label for="modCategory">Mover a categoría</label><select id="modCategory"><option value="">Sin cambio</option>${state.categories.map(category => `<option value="${category.id}">${escapeHtml(category.name)}</option>`).join('')}</select></div>`
            + '<div class="field"><label for="modReason">Motivo</label><input id="modReason" maxlength="300"></div>'
            + '<div class="actions"><button class="button button-primary" type="submit">Aplicar</button><button class="button button-secondary" type="button" data-close>Cancelar</button></div></form>');
        const form = elements.modalContent.querySelector('#moderateThreadForm');
        form.querySelector('[data-close]').addEventListener('click', closeModal);
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const result = await perform(form, () => request(`moderation/threads/${thread.id}`, {
                method: 'POST',
                body: {
                    isPinned: form.querySelector('#modPinned').checked,
                    isLocked: form.querySelector('#modLocked').checked,
                    isArchived: form.querySelector('#modArchived').checked,
                    isHidden: form.querySelector('#modHidden').checked,
                    moveToCategoryId: Number(form.querySelector('#modCategory').value) || null,
                    reason: form.querySelector('#modReason').value
                }
            }));
            if (result === null && !formError(form).hidden) return;
            closeModal();
            await openThread(thread.id);
        });
    }

    async function renderNotifications() {
        elements.content.innerHTML = '<div class="loading-card">Cargando notificaciones…</div>';
        try {
            const data = await request('notifications?page=1&pageSize=100');
            setBanner();
            elements.content.innerHTML = '<div class="actions"><button id="markAllRead" class="button button-secondary button-small" type="button">Marcar todo leído</button></div>'
                + (data.items.length ? data.items.map(notification => `<article class="card notification ${notification.isRead ? '' : 'unread'}"><button class="notification-button" type="button" data-notification="${notification.id}" data-thread="${notification.threadId || ''}">`
                    + `<strong>${escapeHtml(notification.title)}</strong><p>${escapeHtml(notification.message)}</p><div class="meta">${formatDate(notification.createdUtc)}</div></button></article>`).join('')
                    : '<div class="empty-card">No hay notificaciones.</div>');
            elements.content.querySelector('#markAllRead').addEventListener('click', async () => {
                try {
                    await request('notifications/read', { method: 'POST', body: { notificationIds: [], markAll: true } });
                    await renderNotifications();
                } catch (error) {
                    showError(error);
                }
            });
            elements.content.querySelectorAll('[data-notification]').forEach(button => button.addEventListener('click', async () => {
                try {
                    await request('notifications/read', { method: 'POST', body: { notificationIds: [Number(button.dataset.notification)], markAll: false } });
                    if (button.dataset.thread) await openThread(Number(button.dataset.thread));
                    else await renderNotifications();
                } catch (error) {
                    showError(error);
                }
            }));
        } catch (error) {
            elements.content.innerHTML = '<div class="empty-card">No se pudieron cargar las notificaciones.</div>';
            showError(error);
        }
    }

    async function renderModeration() {
        elements.content.innerHTML = '<div class="loading-card">Cargando moderación…</div>';
        try {
            const data = await request('moderation/reports?state=0&page=1&pageSize=100');
            setBanner();
            elements.content.innerHTML = data.items.length
                ? data.items.map(report => `<article class="card"><h3>${escapeHtml(report.reason)}</h3><p>${escapeHtml(report.comment || 'Sin comentario')}</p>`
                    + `<div class="meta"><span>${escapeHtml(report.reporterName)}</span><span>${formatDate(report.createdUtc)}</span><span>Mensaje ${report.postId}</span></div>`
                    + `<div class="actions"><button class="link-button" type="button" data-resolve="${report.id}" data-state="2">Resolver</button><button class="link-button" type="button" data-resolve="${report.id}" data-state="3">Rechazar</button><button class="link-button" type="button" data-open-thread="${report.threadId}">Abrir conversación</button></div></article>`).join('')
                : '<div class="empty-card">No hay denuncias abiertas.</div>';
            elements.content.querySelectorAll('[data-resolve]').forEach(button => button.addEventListener('click', async () => {
                const resolution = prompt('Escribe la resolución:');
                if (!resolution) return;
                try {
                    await request(`moderation/reports/${button.dataset.resolve}/resolve`, {
                        method: 'POST',
                        body: { state: Number(button.dataset.state), resolution }
                    });
                    await renderModeration();
                } catch (error) {
                    showError(error);
                }
            }));
            elements.content.querySelectorAll('[data-open-thread]').forEach(button => button.addEventListener('click', () => {
                void openThread(Number(button.dataset.openThread));
            }));
        } catch (error) {
            elements.content.innerHTML = '<div class="empty-card">No se pudo cargar la moderación.</div>';
            showError(error);
        }
    }

    function categoryDialog(category = null) {
        const edit = Boolean(category);
        openModal(`<h2 id="modalTitle">${edit ? 'Editar' : 'Nueva'} categoría</h2><form id="categoryForm"><div class="form-error" hidden></div>`
            + `<div class="field"><label for="categoryName">Nombre</label><input id="categoryName" required maxlength="100" value="${escapeHtml(category?.name || '')}"></div>`
            + `<div class="field"><label for="categoryDescription">Descripción</label><textarea id="categoryDescription" maxlength="2000">${escapeHtml(category?.description || '')}</textarea></div>`
            + `<div class="field"><label for="categorySort">Orden</label><input id="categorySort" type="number" value="${category?.sortOrder ?? state.categories.length * 10}"></div>`
            + `<label class="check-row"><input id="categoryReadOnly" type="checkbox" ${category?.isReadOnly ? 'checked' : ''}><span>Solo lectura</span></label>`
            + `<label class="check-row"><input id="categoryApproval" type="checkbox" ${category?.requiresApproval ? 'checked' : ''}><span>Requerir aprobación</span></label>`
            + (edit ? `<label class="check-row"><input id="categoryArchived" type="checkbox" ${category.isArchived ? 'checked' : ''}><span>Archivada</span></label>` : '')
            + `<div class="actions"><button class="button button-primary" type="submit">${edit ? 'Guardar' : 'Crear categoría'}</button><button class="button button-secondary" type="button" data-close>Cancelar</button></div></form>`);
        const form = elements.modalContent.querySelector('#categoryForm');
        form.querySelector('[data-close]').addEventListener('click', closeModal);
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const payload = {
                name: form.querySelector('#categoryName').value,
                description: form.querySelector('#categoryDescription').value,
                libraryId: category?.libraryId || null,
                sortOrder: Number(form.querySelector('#categorySort').value) || 0,
                isReadOnly: form.querySelector('#categoryReadOnly').checked,
                requiresApproval: form.querySelector('#categoryApproval').checked,
                ...(edit ? { isArchived: form.querySelector('#categoryArchived').checked } : {})
            };
            const result = await perform(form, () => request(edit ? `categories/${category.id}` : 'categories', {
                method: edit ? 'PUT' : 'POST', body: payload
            }));
            if (result) {
                state.categories = await request('categories');
                closeModal();
                await renderAdmin();
            }
        });
    }

    async function downloadBackup() {
        setBusy(true);
        try {
            const response = await fetch(apiRoot + 'admin/backups', {
                method: 'POST',
                headers: { 'X-Emby-Token': state.auth.token },
                credentials: 'same-origin',
                cache: 'no-store'
            });
            if (!response.ok) {
                const text = await response.text();
                let payload = {};
                try { payload = normalizeJson(JSON.parse(text)); } catch (_) {}
                throw new ForumApiError(friendlyHttpMessage(response.status, payload.message), response.status, payload.error, payload.requestId);
            }
            const blob = await response.blob();
            const disposition = response.headers.get('content-disposition') || '';
            const match = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(disposition);
            const filename = match ? decodeURIComponent(match[1].replace(/"/g, '')) : 'jellyfin-community-backup.zip';
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = objectUrl;
            link.download = filename;
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
        } catch (error) {
            showError(error);
        } finally {
            setBusy(false);
        }
    }

    async function renderAdmin() {
        elements.content.innerHTML = '<div class="loading-card">Cargando administración…</div>';
        try {
            const [stats, web, users] = await Promise.all([
                request('admin/stats'), request('admin/web-integration'), request('admin/users')
            ]);
            setBanner();
            const webReady = web.configResponsesTransformed > 0 && !web.lastError;
            elements.content.innerHTML = `<section class="admin-grid" aria-label="Resumen"><div class="card stat"><span>Base de datos</span><strong>${formatBytes(stats.databaseBytes)}</strong></div>`
                + `<div class="card stat"><span>Categorías</span><strong>${stats.categories}</strong></div><div class="card stat"><span>Conversaciones</span><strong>${stats.threads}</strong></div>`
                + `<div class="card stat"><span>Mensajes</span><strong>${stats.posts}</strong></div><div class="card stat"><span>Usuarios</span><strong>${stats.users}</strong></div>`
                + `<div class="card stat"><span>Denuncias abiertas</span><strong>${stats.openReports}</strong></div></section>`
                + `<section class="card"><div class="section-heading"><h2>Acceso de usuarios</h2><span class="badge ${webReady ? '' : 'warning'}">${webReady ? 'Foro publicado' : 'Pendiente de una carga de Jellyfin Web'}</span></div>`
                + `<p>Community ${escapeHtml(web.version)} · menú oficial ${web.configResponsesTransformed} veces · índice ${web.indexResponsesTransformed} veces.</p>`
                + (web.lastError ? `<div class="banner error">${escapeHtml(web.lastError)}</div>` : '') + '</section>'
                + `<section class="card"><div class="section-heading"><h2>Categorías</h2><button id="newCategory" class="button button-secondary button-small" type="button">Nueva categoría</button></div>`
                + state.categories.map(category => `<div class="admin-row"><div><strong>${escapeHtml(category.name)}</strong><div class="meta"><span>${category.threadCount} temas</span>${category.isReadOnly ? '<span>solo lectura</span>' : ''}${category.requiresApproval ? '<span>requiere aprobación</span>' : ''}${category.isArchived ? '<span>archivada</span>' : ''}</div></div><button class="link-button" type="button" data-edit-category="${category.id}">Editar</button></div>`).join('') + '</section>'
                + `<section class="card"><h2>Usuarios y moderadores</h2>${users.length ? users.map(user => `<div class="admin-row"><div><strong>${escapeHtml(user.username)}</strong><div class="meta">${user.isModerator ? '<span class="badge">moderador</span>' : ''}${user.isMuted ? '<span>silenciado</span>' : ''}${user.isSuspended ? '<span>suspendido</span>' : ''}</div></div><div class="actions"><button class="link-button" type="button" data-user-moderator="${escapeHtml(user.id)}" data-enabled="${!user.isModerator}">${user.isModerator ? 'Quitar moderador' : 'Hacer moderador'}</button><button class="link-button" type="button" data-user-mute="${escapeHtml(user.id)}" data-muted="${!user.isMuted}">${user.isMuted ? 'Quitar silencio' : 'Silenciar'}</button></div></div>`).join('') : '<div class="empty-card">Los usuarios aparecerán aquí cuando abran el Foro.</div>'}</section>`
                + '<section class="card"><h2>Mantenimiento</h2><div class="actions"><button id="adminCleanup" class="link-button" type="button">Limpiar ahora</button><button id="adminIntegrity" class="link-button" type="button">Comprobar integridad</button><button id="adminBackup" class="link-button" type="button">Descargar copia</button><a class="button button-secondary button-small" href="' + webRoot + 'index.html#!/configurationpage?name=CommunityConfiguration">Ajustes avanzados</a></div></section>';

            elements.content.querySelector('#newCategory').addEventListener('click', () => categoryDialog());
            elements.content.querySelectorAll('[data-edit-category]').forEach(button => button.addEventListener('click', () => {
                categoryDialog(state.categories.find(category => category.id === Number(button.dataset.editCategory)));
            }));
            elements.content.querySelectorAll('[data-user-moderator]').forEach(button => button.addEventListener('click', async () => {
                try {
                    await request(`admin/moderators/${button.dataset.userModerator}?enabled=${button.dataset.enabled}`, { method: 'PUT' });
                    await renderAdmin();
                } catch (error) {
                    showError(error);
                }
            }));
            elements.content.querySelectorAll('[data-user-mute]').forEach(button => button.addEventListener('click', async () => {
                const target = users.find(user => user.id === button.dataset.userMute);
                if (!target) return;
                try {
                    await request(`moderation/users/${button.dataset.userMute}/status`, {
                        method: 'PUT',
                        body: {
                            isSuspended: target.isSuspended,
                            suspendedUntilUtc: target.suspendedUntilUtc,
                            isMuted: button.dataset.muted === 'true',
                            mutedUntilUtc: null,
                            reason: 'Cambio desde Administración de Community'
                        }
                    });
                    await renderAdmin();
                } catch (error) {
                    showError(error);
                }
            }));
            elements.content.querySelector('#adminCleanup').addEventListener('click', async () => {
                try {
                    const result = await request('admin/maintenance/cleanup', { method: 'POST' });
                    setBanner(`Limpieza terminada: ${result.notifications} notificaciones, ${result.drafts} borradores y ${result.attachments} adjuntos.`, 'success');
                    await renderAdmin();
                } catch (error) { showError(error); }
            });
            elements.content.querySelector('#adminIntegrity').addEventListener('click', async () => {
                try {
                    const result = await request('admin/maintenance/integrity');
                    setBanner(`Integridad de la base de datos: ${result.result}.`, result.result === 'ok' ? 'success' : 'error');
                } catch (error) { showError(error); }
            });
            elements.content.querySelector('#adminBackup').addEventListener('click', () => void downloadBackup());
        } catch (error) {
            elements.content.innerHTML = '<div class="empty-card">No se pudo cargar la administración.</div>';
            showError(error);
        }
    }

    async function renderCurrentView() {
        updateTabs();
        if (state.view === 'notifications') return renderNotifications();
        if (state.view === 'moderation') return renderModeration();
        if (state.view === 'admin') return renderAdmin();
        return renderThreads();
    }

    async function initialize() {
        elements.back.href = webRoot;
        state.auth = findStoredAuthentication();
        if (!state.auth) {
            elements.content.innerHTML = `<div class="card"><h2>Inicia sesión en Jellyfin</h2><p>El Foro utiliza automáticamente tu sesión actual; no necesita IP, dominio ni contraseña propios.</p><a class="button button-primary" href="${escapeHtml(webRoot)}">Volver a Jellyfin</a></div>`;
            showError(new ForumApiError('No se encontró una sesión activa de Jellyfin en este dispositivo.', 401, 'missing_session'));
            return;
        }

        try {
            [state.me, state.categories] = await Promise.all([request('me'), request('categories')]);
            elements.user.textContent = state.me.username;
            elements.moderationTab.hidden = !state.me.isModerator;
            elements.adminTab.hidden = !state.me.isAdministrator;
            if (state.me.isSuspended) setBanner('Tu cuenta está suspendida en el Foro.', 'error');
            else if (state.me.isMuted) setBanner('Tu cuenta puede leer, pero está temporalmente silenciada.', 'error');
            await renderCurrentView();
        } catch (error) {
            elements.content.innerHTML = `<div class="card"><h2>El Foro no pudo iniciarse</h2><p>Vuelve a Jellyfin o entrega al administrador la referencia mostrada arriba.</p><a class="button button-secondary" href="${escapeHtml(webRoot)}">Volver a Jellyfin</a></div>`;
            showError(error);
        }
    }

    elements.tabs.addEventListener('click', event => {
        const tab = event.target.closest('[data-view]');
        if (!tab || tab.hidden) return;
        state.view = tab.dataset.view;
        state.page = 1;
        void renderCurrentView();
    });
    elements.searchForm.addEventListener('submit', event => {
        event.preventDefault();
        state.query = elements.search.value.trim();
        state.view = 'threads';
        state.page = 1;
        void renderCurrentView();
    });
    elements.newThread.addEventListener('click', openNewThreadDialog);
    elements.modalClose.addEventListener('click', closeModal);
    elements.modal.addEventListener('click', event => { if (event.target === elements.modal) closeModal(); });
    document.addEventListener('keydown', event => { if (event.key === 'Escape' && !elements.modal.hidden) closeModal(); });

    window.JellyfinCommunityForum15 = Object.freeze({ VERSION, apiRoot, normalizeJson, findStoredAuthentication });
    void initialize();
})();
