const port = Number(process.env.OPENCODE_SDK_TEST_TREE_PORT);
const nonce = process.env.OPENCODE_SDK_TEST_TREE_NONCE;
const mode = process.env.OPENCODE_SDK_TEST_TREE_MODE;

if (!Number.isInteger(port) || port <= 0 || !nonce || !mode) {
  throw new Error("The child-tree fixture requires its port, nonce, and mode environment.");
}

const child = Bun.spawn(
  [process.execPath, "-e", "setTimeout(() => {}, 120000)"],
  { stdin: "ignore", stdout: "ignore", stderr: "ignore" },
);
let socket;
try {
  const net = await import("node:net");
  socket = net.createConnection({ host: "127.0.0.1", port });

  await new Promise((resolve, reject) => {
    let response = "";
    socket.setEncoding("utf8");
    socket.once("connect", () => {
      socket.write(`${JSON.stringify({ nonce, rootPid: process.pid, childPid: child.pid })}\n`);
    });
    socket.on("data", (chunk) => {
      response += chunk;
      const newline = response.indexOf("\n");
      if (newline < 0) return;

      if (response.slice(0, newline).trim() !== "ACK") {
        reject(new Error("The child-tree fixture received an invalid acknowledgement."));
        return;
      }

      resolve();
    });
    socket.once("end", () => reject(new Error("The child-tree handshake ended before acknowledgement.")));
    socket.once("error", reject);
  });

  socket.end();
  if (mode === "invalid-line") {
    console.log("hello");
  } else if (mode !== "silent") {
    throw new Error(`Unknown child-tree fixture mode '${mode}'.`);
  }

  await new Promise(() => {});
} finally {
  socket?.destroy();
  if (child.exitCode === null) {
    child.kill();
  }

  await child.exited;
}
