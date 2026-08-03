const { test, expect } = require('@playwright/test');
const { installJellyfinMocks, openPluginPage } = require('./support');

test('configuration shell loads once and tabs support complete keyboard navigation', async ({ page }) => {
  const errors = await installJellyfinMocks(page);
  await openPluginPage(page, 'KometaThemes', 'KometaThemesConfigPage');

  const general = page.getByRole('tab', { name: 'General' });
  const themes = page.getByRole('tab', { name: 'Themes & Download' });
  await expect(general).toHaveAttribute('aria-selected', 'true');
  await general.focus();
  await general.press('ArrowRight');
  await expect(themes).toBeFocused();
  await expect(themes).toHaveAttribute('aria-selected', 'true');
  await themes.press('End');
  await expect(page.getByRole('tab', { name: 'Unresolved' })).toBeFocused();
  await page.getByRole('tab', { name: 'Unresolved' }).press('Home');
  await expect(general).toBeFocused();

  await page.locator('#KometaThemesConfigPage').evaluate(node => node.dispatchEvent(new Event('pageshow')));
  await expect(page.locator('script[id^="kt-js-KometaThemesCoreJs-"]')).toHaveCount(1);
  await expect(page.locator('script[id^="kt-js-KometaThemesConfigJs-"]')).toHaveCount(1);
  expect(errors).toEqual([]);
});

test('confirmation dialog traps focus, closes with Escape and restores focus', async ({ page }) => {
  const errors = await installJellyfinMocks(page);
  await openPluginPage(page, 'KometaThemes', 'KometaThemesConfigPage');

  const trigger = page.getByRole('button', { name: 'Sync now' });
  await trigger.focus();
  await page.evaluate(() => { window.__confirmResult = window.KT.ui.confirm('Delete test data?'); });
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cancel' })).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(page.getByRole('button', { name: 'Confirm' })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(trigger).toBeFocused();
  expect(errors).toEqual([]);
});

test('Theme Finder exposes searchable results with listbox keyboard behavior', async ({ page }) => {
  const errors = await installJellyfinMocks(page);
  await openPluginPage(page, 'KometaThemesSearch', 'KometaThemesSearchPage', 'test-item');

  const results = page.getByRole('listbox', { name: 'Results' });
  await expect(results).toBeVisible();
  await expect(page.getByRole('option', { name: /Cowboy Bebop/ })).toHaveCount(1);
  await results.focus();
  await results.press('End');
  await expect(results).toHaveAttribute('aria-activedescendant', 'kt-r-1');
  await results.press('Escape');
  await expect(results).not.toHaveAttribute('aria-activedescendant', /.+/);
  expect(errors).toEqual([]);
});

test('item page renders the selected item and remains re-entrant', async ({ page }) => {
  const errors = await installJellyfinMocks(page);
  await openPluginPage(page, 'KometaThemesItem', 'KometaThemesItemPage', 'test-item');

  await expect(page.locator('#ktItemTitle')).toHaveText('Cowboy Bebop');
  await expect(page.getByRole('button', { name: 'Sync this item' })).toBeVisible();
  await expect(page.locator('#ktThemesCount')).toHaveText('0');
  await page.locator('#KometaThemesItemPage').evaluate(node => node.dispatchEvent(new Event('pageshow')));
  await expect(page.locator('script[id^="kt-js-KometaThemesItemJs-"]')).toHaveCount(1);
  expect(errors).toEqual([]);
});



