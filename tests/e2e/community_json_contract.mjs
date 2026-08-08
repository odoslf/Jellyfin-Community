import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const sourcePath = new URL('../../src/Jellyfin.Plugin.Community/Web/communityPageController13.js', import.meta.url);
const source = await fs.readFile(sourcePath, 'utf8');
const moduleUrl = `data:text/javascript;base64,${Buffer.from(source).toString('base64')}`;
const module = await import(moduleUrl);

const payload = {
    Items: [
        {
            Id: 7,
            Name: 'General',
            ThreadCount: 3,
            IsArchived: false,
            Tags: ['jellyfin'],
            Nested: { CreatedUtc: '2026-08-08T12:00:00Z' }
        }
    ],
    Page: 1,
    PageSize: 25,
    Total: 1
};

const normalized = module.normalizeCommunityJson(payload);
assert.equal(normalized.page, 1);
assert.equal(normalized.pageSize, 25);
assert.equal(normalized.total, 1);
assert.ok(Array.isArray(normalized.items));
assert.equal(normalized.items.length, 1);
assert.equal(normalized.items[0].id, 7);
assert.equal(normalized.items[0].name, 'General');
assert.equal(normalized.items[0].threadCount, 3);
assert.equal(normalized.items[0].isArchived, false);
assert.deepEqual(normalized.items[0].tags, ['jellyfin']);
assert.equal(normalized.items[0].nested.createdUtc, '2026-08-08T12:00:00Z');

const alreadyCamel = module.normalizeCommunityJson({ items: [{ id: 4, name: 'Noticias' }] });
assert.equal(alreadyCamel.items[0].name, 'Noticias');

console.log(JSON.stringify({
    status: 'passed',
    pascalCaseContract: true,
    camelCaseContract: true,
    emptyArraySafe: module.normalizeCommunityJson({ Items: [] }).items.length === 0
}));
