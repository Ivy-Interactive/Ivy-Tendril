namespace Ivy.Tendril.Wireframe.Screenshot;

/// <summary>
/// Injected before first paint so repeated screenshots of the same wireframe match.
///
/// Note what is NOT here: rough.js seeds. SketchProvider already defaults to
/// deterministic:true and seedFrom(undefined) folds to a constant, so the geometry is
/// stable on its own. Overriding SketchProvider from the screenshot path would actually
/// make things worse -- the PNG would stop matching what `serve` shows.
/// </summary>
public static class DeterminismPayload
{
    /// <summary>
    /// Freezes animation. tendril.css defines 14 @keyframes with no prefers-reduced-motion
    /// guard, so the media emulation alone does nothing to them.
    ///
    /// The critical detail is `animation-fill-mode: forwards` with a near-zero duration
    /// rather than `animation: none`. `none` leaves elements at their INITIAL keyframe --
    /// and tendril-fade-in starts at opacity:0, so it would produce a blank screenshot of
    /// a page that looks perfectly fine in the browser. Snapping to the final keyframe is
    /// what a settled page actually looks like.
    /// </summary>
    public const string Script =
        """
        (() => {
          const style = document.createElement("style");
          style.textContent = `
            *, *::before, *::after {
              animation-delay: -0.0001s !important;
              animation-duration: 0.0001s !important;
              animation-iteration-count: 1 !important;
              animation-fill-mode: forwards !important;
              transition-duration: 0.0001s !important;
              transition-delay: 0s !important;
              scroll-behavior: auto !important;
              /* A focused TextInput blinks; that is a one-pixel-column diff between two
                 otherwise identical runs. */
              caret-color: transparent !important;
            }
          `;
          const attach = () => (document.head || document.documentElement).appendChild(style);
          if (document.head) attach();
          else document.addEventListener("DOMContentLoaded", attach, { once: true });
        })();
        """;

    /// <summary>
    /// Polls the readiness contract from src/wireframe-ready.ts.
    ///
    /// The fallback matters: a hand-written project may not call signalWireframeReady().
    /// Counting SVG paths is a sound proxy because SketchFrame returns null at zero
    /// measured size -- so "no paths" provably means measurement has not happened yet.
    /// </summary>
    public const string ReadinessProbe =
        """
        (() => {
          const w = window.__wireframe;
          if (w && w.version === 1) {
            return { ready: !!w.ready, reason: w.reason ?? null, hook: true,
                     deterministic: w.deterministic !== false };
          }
          const paths = document.querySelectorAll("svg path").length;
          const signature = paths + ":" + document.documentElement.scrollHeight +
                            ":" + document.body.innerHTML.length;
          const previous = window.__wfProbeSignature;
          window.__wfProbeSignature = signature;
          const ready = previous === signature && paths > 0 && document.fonts.status === "loaded";
          return { ready, reason: ready ? null : `heuristic probe (paths=${paths})`,
                   hook: false, deterministic: true };
        })()
        """;

    /// <summary>Forces one more paint before capture.</summary>
    public const string SettlePaint =
        "new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => r(true))))";
}
