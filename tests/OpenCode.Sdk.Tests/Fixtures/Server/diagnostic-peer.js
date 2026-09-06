console.log(JSON.stringify({ url: "http://127.0.0.1:1" }));
console.log("BODY-STDOUT");
console.error("BODY-STDERR");
process.stdin.resume();
process.stdin.on("end", () => {
    console.log("FINAL-STDOUT");
    console.error("FINAL-STDERR");
});
