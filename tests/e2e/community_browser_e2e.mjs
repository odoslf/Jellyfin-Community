import { chromium } from 'playwright';

const base = (process.env.JELLYFIN_URL || 'http://127.0.0.1:8096').replace(/\/$/, '');
const user = { name: 'community-user', password: 'community-user-password' };
const admin = { name: 'community-admin', password: 'community-admin-password' };

function attachDiagnostics(page, label) {
    const pageErrors = [];
    const communityConsoleErrors = [];
    page.on('pageerror', error => pageErrors.push(`${label}: ${error.message}`));
    page.on('console', message => {
        if (message.type() === 'error' && /community/i.test(message.text())) {
            communityConsoleErrors.push(`${label}: ${message.text()}`);
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
    await page.locator('#CommunityPage').waitFor({ state: 'visible', timeout: 30_000 });
    await page.locator('#communityMain .community-grid').waitFor({ state: 'visible', timeout: 30_000 });
    await page.locator('.community-category').first().waitFor({ state: 'visible' });
}

async function assertNoPageOverflow(page) {
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (overflow > 4) {
        throw new Error(`Community produces ${overflow}px of horizontal overflow on the mobile viewport.`);
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
    await adminPage.locator('#communityAdminTab').waitFor({ state: 'visible' });
    await adminPage.locator('#communityModerationTab').waitFor({ state: 'visible' });
    await adminPage.locator('#communityAdminTab').click();
    await adminPage.getByText('Integración con Jellyfin Web', { exact: true }).waitFor({ state: 'visible', timeout: 30_000 });
    await adminPage.getByText('Usuarios conocidos', { exact: true }).waitFor({ state: 'visible' });
    await adminPage.getByText('Activa', { exact: true }).waitFor({ state: 'visible' });
    await adminPage.getByText('community-user', { exact: true }).waitFor({ state: 'visible' });
    await assertNoPageOverflow(adminPage);
    await adminPage.screenshot({ path: 'artifacts/e2e-admin-mobile.png', fullPage: true });
    assertAdminNoErrors();
    await adminContext.close();

    console.log(JSON.stringify({
        status: 'passed',
        ordinaryUser: true,
        administrator: true,
        menu: true,
        createThread: true,
        adminPanel: true,
        mobileViewport: true,
        horizontalOverflow: false,
    }));
} finally {
    await browser.close();
}
