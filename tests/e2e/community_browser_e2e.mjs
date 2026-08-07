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
    await page.locator('[data-jellyfin-community-menu]').first().click({ force: true });
    await page.locator('#CommunityPage').waitFor({ state: 'visible', timeout: 30_000 });
    await page.locator('#communityMain .community-grid').waitFor({ state: 'visible', timeout: 30_000 });
    await page.locator('.community-category').first().waitFor({ state: 'visible' });
}

const browser = await chromium.launch({ headless: true });
try {
    const userContext = await browser.newContext();
    const userPage = await userContext.newPage();
    const assertUserNoErrors = attachDiagnostics(userPage, 'ordinary-user');
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

    await userPage.locator('#communityNewThread').click();
    await userPage.locator('#newThreadForm').waitFor({ state: 'visible' });
    await userPage.locator('#newTitle').fill('Community browser E2E thread');
    await userPage.locator('#newBody').fill('Tema creado desde la interfaz real de Jellyfin Web.');
    await userPage.waitForTimeout(11_000);
    await userPage.locator('#newThreadForm button[type="submit"]').click();
    await userPage.locator('h2').filter({ hasText: 'Community browser E2E thread' }).waitFor({ state: 'visible', timeout: 30_000 });
    await userPage.locator('#communityReplyForm').waitFor({ state: 'visible' });
    assertUserNoErrors();
    await userContext.close();

    const adminContext = await browser.newContext();
    const adminPage = await adminContext.newPage();
    const assertAdminNoErrors = attachDiagnostics(adminPage, 'administrator');
    await login(adminPage, admin);
    await openCommunity(adminPage);
    await adminPage.locator('#communityAdminTab').waitFor({ state: 'visible' });
    await adminPage.locator('#communityModerationTab').waitFor({ state: 'visible' });
    await adminPage.locator('#communityAdminTab').click();
    await adminPage.getByText('Integración con Jellyfin Web', { exact: true }).waitFor({ state: 'visible', timeout: 30_000 });
    await adminPage.getByText('Usuarios conocidos', { exact: true }).waitFor({ state: 'visible' });
    await adminPage.getByText('Activa', { exact: true }).waitFor({ state: 'visible' });
    await adminPage.getByText('community-user', { exact: true }).waitFor({ state: 'visible' });
    assertAdminNoErrors();
    await adminContext.close();

    console.log(JSON.stringify({ status: 'passed', ordinaryUser: true, administrator: true, menu: true, createThread: true, adminPanel: true }));
} finally {
    await browser.close();
}
