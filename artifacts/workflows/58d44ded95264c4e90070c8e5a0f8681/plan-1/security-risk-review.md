# Security and risk review

- Workflow: `58d44ded-9526-4c4e-9007-0c8e5a0f8681`
- Plan version: `1`
- Category: `security`

## Output

Security review of the local POC (not a penetration test):

Controls present: only absolute HTTP/HTTPS targets are accepted; link codes use cryptographic randomness; public base URL comes from configuration rather than request Host; click increments are atomic; API keys are read from configuration and the tracked appsettings value is a placeholder; approval routes require a configured shared token and caller-provided role header.

Risks and mitigations: public redirect service can be abused for phishing/spam; add rate limits, abuse reporting, domain reputation controls and monitoring before public production. Shared approval token and caller-declared role are not real identity/authorization; replace with authenticated per-user roles and audit identity. Workflow-submitted test evidence is trusted unless fixed trusted validation is enabled; use isolated runners and verify outputs. Restrict Swagger to Development as configured. Store live model credentials in user secrets/environment variables only.

Supply-chain: prior build reports NU1903 for SQLitePCLRaw.lib.e_sqlite3 2.1.11, a high-severity advisory. Upgrade to a compatible patched dependency and rerun tests before production use. No production security certification is claimed.

Credential handling: repository/history scan found no Ollama/OpenAI key-shaped secret; appsettings contains only YOUR_API_KEY_HERE. The API key shared in chat should be revoked/rotated independently.
