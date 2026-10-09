# Security Notes

## Windows Hello session cache

Windows Hello is an interactive verification step before PejPass restores a master password protected with Windows DPAPI using the current-user scope. It is not itself the encryption key for the vault or a cryptographic key that directly protects the cached password. DPAPI and the timeout reduce exposure, but code running as the same Windows user may be able to unprotect data it can access.

The cache lifetime begins after successful master-password authentication. A Windows Hello unlock does not renew the lifetime. The cache is cleared when its timeout expires, Windows locks or suspends, Windows Hello is disabled, the master password changes, three consecutive completed Windows Hello verifications exhaust authentication retries, or the application exits. Cancelling the Hello prompt and system/unavailability errors do not count as authentication failures. A successful master-password authentication resets the failure counter.

The cache service uses `CryptographicOperations.ZeroMemory` for byte buffers it owns. This does **not** guarantee that all plaintext copies of the password are erased from process memory: managed strings and copies held by other components may remain until reclaimed by the runtime.

The “Until app closes” timeout disables the elapsed-time limit only; it does not override the Windows lock/suspend, password-change, Hello-disable, failure-limit, or app-exit clearing rules.
