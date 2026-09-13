import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import {
  discoverMaster,
  getMasterFilePath,
  HEARTBEAT_TIMEOUT_MS,
  isHeartbeatFresh,
  isProcessAlive,
  parseMasterJson,
  readApiKeyFromConfig,
  resolveTendrilHome
} from '../../server/masterDiscovery';

describe('Tendril Master Discovery Suite', () => {
  describe('parseMasterJson', () => {
    it('should parse valid camelCase master file JSON', () => {
      const json = JSON.stringify({
        pid: 12345,
        port: 5000,
        scheme: 'http',
        startedAt: '2026-09-10T12:00:00Z',
        heartbeat: '2026-09-10T12:01:00Z'
      });

      const parsed = parseMasterJson(json);
      assert.ok(parsed);
      assert.strictEqual(parsed.pid, 12345);
      assert.strictEqual(parsed.port, 5000);
      assert.strictEqual(parsed.scheme, 'http');
      assert.strictEqual(parsed.heartbeat, '2026-09-10T12:01:00Z');
    });

    it('should parse valid PascalCase master file JSON', () => {
      const json = JSON.stringify({
        Pid: 54321,
        Port: 5001,
        Scheme: 'https',
        StartedAt: '2026-09-10T12:00:00Z',
        Heartbeat: '2026-09-10T12:01:30Z'
      });

      const parsed = parseMasterJson(json);
      assert.ok(parsed);
      assert.strictEqual(parsed.pid, 54321);
      assert.strictEqual(parsed.port, 5001);
      assert.strictEqual(parsed.scheme, 'https');
    });

    it('should return null for malformed JSON or missing required fields', () => {
      assert.strictEqual(parseMasterJson('invalid json'), null);
      assert.strictEqual(parseMasterJson('{}'), null);
      assert.strictEqual(parseMasterJson(JSON.stringify({ pid: 100 })), null);
      assert.strictEqual(parseMasterJson(JSON.stringify({ pid: 100, port: 5000 })), null);
    });
  });

  describe('isProcessAlive', () => {
    it('should return true for current process PID', () => {
      assert.strictEqual(isProcessAlive(process.pid), true);
    });

    it('should return false for invalid or dead PIDs', () => {
      assert.strictEqual(isProcessAlive(0), false);
      assert.strictEqual(isProcessAlive(-1), false);
      assert.strictEqual(isProcessAlive(99999999), false);
    });
  });

  describe('isHeartbeatFresh', () => {
    it('should return true for heartbeat within 90 seconds', () => {
      const now = Date.now();
      const freshDate = new Date(now - 30_000).toISOString();
      assert.strictEqual(isHeartbeatFresh(freshDate, HEARTBEAT_TIMEOUT_MS, now), true);
    });

    it('should return false for heartbeat older than 90 seconds', () => {
      const now = Date.now();
      const staleDate = new Date(now - 95_000).toISOString();
      assert.strictEqual(isHeartbeatFresh(staleDate, HEARTBEAT_TIMEOUT_MS, now), false);
    });

    it('should return false for invalid date format', () => {
      assert.strictEqual(isHeartbeatFresh('not-a-date'), false);
    });
  });

  describe('resolveTendrilHome', () => {
    const originalEnv = process.env.TENDRIL_HOME;

    afterEach(() => {
      if (originalEnv !== undefined) {
        process.env.TENDRIL_HOME = originalEnv;
      } else {
        delete process.env.TENDRIL_HOME;
      }
    });

    it('should prioritize explicit override if provided', () => {
      const custom = '/custom/tendril/home';
      const resolved = resolveTendrilHome(custom);
      assert.strictEqual(resolved, path.resolve(custom));
    });

    it('should use TENDRIL_HOME environment variable when override is absent', () => {
      const envPath = '/env/tendril/home';
      process.env.TENDRIL_HOME = envPath;
      const resolved = resolveTendrilHome();
      assert.strictEqual(resolved, path.resolve(envPath));
    });

    it('should fall back to ~/.tendril when neither is provided', () => {
      delete process.env.TENDRIL_HOME;
      const resolved = resolveTendrilHome();
      assert.strictEqual(resolved, path.join(os.homedir(), '.tendril'));
    });
  });

  describe('discoverMaster file integration', () => {
    let tempDir: string;

    beforeEach(() => {
      tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'tendril-discovery-test-'));
    });

    afterEach(() => {
      fs.rmSync(tempDir, { recursive: true, force: true });
    });

    it('should return not_found when .master file does not exist', () => {
      const status = discoverMaster(tempDir);
      assert.strictEqual(status.status, 'not_found');
    });

    it('should return found when .master file is valid and process is alive', () => {
      const masterFile = getMasterFilePath(tempDir);
      const data = {
        pid: process.pid,
        port: 4567,
        scheme: 'http',
        heartbeat: new Date().toISOString()
      };
      fs.writeFileSync(masterFile, JSON.stringify(data), 'utf-8');

      const status = discoverMaster(tempDir, true);
      assert.strictEqual(status.status, 'found');
      if (status.status === 'found') {
        assert.strictEqual(status.result.port, 4567);
        assert.strictEqual(status.result.pid, process.pid);
        assert.strictEqual(status.result.baseUrl, 'http://localhost:4567');
      }
    });

    it('should detect stale heartbeat in .master file', () => {
      const masterFile = getMasterFilePath(tempDir);
      const data = {
        pid: process.pid,
        port: 4567,
        scheme: 'http',
        heartbeat: new Date(Date.now() - 120_000).toISOString()
      };
      fs.writeFileSync(masterFile, JSON.stringify(data), 'utf-8');

      const status = discoverMaster(tempDir, true);
      assert.strictEqual(status.status, 'stale_heartbeat');
    });

    it('should read ApiKey from config.yaml if present', () => {
      const configPath = path.join(tempDir, 'config.yaml');
      fs.writeFileSync(
        configPath,
        ['Api:', '  ApiKey: test-secret-token', 'theme: dark'].join('\n'),
        'utf-8'
      );

      const apiKey = readApiKeyFromConfig(tempDir);
      assert.strictEqual(apiKey, 'test-secret-token');
    });
  });
});
