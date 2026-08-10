# HANDOFF 2026-08-10 — writing-plans session: implementation plan(s)

Consume against live git; delete when the session's outcome ships (per
`handover-prompts/README.md`). Paste the block below as the priming prompt of a
fresh-context session.

---

/deniz-process:writing-plans

Hedef: üç grill-hardened spec'ten multi-phase implementation planı üretmek. Fazlar dikey
dilim: `tools/` + SDK + Extensions + testler birlikte gelişir (ADR-0006; testing spec §1).
Çıktı: `docs/superpowers/plans/YYYY-MM-DD-<name>.md` (checkbox'lı task'lar) + dilim
issue'ları + ROADMAP güncellemesi.

ONBOARDING (grill'den hafif — spec'ler self-contained, upstream YENİDEN OKUNMAZ; bir iddia
şüpheli görünürse research log'daki kanıt zincirine in):

1. `AGENTS.md` (Locked Decisions + Hard Rules + Engineering Conventions — testing-posture
   ve defensive-programming maddeleri yeni); `CONTEXT.md`; `docs/adr/README.md` +
   ADR 0001–0009.
2. `docs/ROADMAP.md` — queue 1'in son adımı bu session; queue 2 bu planın alanı.
3. ÜÇ SPEC TAM: `docs/superpowers/specs/2026-08-09-public-api-design.md`,
   `2026-08-09-generator-architecture.md`, `2026-08-10-testing-architecture-design.md`.
4. Research log session 6–9 (`docs/research/00-research-log.md`) — karar zinciri;
   session 9 = grill düzeltmeleri (fake-LLM sözleşmesi, workspace modeli, sweep, ledger,
   TUnit spike).
5. Repo altyapısı: `Directory.Build.props` / `Directory.Packages.props`, `global.json`,
   `.editorconfig` (§14 generated code, §15 test overrides), `.github/workflows/ci.yml`,
   `tests/OpenCode.Sdk.Tests/`.

PLAN KISITLARI (çiğnenemez; kaynak spec'ler):

- Dikey dilimler; her dilim tek başına çalışan, test edilen yazılım (writing-plans scope
  check).
- **Streams-early:** SSE engine, testing spec §9.1 stream senaryolarıyla AYNI dilimde.
- **Downlevel-early:** full-spec generated output 5 TFM'de derlenir milestone'u emitter
  polish'ten ÖNCE (generator spec §12; ROADMAP net472 spike maddeleri o dilime katılır).
- Her dilimin "done" tanımı: analyzer wall temiz + dilimin testleri yeşil + format gate +
  (ilgiliyse) `generate --verify`.
- TDD (transport/SSE/launcher — ROADMAP queue 2 notları); test naming convention ve
  defensive-programming default (AGENTS.md).

PROGRESS MODELİ (mühürlü, 2026-08-10):

- Task/adım düzeyi: plan dosyasının checkbox'ları — kanonik ev. Task-düzeyi issue YOK.
- Dilim düzeyi: dilim başına bir GitHub issue (`docs/agents/issue-tracker.md` mekaniği;
  plan dosyasına link verir, içerik kopyalamaz); dilimler arası sıra native `blocked_by`
  kenarları; planı hazır dilim `ready-for-agent` etiketi alır
  (`docs/agents/triage-labels.md`); execution session issue'yu claim eder
  (`--add-assignee @me`); dilim landiğinde issue kapanır. Wayfinder'ın map/fog/frontier
  katmanı KULLANILMAZ (keşif işi değil). Bu session planlarla birlikte dilim issue'larını
  açar.
- ROADMAP iki katmana relay yapar (issue aralığı + `superpowers/plans/` + tek satır durum);
  ince progress ROADMAP'e girmez.

SESSION'IN MAINTAINER'LA MÜHÜRLETECEĞİ AÇIK KARARLAR:

- Dilim sayısı/sınırları/sırası. İskelet önerisi (bağlayıcı değil, session gerekçelendirir):
  (0) test projeleri + tooling iskeleti → (1) parser/SpecIR + ilk model dilimi + 5-TFM
  compile milestone → (2) binder/curation + emitters → (3) transport core + envelopes +
  op methods → (4) SSE engine + stream senaryoları → (5) launcher + üç-OS acceptance →
  (6) CI legleri (integration direct/container + sweep + canary).
- Tek plan mı, dilim başına plan mı (scope-check dilim-başına yönünde işaret ediyor).
- Execution modeli: `subagent-driven-development` (önerilen) vs `executing-plans`; commit
  ritmi — development-loop anlaşması (AGENTS.md commit kuralının istisna alanı burada
  açılır).
- Branch/worktree stratejisi (master'da implementation başlamaz — `executing-plans` kuralı;
  `using-git-worktrees`).

KURALLAR: konuşma Türkçe, artefaktlar İngilizce. Kararlar maintainer'la tek tek mühürlenir;
canonical doc düzenlemeleri ve commit tekil onayla. Session sonu: plan(lar) +
dilim issue'ları + ROADMAP güncellemesi (queue 1 kapanır, queue 2 plan/issue'lara bağlanır) +
bu handover'ın silinmesi; tek commit.
