import React, { useState } from "react";

interface CounterProps {
  id: string;
  label?: string;
  initialValue?: number;
  step?: number;
  color?: string;
}

export const Counter: React.FC<CounterProps> = ({
  label = "Count",
  initialValue = 0,
  step = 1,
}) => {
  const [count, setCount] = useState(initialValue);

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: "8px",
        padding: "16px",
        borderRadius: "8px",
        border: "1px solid var(--border)",
        background: "var(--card)",
        minWidth: "120px",
      }}
    >
      <span
        style={{
          fontSize: "12px",
          fontWeight: 500,
          color: "var(--muted-foreground)",
          textTransform: "uppercase",
          letterSpacing: "0.05em",
        }}
      >
        {label}
      </span>
      <span
        style={{
          fontSize: "32px",
          fontWeight: 700,
          color: "var(--foreground)",
          fontVariantNumeric: "tabular-nums",
        }}
      >
        {count}
      </span>
      <div style={{ display: "flex", gap: "4px" }}>
        <button
          onClick={() => setCount((c) => c - step)}
          style={{
            padding: "4px 12px",
            borderRadius: "4px",
            border: "1px solid var(--border)",
            background: "var(--muted)",
            color: "var(--foreground)",
            cursor: "pointer",
            fontSize: "16px",
            fontWeight: 600,
          }}
        >
          −
        </button>
        <button
          onClick={() => setCount((c) => c + step)}
          style={{
            padding: "4px 12px",
            borderRadius: "4px",
            border: "1px solid var(--border)",
            background: "var(--muted)",
            color: "var(--foreground)",
            cursor: "pointer",
            fontSize: "16px",
            fontWeight: 600,
          }}
        >
          +
        </button>
      </div>
    </div>
  );
};
