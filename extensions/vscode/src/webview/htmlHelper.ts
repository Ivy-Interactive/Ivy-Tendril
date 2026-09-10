export function getNonce(): string {
  let text = '';
  const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  for (let i = 0; i < 32; i++) {
    text += possible.charAt(Math.floor(Math.random() * possible.length));
  }
  return text;
}

export function getWebviewContent(
  externalUri: string,
  initialTheme: 'dark' | 'light' | 'hc' = 'dark'
): string {
  const nonce = getNonce();

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; frame-src ${externalUri} http: https:; script-src 'nonce-${nonce}'; style-src 'unsafe-inline';">
  <title>Tendril Dashboard</title>
  <style>
    html, body {
      margin: 0;
      padding: 0;
      width: 100%;
      height: 100%;
      overflow: hidden;
      background-color: var(--vscode-editor-background, #1e1e1e);
    }
    #tendril-frame {
      width: 100%;
      height: 100%;
      border: none;
      display: block;
    }
  </style>
</head>
<body>
  <iframe
    id="tendril-frame"
    src="${externalUri}"
    allow="clipboard-read; clipboard-write"
    sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-modals allow-downloads">
  </iframe>

  <script nonce="${nonce}">
    (function() {
      const vscode = acquireVsCodeApi();
      const iframe = document.getElementById('tendril-frame');
      let currentTheme = '${initialTheme}';

      function sendThemeToIframe(theme) {
        if (iframe && iframe.contentWindow) {
          iframe.contentWindow.postMessage({ type: 'themeChanged', theme: theme }, '*');
        }
      }

      iframe.addEventListener('load', function() {
        sendThemeToIframe(currentTheme);
      });

      // Handle messages from both the embedded iframe and the VS Code extension host
      window.addEventListener('message', function(event) {
        if (event.source === iframe.contentWindow) {
          // Message originated from Tendril Web UI inside the iframe
          if (event.data && typeof event.data === 'object') {
            vscode.postMessage(event.data);
          }
        } else {
          // Message originated from VS Code Extension Host
          if (event.data && typeof event.data === 'object') {
            if (event.data.type === 'themeChanged') {
              currentTheme = event.data.theme;
            }
            sendThemeToIframe(currentTheme);
          }
        }
      });
    })();
  </script>
</body>
</html>`;
}
