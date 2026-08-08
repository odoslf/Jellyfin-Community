import { chromium } from 'playwright';

const base = (process.env.JELLYFIN_URL || 'http://127.0.0.1:8096').replace(/\/$/, '');
const user = { name: 'community-user', password: 'community-user-password' };
const admin = { name: 'community-admin', password: 'community-admin-password' };
const KNOWN_JELLYFIN_SCROLL_ERROR = "Failed to execute 'scrollTo' on 'Element': Failed to read the 'behavior' property from 'ScrollOptions': The provided value 'null' is not a valid enum value of type ScrollBehavior.";
const COMMUNITY_TOOLBAR_SELECTORS = [
    '#communityClose',
    '#CommunityPage .community-toolbar h1',
    '#communitySearch',
    '#communitySearchButton',
    '#communityNewThread'
];

function attachDiagnostics(page, label) {
    const pageErrors = [];
    const communityConsoleErrors = [];
    page.on('pageerror', error => {
        if (error.message === KNOWN_JELLYFIN_SCROLL_ERROR) {
            console.log(`${label}: ignored known Jellyfin 10.10.7 scrollTo(null) browser error`);
            return;
        }
        const value = `${label}: ${error.message}`;
        pageErrors.push(value);
        console.error(value);
    });
    page.on('console', message => {
        if (message.type() === 'error' && /community/i.test(message.text())) {
            const value = `${label}: ${message.text()}`;
            communityConsoleErrors.push(value);
            console.error(value);
        }
    });
    return () => {
        if (pageErrors.length || communityConsoleErrors.length) {
            throw new Error([...pageErrors, ...communityConsoleErrors].join('\n'));
        }
    };
}

async function login(page, credentials) {
    await page.goto(`${base}/web/index.html#!/login.html`, { waitUntil: 'domcontentloaded' });
    await page.locator('#loginPage').waitFor({ state: 'visible', timeout: 30_000 });
    await page.waitForFunction(() => {
        const manual = document.querySelector('.manualLoginForm');
        const button = document.querySelector('.btnManual');
        return (manual && !manual.classList.contains('hide'))
            || (button && !button.classList.contains('hide'));
    }, null, { timeout: 30_000 });

    if (!(await page.locator('.manualLoginForm').isVisible())) {
        await page.locator('.btnManual').click();
    }

    await page.locator('#txtManualName').fill(credentials.name);
    await page.locator('#txtManualPassword').fill(credentials.password);
    await page.locator('.manualLoginForm button[type="submit"]').click();
    await page.waitForFunction(() => typeof window.ApiClient?.accessToken === 'function' && Boolean(window.ApiClient.accessToken()), null, { timeout: 30_000 });
    await page.waitForFunction(() => Boolean(window.JellyfinCommunityBootstrap), null, { timeout: 30_000 });
    await page.locator('[data-jellyfin-community-menu]').first().waitFor({ state: 'attached', timeout: 30_000 });
}

async function waitForCommunityReady(page) {
    await page.locator('#CommunityPage').waitFor({ state: 'visible', timeout: 30_000 });
    await page.waitForFunction(() => {
        return Boolean(document.querySelector('#communityMain .community-grid'))
            || Boolean(document.querySelector('#communityBanner .community-error'));
    }, null, { timeout: 30_000 });

    const errorBanner = page.locator('#communityBanner .community-error');
    if (await errorBanner.count()) {
        throw new Error(`Community initialization error: ${await errorBanner.innerText()}`);
    }
    await page.locator('#communityMain .community-grid').waitFor({ state: 'visible', timeout: 10_000 });
    await page.locator('.community-category').first().waitFor({ state: 'visible', timeout: 10_000 });
}

async function openCommunityFromNormalMenu(page) {
    const drawerButton = page.locator('.mainDrawerButton:not(.hide)').first();
    await drawerButton.waitFor({ state: 'visible', timeout: 30_000 });
    await drawerButton.click();
    await page.locator('.mainDrawer.drawer-open').waitFor({ state: 'attached', timeout: 30_000 });
    const communityLink = page.locator('[data-jellyfin-community-menu]').first();
    await communityLink.waitFor({ state: 'visible', timeout: 30_000 });
    await communityLink.click();
    await waitForCommunityReady(page);
}

async function openCommunityFromDashboardRoute(page) {
    await page.evaluate(() => {
        window.Dashboard.navigate(window.Dashboard.getPluginUrl('Community'));
    });
    await page.waitForFunction(() => location.href.toLowerCase().includes('configurationpage') && location.href.includes('name=Community'), null, { timeout: 30_000 });
    await waitForCommunityReady(page);
}

async function assertCategoryDataIsUsable(page) {
    const labels = await page.locator('.community-category').evaluateAll(elements =>
        elements.map(element => element.textContent?.trim() || '').filter(Boolean));
    if (labels.length < 2 || !labels.some(label => label !== 'Todas')) {
        throw new Error(`Community rendered blank category labels: ${JSON.stringify(labels)}`);
    }

    await page.locator('#communityNewThread').click();
    await page.locator('#newThreadForm').waitFor({ state: 'visible', timeout: 10_000 });
    const options = await page.locator('#newCategory option').evaluateAll(elements =>
        elements.map(element => ({ value: element.value, text: element.textContent?.trim() || '' })));
    if (!options.length || options.some(option => !option.value || !option.text)) {
        throw new Error(`Community rendered invalid category options: ${JSON.stringify(options)}`);
    }
    return options;
}

async function assertNoPageOverflow(page) {
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (overflow > 4) {
        throw new Error(`Community produces ${overflow}px of horizontal overflow on the mobile viewport.`);
    }
}

