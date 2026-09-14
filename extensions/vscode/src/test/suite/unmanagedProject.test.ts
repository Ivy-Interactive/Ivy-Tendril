import * as assert from 'assert';
import { fetchProjects, isWorkspaceManaged } from '../../server/masterDiscovery';
import { TendrilProjectSummary } from '../../server/types';

describe('Unmanaged Project Detection Suite', () => {
  describe('fetchProjects', () => {
    const originalFetch = globalThis.fetch;

    afterEach(() => {
      globalThis.fetch = originalFetch;
    });

    it('should parse valid /api/projects response correctly', async () => {
      const mockProjects = [
        {
          name: 'ProjectAlpha',
          color: 'green',
          repos: ['/repos/alpha', '/repos/alpha-docs']
        },
        {
          name: 'ProjectBeta',
          repos: ['/repos/beta']
        }
      ];

      globalThis.fetch = (async (url: RequestInfo | URL) => {
        assert.ok(String(url).includes('/api/projects'));
        return {
          ok: true,
          json: async () => mockProjects
        } as Response;
      }) as typeof fetch;

      const result = await fetchProjects('http://localhost:5000');
      assert.strictEqual(result.length, 2);
      assert.strictEqual(result[0].name, 'ProjectAlpha');
      assert.strictEqual(result[0].color, 'green');
      assert.deepStrictEqual(result[0].repos, ['/repos/alpha', '/repos/alpha-docs']);
      assert.strictEqual(result[1].name, 'ProjectBeta');
      assert.strictEqual(result[1].color, undefined);
      assert.deepStrictEqual(result[1].repos, ['/repos/beta']);
    });

    it('should return empty array on non-ok HTTP response', async () => {
      globalThis.fetch = (async () => {
        return {
          ok: false,
          status: 500
        } as Response;
      }) as typeof fetch;

      const result = await fetchProjects('http://localhost:5000');
      assert.deepStrictEqual(result, []);
    });
  });

  describe('isWorkspaceManaged', () => {
    const projects: TendrilProjectSummary[] = [
      {
        name: 'Ivy-Tendril',
        repos: ['/Users/dev/git/ivy-tendril', 'C:\\repos\\Ivy-Tendril']
      },
      {
        name: 'SecondaryProject',
        repos: ['/Users/dev/git/secondary/']
      }
    ];

    it('should match workspace folder with exact match', () => {
      const status = isWorkspaceManaged('/Users/dev/git/ivy-tendril', projects);
      assert.strictEqual(status.isManaged, true);
      assert.strictEqual(status.projectName, 'Ivy-Tendril');
    });

    it('should match workspace folder handling case insensitivity and normalized separators', () => {
      // Different casing
      const caseStatus = isWorkspaceManaged('/users/dev/git/IVY-TENDRIL', projects);
      assert.strictEqual(caseStatus.isManaged, true);
      assert.strictEqual(caseStatus.projectName, 'Ivy-Tendril');

      // Trailing slashes
      const trailingSlashStatus = isWorkspaceManaged('/Users/dev/git/ivy-tendril/', projects);
      assert.strictEqual(trailingSlashStatus.isManaged, true);
      assert.strictEqual(trailingSlashStatus.projectName, 'Ivy-Tendril');

      // Windows paths with backslashes
      const windowsStatus = isWorkspaceManaged('c:\\repos\\ivy-tendril', projects);
      assert.strictEqual(windowsStatus.isManaged, true);
      assert.strictEqual(windowsStatus.projectName, 'Ivy-Tendril');

      // Subfolder inside registered repo
      const subfolderStatus = isWorkspaceManaged('/Users/dev/git/ivy-tendril/src/Ivy.Tendril', projects);
      assert.strictEqual(subfolderStatus.isManaged, true);
      assert.strictEqual(subfolderStatus.projectName, 'Ivy-Tendril');
    });

    it('should detect unmanaged status when workspace folder is not in registered projects list', () => {
      const unmanaged = isWorkspaceManaged('/Users/dev/git/unknown-new-project', projects);
      assert.strictEqual(unmanaged.isManaged, false);
      assert.strictEqual(unmanaged.projectName, undefined);
    });

    it('should return isManaged true for empty workspace path', () => {
      const empty = isWorkspaceManaged('', projects);
      assert.strictEqual(empty.isManaged, true);
    });
  });
});
