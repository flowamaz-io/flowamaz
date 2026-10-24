/**
 * Node config written by hand / AI in YAML uses the flowamaz/v1 snake_case keys
 * (timeout_hours, on_timeout, system_prompt …); nodes drawn on the canvas historically used
 * camelCase keys (timeoutValue, onTimeout, systemPrompt …). The engine accepts both, with the
 * snake_case key winning. Config panels must therefore:
 *   - read the keys in the same order the engine does, so the panel shows what will run;
 *   - write an edit back to the key that is already present, so an untouched YAML setting is
 *     never shadowed or replaced by a panel default.
 */
export type Config = Record<string, unknown>;

/** True when `key` is set on the config (null/undefined count as unset). */
export function hasKey(config: Config, key: string): boolean {
  return config[key] !== undefined && config[key] !== null;
}

/** First set value among `keys` (engine precedence order). */
export function readAlias(config: Config, keys: readonly string[]): unknown {
  for (const key of keys) {
    if (hasKey(config, key)) return config[key];
  }
  return undefined;
}

export function readString(config: Config, keys: readonly string[]): string | undefined {
  const value = readAlias(config, keys);
  return typeof value === 'string' ? value : undefined;
}

export function readNumber(config: Config, keys: readonly string[]): number | undefined {
  for (const key of keys) {
    const value = config[key];
    if (typeof value === 'number' && Number.isFinite(value)) return value;
  }
  return undefined;
}

/**
 * Return a copy of `config` with `value` stored under the alias already in use (the first of
 * `keys` that is set), or under `fallbackKey` when none is. Other aliases of the same setting
 * are removed so a stale value can't shadow the edit. `undefined` removes the setting.
 */
export function writeAlias(config: Config, keys: readonly string[], fallbackKey: string, value: unknown): Config {
  const target = keys.find((k) => hasKey(config, k)) ?? fallbackKey;
  const next: Config = { ...config };
  for (const key of keys) {
    if (key !== target) delete next[key];
  }
  if (value === undefined) delete next[target];
  else next[target] = value;
  return next;
}

/** Copy of `config` without the given keys. */
export function withoutKeys(config: Config, keys: readonly string[]): Config {
  const next: Config = { ...config };
  for (const key of keys) delete next[key];
  return next;
}
