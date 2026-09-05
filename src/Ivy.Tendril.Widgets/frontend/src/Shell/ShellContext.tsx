import { createContext, useContext } from "react";

export interface ShellContextValue {
  collapsed: boolean;
  toggle: () => void;
}

/** Shared collapse state for every widget rendered inside a TendrilShell.
    Defaults keep the widgets usable when hosted standalone (samples, tests). */
export const ShellContext = createContext<ShellContextValue>({
  collapsed: false,
  toggle: () => {},
});

export const useShell = (): ShellContextValue => useContext(ShellContext);
