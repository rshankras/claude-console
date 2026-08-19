# Working in this repo

Keypad plugins for terminal coding agents. One engine, one package per agent.

Deliberately short — it holds what is expensive to rediscover, and links out for the rest.
For the architecture and the reasoning behind it, read [docs/multi-agent-architecture.md](docs/multi-agent-architecture.md) first.

## Layout

```
src/Core/            the engine — platform bridges, session grid, file IPC, actions,
                     voice, self-registration + heal, key rendering, risk classifier
src/Core/Agents/     IAgentAdapter, AgentCapabilities, NoAgentAdapter (the contract)
src/Agents/<agent>/  one adapter per agent: ClaudeCode, CodexCli
src/Products/<name>/ a thin product: plugin class, application binding, package metadata,
                     profile, icon → one .lplug4
tests/               one suite covering the engine and every agent
scripts/             the shell writers the agents run (statusline, activity, codex hook)
```

Two orthogonal seams: `IPlatformBridge` hides the OS, `IAgentAdapter` hides the agent.
Neither may look through the other. Everything else is neutral to both.

## Build, test, pack

```bash
dotnet build src/Products/ClaudeConsole -t:Compile      # compile-check one product
bash tests/run-all.sh                                    # C# + shell suites
DOTNET_ROLL_FORWARD=LatestMajor bash tools/voice/pack-release.sh <ver> <Product>
```

- **`dotnet build` mutates your live Logi install.** It writes a dev `.link` and reloads
  LogiPluginService. Use `-t:Compile` to check compilation, or `-p:SkipPluginLink=true` for a
  full build that leaves the installed plugin alone.
- **`logiplugintool` needs `DOTNET_ROLL_FORWARD=LatestMajor`** — it targets .NET 8 and the
  default runtime here is 10, so packing fails without it.
- Never install a package *and* keep a dev `.link`: the service sees the plugin twice, refuses
  the duplicate, and the keys render as bare text.

## Rules that are not obvious

**Never bundle `PluginApi.dll`** or its dependency closure. The host provides it; Marketplace QA
rejected a submission over exactly this. Enforced by `<Private>false</Private>` on the reference.

**A key must never show a value the agent did not report.** Codex reports no cost, so the Cost
key renders a dash and its binding is dropped from that product's profile — a `$0.00` is
indistinguishable from a free session, and borrowing another key's value (it briefly showed the
model) just gives the keypad two keys saying the same thing. Gate on `Capabilities`, and return
`null` from `SlashCommand` for a verb the agent lacks so the key is never added at all.

**Capability gaps and packaging gaps are not the same thing.** Codex cannot report cost — that is
the agent. Voice, by contrast, is agent-neutral: it records, transcribes and injects into the
focused session without asking what runs there, so it ships in every product. Before dropping a key
from a product, decide which kind of gap you are looking at; only the first is permanent.

**Each product declares itself in its plugin CONSTRUCTOR, not `Load()`.** The SDK builds every
action in between, and an action reads the agent to decide which keys to add and the product slug
to resolve its IPC paths. Declaring late builds the keys against no agent.

**Products must not share on-disk namespaces.** IPC root, `@_` registration, profile GUID,
runtime home. Two consoles sharing an IPC root would reap each other's sessions, since the grid
deletes state for sessions whose process it cannot see.

**Registration identity comes from the packaged profile**, never a literal — `ApplicationInfo.json`
carries it. Key bindings inside a profile are `<PluginShortName>___<Type>___<param>`, so a profile
copied between products must have that prefix rewritten or every key silently does nothing.

**Codex trusts hooks by hash** and re-prompts when one changes. The installed command and the
launcher's contents are part of the install contract: keep the launcher stable and put churn in
the C#, or every user is asked to re-approve on every update. Never suggest
`--dangerously-bypass-hook-trust`.

**Version lives in two files per product** (csproj and `LoupedeckPackage.yaml`) and they must
agree. The assembly version is what the crash-disable marker keys on. `ProductVersionTests`
guards this.

**Injection is atomic or it does not happen.** Every `Inject*` focuses the target session and
types in one indivisible operation, or types nothing and reports why. Focus-then-type as two
steps is a bug however convenient — that is how a keystroke lands in the wrong application.

## Testing

`bash tests/run-all.sh` runs the C# suite plus the shell tests for the writer scripts. Tests
compile the engine and both agents from source rather than referencing a product, so one suite
covers every agent and a test run cannot touch your installed plugin.

The suite runs serially on purpose: `IpcPaths.ProductSlug` is process-global, so parallel
collections would let one class observe another's product.

Grid code DELETES state for sessions it judges dead — always drive a temp IPC root in tests,
never the live one. `run-all.sh` leaves a canary to prove the live root survived.

## Where things are written down

- [docs/multi-agent-architecture.md](docs/multi-agent-architecture.md) — the two seams, captured
  Codex payloads, per-product packaging, risks
- [SUBMISSION.md](SUBMISSION.md) — Marketplace packaging, signing, notarization
- [docs/marketplace-listing.md](docs/marketplace-listing.md) — listing copy, reusable per release
- [CHANGELOG.md](CHANGELOG.md) — release history
- [docs/HANDOFF.md](docs/HANDOFF.md) — Windows port state
