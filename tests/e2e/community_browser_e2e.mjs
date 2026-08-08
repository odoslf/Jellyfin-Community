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

    const manualForm = page.locator('.manualLoginForm');
    if (!(await manualForm.isVisible())) {
        await page.locator('.btnManual').click();
    }

    await page.locator('#txtManualName').waitFor({ state: 'visible' });
    await page.locator('#txtManualName').fill(credentials.name);
    await page.locator('#txtManualPassword').fill(credentials.password);
    await page.locator('.manualLoginForm button[type="submit"]').click();
    await page.waitForFunction(() => typeof window.ApiClient?.accessToken === 'function' && Boolean(window.ApiClient.accessToken()), null, { timeout: 30_000 });
    await page.waitForFunction(() => Boolean(window.JellyfinCommunityBootstrap), null, { timeout: 30_000 });
    await page.locator('[data-jellyfin-community-menu]').first().waitFor({ state: 'attached', timeout: 30_000 });
}

async function waitForCommunityReady(page) {
    await page.locator('#CommunityPage').waitFor({ state: 'visible', timeout: 30_000 });
    try {
        await page.waitForFunction(() => {
            return Boolean(document.querySelector('#communityMain .community-grid'))
                || Boolean(document.querySelector('#communityBanner .community-error'));
        }, null, { timeout: 30_000 });
    } catch (error) {
        const snapshot = await page.locator('#CommunityPage').innerText().catch(() => '<CommunityPage missing>');
        throw new Error(`Community controller did not finish initialization. Visible page text:\n${snapshot}\n${error.message}`);
    }

    const errorBanner = page.locator('#communityBanner .community-error');
    if (await errorBanner.count()) {
        throw new Error(`Community initialization error: ${await errorBanner.innerText()}`);
    }
    await page.locator('#communityMain .community-grid').waitFor({ state: 'visible', timeout: 5_000 });
    await page.locator('.community-category').first().waitFor({ state: 'visible' });
}

async function openCommunity(page) {
    const communityLink = page.locator('[data-jellyfin-community-menu]').first();
    const drawerButton = page.locator('.mainDrawerButton:not(.hide)').first();
    await drawerButton.waitFor({ state: 'visible', timeout: 30_000 });
    await drawerButton.click();
    await page.locator('.mainDrawer.drawer-open').waitFor({ state: 'attached', timeout: 30_000 });
    await page.waitForFunction(() => {
        const link = document.querySelector('[data-jellyfin-community-menu]');
        if (!link) return false;
        const rect = link.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0
            && rect.right > 0 && rect.left < window.innerWidth
            && rect.bottom > 0 && rect.top < window.innerHeight;
    }, null, { timeout: 30_000 });

    await communityLink.click();
    await waitForCommunityReady(page);
}

async function openCommunityFromDashboardConfiguration(page) {
    await page.evaluate(() => window.JellyfinCommunityBootstrap.closeCommunity());
    await page.waitForFunction(() => !document.querySelector('#CommunityPage'), null, { timeout: 10_000 });
    await page.evaluate(() => {
        window.Dashboard.navigate(window.Dashboard.getPluginUrl('Community'));
    });
    await waitForCommunityReady(page);

    const visibleCategoryLabels = await page.locator('.community-category').evaluateAll(elements =>
        elements.map(element => element.textContent?.trim() || '').filter(Boolean));
    if (visibleCategoryLabels.length < 2 || !visibleCategoryLabels.some(label => label !== 'Todas')) {
        throw new Error(`Dashboard Community route rendered blank category labels: ${JSON.stringify(visibleCategoryLabels)}`);
    }

    await page.locator('#communityNewThread').click();
    await page.locator('#newThreadForm').waitFor({ state: 'visible' });
    const categoryOptions = await page.locator('#newCategory option').evaluateAll(options =>
        options.map(option => ({ value: option.value, text: option.textContent?.trim() || '' })));
    if (!categoryOptions.length || categoryOptions.some(option => !option.value || !option.text)) {
        throw new Error(`Dashboard Community route produced invalid category options: ${JSON.stringify(categoryOptions)}`);
    }

    await page.locator('#newTitle').fill('Community dashboard E2E thread');
    await page.locator('#newBody').fill('Tema creado desde la ruta de configuración real para reproducir el fallo observado en 1.2.');
    await page.waitForTimeout(11_000);
    await page.locator('#newThreadForm button[type="submit"]').click();
    await page.locator('h2').filter({ hasText: 'Community dashboard E2E thread' }).waitFor({ state: 'visible', timeout: 30_000 });
    await page.locator('#communityReplyForm').waitFor({ state: 'visible' });
}

async function assertNoPageOverflow(page) {
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (overflow > 4) {
        throw new Error(`Community produces ${overflow}px of horizontal overflow on the mobile viewport.`);
    }
}

async function getCommunityToolbarChecks(page) {
    return page.evaluate(selectors => selectors.map(selector => {
        const element = document.querySelector(selector);
        if (!(element instanceof HTMLElement)) {
            return { selector, ok: false, reason: 'missing' };
        }

        const rect = element.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0
            || rect.bottom <= 0 || rect.top >= window.innerHeight
            || rect.right <= 0 || rect.left >= window.innerWidth) {
            return {
                selector,
                ok: false,
                reason: 'outside viewport',
                rect: { top: rect.top, bottom: rect.bottom, left: rect.left, right: rect.right }
            };
        }

        const x = Math.min(window.innerWidth - 1, Math.max(0, rect.left + rect.width / 2));
        const y = Math.min(window.innerHeight - 1, Math.max(0, rect.top + rect.height / 2));
        const topElement = document.elementFromPoint(x, y);
        const unoccluded = Boolean(topElement && (topElement === element || element.contains(topElement)));
        return {
            selector,
            ok: unoccluded,
            reason: unoccluded ? 'visible' : `occluded by ${topElement?.tagName || 'nothing'}.${topElement?.className || ''}`,
            rect: { top: rect.top, bottom: rect.bottom, left: rect.left, right: rect.right }
        };
    }), COMMUNITY_TOOLBAR_SELECTORS);
}

