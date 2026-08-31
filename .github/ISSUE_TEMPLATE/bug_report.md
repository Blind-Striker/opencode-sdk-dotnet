---
name: Bug report
about: Create a report to help us improve the opencode .NET SDK
title: ''
labels: bug, needs-triage
assignees: ''

---

## 🐛 Bug Description

**What happened?**
A clear and concise description of the bug.

**What did you expect to happen?**
A clear and concise description of what you expected to happen.

## 🔄 Steps to Reproduce

1.
2.
3.
4.

**Minimal code example:**

```csharp
// The smallest snippet that reproduces the issue.
using OpenCode.Sdk;

using var client = new OpenCodeClient(new OpenCodeClientOptions
{
    Endpoint = new Uri("http://127.0.0.1:4096"),
});

// Your code that reproduces the issue here
```

## 📋 Environment Information

**OpenCode.Sdk Version:**

- Version: (e.g., `0.1.0-nightly.20260831.abc1234`)
- Package source: (NuGet.org / GitHub Packages nightly / built from source at commit …)
- Also using `OpenCode.Sdk.Extensions`? (yes / no)

**opencode Server:**

- Server version: (the `version` field from `GetHealthAsync`, e.g., `v0.0.0-next-17403`)
- How it was started: (`opencode2 serve` yourself / `OpenCodeServer.StartAsync()` / a background
  service you already had)

**.NET Information:**

- Target framework: (e.g., `net10.0`, `net8.0`, `net472`, `netstandard2.0` consumer)
- .NET SDK / runtime version:
- Operating System: (e.g., Windows 11, Ubuntu 24.04, macOS 15)
- IDE/Editor: (e.g., Rider, Visual Studio 2022, VS Code)

## 🔍 Additional Context

**Which part of the SDK?**

- [ ] A generated operation (name it, e.g., `Sessions.CreateSessionAsync`)
- [ ] Event streaming (`EventsClient.SubscribeAsync` / `SessionClient.GetLogAsync`)
- [ ] A terminal session (`PtySession` / `PersistentPtySession`)
- [ ] The launcher (`OpenCodeServer`)
- [ ] Dependency injection (`AddOpenCode`)
- [ ] Serialization / a response that did not deserialize
- [ ] Other:

**Error Messages/Stack Traces:**

```text
Paste any error messages or stack traces here
```

**Raw request/response (Optional):**
If the problem is a payload the SDK refused or mistyped, the raw JSON the server returned is the
most useful thing you can attach. Redact credentials and any project content first.

```json
```

**Additional Information:**
Add any other context about the problem here.

## ✅ Checklist

- [ ] I have searched existing issues to ensure this is not a duplicate
- [ ] I have provided all the requested information above
- [ ] I have tested this with the latest nightly package (or the current `master`)
- [ ] I have checked the [Known Issues](https://github.com/Blind-Striker/opencode-sdk-dotnet#known-issues) section of the README
- [ ] This is a problem in the SDK, not in the opencode server itself (as far as I can tell)
