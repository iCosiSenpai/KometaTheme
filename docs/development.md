# Development

Requirements: .NET 9 SDK, Node.js 20, npm.

## Build and test

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build

npm ci
npx playwright install chromium
npm run test:browser
```

Narrower runs:

```bash
npm run test:e2e     # Playwright end-to-end only
npm run test:a11y    # axe accessibility audit only
```

The browser suite loads the real embedded page shells against a local Jellyfin API
fixture, so it exercises the shipped frontend rather than a copy. Playwright covers
the critical flows and axe-core checks WCAG A/AA serious and critical violations.

## How the pieces fit together

```text
Jellyfin library
      │
      ▼
LibrarySelection ──► CompositeResolver ──► AnimeThemes API
  pattern/type          │ provider IDs        │ themes + seasons
  eligibility           └ title fallback      ▼
      │                                   Download engine
      │                                 ffmpeg + resilience
      ▼                                         │
Item sync / scheduler ──────────────────────────┤
      │                                         │
      │            YouTube import ──────────────┤
      │              yt-dlp                     │
      ▼                                         ▼
      ├── JsonResolutionCache            Theme files + repair
      ├── FailedItemsStore                      │
      ├── manual bindings                       ▼
      └── excluded items                 Global M3U playlist
```

The frontend has no separate build step. Three HTML shells load a versioned chain of
plain JavaScript modules:

```text
Jellyfin page shells
  └── kometa-loader.js
      ├── kometa-core.js       API, i18n, dialogs, sync, preview, lifecycle
      ├── kometa-a11y.js       tabs, listbox, busy state, announcements
      ├── config.js
      ├── search.js
      └── item.js
```

## Design decisions worth knowing

**YouTube extraction runs `yt-dlp` as an external process** instead of using a managed
library. The plugin ships as a single DLL with no side-by-side dependencies, so a
NuGet extractor would not reach users at all. YouTube's player also changes far more
often than this plugin releases, and `yt-dlp` is maintained against exactly those
changes.

**Imported media is never re-encoded for the video container.** The extractor is asked
for a pre-muxed file, and ffmpeg stream-copies it. Re-encoding to VP9 was measured at
roughly 0.27x realtime on four cores, which turns a normal-length theme into minutes
of full-CPU work; stream copy finishes in seconds. Audio is still encoded to MP3,
because Jellyfin theme songs need it.

**The pasted YouTube URL never reaches the command line.** It is reduced server-side
to an eleven-character video ID, the URL is rebuilt from that ID, and it is passed
after `--`.

## Release flow

Versions are `Major.Minor.Build.Revision`. Feature work increments Minor or Build,
focused fixes increment Revision.

CI builds and tests on every push, then produces a DLL-only `KometaThemes.zip` and
prints its MD5. Publishing stays explicit:

1. Bump the assembly version and the frontend version badges together.
2. Push, then tag.
3. Publish the GitHub release with the built ZIP and its checksum.
4. Add the new version to the top of the plugin's `versions` array in the catalog
   manifest, then push that repository.

A release that is not in the manifest is invisible to users, and a manifest entry
whose checksum or URL does not resolve makes installation fail inside Jellyfin, so
steps 3 and 4 belong to the same piece of work.

Deploying to a Jellyfin server is intentionally not automated. Administrators install
through the plugin catalog.
