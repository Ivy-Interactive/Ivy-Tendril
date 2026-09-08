export interface AddressParts {
  host: string;
  path: string;
}

export function addressParts(url: string): AddressParts {
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return { host: url, path: "" };
    const path = parsed.pathname + parsed.search + parsed.hash;
    return { host: parsed.host, path: path === "/" ? "" : path };
  } catch {
    return { host: url, path: "" };
  }
}
