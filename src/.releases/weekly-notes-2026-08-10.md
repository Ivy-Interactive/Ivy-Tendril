# Ivy Tendril v1.1.28 Release Notes

> [!NOTE]
> We release regular updates for Ivy Tendril. Sign up at [https://ivy.app/](https://ivy.app/auth/sign-up) to get release notes directly in your inbox.

## What's New & Key Highlights

### Security & Certificate Improvements
- **Fixed System Password Prompt on Launch (macOS)**: Resolved an issue where macOS would prompt for system password authorization on every application launch. The certificate trust check now checks the System Keychain (`LocalMachine`), correctly identifying pre-trusted installer certificates without re-prompting.

### Installer & Agent Stability
- **Locked File Handling on Upgrade**: Improved the installer update process to gracefully handle locked files and terminate running background `ivy-agent` instances prior to replacing application binaries.

### LLM Provider Integration & Custom API Support
- **Berget AI Provider Support**: Added **Berget AI** as a dedicated BYO LLM provider card in beta mode, exposing Kimi K3 models (`moonshotai/Kimi-K3`) with full branding and default profile support.
- **Custom Anthropic Base URL**: Users can now configure custom `API Base URL` endpoints for Anthropic providers in settings.
- **OpenAI Proxy Enhancements**: Fixed environment variable propagation (`OPENAI_BASE_URL` and `OPENAI_API_KEY`) and improved model routing in OpenAI Proxy sessions.

### Ivy Agent CLI Management
- **Background Update Checks & 1-Click Update**: Integrated background version checking and a 1-click update button in settings for the Ivy Agent CLI.
- **Reliable CDN Fallbacks**: Switched release version checks to use CDN endpoints with cache-busting timestamping.

---

### Bug Fixes & Code Health
- **Test Isolation**: Fixed configuration service to prevent test runs from polluting live `TENDRIL_HOME` user directories.
- **Model Catalog Updates**: Updated model catalog listings and test assertions for `ivy-stem`, `ivy-root`, and `ivy-leaf`.
