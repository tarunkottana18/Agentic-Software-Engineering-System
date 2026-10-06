# Run and review tests

- Workflow: `58d44ded-9526-4c4e-9007-0c8e5a0f8681`
- Plan version: `1`
- Category: `validation`

## Output

Fresh validation executed for this workflow on 2026-10-06:

1. `dotnet test .\UrlShortenerSystem.sln --no-build --nologo --logger "console;verbosity=minimal"` -> exit 0; 49 passed, 0 failed, 0 skipped.
2. `dotnet test .\generated\greenfield\url-shortener\UrlShortener.slnx --no-build --nologo --logger "console;verbosity=minimal"` -> exit 0; 19 passed, 0 failed, 0 skipped. Build reports NU1903 for SQLitePCLRaw.lib.e_sqlite3 2.1.11; unresolved.
3. `dotnet test .\tests\UrlShortener.Tests\UrlShortener.Tests.csproj --filter FullyQualifiedName~Workflow_ImplementationApproval_IgnoresLockedSqliteArtifacts` -> exit 0; 1 passed. Confirms the implementation approval/hash tracker ignores locked SQLite DB/WAL/SHM/journal artifacts.
4. Runtime smoke against http://localhost:5209: GET /swagger -> 200 HTML; GET /swagger/v1/swagger.json -> OpenAPI 3.0.1 with the expected root, create, redirect and analytics routes. POST created a unique test URL; analytics before redirect was 0; GET short URL returned 302 with Location matching the original URL; analytics after redirect was 1.

The implementation baseline existed before this workflow. Workspace hash verification recorded zero implementation source changes; this run validates the baseline rather than claiming the project was authored during the workflow. No production load/soak or separate-process deployment test was run.
