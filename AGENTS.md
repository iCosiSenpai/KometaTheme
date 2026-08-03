# AGENTS.md — KometaTheme Rules

## ⚠️ READ THIS FIRST — MANDATORY

### AI Agent — Absolute Constraints

**1. Every change MUST bump the version. No exceptions.**
- `Major.Minor.Build.Revision` (e.g., `2.5.0.0`)
- Bump the **Revision** (4th digit) for hotfixes, typos, small fixes
- Bump the **Build** (3rd digit) for new features, UI changes
- Bump the **Minor** (2nd digit) for significant new functionality
- Bump the **Major** (1st digit) for breaking changes
- Files: `Directory.Build.props` + `configPage.html` + `itemPage.html` + `SearchPage.html` (any that have the badge)

**2. Every change MUST be committed and pushed to GitHub.**
- `git add -A && git commit -m "..." && git push origin main`
- Meaningful commit messages describing WHY, not just WHAT

**3. Every change MUST have a GitHub Release with changelog.**
- `gh release create vX.Y.Z.W KometaThemes.zip --repo iCosiSenpai/KometaTheme --title "..." --notes "## vX.Y.Z.W\n\n### ..."`
- Changelog must list: Features, Fixes, Breaking changes
- Checksum must be computed: `md5sum KometaThemes.zip`

**4. Every change MUST update the manifest.**
- Edit `iCosiSenpai-Plugins/manifest.json` — add new version entry at the top
- Fields: `version`, `changelog`, `targetAbi`, `sourceUrl`, `checksum`, `timestamp`
- Commit + push the manifest repo separately

**5. NEVER auto-deploy to the NAS / Jellyfin.**
- **DO NOT** run `docker cp`, `docker restart`, `curl` to Jellyfin, or any NAS-side install
- The release flow ends at: push code → push release → push manifest → **STOP**
- The user will deploy manually from Dashboard → Plugins → Catalog when ready
- The only exception: reading Jellyfin logs for debugging (`/volume1/docker/05-media/jellyfin/config/log/`)

---

## AI Agent Constraints (Detail)

### Plugin Auto-Update — STRICTLY FORBIDDEN
- **NO AI must ever manually copy/paste the plugin DLL into the Jellyfin container or plugins directory.**
- Plugin installation is done exclusively through Jellyfin's built-in Plugin Catalog (Dashboard → Plugins → Catalog).
- If a new build is created, the proper flow is: bump version → build → zip → GitHub Release → update manifest. Jellyfin's auto-update mechanism handles the rest.
- Never use `docker cp`, `sudo cp`, or any file copy command to deploy a plugin into `/volume1/docker/05-media/jellyfin/config/plugins/`.

### Version Bumping — MANDATORY
- **Every single source change MUST increment the version number.**
- Version format: `Major.Minor.Build.Revision` (e.g., `2.5.0.0`).
- Files to update:
  - `Directory.Build.props`: `<Version>`, `<AssemblyVersion>`, `<FileVersion>`
  - `configPage.html`: `.release-badge` text
  - `itemPage.html`: `.release-badge` text
  - `SearchPage.html`: `.release-badge` text
- The manifest.json in `iCosiSenpai-Plugins/` must be updated with the new version, checksum, and timestamp.

### Release Flow
1. Bump version in `Directory.Build.props` and HTML badges
2. `dotnet build -c Release`
3. Package **four** DLLs, not one — the managed YouTube extractor travels with the plugin:
   ```bash
   out=Jellyfin.Plugin.KometaThemes/bin/Release/net9.0
   zip -j KometaThemes.zip \
     "$out/Jellyfin.Plugin.KometaThemes.dll" \
     "$out/YoutubeExplode.dll" \
     "$out/AngleSharp.dll" \
     "$out/JsonExtensions.dll"
   ```
   Never `zip -j "$out"/*.dll`: the build also emits ~30 Jellyfin and Microsoft.Extensions
   assemblies there, and shipping those puts a second copy of the server's own types in the
   plugin folder. CI fails if the archive contents change.
4. `md5sum KometaThemes.zip` → get checksum
5. `gh release create v2.X.Y.Z KometaThemes.zip --repo iCosiSenpai/KometaTheme`
6. Update `iCosiSenpai-Plugins/manifest.json` with new version entry (version, changelog, targetAbi, sourceUrl, checksum, timestamp)
7. Commit + push both repositories
8. **STOP. Do not deploy. User deploys manually.**

### Jellyfin Log Path
```
/volume1/docker/05-media/jellyfin/config/log/log_YYYYMMDD.log
```

### Known Jellyfin API Quirks
- `POST /ScheduledTasks/Running/{key}` does NOT work with string key on Jellyfin 10.11.x. Must use numeric task GUID. Fetch it first from `GET /ScheduledTasks`, filter by `Key` field, then POST to `/ScheduledTasks/Running/{id}`.

### Don't Touch
- Do not modify `komga` or `kometamanga` services.
- Do not modify other plugins without asking.
