const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..');
const resources = {
  KometaThemes: 'Jellyfin.Plugin.KometaThemes/Configuration/configPage.html',
  KometaThemesSearch: 'Jellyfin.Plugin.KometaThemes/Web/SearchPage.html',
  KometaThemesItem: 'Jellyfin.Plugin.KometaThemes/Configuration/itemPage.html',
  KometaThemesCss: 'Jellyfin.Plugin.KometaThemes/Web/assets/kometa.css',
  KometaThemesLoaderJs: 'Jellyfin.Plugin.KometaThemes/Web/assets/kometa-loader.js',
  KometaThemesCoreJs: 'Jellyfin.Plugin.KometaThemes/Web/assets/kometa-core.js',
  KometaThemesA11yJs: 'Jellyfin.Plugin.KometaThemes/Web/assets/kometa-a11y.js',
  KometaThemesConfigJs: 'Jellyfin.Plugin.KometaThemes/Web/assets/config.js',
  KometaThemesSearchJs: 'Jellyfin.Plugin.KometaThemes/Web/assets/search.js',
  KometaThemesItemJs: 'Jellyfin.Plugin.KometaThemes/Web/assets/item.js',
  KometaThemesIconPng: 'Jellyfin.Plugin.KometaThemes/Web/assets/kometathemes-icon.png'
};

function typeFor(file) {
  if (file.endsWith('.html')) return 'text/html; charset=utf-8';
  if (file.endsWith('.css')) return 'text/css; charset=utf-8';
  if (file.endsWith('.js')) return 'text/javascript; charset=utf-8';
  if (file.endsWith('.svg')) return 'image/svg+xml';
  if (file.endsWith('.png')) return 'image/png';
  return 'application/octet-stream';
}

const server = http.createServer((request, response) => {
  const url = new URL(request.url, 'http://127.0.0.1:4173');
  if (url.pathname === '/healthz') {
    response.writeHead(200, { 'Content-Type': 'text/plain' });
    response.end('ok');
    return;
  }
  if (url.pathname !== '/configurationpage') {
    response.writeHead(404, { 'Content-Type': 'text/plain' });
    response.end('not found');
    return;
  }

  const relative = resources[url.searchParams.get('name')];
  if (!relative) {
    response.writeHead(404, { 'Content-Type': 'text/plain' });
    response.end('unknown resource');
    return;
  }

  const file = path.join(root, relative);
  response.writeHead(200, {
    'Content-Type': typeFor(file),
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff'
  });
  fs.createReadStream(file).pipe(response);
});

server.listen(4173, '127.0.0.1');
