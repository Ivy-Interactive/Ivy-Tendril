import { memo, useRef, useState, useEffect, useCallback } from "react";

interface GraphvizRendererProps {
  content: string;
}

const sanitizeSvg = (svg: string): string => {
  const parser = new DOMParser();
  const doc = parser.parseFromString(svg, "image/svg+xml");
  const parserError = doc.querySelector("parsererror");
  if (parserError) return "<svg></svg>";

  const svgElement = doc.documentElement;

  const dangerous = ["script", "object", "embed", "link", "meta", "iframe", "frame", "frameset", "form", "input", "button", "textarea", "select"];
  dangerous.forEach((tag) => {
    svgElement.querySelectorAll(tag).forEach((el) => el.remove());
  });

  const walker = document.createTreeWalker(svgElement, NodeFilter.SHOW_ELEMENT, null);
  let node;
  while ((node = walker.nextNode())) {
    const el = node as Element;
    Array.from(el.attributes).forEach((attr) => {
      if (attr.name.toLowerCase().startsWith("on") || attr.name === "href" || attr.name === "xlink:href") {
        el.removeAttribute(attr.name);
      }
    });
  }

  return new XMLSerializer().serializeToString(svgElement);
};

const applyFontToSvg = (svgString: string): string => {
  let fontSans = "Geist, sans-serif";
  try {
    const resolved = getComputedStyle(document.documentElement).getPropertyValue("--font-sans").trim();
    if (resolved) fontSans = resolved;
  } catch { /* use fallback */ }

  const parser = new DOMParser();
  const doc = parser.parseFromString(svgString, "image/svg+xml");
  const svgElement = doc.documentElement;

  svgElement.querySelectorAll("text").forEach((el) => {
    el.setAttribute("font-family", fontSans);
    const fontSize = el.getAttribute("font-size");
    if (fontSize) {
      const match = fontSize.match(/^([\d.]+)(.*)$/);
      if (match) {
        el.setAttribute("font-size", `${(parseFloat(match[1]) * 0.8).toFixed(2)}${match[2] || ""}`);
      }
    }
  });

  return new XMLSerializer().serializeToString(svgElement);
};

export const GraphvizRenderer = memo(({ content }: GraphvizRendererProps) => {
  const elementRef = useRef<HTMLDivElement>(null);
  const [state, setState] = useState<{ isLoading: boolean; error: string | null }>({ isLoading: true, error: null });
  const [currentTheme, setCurrentTheme] = useState<"light" | "dark">("light");
  const themeRef = useRef<"light" | "dark">("light");

  const detectTheme = useCallback(() => {
    return document.documentElement.classList.contains("dark") ? "dark" : "light";
  }, []);

  useEffect(() => {
    const initialTheme = detectTheme();
    setCurrentTheme(initialTheme);
    themeRef.current = initialTheme;

    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        if (mutation.type === "attributes" && mutation.attributeName === "class") {
          const newTheme = detectTheme();
          if (newTheme !== themeRef.current) {
            setCurrentTheme(newTheme);
            themeRef.current = newTheme;
          }
        }
      }
    });

    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, [detectTheme]);

  const renderDiagram = useCallback(
    async (mountedObj: { current: boolean }) => {
      if (!elementRef.current) return;

      try {
        setState({ isLoading: true, error: null });
        const { Graphviz } = await import("@hpcc-js/wasm-graphviz");
        const graphviz = await Graphviz.load();
        const svg = graphviz.dot(content.trim());

        if (mountedObj.current && elementRef.current) {
          elementRef.current.innerHTML = sanitizeSvg(applyFontToSvg(svg));
          setState((prev) => ({ ...prev, isLoading: false }));
        }
      } catch (err) {
        if (mountedObj.current) {
          setState({ isLoading: false, error: err instanceof Error ? err.message : "Failed to render diagram" });
        }
      }
    },
    [content],
  );

  useEffect(() => {
    const mountedObj = { current: true };
    renderDiagram(mountedObj);
    return () => { mountedObj.current = false; };
  }, [renderDiagram, currentTheme]);

  if (state.error) {
    return (
      <div className="pmv-diagram-error">
        <span>Invalid Graphviz DOT syntax</span>
      </div>
    );
  }

  return (
    <div className="pmv-diagram-container">
      {state.isLoading && (
        <div className="pmv-diagram-loading">
          <span>Loading diagram...</span>
        </div>
      )}
      <div ref={elementRef} style={{ minHeight: state.isLoading ? "100px" : "auto" }} />
    </div>
  );
});

GraphvizRenderer.displayName = "GraphvizRenderer";
