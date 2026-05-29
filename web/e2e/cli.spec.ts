import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { expect, test } from '@playwright/test';

// fmz CLI E2E (prompt 05-08, S47–S48). Exercises the CLI's local YAML validation exit codes via a
// subprocess. The built CLI (cli/dist/index.js) must exist — the nightly e2e job runs `npm run build
// --prefix cli` first. Workflow validation that hits the API is covered by backend integration tests;
// here we assert the CLI's exit-code contract on local connector-manifest validation (no backend).
const CLI = resolve(__dirname, '../../cli/dist/index.js');

function runCli(args: string[]): { code: number; output: string } {
  try {
    const output = execFileSync('node', [CLI, ...args], { encoding: 'utf8', stdio: 'pipe' });
    return { code: 0, output };
  } catch (err) {
    const e = err as { status?: number; stdout?: string; stderr?: string };
    return { code: e.status ?? 1, output: `${e.stdout ?? ''}${e.stderr ?? ''}` };
  }
}

test.describe('fmz CLI (S47–S48)', () => {
  // S47: a valid manifest validates and the CLI exits 0.
  test('S47: validate a valid connector manifest → exit 0', () => {
    const dir = mkdtempSync(join(tmpdir(), 'fmz-cli-'));
    const file = join(dir, 'valid.yaml');
    writeFileSync(file, 'name: my-connector\nversion: 1.0.0\n');

    const { code } = runCli(['connector', 'validate', file]);
    expect(code).toBe(0);
  });

  // S48: an invalid manifest fails validation and the CLI exits non-zero with an error.
  test('S48: validate an invalid connector manifest → exit 1 with error', () => {
    const dir = mkdtempSync(join(tmpdir(), 'fmz-cli-'));
    const file = join(dir, 'invalid.yaml');
    writeFileSync(file, 'description: missing name and version\n');

    const { code, output } = runCli(['connector', 'validate', file]);
    expect(code).not.toBe(0);
    expect(output.toLowerCase()).toMatch(/name|version|invalid|required/);
  });

  test('S47b: --help exits 0', () => {
    expect(runCli(['--help']).code).toBe(0);
  });
});
