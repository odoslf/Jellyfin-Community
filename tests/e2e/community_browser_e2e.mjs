import { chromium } from 'playwright';

const base = (process.env.JELLYFIN_URL || 'http://127.0.0.1:8096').replace(/\/$/, '');
const user = { name: 'community-user', password: 'community-user-password' };
const admin = { name: 'community-admin', password: 'community-admin-password' };
const KNOWN_JELLYFIN_SCROLL_ERROR = "Failed to execute 'scrollTo' on 'Element': Failed to read the 'behavior' property from 'ScrollOptions': The provided value 'null' is not a valid enum value of type ScrollBehavior.";

function attachDiagnostics(page, label) {
    const errors = [];
    page.on('pageerror', error => {
        if (error.message === KNOWN_JELLYFIN_SCROLL_ERROR) return;
        errors.push(`${label}: ${error.message}`);
    });
    page.on('console', message => {
        if (message.type() === 'error' && /community|foro/i.test(message.text())) {
            errors.push(`${label}: ${message.text()}`);
        }
    });
    return () => {
        if (errors.length) throw new Error(errors.join('\n'));
    };
}

async function login(page, credentials) {
    await page.goto(`${base}/web/index.html#!/login.html`, { waitUntil: 'domcontentloaded' });
    await page.locator('#loginPage').waitFor({ state: 'visible', timeout: 30_000 });
    await page.waitForFunction(() => {
        const manual = document.querySelector('.manualLoginForm');
        const button = document.querySelector('.btnManual');
        return (manual && !manual.classList.contains('hide')) || (button && !button.classList.contains('hide'));
    }, null, { timeout: 30_000 });

    if (!(await page.locator('.manualLoginForm').isVisible())) await page.locator('.btnManual').click();
    await page.locator('#txtManualName').fill(credentials.name);
    await page.locator('#txtManualPassword').fill(credentials.password);
    await page.locator('.manualLoginForm button[type="submit"]').click();
    await page.waitForFunction(() => typeof window.ApiClient?.accessToken === 'function' && Boolean(window.ApiClient.accessToken()), null, { timeout: 30_000 });
    await page.waitForFunction(() => Boolean(window.JellyfinCommunityBootstrap?.VERSION === '1.6.0.0'), null, { timeout: 30_000 });
    // Authentication completes before Jellyfin's own post-login navigation has
    // necessarily settled.  Waiting for the normal shell prevents that pending
    // navigation from racing (and replacing) a subsequent plugin route.
    await page.locator('.mainDrawerButton:not(.hide)').first().waitFor({ state: 'visible', timeout: 30_000 });
}

async function openForumFromNormalMenu(page) {
    const drawerButton = page.locator('.mainDrawerButton:not(.hide)').first();
    await drawerButton.waitFor({ state: 'visible', timeout: 30_000 });
    await drawerButton.click();
    await page.locator('.mainDrawer.drawer-open').waitFor({ state: 'attached', timeout: 30_000 });

    const forumLink = page.locator('a[data-jellyfin-community-menu]').filter({ hasText: 'Foro' }).first();
    await forumLink.waitFor({ state: 'visible', timeout: 30_000 });
    const link = await forumLink.evaluate(anchor => ({ href: anchor.href, target: anchor.target }));
    if (!/\/Community\/app(?:\?|$)/i.test(link.href)) throw new Error(`Unexpected Forum URL: ${link.href}`);
    if (link.target !== '_self') throw new Error(`Forum link must stay in the Android WebView, got target=${link.target}`);
    await forumLink.click();

    await page.waitForURL(url => /\/Community\/app(?:\?|$)/i.test(url.pathname + url.search), { timeout: 30_000 });
    await page.waitForFunction(() => Boolean(window.JellyfinCommunityForum15?.VERSION === '1.6.0.0'), null, { timeout: 30_000 });
    await page.locator('#forumContent .forum-grid').waitFor({ state: 'visible', timeout: 30_000 });
    const error = page.locator('#forumBanner.error');
    if (await error.isVisible()) throw new Error(`Forum initialization error: ${await error.innerText()}`);
}

