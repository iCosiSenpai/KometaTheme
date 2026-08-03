const defaultConfig = {
  UiLanguage: 'en',
  LibraryPattern: 'Anime',
  SyncIntervalHours: 24,
  AutoSyncNewItems: true,
  CleanupRemovedItems: false,
  NotifyAdminsOnSync: true,
  AudioSettings: {},
  VideoSettings: {},
  MovieSettings: { AudioSettings: {}, VideoSettings: {} },
  SeasonDetection: 'Auto',
  MaxThemesPerSeason: 0,
  FallbackMode: 'None',
  MaxParallelDownloads: 2,
  FfmpegTimeoutSeconds: 120,
  ForceSync: false,
  DryRun: false,
  ProviderPriority: ['AniList', 'MyAnimeList'],
  EnableTitleFallback: true,
  TitleMatchThreshold: 0.75,
  RateLimitPerMinute: 30,
  PositiveCacheTtlDays: 30,
  NegativeCacheTtlHours: 12,
  SkippedItems: [],
  MaintainPlaylist: true,
  PlaylistName: 'Anime Themes',
  PlaylistRootPath: '',
  EnableYouTubeImport: true,
  YtDlpPath: ''
};

async function installJellyfinMocks(page, configOverrides) {
  const config = Object.assign({}, defaultConfig, configOverrides || {});
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));

  await page.addInitScript(mockConfig => {
    window.Dashboard = {
      showLoadingMsg() {},
      hideLoadingMsg() {},
      navigate(url) { window.__lastNavigation = url; }
    };
    window.ApiClient = {
      accessToken() { return 'browser-test-token'; },
      getUrl(path) { return '/' + String(path).replace(/^\//, ''); },
      getPluginConfiguration() { return Promise.resolve(JSON.parse(JSON.stringify(mockConfig))); },
      updatePluginConfiguration() { return Promise.resolve({}); },
      getCurrentUserId() { return 'test-user'; },
      getItem() {
        return Promise.resolve({
          Id: 'test-item', Name: 'Cowboy Bebop', Type: 'Series', ProductionYear: 1998,
          ImageTags: {}, BackdropImageTags: []
        });
      },
      getScaledImageUrl() { return ''; }
    };
  }, config);

  await page.route('**/Plugins/KometaThemes/**', async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    let body;

    if (pathname.endsWith('/Health')) {
      body = { version: '1.2.0.0', isRunning: false, lastFullSyncUtc: null, lastSyncSummary: '' };
    } else if (pathname.endsWith('/Cache/stats')) {
      body = { TotalEntries: 0, TotalHits: 0, TotalMisses: 0, HitRatePercent: 0 };
    } else if (pathname.endsWith('/Skipped/items') || pathname.endsWith('/Failed/items') || pathname.endsWith('/Bindings')) {
      body = [];
    } else if (pathname.endsWith('/YouTube/status')) {
      body = { enabled: true, available: true, executablePath: '/usr/local/bin/yt-dlp', error: '' };
    } else if (pathname.endsWith('/youtube')) {
      body = { title: 'Tank!', videoId: 'dQw4w9WgXcQ', results: [{ mediaType: 'audio', fileName: 'OP1 - Tank!__50.mp3', success: true, skipped: false }] };
    } else if (pathname.endsWith('/Sync/status')) {
      body = { isFinished: true, phase: 'idle', totalItems: 0, processedItems: 0, resolvedItems: 0, downloadedItems: 0, skippedItems: 0, failedItems: 0 };
    } else if (pathname.endsWith('/eligible')) {
      body = { eligible: true };
    } else if (pathname.endsWith('/binding')) {
      body = { hasBinding: false };
    } else if (pathname.includes('/Anime/1/themes')) {
      body = {
        anime: { id: 1, name: 'Cowboy Bebop', year: 1998, season: 'Fall', mediaFormat: 'TV' },
        themes: [], seasonGroups: [], audioVolume: 0.5, videoVolume: 0.5
      };
    } else if (pathname.endsWith('/themes')) {
      body = [];
    } else if (pathname.endsWith('/info')) {
      body = {
        name: 'Cowboy Bebop', type: 'Series', productionYear: 1998,
        originalTitle: 'Cowboy Bebop', directoryPath: '/media/anime/Cowboy Bebop', filePath: '',
        themeStatus: { songsOnDisk: 0, videosOnDisk: 0, registeredSongs: 0, registeredVideos: 0 }
      };
    } else if (pathname.endsWith('/Search')) {
      body = {
        results: [{ id: 1, name: 'Cowboy Bebop', year: 1998, season: 'Fall', mediaFormat: 'TV', confidence: 'Exact', slug: 'cowboy-bebop' }],
        broadResults: [], retriedTitle: null
      };
    } else if (pathname.endsWith('/Logs')) {
      body = { entries: [] };
    } else {
      await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ error: 'Unmocked endpoint: ' + pathname }) });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  });

  return errors;
}

async function openPluginPage(page, name, pageId, itemId) {
  const suffix = itemId ? '&itemId=' + encodeURIComponent(itemId) : '';
  await page.goto('/configurationpage?name=' + encodeURIComponent(name) + suffix);
  await page.locator('#' + pageId).evaluate(node => node.dispatchEvent(new Event('pageshow')));
  await page.waitForFunction(id => {
    const node = document.getElementById(id);
    return node && node.dataset.ktBound === '1';
  }, pageId);
}

module.exports = { installJellyfinMocks, openPluginPage };
