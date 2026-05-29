#!/usr/bin/env node
import { Command } from "commander";
import { registerAuthCommands } from "./commands/auth";
import { registerWorkspaceCommands } from "./commands/workspace";
import { registerWorkflowCommands } from "./commands/workflow";
import { registerInstanceCommands } from "./commands/instance";
import { registerLibraryCommands } from "./commands/library";
import { registerVersionCommands } from "./commands/version";
import { error } from "./lib/output";
import { describeError } from "./lib/util";

const program = new Command();

program
  .name("fmz")
  .description("Flowamaz platform command-line interface")
  .version("0.1.0")
  .option("--json", "Output JSON")
  .option("--yaml", "Output YAML")
  .option("--quiet", "Suppress informational output")
  .showHelpAfterError();

registerAuthCommands(program);
registerWorkspaceCommands(program);
registerWorkflowCommands(program);
registerInstanceCommands(program);
registerLibraryCommands(program);
registerVersionCommands(program);

async function main(): Promise<void> {
  await program.parseAsync(process.argv);
}

main().catch((err: unknown) => {
  error(describeError(err));
  process.exitCode = 1;
});
