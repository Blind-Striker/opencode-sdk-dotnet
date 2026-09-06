console.log("INVALID-READY");
process.stdin.resume();
process.stdin.on("end", () => {
    console.log("FINAL-STDOUT");
    console.error("FINAL-STDERR");
});
