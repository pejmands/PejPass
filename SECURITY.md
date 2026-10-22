# Security Notes

## Legacy vault migration backups

Migrating a v1 or v2 vault may leave a full `.v1.bak` or `.v2.bak` copy encrypted with the old master password; changing the vault password does not re-encrypt these files, so PejPass offers to remove them after a successful password change, but file deletion does not guarantee secure erasure from the underlying storage.

## Favicon cache privacy

Favicon cache filenames use HMAC-SHA-256 with a random per-user key protected by Windows DPAPI (`CurrentUser`). Cached image bytes are encrypted with AES-256-GCM using a fresh nonce and the normalized host as authenticated associated data. Invalid or mismatched cache files are rejected. The protected key is stored outside the favicon image directory.

On startup, legacy cache files using the old SHA-256 filenames are deleted instead of migrated. The cache is disposable and can be downloaded again. DPAPI protects data at rest but is not a boundary against code running as the same Windows user.

## Windows Hello session cache

Windows Hello is an interactive verification step before PejPass restores the vault's derived encryption key material protected with Windows DPAPI using the current-user scope. The cache includes the derived 256-bit key, its salt, and the KDF parameters; it does not cache the master password. The restored key material is accepted only when its metadata matches the vault header and authenticated decryption succeeds. DPAPI and the timeout reduce exposure, but code running as the same Windows user may be able to unprotect data it can access.

The cache lifetime begins after successful master-password authentication. A Windows Hello unlock does not renew the lifetime. The cache is cleared when its timeout expires, Windows locks or disconnects the user session, the system suspends, Windows Hello is disabled, the master password changes, three consecutive completed Windows Hello verifications exhaust authentication retries, or the application exits. Cancelling the Hello prompt and system/unavailability errors do not count as authentication failures. A successful master-password authentication or Windows Hello verification resets the failure counter.

The cache service uses `CryptographicOperations.ZeroMemory` for byte buffers it owns. This does **not** guarantee that all plaintext copies of the password are erased from process memory: managed strings and copies held by other components may remain until reclaimed by the runtime.

The “Until app closes” timeout disables the elapsed-time limit only; it does not override the Windows lock/disconnect/suspend, password-change, Hello-disable, failure-limit, or app-exit clearing rules.

## Vault locking when hidden to the system tray

When the unlocked main window is minimized to the tray or closed with “Close to system tray” enabled, PejPass schedules a lock after the WPF window event returns. This clears the active vault session and sensitive UI state, closes the unlocked window, and leaves the login window hidden until the user opens PejPass from the tray. This remains in effect when the Auto-lock timer is disabled.

If Windows Hello session caching is enabled, the separate DPAPI-protected key cache may remain available until its configured timeout or one of its documented clearing events. Returning to the hidden login window still requires an explicit unlock with the master password or Windows Hello verification; the previous in-memory vault session is not resumed.

## Update manifest rollback and freshness

The root `notes` field and per-release `releases[].notes` are not included in the ECDSA-signed payload; they are display-only metadata delivered over HTTPS, so the signature does not independently authenticate that text.

The updater verifies the ECDSA signature over the manifest's version, release date, download URL, and package SHA-256, then checks that the manifest version is newer than the installed version. These checks authenticate the signed metadata and the downloaded package, but they do not establish that the manifest is the newest one published.

An attacker able to interfere with update traffic can replay an older, still-valid signed manifest. If that manifest points to a legitimate signed-by-hash release that is newer than the client's installed version but older than the current release, the client may offer that older release for installation. This is a **rollback of update freshness**, not a signature forgery; the attacker cannot alter the authenticated version, URL, or hash without a trusted signing key. The updater checks for updates manually rather than installing them automatically, which limits exposure but does not eliminate it.

**Current status: known limitation; not prevented by the current manifest format.** Do not treat a valid signature alone as proof that an update is current. Until freshness protection is implemented, publish only through the trusted release channel and keep release artifacts and the manifest consistent.

A future mitigation should be designed as a separate compatibility change. A signed `expires` timestamp can bound how long a manifest remains acceptable, but it requires a defined clock policy and a migration plan for clients that do not understand the field; by itself, it does not prevent replay of an older manifest before that manifest expires. Stronger rollback resistance may also require a signed monotonically increasing release sequence and client-side state that remembers the highest accepted sequence. Any design must account for clock rollback, deletion or restoration of local state, offline use, emergency key rotation, and how older installed versions transition to the new schema.

## Update-signing key rotation

PejPass verifies update manifests using ECDSA P-256 public keys embedded in `src/PejPass.Application/Security/TrustedUpdateKeys.cs`. The private signing keys must never be committed to the repository, included in releases, or shared through chat or email. Keep the trusted-key list small (currently limited by code review guidance to at most three keys).

### Key formats and storage

- A signing key is an ECDSA P-256 private key in PEM format. The matching public key is exported as Base64-encoded SubjectPublicKeyInfo (SPKI), which is the format stored in `TrustedUpdateKeys.cs` and passed to the signer as `--client-key`.
- Generate a new key only for a planned rotation or a documented incident. Do not regenerate, overwrite, or discard K1 merely to test the workflow.
- Generate K2 on a trusted machine. Store its private PEM outside the repository in a directory protected by the current Windows account, and keep a securely protected offline backup. Restrict access to the private key and verify the backup before relying on it. The public key may be committed; the private key may not.
- Record the new key's fingerprint and label (for example, K2) in the rotation notes. Confirm the fingerprint and key pair before using it for a release.

### Planned K1-to-K2 rotation

