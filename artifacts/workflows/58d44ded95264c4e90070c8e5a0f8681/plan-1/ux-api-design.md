# UX and API design

- Workflow: `58d44ded-9526-4c4e-9007-0c8e5a0f8681`
- Plan version: `1`
- Category: `ux`

## Output

API contract review of generated/greenfield/url-shortener:

POST /api/v1/links request: {"originalUrl":"https://example.com"}. Success returns code and shortUrl; a new mapping returns 201 Created with Location, an exact existing mapping returns 200. Invalid/non-HTTP(S) URL returns 400; short-code allocation exhaustion returns 503.

GET /{code}: known code returns 302 Found with Location equal to the saved destination and increments clickCount atomically; unknown code returns 404.

GET /api/v1/links/{code}/analytics: known code returns {"code":"...","clickCount":0}; unknown code returns 404. This read does not increment the count.

GET /: returns JSON service status and route list. Development Swagger is the interactive discovery/testing surface; it is not enabled outside Development.

Usage: create link -> retain returned code/shortUrl -> share shortUrl -> query analytics. There is no account or authorization workflow in the standalone POC. Keep response DTOs stable and avoid exposing database internals.
