# Plug-ins for 0.9.8

The Mods page includes Email, Notion and Linear, plus Todoist, GitHub,
Atlassian, monday.com, Miro, Cloudflare Docs and Context7. New entries are installed **off**. Connecting
does not grant tools permission to bypass SECURITY or a tool's Off setting.
An organization's approval, account permissions and service availability still
apply. These entries do not install local runtimes or silently connect accounts.

## Connection routes

| Starter | Official service | Setup |
| --- | --- | --- |
| Todoist | `https://ai.todoist.net/mcp` | Browser OAuth |
| GitHub | `https://api.githubcopilot.com/mcp/` | Personal access token in the plug-in's masked setup window |
| Atlassian | `https://mcp.atlassian.com/v2/mcp` | Browser OAuth; organization policies may require approval |
| monday.com | `https://mcp.monday.com/mcp` | Browser OAuth; an account admin may need to approve the dynamic connection and redirect |
| Miro | `https://mcp.miro.com/` | Browser OAuth; select the team containing the boards; Enterprise may require admin enablement |
| Cloudflare Docs | `https://docs.mcp.cloudflare.com/mcp` | Public developer documentation; no account or sign-in |
| Context7 | `https://mcp.context7.com/mcp` | Context7 API key entered in the masked setup window |

GitHub token setup links to its fine-grained-token settings. The user chooses
repository access, permissions and expiry there; Capri does not create the token.
Tokens are protected by Windows on Windows, and held in memory until Capri closes
on Mac/Linux. Forget token turns the plug-in off and removes its saved sign-in.
HTTP 401 clears a rejected personal token and asks for setup again. No account
sign-in or live account tool call is part of the automated checks.

Context7 setup links to [its official dashboard](https://context7.com/dashboard).
Create an API key there; Capri sends it as `Authorization: Bearer ...` only to
the configured Context7 endpoint. Core documentation tools are
`resolve-library-id` and `query-docs`; available libraries, account limits and
optional dashboard-enabled tools can change. This entry uses the API-key route,
not the separate `/mcp/oauth` route. Anonymous access is not promised; current
provider source defaults to requiring authentication. No Context7 account was
connected or used for acceptance testing.

Cloudflare Docs is limited to public developer documentation and needs no
credentials. A public protocol probe on 22 November 2026 at 13:20:53 UTC selected
MCP `2025-06-18`, accepted the initialized notification, and listed
`search_cloudflare_documentation` and `migrate_pages_to_workers_guide`, with no
next cursor. No tool was called. Tool names and counts may change. Turning this
known public starter on does not open a browser sign-in.

Sentry is deferred. Its public metadata advertises dynamic registration, S256
and public token authentication `none`, but current compatibility with Capri's
accepted protocol versions has not been established from the reviewed primary
sources. Its direct upstream token route also uses `Sentry-Bearer`, rather than
Capri's personal-token `Bearer` route. No Sentry client was registered or account
connected.

## Custom public OAuth clients

Existing HTTP manifests keep the discovery and dynamic-registration flow.
A provider that supports native public clients can instead supply an already
registered client ID:

```json
{
  "mcpServers": {
    "service": {
      "type": "http",
      "url": "https://service.example/mcp",
      "auth": {
        "type": "oauth",
        "clientId": "the-provider-registered-public-client-id",
        "scope": "the provider's supported scopes"
      }
    }
  }
}
```

This skips registration, retaining metadata discovery, S256 PKCE, state checking,
an ephemeral `http://127.0.0.1:<port>/callback`, and public-client token exchange
and refresh. The provider must accept that redirect and token authentication
method `none`. A confidential client secret or fixed redirect is not supported;
the parser rejects unsupported auth options instead of pretending they work.
Changing the configured client ID, scope, auth mode or resource URL makes old
credentials inapplicable. Disabling, changing, removing or forgetting a connection
cancels pending authorization. This alone does not supply provider registration
or make every Google/Slack/Asana/Figma/Dropbox integration ready.

For a single HTTP server using a personal bearer token, use
`"auth": {"type":"token"}`. The token belongs in the setup window, never in
distributed JSON. Combining this with an Authorization header is rejected.

## Large tool catalogues

Discovery retains the complete validated catalogue within explicit budgets:
2,048 tools per plug-in, 8 MiB per catalogue, 256 pages and a total list deadline.
Oversized definitions, repeated cursors and partial failures are errors; they do
not overwrite the last complete catalogue. Old caches are invalidated and new
caches retain full input schemas.

The model receives compact plug-in summaries. It calls `mod` with
`name: "plugin_tools"` and JSON in `text`, with optional `plugin`, `query` and
`offset`. Results include exact callable names, input schemas and `nextOffset`.
Calls then use the existing `mod` execution path. Disabled plug-ins and tools
remain unavailable. Names are stable across ordering and distinct across servers;
an old readable alias works only when unambiguous. Multi-server grants use a
server-qualified permission key, so an old raw-name grant is not silently shared
between servers.

## Verification and sources

Runner smoke checks cover 12 pages/48 tools, last-tool invocation, complete cache
and schema round trips, failed refreshes, collisions, off settings, compact offers,
off-by-default starters and paging across every starter/import page, masked token setup layout, public OAuth
without a registration endpoint, PKCE/scopes/refresh, personal-token connections,
401 rejection, changed identities, and forgetting a pending connection. Real
account authorization and useful read-only calls remain a separate acceptance
step for each service, followed by explicit write-permission checks.

Provider references reviewed 22–23 November 2026:

- [Todoist's official server](https://github.com/Doist/todoist-mcp)
- [GitHub host authentication](https://github.com/github/github-mcp-server/blob/main/docs/host-integration.md)
- [GitHub personal tokens](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)
- [Atlassian's official v2 service](https://atlassian.github.io/atlassian-mcp-server/)
- [monday.com dynamic registration](https://developer.monday.com/api-reference/docs/mcp-dynamic-client-registration)
- [Miro setup](https://developers.miro.com/docs/connecting-to-miro-mcp)
- [Miro authentication and team restrictions](https://developers.miro.com/docs/miro-mcp-server-frequently-asked-questions)

- [Cloudflare Documentation MCP](https://github.com/cloudflare/mcp-server-cloudflare/blob/main/apps/docs-ai-search/README.md)
- [Cloudflare 2025 Streamable HTTP compatibility](https://developers.cloudflare.com/agents/model-context-protocol/cloudflare/servers-for-cloudflare/)
- [Context7 official setup and dashboard](https://github.com/upstash/context7)
- [Context7 client setup and tools](https://github.com/upstash/context7/blob/master/docs/resources/all-clients.mdx)
- [Context7 authentication enforcement](https://github.com/upstash/context7/blob/master/packages/mcp/src/lib/mcp-http-auth.ts)
- [Sentry OAuth metadata](https://mcp.sentry.dev/.well-known/oauth-authorization-server)
- [Sentry token schemes](https://github.com/getsentry/toolkit)
