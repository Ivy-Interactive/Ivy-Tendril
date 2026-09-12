export const COMMANDS = {
  openDashboard: 'tendril.openDashboard',
  openInBrowser: 'tendril.openInBrowser',
  openWorktree: 'tendril.openWorktree',
  startServer: 'tendril.startServer',
  stopServer: 'tendril.stopServer',
  restartServer: 'tendril.restartServer'
} as const;

export const VIEWS = {
  sidebarContainer: 'tendril-sidebar',
  quickAccess: 'tendril.quickAccess'
} as const;

export const CONFIG_KEYS = {
  executablePath: 'tendril.executablePath',
  serverAutoStart: 'tendril.server.autoStart',
  serverStopOnExit: 'tendril.server.stopOnExit',
  serverPort: 'tendril.server.port',
  serverPollTimeout: 'tendril.server.pollTimeout',
  homeDirectory: 'tendril.homeDirectory'
} as const;
