// Compiles the Tailwind utility superset that ships inside the tool.
//
// The library's own dist/tendril.css carries only 355 class selectors -- every class the
// COMPONENTS use, and nothing else. It has no grid-cols-*, no space-y-*, no mt-4, no
// max-w-md, no min-h-screen, no mx-auto. An agent laying out a wireframe hits a missing
// utility within minutes, and the failure is silent: the class sits in the DOM with no
// rule behind it.
//
// So we run REAL Tailwind here, at tool-build time, over an explicit safelist. Because
// Tailwind itself generates it, the output is exactly correct -- this is not a
// hand-written approximation of Tailwind's output.
//
// Two constraints, both load-bearing:
//   * NO preflight. tendril.css already ships one; a second reset would apply twice.
//     Hence importing theme.css/utilities.css individually rather than "tailwindcss".
//   * Emit into the SAME cascade layers tendril.css declares (@layer properties, theme,
//     base, utilities). tendril.css declares that order first, which fixes it for the
//     document. An unlayered sheet, or one using different layer names, would lose to
//     the library's rules no matter what order the <link> tags are in.
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { pathToFileURL } from "node:url";

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1"));
const cssDir = path.resolve(here, "../../artifacts/css");
const tw = path.join(here, "tailwind");

/** Tailwind v4 brace-expansion patterns. Each line becomes one `@source inline(...)`. */
function safelist() {
  // Responsive prefixes are applied to the layout-bearing utilities only; applying them
  // to everything multiplies the sheet without buying much for a wireframe.
  const R = "{,sm:,md:,lg:,xl:}";
  const S = "{0,0.5,1,1.5,2,2.5,3,3.5,4,5,6,7,8,9,10,11,12,14,16,20,24,28,32,36,40,44,48,56,64,72,80,96}";
  const FRAC = "{1/2,1/3,2/3,1/4,3/4,1/5,2/5,3/5,4/5,1/6,5/6,1/12,5/12,7/12,11/12}";
  const SIZE_KW = "{auto,full,screen,min,max,fit,px,dvh,dvw}";
  const MAXW = "{none,xs,sm,md,lg,xl,2xl,3xl,4xl,5xl,6xl,7xl,full,min,max,fit,prose,screen-sm,screen-md,screen-lg,screen-xl,screen-2xl}";
  const TEXT = "{xs,sm,base,lg,xl,2xl,3xl,4xl,5xl,6xl,7xl,8xl,9xl}";
  const WEIGHT = "{thin,extralight,light,normal,medium,semibold,bold,extrabold,black}";
  const RADIUS = "{none,xs,sm,md,lg,xl,2xl,3xl,full}";
  const SHADOW = "{none,xs,sm,md,lg,xl,2xl,inner}";
  const NUM12 = "{1,2,3,4,5,6,7,8,9,10,11,12}";
  // The tendril palette, from the library's @theme block.
  const COLOR =
    "{ink,ink-muted,ink-faint,paper,paper-raised,paper-sunken,highlight,accent,success,warning,destructive,info,transparent,current,inherit,white,black}";
  const STATE = "{,hover:,focus:,focus-visible:,active:,disabled:,first:,last:,odd:,even:,group-hover:}";

  return [
    // ---- spacing -------------------------------------------------------------
    `${R}{p,px,py,pt,pr,pb,pl,ps,pe}-${S}`,
    `${R}{m,mx,my,mt,mr,mb,ml,ms,me}-${S}`,
    `${R}{-m,-mx,-my,-mt,-mr,-mb,-ml}-${S}`,
    `${R}{m,mx,my,mt,mr,mb,ml}-auto`,
    `${R}{gap,gap-x,gap-y}-${S}`,
    `${R}{space-x,space-y}-${S}`,

    // ---- sizing --------------------------------------------------------------
    `${R}{w,h,size,min-w,min-h}-${S}`,
    `${R}{w,h,size,min-w,min-h,max-w,max-h}-${SIZE_KW}`,
    `${R}{w,h}-${FRAC}`,
    `${R}max-w-${MAXW}`,
    `${R}{min-h,h}-screen`,

    // ---- display / flex / grid ----------------------------------------------
    `${R}{block,inline-block,inline,flex,inline-flex,grid,inline-grid,contents,hidden,table,flow-root}`,
    `${R}{flex-row,flex-row-reverse,flex-col,flex-col-reverse,flex-wrap,flex-wrap-reverse,flex-nowrap}`,
    `${R}{flex-1,flex-auto,flex-initial,flex-none,grow,grow-0,shrink,shrink-0}`,
    `${R}{items-start,items-center,items-end,items-baseline,items-stretch}`,
    `${R}{justify-start,justify-center,justify-end,justify-between,justify-around,justify-evenly,justify-stretch}`,
    `${R}{self-auto,self-start,self-center,self-end,self-stretch,self-baseline}`,
    `${R}{content-start,content-center,content-end,content-between,content-around,content-evenly}`,
    `${R}{justify-items-start,justify-items-center,justify-items-end,justify-items-stretch}`,
    `${R}grid-cols-${NUM12}`,
    `${R}grid-rows-{1,2,3,4,5,6}`,
    `${R}{col-span,row-span}-${NUM12}`,
    `${R}{col-span-full,row-span-full}`,
    `${R}{col-start,col-end}-${NUM12}`,
    `${R}{grid-flow-row,grid-flow-col,grid-flow-dense,auto-cols-auto,auto-cols-fr,auto-rows-auto,auto-rows-fr,auto-rows-min}`,
    `${R}order-${NUM12}`,
    `${R}{order-first,order-last,order-none}`,

    // ---- typography ----------------------------------------------------------
    `text-${TEXT}`,
    `font-${WEIGHT}`,
    `{font-sketch,font-sketch-mono,font-sans,font-serif,font-mono}`,
    `${R}{text-left,text-center,text-right,text-justify,text-start,text-end}`,
    `{leading-none,leading-tight,leading-snug,leading-normal,leading-relaxed,leading-loose}`,
    `{tracking-tighter,tracking-tight,tracking-normal,tracking-wide,tracking-wider,tracking-widest}`,
    `{uppercase,lowercase,capitalize,normal-case,italic,not-italic}`,
    `{underline,overline,line-through,no-underline,truncate,text-nowrap,text-wrap,text-balance,text-pretty}`,
    `{break-words,break-all,break-keep,whitespace-normal,whitespace-nowrap,whitespace-pre,whitespace-pre-wrap,whitespace-pre-line}`,
    `{align-baseline,align-top,align-middle,align-bottom}`,
    `line-clamp-{1,2,3,4,5,6,none}`,
    `{list-none,list-disc,list-decimal,list-inside,list-outside}`,

    // ---- color ---------------------------------------------------------------
    `${STATE}{bg,text,border,fill,stroke,decoration}-${COLOR}`,
    `${STATE}{bg,text,border}-opacity-{0,5,10,20,25,30,40,50,60,70,75,80,90,95,100}`,

    // ---- border / radius / shadow -------------------------------------------
    `rounded-${RADIUS}`,
    `{rounded-t,rounded-r,rounded-b,rounded-l,rounded-tl,rounded-tr,rounded-br,rounded-bl}-${RADIUS}`,
    `{border,border-0,border-2,border-4,border-8}`,
    `{border-t,border-r,border-b,border-l,border-x,border-y}-{0,2,4,8}`,
    `{border-t,border-r,border-b,border-l,border-x,border-y}`,
    `{border-solid,border-dashed,border-dotted,border-double,border-none}`,
    `${STATE}shadow-${SHADOW}`,
    `{divide-x,divide-y}`,
    `{ring,ring-0,ring-1,ring-2,ring-4,ring-8,ring-inset}`,
    `outline-{none,0,1,2,4,8}`,

    // ---- layout / position ---------------------------------------------------
    `${R}{static,relative,absolute,fixed,sticky}`,
    `${R}{inset,inset-x,inset-y,top,right,bottom,left}-${S}`,
    `${R}{inset,inset-x,inset-y,top,right,bottom,left}-{0,auto,full,1/2}`,
    `${R}{-top,-right,-bottom,-left}-${S}`,
    `z-{0,10,20,30,40,50,auto}`,
    `${R}{overflow,overflow-x,overflow-y}-{auto,hidden,clip,visible,scroll}`,
    `{object-contain,object-cover,object-fill,object-none,object-scale-down}`,
    `{object-top,object-center,object-bottom,object-left,object-right}`,
    `aspect-{auto,square,video}`,
    `{float-left,float-right,float-none,clear-both,clear-none}`,
    `{box-border,box-content,isolate,isolation-auto}`,
    `{columns-1,columns-2,columns-3,columns-4}`,

    // ---- effects / interactivity --------------------------------------------
    `${STATE}opacity-{0,5,10,20,25,30,40,50,60,70,75,80,90,95,100}`,
    `{cursor-auto,cursor-default,cursor-pointer,cursor-wait,cursor-text,cursor-move,cursor-help,cursor-not-allowed,cursor-grab,cursor-grabbing}`,
    `{select-none,select-text,select-all,select-auto}`,
    `{pointer-events-none,pointer-events-auto,resize,resize-none,resize-x,resize-y}`,
    `{transition,transition-all,transition-colors,transition-opacity,transition-transform,transition-none}`,
    `duration-{0,75,100,150,200,300,500,700,1000}`,
    `{ease-linear,ease-in,ease-out,ease-in-out}`,
    `${STATE}{scale,rotate,translate-x,translate-y}-{0,1,2,45,90,180}`,
    `{animate-none,animate-spin,animate-ping,animate-pulse,animate-bounce}`,
    `{sr-only,not-sr-only}`,
    `{invisible,visible,collapse}`,
    `{backdrop-blur,backdrop-blur-sm,backdrop-blur-md,blur,blur-sm,blur-md,grayscale,invert}`,

    // ---- the library's own custom utilities ---------------------------------
    `{sketch-wobble,sketch-underline,sketch-hatch,tendril}`,
  ];
}

