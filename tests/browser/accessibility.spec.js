const { test, expect } = require('@playwright/test');
const AxeBuilder = require('@axe-core/playwright').default;
const { installJellyfinMocks, openPluginPage } = require('./support');

const pages = [
  ['configuration', 'KometaThemes', 'KometaThemesConfigPage', null],
  ['Theme Finder', 'KometaThemesSearch', 'KometaThemesSearchPage', 'test-item'],
  ['item management', 'KometaThemesItem', 'KometaThemesItemPage', 'test-item']
];

async function assertAccessible(page, selector, stateLabel) {
  const result = await new AxeBuilder({ page })
    .include(selector)
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  const severe = result.violations.filter(item => item.impact === 'serious' || item.impact === 'critical');
  expect(severe, stateLabel + '\n' + JSON.stringify(severe, null, 2)).toEqual([]);
}

for (const [label, name, pageId, itemId] of pages) {
  test(label + ' has no serious or critical axe violations', async ({ page }) => {
    const errors = await installJellyfinMocks(page);
    await openPluginPage(page, name, pageId, itemId);
    await page.addStyleTag({
      content: 'html,body{background:#10131d;color:#f5f7ff}.kt-page *,.kt-page *::before,.kt-page *::after{animation:none!important;transition:none!important}'
    });

    if (name === 'KometaThemes') {
      await expect(page.locator('#ktCacheStats .kt-stat')).toHaveCount(4);
      const tabs = page.locator('#ktTabs .kt-tab');
      for (let index = 0; index < await tabs.count(); index += 1) {
        await tabs.nth(index).click();
        await assertAccessible(page, '#' + pageId, label + ' tab ' + index);
      }
      await page.evaluate(() => { window.__a11yConfirm = window.KT.ui.confirm('Accessibility confirmation'); });
      await expect(page.getByRole('alertdialog')).toBeVisible();
      await assertAccessible(page, '.kt-modal', label + ' dialog');
      await page.keyboard.press('Escape');
    } else {
      await assertAccessible(page, '#' + pageId, label + ' initial');
    }

    if (name === 'KometaThemesSearch') {
      await page.locator('.kt-result').first().click();
      await expect(page.locator('#ktAnimeCard')).toBeVisible();
      await assertAccessible(page, '#' + pageId, label + ' selected anime');
    }

    expect(errors).toEqual([]);
  });
}
