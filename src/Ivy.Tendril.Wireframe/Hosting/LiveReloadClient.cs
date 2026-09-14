namespace Ivy.Tendril.Wireframe.Hosting;

/// <summary>
/// The browser half of hot reload. Served at <c>{base}__wireframe/client.js</c> in serve mode
/// only; its script tag carries the wireframe's base in <c>data-base</c>, so the same file works
/// at the root of the standalone server and under a plan preview's address.
///
/// Full-page reload rather than React Fast Refresh: Fast Refresh needs a Babel/SWC
/// transform esbuild does not ship, and it would be actively wrong for this library
/// anyway. Tendril memoises rough.js paths against ResizeObserver-measured boxes, so a
/// partial refresh leaves stale SVG strokes sized for the old layout sitting behind
/// freshly-rendered content, which reads as a rendering bug in the library rather than a
/// stale frame. A reload with a warm vendor bundle is about 200 ms and always correct.
///
/// Inside a frame (a plan preview) the page talks to its parent instead of drawing on itself:
/// it reports its height so the frame can fit it, and a failed rebuild is reported rather than
/// shown, because the error overlay quotes source code and a reviewer is never shown code.
/// </summary>
public static class LiveReloadClient
{
    public const string Source =
        """
        (() => {
          const script = document.currentScript;
          const BASE = (script && script.dataset.base) || "/";
          const EMBEDDED = window.parent !== window;
          const ENDPOINT = `${location.protocol === "https:" ? "wss:" : "ws:"}//${location.host}${BASE}__wireframe/hmr`;
          const SCROLL_KEY = "__wireframe_scroll";

          // Messages to the page this wireframe is framed in. Same origin only.
          function toParent(message) {
            if (!EMBEDDED) return;
            try { parent.postMessage({ source: "tendril-wireframe", ...message }, location.origin); } catch {}
          }

          // Restore scroll across the reload so editing feels continuous.
          try {
            const saved = sessionStorage.getItem(SCROLL_KEY);
            if (saved !== null) {
              sessionStorage.removeItem(SCROLL_KEY);
              addEventListener("load", () => scrollTo(0, parseInt(saved, 10) || 0));
            }
          } catch {}

          function reload() {
            try { sessionStorage.setItem(SCROLL_KEY, String(scrollY)); } catch {}
            location.reload();
          }

          // --- error overlay ------------------------------------------------------
          // In a shadow root so the app's CSS reset and the .tendril font cannot
          // restyle it, and so it never collides with the wireframe's own markup.
          let overlay = null;

          function ensureOverlay() {
            if (overlay) return overlay;
            overlay = document.createElement("div");
            overlay.style.cssText = "position:fixed;inset:0;z-index:2147483647";
            overlay.attachShadow({ mode: "open" }).innerHTML = `
              <style>
                .wrap {
                  position: fixed; inset: 0; overflow: auto; box-sizing: border-box;
                  padding: 28px 32px;
                  background: rgba(24,24,27,.94); color: #fafafa;
                  font: 13px/1.55 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
                }
                h1 { margin: 0 0 4px; font-size: 12px; font-weight: 700; color: #fca5a5;
                     letter-spacing: .08em; text-transform: uppercase; }
                .loc  { margin: 0 0 18px; color: #a1a1aa; }
                pre   { margin: 0; white-space: pre-wrap; word-break: break-word; }
                .hint { margin-top: 22px; color: #71717a; }
              </style>
              <div class="wrap">
                <h1>build failed</h1>
                <p class="loc"></p>
                <pre></pre>
                <p class="hint">The last working version is still running underneath. Press Esc to dismiss.</p>
              </div>`;
            document.body.appendChild(overlay);
            return overlay;
          }

          function showError(location, text) {
            if (EMBEDDED) { toParent({ type: "build-failed" }); return; }
            const el = ensureOverlay();
            el.shadowRoot.querySelector(".loc").textContent = location || "";
            el.shadowRoot.querySelector("pre").textContent = text || "";
            el.style.display = "block";
          }

          function hideError() {
            if (EMBEDDED) { toParent({ type: "build-ok" }); return; }
            if (overlay) overlay.style.display = "none";
          }

          addEventListener("keydown", (e) => { if (e.key === "Escape" && !EMBEDDED) hideError(); });

          // --- surface page errors in the terminal --------------------------------
          // The whole point of this tool is that the user may never open devtools.
          function report(kind, detail) {
            try {
              navigator.sendBeacon(
                `${BASE}__wireframe/report`,
                new Blob([JSON.stringify({ kind, detail })], { type: "application/json" })
              );
            } catch {}
          }
          addEventListener("error", (e) =>
            report("error", e.error ? `${e.error.message}\n${e.error.stack ?? ""}` : e.message));
          addEventListener("unhandledrejection", (e) =>
            report("unhandledrejection", String(e.reason?.stack ?? e.reason)));

          // --- size, for the frame this page sits in ------------------------------
          if (EMBEDDED) {
            // The content's own size at the frame's width: its full height, and its width when
            // something (a wide table) overflows. The parent sizes the frame to exactly this and
            // scales it down to fit, so nothing scrolls inside the frame.
            const measure = () => {
              const root = document.getElementById("root") || document.body;
              const rect = root.getBoundingClientRect();
              const height = Math.max(rect.bottom + scrollY, root.scrollHeight);
              const width = Math.max(document.documentElement.scrollWidth, root.scrollWidth);
              toParent({ type: "size", height: Math.ceil(height), width: Math.ceil(width) });
            };
            addEventListener("load", () => {
              measure();
              const root = document.getElementById("root") || document.body;
              const observer = new ResizeObserver(measure);
              observer.observe(root);
              for (const child of root.children) observer.observe(child);
            });
            // Swapping in the handwriting font reflows every box after load.
            document.fonts?.ready.then(measure);
          }

          // --- socket -------------------------------------------------------------
          let everConnected = false;
          let stopped = false;

          function connect() {
            const ws = new WebSocket(ENDPOINT);

            ws.onopen = () => {
              // A reconnect means the server restarted, so our bundle is stale.
              if (everConnected) return reload();
              everConnected = true;
            };

            ws.onmessage = (event) => {
              const msg = JSON.parse(event.data);
              // On error we deliberately do NOT reload: the last good bundle stays
              // mounted underneath, so the page keeps working while the typo is fixed.
              if (msg.type === "reload") { hideError(); reload(); }
              else if (msg.type === "error") showError(msg.location, msg.text);
              else if (msg.type === "ok") hideError();
              // Another plan is live now. Keep showing the last build, and stop listening:
              // reconnecting would reload this page and take the preview back.
              else if (msg.type === "stopped") { stopped = true; toParent({ type: "stopped" }); ws.close(); }
            };

            ws.onclose = () => { if (!stopped) setTimeout(connect, 500); };
            ws.onerror = () => ws.close();
          }

          connect();
        })();

        """;
}