export function buildCss() {
  fs.mkdirSync(tw, { recursive: true });
  fs.mkdirSync(cssDir, { recursive: true });

  const lines = safelist();
  const input = [
    "/* GENERATED by build-css.mjs -- do not edit. */",
    "",
    "/* No preflight: tendril.css already ships one. Importing the theme and utilities",
    "   layers individually is the supported v4 way to opt out of the reset. */",
    '@import "tailwindcss/theme.css" layer(theme);',
    '@import "tailwindcss/utilities.css" layer(utilities);',
    "",
    "/* The library's design tokens, so bg-paper / text-ink / font-sketch resolve. */",
    '@import "../node_modules/tendril-wireframes/dist/theme.css";',
    "",
    "/* Nothing is scanned from disk -- the safelist below IS the content source. */",
    ...lines.map((l) => `@source inline(${JSON.stringify(l)});`),
    "",
  ].join("\n");

  const inputPath = path.join(tw, "input.css");
  fs.writeFileSync(inputPath, input);
  fs.writeFileSync(path.join(tw, "safelist.txt"), lines.join("\n") + "\n");

  const outPath = path.join(cssDir, "wireframe-utilities.css");
  const cli = path.join(here, "node_modules/@tailwindcss/cli/dist/index.mjs");
  execFileSync(process.execPath, [cli, "-i", inputPath, "-o", outPath, "--minify"], {
    cwd: here,
    stdio: ["ignore", "pipe", "pipe"],
  });

  const css = fs.readFileSync(outPath, "utf8");
  return {
    patterns: lines.length,
    bytes: css.length,
    // A cheap smoke test on the classes we know the library's own sheet lacks.
    has: Object.fromEntries(
      ["grid-cols-3", "space-y-4", "mt-4", "max-w-md", "min-h-screen", "mx-auto", "gap-8", "p-8", "bg-paper", "text-4xl", "rounded-lg", "col-span-2"].map(
        (c) => [c, css.includes("." + c.replace(/\//g, "\\/")) ]
      )
    ),
  };
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const r = buildCss();
  console.log(`  wireframe-utilities.css  ${(r.bytes / 1024).toFixed(1)} KB from ${r.patterns} safelist patterns`);
  const missing = Object.entries(r.has).filter(([, ok]) => !ok).map(([c]) => c);
  console.log(`  smoke test: ${Object.keys(r.has).length - missing.length}/${Object.keys(r.has).length} present`);
  if (missing.length) {
    console.error(`  MISSING: ${missing.join(", ")}`);
    process.exit(1);
  }
}
