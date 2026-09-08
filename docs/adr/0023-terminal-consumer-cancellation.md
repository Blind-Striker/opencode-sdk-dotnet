# Terminal consumer cancellation is independent of connection lifetime

Date: 2026-09-08

A terminal consumer needs to stop waiting for output and later read or write on the same
attachment. Passing its cancellation token to physical WebSocket receive can abort that
connection. Both terminal families therefore use a connection-owned receiver and an unbounded
internal channel; public enumeration cancellation affects only the consumer. This is an SDK
contract, not a guarantee implied by `IAsyncEnumerable` or an upstream cancel/resume API.
The queue retains undelivered frames and may grow with an absent or slow consumer. A memory
limit and overflow policy require workload evidence before changing this contract. Connection
termination preserves queued output before reporting its outcome; explicit disposal abandons
unread output and ends owned work without requiring consumer progress.
