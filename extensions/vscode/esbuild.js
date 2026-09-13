const esbuild = require('esbuild');

const isProduction = process.argv.includes('--production');
const isWatch = process.argv.includes('--watch');

async function main() {
  const extensionCtx = await esbuild.context({
    entryPoints: ['src/extension.ts'],
    bundle: true,
    format: 'cjs',
    minify: isProduction,
    sourcemap: !isProduction,
    sourcesContent: false,
    platform: 'node',
    outfile: 'out/extension.js',
    external: ['vscode'],
    logLevel: 'info'
  });

  const testCtx = await esbuild.context({
    entryPoints: [
      'src/test/runTest.ts',
      'src/test/suite/index.ts',
      'src/test/suite/discovery.test.ts',
      'src/test/suite/bridge.test.ts'
    ],
    bundle: true,
    format: 'cjs',
    sourcemap: true,
    sourcesContent: false,
    platform: 'node',
    outdir: 'out/test',
    external: ['vscode', 'mocha'],
    logLevel: 'info'
  });

  if (isWatch) {
    await Promise.all([extensionCtx.watch(), testCtx.watch()]);
  } else {
    await Promise.all([extensionCtx.rebuild(), testCtx.rebuild()]);
    await Promise.all([extensionCtx.dispose(), testCtx.dispose()]);
  }
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
