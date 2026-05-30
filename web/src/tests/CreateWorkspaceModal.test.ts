import { describe, expect, it } from 'vitest';
import { slugify } from '@/components/workspace/createWorkspace';

describe('CreateWorkspaceModal slugify', () => {
  it('auto-generates the slug from the name', () => {
    expect(slugify('Finance Ops!')).toBe('finance-ops');
  });

  it('lowercases, collapses repeated separators and trims edges', () => {
    expect(slugify('  My  Cool__Workspace  ')).toBe('my-cool-workspace');
  });

  it('preserves digits and existing hyphens', () => {
    expect(slugify('Team-42 Beta')).toBe('team-42-beta');
  });
});