async function assertCommunityToolbarExposed(page) {
    try {
        await page.waitForFunction(selectors => selectors.every(selector => {
            const element = document.querySelector(selector);
            if (!(element instanceof HTMLElement)) {
                return false;
            }

            const rect = element.getBoundingClientRect();
            if (rect.width <= 0 || rect.height <= 0
                || rect.bottom <= 0 || rect.top >= window.innerHeight
                || rect.right <= 0 || rect.left >= window.innerWidth) {
                return false;
            }

            const x = Math.min(window.innerWidth - 1, Math.max(0, rect.left + rect.width / 2));
            const y = Math.min(window.innerHeight - 1, Math.max(0, rect.top + rect.height / 2));
            const topElement = document.elementFromPoint(x, y);
            return Boolean(topElement && (topElement === element || element.contains(topElement)));
        }), COMMUNITY_TOOLBAR_SELECTORS, { timeout: 5_000, polling: 100 });
    } catch (error) {
        const checks = await getCommunityToolbarChecks(page);
        const failures = checks.filter(check => !check.ok);
        throw new Error(`Community toolbar did not become fully visible and clickable after Jellyfin's transient navigation finished: ${JSON.stringify(failures)}. ${error.message}`);
    }
}

const browser = await chromium.launch({ headless: true });
try {
    const userContext = await browser.newContext({
        viewport: { width: 390, height: 844 },
        isMobile: true,
        hasTouch: true,
        deviceScaleFactor: 1,
    });
    const userPage = await userContext.newPage();
    const assertUserNoErrors = attachDiagnostics(userPage, 'ordinary-user-mobile');
    await login(userPage, user);
    await openCommunity(userPage);
    await assertCommunityToolbarExposed(userPage);

    await userPage.locator('text=Community E2E thread').first().waitFor({ state: 'visible', timeout: 20_000 });
    await userPage.locator('#communityAdminTab').waitFor({ state: 'attached' });
    if (await userPage.locator('#communityAdminTab').isVisible()) {
        throw new Error('Ordinary users must not see the Community administration tab.');
    }
    if (await userPage.locator('#communityModerationTab').isVisible()) {
        throw new Error('Ordinary users must not see the Community moderation tab.');
    }

    await assertNoPageOverflow(userPage);
    await userPage.locator('#communityNewThread').click();
    await userPage.locator('#newThreadForm').waitFor({ state: 'visible' });
    await userPage.locator('#newTitle').fill('Community browser E2E thread');
    await userPage.locator('#newBody').fill('Tema creado desde la interfaz real de Jellyfin Web.');
    await userPage.waitForTimeout(11_000);
    await userPage.locator('#newThreadForm button[type="submit"]').click();
    await userPage.locator('h2').filter({ hasText: 'Community browser E2E thread' }).waitFor({ state: 'visible', timeout: 30_000 });
    await userPage.locator('#communityReplyForm').waitFor({ state: 'visible' });
    await assertNoPageOverflow(userPage);
    await userPage.screenshot({ path: 'artifacts/e2e-user-mobile.png', fullPage: true });
    assertUserNoErrors();
    await userContext.close();

    const adminContext = await browser.newContext({
        viewport: { width: 430, height: 932 },
        isMobile: true,
        hasTouch: true,
        deviceScaleFactor: 1,
    });
    const adminPage = await adminContext.newPage();
    const assertAdminNoErrors = attachDiagnostics(adminPage, 'administrator-mobile');
    await login(adminPage, admin);
    await openCommunity(adminPage);
    await assertCommunityToolbarExposed(adminPage);
    await adminPage.locator('#communityAdminTab').waitFor({ state: 'visible' });
    await adminPage.locator('#communityModerationTab').waitFor({ state: 'visible' });
    await adminPage.locator('#communityAdminTab').click();
    await adminPage.getByText('Integración con Jellyfin Web', { exact: true }).waitFor({ state: 'visible', timeout: 30_000 });
    await adminPage.getByText('Usuarios conocidos', { exact: true }).waitFor({ state: 'visible' });
    await adminPage.getByText('Activa', { exact: true }).waitFor({ state: 'visible' });
    await adminPage.getByText('community-user', { exact: true }).waitFor({ state: 'visible' });
    await assertNoPageOverflow(adminPage);
    await adminPage.screenshot({ path: 'artifacts/e2e-admin-mobile.png', fullPage: true });

    // Reproduce the exact path that failed in the user's 1.2 screenshots: open the
    // Community PluginPage through Jellyfin's Dashboard/configuration-page router,
    // not through the injected normal-user menu. Category names and creation must
    // work here as well, proving the JSON normalizer is not bootstrap-dependent.
    await openCommunityFromDashboardConfiguration(adminPage);
    await assertNoPageOverflow(adminPage);
    await adminPage.screenshot({ path: 'artifacts/e2e-admin-dashboard-route.png', fullPage: true });

    assertAdminNoErrors();
    await adminContext.close();

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
        horizontalOverflow: false,
    }));
} finally {
    await browser.close();
}