1. **Prepare K2.** Generate a new ECDSA P-256 key pair and securely back up the private key. Keep K1 available; it remains necessary while installed versions that trust only K1 are supported.
2. **Add trust before using K2 to sign releases.** Add K2's public SPKI Base64 value to `TrustedUpdateKeys.cs`, retaining K1. Review the key fingerprint and run the full test suite. Build and test the signer with `dotnet test PejPass.sln`; both `PejPass.UpdateSigner` and `PejPass.UpdateSigner.Tests` are included in the solution.
3. **Release a bridge version.** Publish an application version that trusts both K1 and K2, while update manifests are still signed with K1 as the primary `signature`. This gives existing K1-only clients a path to install a version that also trusts K2.
4. **Dual-sign subsequent manifests.** Once the bridge version is released, sign each manifest with K1 first and K2 second. The signer writes K1's signature to the legacy `signature` field and K2's signature to `additionalSignatures`. Keep the K1 signature first: older clients only read `signature` and ignore `additionalSignatures`. Newer clients can verify either signature against their trusted-key list.
5. **Verify before publishing.** Work on a copy or staging manifest first; do not experiment on the production `update.json`. Run the tests and build the signer, verify the existing manifest before changes, and verify the newly signed manifest after signing. Confirm both compatibility cases using disposable manifests: a K1-only verifier must accept the primary signature, and a K2-only verifier must accept the additional signature while the primary signature still verifies with K1. Also check the ZIP hash and package contents when preparing a real release. Publish the release ZIP before publishing its manifest.
6. **Remove K1 from the application's trust list only after the transition is deliberate.** First ensure a released application trusts K2 and that the support policy for older K1-only installations is understood. A K2-only application can continue to accept a dual-signed manifest through `additionalSignatures`, while old K1-only applications can continue to use the K1 primary signature. The signer supports this transition when K1 is explicitly supplied as `--client-key` and a signature from a currently trusted key (K2) is also present. Do not remove K1 from the signing workflow or stop emitting its primary signature while continued updates for K1-only clients are required.
7. **End legacy compatibility only intentionally.** Switching the primary `signature` to K2 means clients that trust only K1 will no longer be able to verify that manifest. Make that change only after deciding to end support for those clients and documenting the consequence.

### Signer and verification workflow

When both public keys are listed in `TrustedUpdateKeys.cs`, sign a staging manifest with K1 as the first private key and K2 as the second. Supply K1's public key to `--client-key` so the tool checks the legacy primary signature:

```powershell
dotnet run --project tools\PejPass.UpdateSigner -- <staging-update.json> <K1-private.pem> <K2-private.pem> --client-key <K1-public-base64>
```

Do not pass `--public-key` for normal production signing: that option replaces the embedded trusted-key list with the one supplied key and is intended for targeted verification or isolated tests. For a disposable test manifest, simulate a K1-only client and a K2-only client separately:

```powershell
dotnet run --project tools\PejPass.UpdateSigner -- <staging-update.json> --verify-only --public-key <K1-public-base64> --client-key <K1-public-base64>
dotnet run --project tools\PejPass.UpdateSigner -- <staging-update.json> --verify-only --public-key <K2-public-base64> --client-key <K1-public-base64>
```

Both checks should succeed on a dual-signed staging manifest. The second check verifies the manifest using K2 while also requiring that the primary `signature` is still valid for K1. These test commands do not replace verification using the actual trusted-key list.

### If a private signing key may be compromised

Treat suspected private-key exposure as an incident, not as an ordinary scheduled rotation. Stop and assess releases, protect the remaining signing keys, and plan an explicit recovery path. Simply deleting K1 from the current source does not revoke it from already installed older applications; those applications still trust K1 and may accept a forged primary signature. Do not claim that a key has been revoked for existing clients unless those clients have a mechanism to receive and enforce that revocation.

## Browser extension unlock rate limiting

Browser-based master-password unlock attempts are rate-limited in memory: the first four failed attempts are allowed without delay; subsequent failures trigger exponential lockouts starting at 30 seconds and capped at 15 minutes. Failure history expires after 30 minutes, successful unlock resets it, and only one browser unlock attempt may be in flight at a time. Desktop login attempts are not covered by this browser-only limiter. The limiter is not persistent and resets when PejPass exits; it slows repeated guesses but is not a boundary against malicious code running as the same Windows user.

## Browser extension vault-state event pipe

The desktop app publishes vault lock-state changes over the separate named pipe `PejPass.BrowserEvents.v1`. The pipe is created with `PipeOptions.CurrentUserOnly` by the desktop server, and BrowserHost connects with the same current-user-only option. It carries only the `locked` or `unlocked` state and no passwords, vault contents, key material, or other secrets. The desktop accepts at most four concurrent event subscribers.

The event pipe is separate from `PejPass.BrowserStatus.v1`, which remains the request/response endpoint. The current-user restriction limits access by Windows user, not by trusted process; it is not a security boundary against malicious code already running as the same user.

## Single-instance vault-open IPC

A secondary PejPass process forwards a vault path through a named pipe scoped to the current Windows session. The pipe server uses `PipeOptions.CurrentUserOnly`; messages are length-prefixed UTF-8, bounded to 128 KiB, and incomplete messages are dropped after a short timeout. The sender waits for an acknowledgement before signaling the primary instance, avoiding a race between delivery and activation.

Before a path received from another instance is applied to the login view, PejPass requires an absolute existing `.pejpass` path and checks the file's `PEJP` magic, supported vault version (1–3), minimum header length, salt length, and the version 3 Argon2id algorithm identifier. This is only a preflight check; `VaultStore` remains responsible for fully parsing and authenticating the vault.

The pipe ACL restricts access to the current Windows user, not to a particular trusted process. It is intended to prevent cross-user access and replace the broadly writable filesystem handoff; it does not protect against malicious code already running as the same user.