test('YouTube import asks for OP/ED and format, then posts the link', async ({ page }) => {
  const errors = await installJellyfinMocks(page);

  const posted = [];
  await page.route('**/Plugins/KometaThemes/Items/*/youtube', async route => {
    posted.push(route.request().postDataJSON());
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        title: 'Tank!',
        videoId: 'dQw4w9WgXcQ',
        results: [
          { mediaType: 'audio', fileName: 'OP2 - Tank!__50.mp3', success: true, skipped: false },
          { mediaType: 'video', fileName: 'OP2 - Tank!__50.webm', success: true, skipped: false }
        ]
      })
    });
  });

  await openPluginPage(page, 'KometaThemesSearch', 'KometaThemesSearchPage', 'test-item');

  // The card is only shown once the availability probe reports the extractor is usable.
  await expect(page.locator('#ktYtCard')).toBeVisible();
  await expect(page.locator('#ktYtForm')).toBeVisible();

  const url = page.locator('#ktYtUrl');
  await url.fill('not a youtube link');
  await page.getByRole('button', { name: 'Add theme' }).click();
  await expect(page.locator('#ktYtState')).toContainText('does not look like a YouTube video link');
  expect(posted).toHaveLength(0);

  await url.fill('https://www.youtube.com/watch?v=dQw4w9WgXcQ');
  await page.getByRole('button', { name: 'Add theme' }).click();

  // The OP/ED + format dialog must appear before anything is sent.
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText('Is this an opening or an ending?');
  await expect(dialog).toContainText('What should be imported?');
  expect(posted).toHaveLength(0);

  await page.locator('#ktYtType').selectOption('ED');
  await page.locator('#ktYtSequence').fill('2');
  await page.locator('#ktYtFormat').selectOption('Both');
  await page.locator('#ktYtName').fill('Tank!');
  await dialog.getByRole('button', { name: 'Confirm' }).click();

  await expect(page.locator('#ktYtState')).toContainText('Imported 2 file(s), 0 failed');
  expect(posted).toEqual([{
    url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ',
    themeType: 'ED',
    format: 'Both',
    sequence: 2,
    title: 'Tank!'
  }]);

  // A successful import clears the field so the next link starts clean.
  await expect(url).toHaveValue('');
  expect(errors).toEqual([]);
});

test('YouTube import card stays hidden when the extractor is missing', async ({ page }) => {
  const errors = await installJellyfinMocks(page);
  await page.route('**/Plugins/KometaThemes/YouTube/status', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ enabled: true, available: false, executablePath: '', error: 'yt-dlp was not found.' })
  }));

  await openPluginPage(page, 'KometaThemesSearch', 'KometaThemesSearchPage', 'test-item');

  // Nothing about the extractor reaches this page: no card, no form, and none of the
  // server's text anywhere in the document.
  await expect(page.locator('#ktYtCard')).toBeHidden();
  await expect(page.locator('#ktYtForm')).toBeHidden();
  await expect(page.locator('body')).not.toContainText('yt-dlp');
  expect(errors).toEqual([]);
});

test('YouTube import card stays hidden when the feature is turned off', async ({ page }) => {
  const errors = await installJellyfinMocks(page);
  await page.route('**/Plugins/KometaThemes/YouTube/status', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ enabled: false, available: false, executablePath: '', error: '' })
  }));

  await openPluginPage(page, 'KometaThemesSearch', 'KometaThemesSearchPage', 'test-item');

  await expect(page.locator('#ktYtCard')).toBeHidden();
  expect(errors).toEqual([]);
});

test('page shell reports a visible error when the bootstrap loader fails', async ({ page }) => {
  await page.route('**/configurationpage?name=KometaThemesLoaderJs*', route =>
    route.fulfill({ status: 404, contentType: 'text/plain', body: 'missing loader' }));
  await page.goto('/configurationpage?name=KometaThemes');
  await page.locator('#KometaThemesConfigPage').evaluate(node => node.dispatchEvent(new Event('pageshow')));
  await expect(page.locator('[data-kt-bootstrap-error]')).toContainText('could not load its page loader');
  await expect(page.locator('#KometaThemesConfigPage')).not.toHaveAttribute('data-kt-bound', '1');
});

test('asset loader stops initialization and reports a missing stylesheet', async ({ page }) => {
  await installJellyfinMocks(page);
  await page.route('**/configurationpage?name=KometaThemesCss*', route =>
    route.fulfill({ status: 404, contentType: 'text/plain', body: 'missing css' }));
  await page.goto('/configurationpage?name=KometaThemes');
  await page.locator('#KometaThemesConfigPage').evaluate(node => node.dispatchEvent(new Event('pageshow')));
  await expect(page.locator('[data-kt-loader-error]')).toContainText('KometaThemesCss');
  await expect(page.locator('#KometaThemesConfigPage')).not.toHaveAttribute('data-kt-bound', '1');
});

test('Jellyfin API fixture fails closed for an unknown plugin endpoint', async ({ page }) => {
  await installJellyfinMocks(page);
  await page.goto('/healthz');
  const result = await page.evaluate(() => fetch('/Plugins/KometaThemes/Unexpected').then(async response => ({
    status: response.status,
    body: await response.json()
  })));
  expect(result.status).toBe(404);
  expect(result.body.error).toContain('Unmocked endpoint');
});