async function assertNativeCategorySelector(page) {
    await page.locator('#newThreadButton').click();
    const form = page.locator('#newThreadForm');
    await form.waitFor({ state: 'visible', timeout: 10_000 });
    const select = form.locator('#newCategory');
    const options = await select.locator('option').evaluateAll(items => items.map(item => ({ value: item.value, text: item.textContent?.trim() || '' })));
    if (!options.length || options.some(option => !option.value || !option.text)) {
        throw new Error(`Forum rendered invalid category options: ${JSON.stringify(options)}`);
    }
    const implementation = await select.evaluate(element => ({ tag: element.tagName, customizedBuiltIn: element.hasAttribute('is') }));
    if (implementation.tag !== 'SELECT' || implementation.customizedBuiltIn) {
        throw new Error(`Category selector is not a native select: ${JSON.stringify(implementation)}`);
    }
    return form;
}

async function assertNoOverflow(page) {
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (overflow > 4) throw new Error(`Forum produces ${overflow}px of horizontal overflow.`);
}

async function runOrdinaryUser(browser) {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 });
    try {
        const page = await context.newPage();
        const assertNoErrors = attachDiagnostics(page, 'ordinary-user-android-webview');
        await login(page, user);
        await openForumFromNormalMenu(page);

        await page.getByText('Community E2E thread', { exact: true }).first().waitFor({ state: 'visible', timeout: 20_000 });
        if (await page.locator('#adminTab').isVisible()) throw new Error('Ordinary users must not see Forum administration.');
        if (await page.locator('#moderationTab').isVisible()) throw new Error('Ordinary users must not see Forum moderation.');

        const form = await assertNativeCategorySelector(page);
        await form.locator('#newTitle').fill('Community browser E2E thread 1.5');
        await form.locator('#newBody').fill('Tema creado por un usuario normal desde el menú Foro de Jellyfin.');
        await page.waitForTimeout(11_000);
        await form.locator('button[type="submit"]').click();
        await page.locator('h2').filter({ hasText: 'Community browser E2E thread 1.5' }).waitFor({ state: 'visible', timeout: 30_000 });
        await page.locator('#replyForm').waitFor({ state: 'visible', timeout: 10_000 });
        await assertNoOverflow(page);
        await page.screenshot({ path: 'artifacts/e2e-user-mobile.png', fullPage: true });
        assertNoErrors();
    } finally {
        await context.close();
    }
}

async function runAdministrator(browser) {
    const context = await browser.newContext({ viewport: { width: 430, height: 932 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 });
    try {
        const page = await context.newPage();
        const assertNoErrors = attachDiagnostics(page, 'administrator-android-webview');
        await login(page, admin);
        await openForumFromNormalMenu(page);

        await page.locator('#adminTab').waitFor({ state: 'visible', timeout: 10_000 });
        await page.locator('#moderationTab').waitFor({ state: 'visible', timeout: 10_000 });
        await page.locator('#adminTab').click();
        await page.getByText('Acceso de usuarios', { exact: true }).waitFor({ state: 'visible', timeout: 30_000 });
        await page.getByText('Categorías', { exact: true }).last().waitFor({ state: 'visible', timeout: 10_000 });
        await page.getByText('Usuarios y moderadores', { exact: true }).waitFor({ state: 'visible', timeout: 10_000 });
        await page.getByText('community-user', { exact: true }).waitFor({ state: 'visible', timeout: 10_000 });
        await assertNoOverflow(page);
        await page.screenshot({ path: 'artifacts/e2e-admin-mobile.png', fullPage: true });
        assertNoErrors();
    } finally {
        await context.close();
    }
}

async function runAdministratorSettings(browser) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    try {
        const page = await context.newPage();
        const assertNoErrors = attachDiagnostics(page, 'administrator-settings');
        await login(page, admin);
        await page.evaluate(() => window.Dashboard.navigate(window.Dashboard.getPluginUrl('CommunityConfiguration')));
        await page.waitForURL(url => url.href.includes('name=CommunityConfiguration'), { timeout: 30_000 });
        await page.locator('#CommunityConfigurationForm').waitFor({ state: 'visible', timeout: 30_000 });
        await page.getByText('Activar Community', { exact: true }).waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: 'artifacts/e2e-admin-settings.png', fullPage: true });
        assertNoErrors();
    } finally {
        await context.close();
    }
}

const browser = await chromium.launch({ headless: true });
try {
    await runOrdinaryUser(browser);
    await runAdministrator(browser);
    await runAdministratorSettings(browser);
    console.log(JSON.stringify({
        status: 'passed',
        ordinaryUser: true,
        administrator: true,
        officialMenuLink: true,
        sameAndroidWebView: true,
        automaticSessionDetection: true,
        nativeCategorySelector: true,
        createThread: true,
        adminPanelSeparated: true,
        mobileViewport: true,
        horizontalOverflow: false
    }));
} finally {
    await browser.close();
}
