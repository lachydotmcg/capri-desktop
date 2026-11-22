# Plug-ins for 0.9.8

The Mods page includes Email, Notion and Linear, plus Todoist, GitHub,
Atlassian, monday.com and Miro. New entries are installed **off**. Connecting
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

GitHub token setup links to its fine-grained-token settings. The user chooses
repository access, permissions and expiry there; Capri does not create the token.
Tokens are protected by Windows on Windows, and held in memory until Capri closes
on Mac/Linux. Forget token turns the plug-in off and removes its saved sign-in.
HTTP 401 clears a rejected personal token and asks for setup again. No account
sign-in or live account tool call is part of the automated checks.

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
eight off-by-default starters and paging, masked token setup layout, public OAuth
without a registration endpoint, PKCE/scopes/refresh, personal-token connections,
401 rejection, changed identities, and forgetting a pending connection. Real
account authorization and useful read-only calls remain a separate acceptance
step for each service, followed by explicit write-permission checks.

Provider references reviewed 22 November 2026:

- [Todoist's official server](https://github.com/Doist/todoist-mcp)
- [GitHub host authentication](https://github.com/github/github-mcp-server/blob/main/docs/host-integration.md)
- [GitHub personal tokens](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)
- [Atlassian's official v2 service](https://atlassian.github.io/atlassian-mcp-server/)
- [monday.com dynamic registration](https://developer.monday.com/api-reference/docs/mcp-dynamic-client-registration)
- [Miro setup](https://developers.miro.com/docs/connecting-to-miro-mcp)
- [Miro authentication and team restrictions](https://developers.miro.com/docs/miro-mcp-server-frequently-asked-questions)
