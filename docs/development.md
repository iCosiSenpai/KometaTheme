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

**YouTube extraction has two backends.** A managed extractor (YoutubeExplode) ships inside the
plugin package, so the feature works on a stock server with nothing to install — which is how
comparable Jellyfin plugins behave. When `yt-dlp` is present it is preferred instead, because it is
maintained continuously against YouTube's changes while the bundled copy is pinned to a plugin
release. Neither is required.

Because of that, the release archive is **not** a single DLL. It carries
`Jellyfin.Plugin.KometaThemes.dll`, `YoutubeExplode.dll`, `AngleSharp.dll` and
`JsonExtensions.dll`. It must not carry anything else: building with
`CopyLocalLockFileAssemblies` also drops around thirty Jellyfin and `Microsoft.Extensions`
assemblies into the output folder, and shipping those would place a second copy of the server's own
types in the plugin directory. CI packs an explicit list and fails if the archive contents change.

**Video is muxed rather than taken pre-muxed.** YouTube's pre-muxed streams top out at 360p — on
every video measured, a single 360p stream was the only pre-muxed option. The extractor instead
takes a container-matched video-only and audio-only pair and joins them with `ffmpeg -c copy`, which
costs no re-encode. Pairing across containers is what would break: it downloads fine and then fails
the copy, because webm cannot carry AAC. `ManagedYouTubeStreamSelectionTests` pins that behaviour.

**No path re-encodes video.** With yt-dlp the format selector asks for an already-muxed file
and ffmpeg stream-copies it; with the bundled extractor the pair is joined with `-c copy`.
Re-encoding to VP9 was measured at roughly 0.27x realtime on four cores, which turns a
normal-length theme into minutes of full-CPU work, against seconds for a copy. Audio is still
encoded to MP3, because that is what Jellyfin theme songs are.

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
