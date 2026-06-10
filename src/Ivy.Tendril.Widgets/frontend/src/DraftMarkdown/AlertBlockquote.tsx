import React from "react";

type AlertType = "NOTE" | "TIP" | "IMPORTANT" | "WARNING" | "CAUTION";

const ALERT_TYPES = new Set<string>(["NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION"]);

const alertConfig: Record<AlertType, { title: string; className: string }> = {
  NOTE: { title: "Note", className: "pmv-alert pmv-alert--note" },
  TIP: { title: "Tip", className: "pmv-alert pmv-alert--tip" },
  IMPORTANT: { title: "Important", className: "pmv-alert pmv-alert--important" },
  WARNING: { title: "Warning", className: "pmv-alert pmv-alert--warning" },
  CAUTION: { title: "Caution", className: "pmv-alert pmv-alert--caution" },
};

const InfoIcon = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <circle cx="12" cy="12" r="10" />
    <path d="M12 16v-4" />
    <path d="M12 8h.01" />
  </svg>
);

const AlertIcon = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" />
    <path d="M12 9v4" />
    <path d="M12 17h.01" />
  </svg>
);

const CheckIcon = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <circle cx="12" cy="12" r="10" />
    <path d="m9 12 2 2 4-4" />
  </svg>
);

function getIcon(type: AlertType) {
  switch (type) {
    case "NOTE": return <InfoIcon />;
    case "TIP": return <CheckIcon />;
    case "IMPORTANT":
    case "WARNING":
    case "CAUTION": return <AlertIcon />;
  }
}

function extractTextContent(node: React.ReactNode): string {
  if (typeof node === "string") return node;
  if (typeof node === "number") return String(node);
  if (!node) return "";
  if (Array.isArray(node)) return node.map(extractTextContent).join("");
  if (React.isValidElement(node)) {
    const props = node.props as { children?: React.ReactNode };
    return extractTextContent(props.children);
  }
  return "";
}

function stripAlertMarker(children: React.ReactNode, markerLength: number): React.ReactNode {
  const childArray = React.Children.toArray(children);
  if (childArray.length === 0) return children;

  const first = childArray[0];
  if (typeof first === "string") {
    const remaining = first.slice(markerLength).replace(/^\n/, "");
    if (remaining.length === 0) return childArray.slice(1);
    return [remaining, ...childArray.slice(1)];
  }
  return children;
}

interface ParsedAlert {
  type: AlertType;
  content: React.ReactNode;
}

function parseGitHubAlert(children: React.ReactNode): ParsedAlert | null {
  const childArray = React.Children.toArray(children);
  if (childArray.length === 0) return null;

  let firstChildIndex = 0;
  let firstChild = childArray[firstChildIndex];
  while (firstChildIndex < childArray.length && !React.isValidElement(firstChild)) {
    firstChildIndex++;
    firstChild = childArray[firstChildIndex];
  }
  if (!firstChild || !React.isValidElement(firstChild)) return null;

  const firstProps = firstChild.props as { children?: React.ReactNode };
  const textContent = extractTextContent(firstProps.children);

  const match = textContent.match(/^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*/);
  if (!match) return null;

  const type = match[1] as AlertType;
  if (!ALERT_TYPES.has(type)) return null;

  const strippedFirstChildren = stripAlertMarker(firstProps.children, match[0].length);

  const hasRemainingContent =
    React.Children.toArray(strippedFirstChildren).length > 0 &&
    extractTextContent(strippedFirstChildren).trim().length > 0;

  const remainingChildren = childArray.slice(firstChildIndex + 1).filter(React.isValidElement);

  let content: React.ReactNode;
  if (hasRemainingContent) {
    const modifiedFirst = React.cloneElement(firstChild, {}, strippedFirstChildren);
    content = remainingChildren.length > 0 ? [modifiedFirst, ...remainingChildren] : modifiedFirst;
  } else {
    content = remainingChildren.length > 0 ? remainingChildren : null;
  }

  return { type, content };
}

export const AlertBlockquote: React.FC<React.HTMLAttributes<HTMLQuoteElement>> = ({ children }) => {
  const alert = parseGitHubAlert(children);

  if (alert) {
    const config = alertConfig[alert.type];
    return (
      <div className={config.className} role="alert">
        <div className="pmv-alert-header">
          {getIcon(alert.type)}
          <span className="pmv-alert-title">{config.title}</span>
        </div>
        <div className="pmv-alert-content">{alert.content}</div>
      </div>
    );
  }

  return <blockquote>{children}</blockquote>;
};
