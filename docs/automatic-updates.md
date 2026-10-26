# Automatic Mac updates

Blast Circuit v0.21 adds the install-once update path. Earlier builds need this
one manual upgrade because they do not contain an updater. Settings includes
**Check automatically** (on by default), **Check now**, and **Restart to
update**. A ready update also marks Settings on the main menu. A current room
must be left before installation; checking and downloading never quit a match.

The updater reads the latest stable, published GitHub release from
`MCMike0399/bomberman-64-multiplayer`. Drafts, prereleases, equal versions and
older versions are ignored. Checks need Internet access; local multiplayer can
still run when the update service is unavailable. The initial check runs from
an idle menu after launch. Automatic checking can be disabled per Mac.

## Trust and installation

This follows IslandBar's signed-release and rollback approach, with an independent
Blast Circuit signing key. The client contains only the P-256 public key in
`src/Bomber.Client/Updates/UpdatePublicKey.txt`. The private key stays outside the
repository and is supplied as the `BLAST_UPDATE_SIGNING_KEY` release secret.
Never substitute IslandBar's key or check a private key into this repository.

Each release attaches these three updater-compatible assets:

- `Blast-Circuit-macos-arm64.zip`
- `Blast-Circuit-update.json` — version, archive size and SHA-256
- `Blast-Circuit-update.json.sig` — base64 ECDSA/SHA-256 signature over the exact manifest bytes

A separate signed `Blast-Circuit-release.json` inventory also binds both client
and server delivery archives and their metadata. Existing clients continue using
the unchanged updater manifest format and public key.

Only official HTTPS release/CDN URLs are accepted. The manifest signature,
archive length/hash, safe extraction paths, app identity/version and recursive
code signature are checked before installation. The app must be in a writable
Applications location, outside App Translocation. No credentials are needed on
players' Macs. Ad-hoc code signing remains distinct from the update signature;
this does not claim Apple notarization.

A helper waits for the running app to quit, parks its bundle beside the staged
update, swaps in the new app and relaunches through Launch Services. The new app
must report its exact version after three seconds and rendered frames. If it
cannot launch successfully within 45 seconds, the helper restores and reopens
the previous app. That failed version is skipped on later checks. Successful
updates remove the parked bundle. Logs and the last result are retained under
`user://updates`; an uninstalled staging copy is removed when the app exits.

## Publishing

The existing Verification workflow builds the Mac and portable server artifacts.
`tools/ci/promote_release.py` verifies the successful main run, required jobs,
source revision, bundle identity and archive inventories before release creation.
For v0.21 onward it also requires the update signing key. The signing helper
checks that the private key matches the public key in the verified source tree.

The **Release verified game** workflow accepts a successful run ID and version.
It creates a signed draft by default; its explicit `publish` input deploys and
verifies the matching server fleet before publishing it.
The **Publish verified update** workflow can publish a new version automatically
when a trusted main Verification run succeeds. It is opt-in through repository
variable `AUTO_RELEASE_ENABLED=true`, requires `BLAST_UPDATE_SIGNING_KEY`, and
preserves existing version tags/releases. Fork/PR runs cannot publish. Changes
to version/build numbers belong in `Release.props`; synchronize exports with
`python3 tools/ci/release_identity.py --sync`. An unchanged version does not
produce another release. Both publication paths use the same protected deploy
gate. See [release identity, setup and failure recovery](releases.md).

Do not rotate the signing key without a migration that existing clients can
verify. A failed release can be removed from the latest channel by marking it
prerelease; do not reuse its version. Ship a new version with the correction.

## Verification

Client checks cover valid and tampered manifests, wrong keys, semantic versions,
size/hash verification, official URLs and archive traversal. The Mac integration
suite runs the production feed/downloader/installer with an isolated signed
fixture feed and real old/new app bundles. It proves both a successful launch
and rollback of an ad-hoc signed app whose executable immediately exits:

```sh
dotnet run --project tests/Bomber.Client.Tests
dotnet run --project tests/Bomber.Update.Tests -- \
  "artifacts/exports/Blast Circuit.app" \
  "$HOME/Applications/Blast Circuit.app" \
  artifacts/update-integration-fresh
```

The integration suite requires a newer export than the installed baseline and
owns the graphical desktop during its windowed trials. It installs only into
its fresh output directory, validates the resulting live process, closes only
its own apps and removes temporary launchable copies. It never updates the
canonical installed app. Retain its receipts; do not call a local fixture test
an observation of a published GitHub release.
