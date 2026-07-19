# Update Signing Process

Status: production security requirement

## Trust decision

Studio launches an update only when every check succeeds:

1. the update/download URL is HTTPS in a product build;
2. the server supplies a valid 64-character SHA-256;
3. downloaded bytes match that SHA-256;
4. the file is a Windows PE executable;
5. Windows `WinVerifyTrust` validates the Authenticode signature and certificate chain;
6. revocation checking succeeds;
7. the signer publisher exactly matches `Erk-S LLC`;
8. a second verification immediately before launch still succeeds.

Failure is final for that artifact. Studio deletes the partial download and does not execute it.

## Production signing

The production certificate must:

- be intended for code signing;
- contain a usable private key in `CurrentUser\My` or `LocalMachine\My`;
- report simple publisher name `Erk-S LLC`;
- be valid and not revoked;
- be protected by the organization's release-key process.

Configure only the thumbprint, never export or commit the private key:

```powershell
$env:ERKS_CODE_SIGN_CERT_THUMBPRINT = '<thumbprint>'
```

The release script signs with SHA-256 and RFC3161 timestamping:

```text
signtool sign /sha1 <thumbprint> /fd SHA256 /tr <timestamp-url> /td SHA256
signtool verify /pa /all /v <file>
```

Both the application executable and final setup executable are signed after their final bytes are
created. Modifying, recompressing, or wrapping a signed setup invalidates the release identity.

## Development policy

Development builds may use loopback HTTP for a local server. They require the configured development
publisher and remain separate from production catalog entries. Non-loopback HTTP is rejected in all
modes. Development certificates and artifacts must never be presented as an `Erk-S LLC` release.

## Server metadata

The update catalog stores product code, version, HTTPS download URL, SHA-256, size, release notes,
required/optional status, and publication time. The artifact hash in the catalog must equal
`release.json` and the uploaded file. Signed catalog metadata is a future defense-in-depth layer; it
does not replace Authenticode or file hashing.

## Certificate rotation

Before rotation:

1. obtain and validate the new code-signing certificate;
2. test chain/revocation/timestamp behavior on supported Windows versions;
3. publish a normally trusted transition release if publisher identity changes;
4. update the expected publisher only through a reviewed product release;
5. revoke and remove access to the old key according to incident policy.

Never weaken publisher matching to accept an unknown certificate during rotation.

## Incident response

For suspected key compromise or malicious update metadata: stop catalog publication, revoke the
certificate, preserve logs/artifact hashes, notify affected users, publish a clean signed recovery
release, and document the event. Do not delete evidence or silently replace an existing version.
