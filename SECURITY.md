# Security Notes

## Favicon cache privacy

Favicon cache filenames use HMAC-SHA-256 with a random per-user key protected by Windows DPAPI (`CurrentUser`). Cached image bytes are encrypted with AES-256-GCM using a fresh nonce and the normalized host as authenticated associated data. Invalid or mismatched cache files are rejected. The protected key is stored outside the favicon image directory.

On startup, legacy cache files using the old SHA-256 filenames are deleted instead of migrated. The cache is disposable and can be downloaded again. DPAPI protects data at rest but is not a boundary against code running as the same Windows user.

## Windows Hello session cache

Windows Hello is an interactive verification step before PejPass restores the vault's derived encryption key material protected with Windows DPAPI using the current-user scope. The cache includes the derived 256-bit key, its salt, and the KDF parameters; it does not cache the master password. The restored key material is accepted only when its metadata matches the vault header and authenticated decryption succeeds. DPAPI and the timeout reduce exposure, but code running as the same Windows user may be able to unprotect data it can access.

The cache lifetime begins after successful master-password authentication. A Windows Hello unlock does not renew the lifetime. The cache is cleared when its timeout expires, Windows locks or disconnects the user session, the system suspends, Windows Hello is disabled, the master password changes, three consecutive completed Windows Hello verifications exhaust authentication retries, or the application exits. Cancelling the Hello prompt and system/unavailability errors do not count as authentication failures. A successful master-password authentication or Windows Hello verification resets the failure counter.

The cache service uses `CryptographicOperations.ZeroMemory` for byte buffers it owns. This does **not** guarantee that all plaintext copies of the password are erased from process memory: managed strings and copies held by other components may remain until reclaimed by the runtime.

The “Until app closes” timeout disables the elapsed-time limit only; it does not override the Windows lock/disconnect/suspend, password-change, Hello-disable, failure-limit, or app-exit clearing rules.


## Single-instance vault-open IPC

A secondary PejPass process forwards a vault path through a named pipe scoped to the current Windows session. The pipe server uses `PipeOptions.CurrentUserOnly`; messages are length-prefixed UTF-8, bounded to 128 KiB, and incomplete messages are dropped after a short timeout. The sender waits for an acknowledgement before signaling the primary instance, avoiding a race between delivery and activation.

Before a path received from another instance is applied to the login view, PejPass requires an absolute existing `.pejpass` path and checks the file's `PEJP` magic, supported vault version (1–3), minimum header length, salt length, and the version 3 Argon2id algorithm identifier. This is only a preflight check; `VaultStore` remains responsible for fully parsing and authenticating the vault.

The pipe ACL restricts access to the current Windows user, not to a particular trusted process. It is intended to prevent cross-user access and replace the broadly writable filesystem handoff; it does not protect against malicious code already running as the same user.
