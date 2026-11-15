import { Command } from "commander";
import { OutputOpts, setQuiet } from "./output";

/**
 * Read global output flags (--json / --yaml / --quiet) from any command by
 * walking up to the root program. Local --json on a command also wins.
 */
export function outputOpts(cmd: Command): OutputOpts {
  const collected: Record<string, unknown> = {};
  let current: Command | null = cmd;
  while (current) {
    Object.assign(collected, current.opts());
    current = current.parent;
  }
  const opts: OutputOpts = {
    json: collected.json === true,
    yaml: collected.yaml === true,
    quiet: collected.quiet === true,
  };
  setQuiet(opts.quiet === true);
  return opts;
}
