import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import vm from 'node:vm';

class StubElement {
    constructor() {
        this.hidden = false;
        this.innerHTML = '';
        this.textContent = '';
        this.className = '';
        this.style = {};
        this.dataset = {};
        this.classList = { toggle() {}, add() {}, remove() {} };
    }
    addEventListener() {}
    querySelector() { return null; }
    querySelectorAll() { return []; }
    replaceChildren() {}
    scrollIntoView() {}
    focus() {}
}

const elements = new Map();
const elementFor = selector => {
    if (!elements.has(selector)) elements.set(selector, new StubElement());
    return elements.get(selector);
};

const credentials = {
    Servers: [{
        Id: 'server-id',
        ManualAddress: 'https://media.example.test/jellyfin',
        UserId: '11111111-1111-1111-1111-111111111111',
        AccessToken: 'test-token'
    }]
};

globalThis.window = globalThis;
globalThis.location = {
    origin: 'https://media.example.test',
    pathname: '/jellyfin/Community/app',
    href: 'https://media.example.test/jellyfin/Community/app'
};
globalThis.localStorage = {
    getItem: key => key === 'jellyfin_credentials' ? JSON.stringify(credentials) : null
};
globalThis.document = {
    body: { style: {}, appendChild() {} },
    querySelector: elementFor,
    addEventListener() {},
    createElement: () => new StubElement()
};
globalThis.confirm = () => false;
globalThis.prompt = () => null;
globalThis.fetch = async () => {
    throw new Error('Network calls are not part of this source contract test.');
};

const sourcePath = new URL('../../src/Jellyfin.Plugin.Community/Web/communityForum15.js', import.meta.url);
const source = await fs.readFile(sourcePath, 'utf8');
vm.runInThisContext(source, { filename: sourcePath.pathname });

const forum = globalThis.JellyfinCommunityForum15;
assert.ok(forum, 'The standalone Forum contract was not exposed.');
assert.equal(forum.VERSION, '1.6.0.0');
assert.equal(forum.apiRoot, 'https://media.example.test/jellyfin/Community/api/v1/');

const normalized = forum.normalizeJson({
    Items: [{ Id: 7, Name: 'General', ThreadCount: 3, Nested: { CreatedUtc: '2026-08-08T12:00:00Z' } }],
    Page: 1,
    PageSize: 25,
    Total: 1
});
assert.equal(normalized.page, 1);
assert.equal(normalized.pageSize, 25);
assert.equal(normalized.items[0].id, 7);
assert.equal(normalized.items[0].name, 'General');
assert.equal(normalized.items[0].threadCount, 3);
assert.equal(normalized.items[0].nested.createdUtc, '2026-08-08T12:00:00Z');

const auth = forum.findStoredAuthentication();
assert.equal(auth.token, 'test-token');
assert.equal(auth.userId, '11111111-1111-1111-1111-111111111111');
assert.equal(auth.serverId, 'server-id');

const htmlPath = new URL('../../src/Jellyfin.Plugin.Community/Web/communityForum15.html', import.meta.url);
const html = await fs.readFile(htmlPath, 'utf8');
assert.match(source, /<select id=\\?"newCategory\\?"/u);
assert.doesNotMatch(source + html, /is=\\?"emby-select\\?"/u);
assert.doesNotMatch(html, /<script[^>]*>\s*[^<]/u, 'The standalone app must not require inline JavaScript.');

console.log(JSON.stringify({
    status: 'passed',
    pascalCaseContract: true,
    camelCaseContract: true,
    emptyArraySafe: forum.normalizeJson({ Items: [] }).items.length === 0,
    reverseProxySubpath: true,
    automaticServerUrl: true,
    automaticSessionDetection: true,
    nativeFormControls: true
}));
