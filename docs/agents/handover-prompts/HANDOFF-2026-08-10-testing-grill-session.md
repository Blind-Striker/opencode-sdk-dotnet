# HANDOFF 2026-08-10 — Grill session: testing architecture spec (holistic, three specs)

Consume against live git; delete when the session's outcome ships (per
`handover-prompts/README.md`). Paste the block below as the priming prompt of a
fresh-context session.

---

/deniz-process:grilling

Hedef: `docs/superpowers/specs/2026-08-10-testing-architecture-design.md` spec'ini grill'lemek.
Kapsam HOLISTIC — üç spec'in (public API, generator architecture, testing architecture)
kesişimleri masada; odak SON SPEC: test stratejisi ve mimarisi. Çıktı: yerinde düzeltilmiş
spec (+ gerekirse ADR / research-log kaydı); sonrası `writing-plans`.

ONBOARDING — TAM yap; önceki session'lar eksik onboarding'in bedelini ödedi. Okuma sırası:

1. `CONTEXT.md`; `docs/adr/README.md` + ADR 0001–0009 — özellikle 0001 (launcher üç-OS
   kabulü), 0002 (net472 = ns2.0 proxy), 0005 (consumer-driven legacy), 0009
   (unknown-variant toleransı — integration'da test edilemezlik sınırı).
2. `docs/ROADMAP.md` — bu session'ın Queue 1'deki yeri.
3. `docs/research/00-research-log.md` BAŞTAN SONA (Q1–Q43; özellikle Q40–Q43 — testing
   spec'inin kanıt zinciri bu dört girdide).
4. ÜÇ SPEC TAM: `2026-08-09-public-api-design.md`, `2026-08-09-generator-architecture.md`,
   `2026-08-10-testing-architecture-design.md` (grill hedefi).
5. Research tam metin: 12 (retry/SSE resilience — StandardResilience timeout'larının SSE'yi
   öldürmesi), 02 (stream garantileri); skim: 06 §3, 08–11.
6. Repo altyapısı: `.github/workflows/ci.yml`, `Directory.Build.props` /
   `Directory.Packages.props` (Verify henüz YOK — spec §10 build-out'a bırakıyor),
   `global.json`, `tests/OpenCode.Sdk.Tests/`, `.editorconfig` §15 test-override.
7. UPSTREAM — spec'in dayandığı dosyaları DOĞRUDAN oku ve iddiaları YENİDEN doğrula
   (önceki session agent raporuyla doğruladı; grill kendi gözüyle bakar):
   `packages/opencode/package.json` (`test:httpapi` script'i),
   `test/server/httpapi-exercise/{index,routing,runner,backend,environment}.ts`,
   `test/lib/{llm-server,cli-process,test-provider}.ts`, `test/preload.ts`, `bunfig.toml`,
   `packages/sdk/js/src/{server,process}.ts`, `.github/workflows/test.yml`,
   `packages/opencode/Dockerfile`.
8. REFERANS REPOLAR (GitHub'dan aç): localstack-dotnet-client (`ci-cd.yml`,
   `aws-sdk-canary.yml`, Cake `TestTask.cs`) + dotnet-aspire-for-localstack (`ci-cd.yml`,
   `run-dotnet-tests` composite) — üç-OS matrix, Linux-gated container, canary desenleri.

DOĞRULAMA KURALI: her olgusal iddia birincil kaynağa vurulur; kanıtlanamayan UNVERIFIED
işaretlenir, sessizce kabul edilmez. Spike/PoC serbest ve teşvikli (`.scratchpad/`).

GRILL ODAKLARI (asgari — kendi bulduklarınla genişlet):

- Spec §14 UNVERIFIED listesi: TUnit mekanizmaları (ClassDataSource shared semantiği
  multi-TFM assembly'de, `[InheritsTests]` dual-run, custom conditional skip,
  `[NotInParallel("group")]`, MTP `testconfig.json`) — mümkünse spike ile KANITLA;
  Testcontainers .NET host-port exposure; GHCR anonim pull; opencode kurulum yolu.
- Fake LLM sadakati: opencode'un `@ai-sdk/openai-compatible` provider'ı gerçekten hangi
  endpoint'i, `stream: true` ile mi çağırıyor? Fake'in taklit edeceği asıl sözleşmeyi
  upstream kodundan çıkar; `/v1/responses`'ın gerçekten gerekip gerekmediğini sorgula.
- Auth/reachability sweep güvenliği: credential'lı probe'lar mutasyon yaratabilir mi
  (empty-body POST validation'ı geçerse)? Adanmış instance yeterli izolasyon mu; 188 op
  üzerinde süre maliyeti ne?
- Coverage gate gerçekçiliği: `[ExercisesOperation]` deklarasyonu ile gerçek koşum
  arasındaki boşluk (deklarasyon var, assert zayıf — gate bunu göremez); karantina
  etkileşimi; Skip yasağının reflection'la denetiminin sağlamlığı.
- Aynı-kaynak döngüselliği (§2 ilke 5): contract katmanı + sweep gerçekten yeterli mi,
  yoksa gerçek-sunucu response'larına karşı şema doğrulaması da mı gerekir?
- Süre bütçesi: tam-TFM integration (4 TFM × 3 OS direct + Linux container) CI'ı ne kadar
  şişirir; `[NotInParallel("llm")]` throughput'u; paylaşılan-sunucu session-izolasyonunun
  sızıntı riskleri (instance-level state: config, auth, PTY?).
- Canary'nin flake yönetimi (non-blocking ama kim bakar?); GHCR imaj bakım maliyeti; pin
  tek-kaynak mekaniğinin (refresh-spec stamping) somut yeri.
- Cross-spec tutarlılık: generator çıktı listesi ↔ testing tüketimi (inventory/fixtures,
  ikinci manifest kökü); public API spec §11 risk notu ↔ testing spec §9.1 "early"
  talimatı; ADR-0005 gate genişlemesi; ADR-0001 kabulü ↔ §9.2; dual-mode'un launcher
  teşhis iddiası (§2 ilke 4) gerçekçi mi?

KURALLAR: konuşma Türkçe, artefaktlar İngilizce. Kararlar maintainer'la tek tek mühürlenir;
spec yerinde düzeltilir; canonical doc düzenlemeleri ve commit tekil onayla. Session sonu
dokümantasyon pası: research log girdisi (soru→bulgu→karar); ROADMAP güncellemesi (grill
tamam → `writing-plans`); bu handover'ın silinmesi. Tek commit.
