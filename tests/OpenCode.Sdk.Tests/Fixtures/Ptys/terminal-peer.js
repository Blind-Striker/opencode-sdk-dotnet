const readline = await import("node:readline");

const input = readline.createInterface({
  input: process.stdin,
  crlfDelay: Infinity,
  terminal: false,
});

input.on("line", (line) => {
  const match = /^RUN ([0-9a-f]+)$/.exec(line);
  if (match) process.stdout.write(`\nACK:${match[1]}\n`);
});

// A leading delimiter keeps the record distinct from terminal-mode controls emitted by a PTY
// host before the child produces its first byte.
process.stdout.write("\nREADY\n");
await new Promise((resolve) => input.once("close", resolve));
