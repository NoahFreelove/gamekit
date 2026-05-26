# GameKit.SampleGame — dev keys

## Generate a throwaway RSA PEM pair

```bash
./scripts/gen-test-rsa-pem.sh
```

Produces `dev-priv.pem` (mode `0600`) and `dev-pub.pem` (mode `0644`) in this directory.
`appsettings.Development.json` references these paths.

**Security:** These keys are for local development only. In production:

- Generate fresh keys per deployment.
- Store private keys outside the source tree.
- Ensure the private key file is mode `0600` and owned by the process user.
- Rotate by adding a new `Kid`, switching issuance to the new key, and keeping the old
  public key in the validator's `IssuerSigningKeys` collection for the refresh-token
  lifetime (30 days by default).

The `.gitignore` in this directory excludes `*.pem` files so accidental commits are
blocked.