async function assertCommunityToolbarExposed(page) {
    await page.waitForFunction(selectors => selectors.every(selector => {
        const element = document.querySelector(selector);
        if (!(element instanceof HTMLElement)) return false;
        const rect = element.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0 || rect.bottom <= 0 || rect.top >= innerHeight || rect.right <= 0 || rect.left >= innerWidth) return false;
        const x = Math.min(innerWidth - 1, Math.max(0, rect.left + rect.width / 2));
        const y = Math.min(innerHeight - 1, Math.max(0, rect.top + rect.height / 2));
        const topElement = document.elementFromPoint(x, y);
        return Boolean(topElement && (topElement === element || element.contains(topElement)));
    }), COMMUNITY_TOOLBAR_SELECTORS, { timeout: 10_000, polling: 100 });
}

async function runOrdinaryUser(browser) {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 });
    try {
        const page = await context.newPage();
        const assertNoErrors = attachDiagnostics(page, 'ordinary-user-mobile');
        await login(page, user);
        await openCommunityFromNormalMenu(page);
        await assertCommunityToolbarExposed(page);
        await page.locator('text=Community E2E thread').first().waitFor({ state: 'visible', timeout: 20_000 });

        if (await page.locator('#communityAdminTab').isVisible()) throw new Error('Ordinary users must not see Community administration.');
        if (await page.locator('#communityModerationTab').isVisible()) throw new Error('Ordinary users must not see Community moderation.');

        await assertCategoryDataIsUsable(page);
        await page.locator('#newTitle').fill('Community browser E2E thread');
        await page.locator('#newBody').fill('Tema creado desde la navegación normal de Jellyfin Web.');
        await page.waitForTimeout(11_000);
        await page.locator('#newThreadForm button[type="submit"]').click();
        await page.locator('h2').filter({ hasText: 'Community browser E2E thread' }).waitFor({ state: 'visible', timeout: 30_000 });
        await page.locator('#communityReplyForm').waitFor({ state: 'visible', timeout: 10_000 });
        await assertNoPageOverflow(page);
        await page.screenshot({ path: 'artifacts/e2e-user-mobile.png', fullPage: true });
        assertNoErrors();
    } finally {
        await context.close();
    }
}

async function runAdminNormalMenu(browser) {
    const context = await browser.newContext({ viewport: { width: 430, height: 932 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 });
    try {
        const page = await context.newPage();
        const assertNoErrors = attachDiagnostics(page, 'administrator-mobile');
        await login(page, admin);
        await openCommunityFromNormalMenu(page);
        await assertCommunityToolbarExposed(page);
        await page.locator('#communityAdminTab').waitFor({ state: 'visible', timeout: 10_000 });
        await page.locator('#communityModerationTab').waitFor({ state: 'visible', timeout: 10_000 });
        await page.locator('#communityAdminTab').click();
        await page.getByText('Integración con Jellyfin Web', { exact: true }).waitFor({ state: 'visible', timeout: 30_000 });
        await page.getByText('Usuarios conocidos', { exact: true }).waitFor({ state: 'visible', timeout: 10_000 });
        await page.getByText('Activa', { exact: true }).waitFor({ state: 'visible', timeout: 10_000 });
        await page.getByText('community-user', { exact: true }).waitFor({ state: 'visible', timeout: 10_000 });
        await assertNoPageOverflow(page);
        await page.screenshot({ path: 'artifacts/e2e-admin-mobile.png', fullPage: true });
        assertNoErrors();
    } finally {
        await context.close();
    }
}

async function runAdminDashboardRoute(browser) {
    const context = await browser.newContext({ viewport: { width: 430, height: 932 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 });
    try {
        const page = await context.newPage();
        const assertNoErrors = attachDiagnostics(page, 'administrator-dashboard-route');
        await login(page, admin);
        await openCommunityFromDashboardRoute(page);
        await assertCategoryDataIsUsable(page);
        await page.locator('#newTitle').fill('Community dashboard E2E thread');
        await page.locator('#newBody').fill('Tema creado desde la ruta configurationpage real que fallaba en Community 1.2.');
        await page.locator('#newThreadForm button[type="submit"]').click();
        await page.locator('h2').filter({ hasText: 'Community dashboard E2E thread' }).waitFor({ state: 'visible', timeout: 30_000 });
        await page.locator('#communityReplyForm').waitFor({ state: 'visible', timeout: 10_000 });
        await page.locator('#communityAdminTab').waitFor({ state: 'visible', timeout: 10_000 });
        await assertNoPageOverflow(page);
        await page.screenshot({ path: 'artifacts/e2e-admin-dashboard-route.png', fullPage: true });
        assertNoErrors();
    } catch (error) {
        console.log(`administrator-dashboard-route failure: ${error?.stack || error}`);
        throw error;
    } finally {
        await context.close();
    }
}

const browser = await chromium.launch({ headless: true });
try {
    await runOrdinaryUser(browser);
    await runAdminNormalMenu(browser);
    await runAdminDashboardRoute(browser);
    console.log(JSON.stringify({
        status: 'passed',
        ordinaryUser: true,
        administrator: true,
        menu: true,
        createThread: true,
        adminPanel: true,
        dashboardFallback: true,
        dashboardCategoryLabels: true,
        dashboardCreateThread: true,
        mobileViewport: true,
        toolbarVisible: true,
        horizontalOverflow: false
    }));
} catch (error) {
    console.log(`browser-e2e failure: ${error?.stack || error}`);
    throw error;
} finally {
    await browser.close();
}
