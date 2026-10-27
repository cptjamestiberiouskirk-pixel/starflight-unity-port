# Figma connector setup for Codex and Claude Code

The Unity project uses the official **UI Toolkit Connector for Figma design 0.2.0-exp.1** with **Unity Pipeline 0.7.0-exp.1** and Unity **6000.3.13f1**.

Keep these package versions together. Connector 0.2.0-exp.1 declares Pipeline 0.7.0-exp.1; Pipeline 0.8.0-exp.1 produced missing `CliCommand`, `CliArg` and related API compilation errors during installation. Game scripts do not reference Pipeline APIs. Recheck compatibility before upgrading either experimental package.

## Two complementary connections

- **Figma desktop to Unity:** run the saved official **Unity UI Toolkit Connector** plugin inside an editable Figma Design file. It imports frames as UXML/USS under `Assets/Figma/` and supports reviewing supported Unity edits in Figma.
- **AI client to Figma:** Codex and Claude Code use their own Figma MCP connections and account authorization. Installing the Unity package does not replace either client's MCP connection. Claude Code can use its existing Figma connection; avoid configuring duplicate MCP servers.

The desktop companion can be paired with the open Unity project using its current session token.

Both agents can also use the installed Unity CLI from this project:

```powershell
unity command --project-path . figma_status
unity command --project-path . editor_status
unity command --project-path . console_status
```

These commands were exercised against the running editor. `figma_status` reported connector 0.2.0-exp.1; the editor was ready and not compiling, and its current compilation-failure flag and console error count were clear after the compatible dependency resolved. Earlier failed installation attempts remain in local logs.

## Pair the desktop plugin

1. Open this project in Unity and an editable Design file in Figma desktop.
2. Open **Window > UI Toolkit > Connector for Figma design** in Unity.
3. The project-local Pipeline settings asset enables **Allow Browser Clients** for the sandboxed Figma plugin. The server was restarted to apply it. The listener remains local, ordinary web origins are refused, and command requests still require authentication.
4. Use **Copy token** in Unity, then paste that current session's connection token into the plugin's Settings tab and select **Connect to Unity**.
5. Select an intended frame and use **Push selection**. Review the import report and rendered UI before wiring imported content into game scenes. Do not enable automatic pushes into existing game assets without choosing the intended frames.

An editor/server restart changes its connection token; reconnect the plugin afterward. Never put that token, a port descriptor, login credentials or OAuth data in source control or reports. `Library/` descriptors are local transient files.

The origin checks were exercised: an `Origin: null` preflight was accepted (204), an ordinary web origin was denied (403), and an unauthenticated command request was denied (401).

## Proxy configuration

If a domain-restricted proxy blocks setup, its administrator can allow these exact required hosts:

- `packages.unity.com`
- `download.packages.unity.com`
- `cdn.packages.unity.com`
- `mcp.figma.com`
- `mcp-proxy.anthropic.com`

These are machine-level network settings. Keep proxy credentials, trust-file backups and local logs outside the repository.

## Official references

- [Unity connector announcement and setup](https://discussions.unity.com/t/experimental-ui-toolkit-connector-for-figma-design/1736969)
- [Official Figma companion plugin](https://www.figma.com/community/plugin/1671239513881857517/unity-ui-toolkit-connector)
- [Figma MCP setup for Claude Code and Codex](https://developers.figma.com/docs/figma-mcp-server/remote-server-installation/)

Installation changes are local; no commit, push, release or game UI import is implied by this setup.
