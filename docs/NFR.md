# Non-Functional Requirements: FitCore

- **Status:** Draft v1. Sizing, availability, budget, support hours and supported OS versions confirmed by the product owner on 2026-10-13. Values marked *review* still need legal sign-off.
- **Date:** 2026-10-13
- **Related:** [`BRD.md`](BRD.md) (business requirements). This document replaces the "to be defined" row in [BRD §9](BRD.md#9-non-functional-requirements).

This document turns the BRD into measurable, testable non-functional requirements (NFRs). The BRD says *what* FitCore does and which rules it follows. This document says *how well* it must do it, and how each target is checked.

---

## 1. How to read this document

- **IDs.** Every requirement has an ID such as `NFR-AVL-1`. Tests, tickets and release checklists cite these IDs.
- **Phase.** The first release phase where the requirement applies, using the phases in [BRD §4.1](BRD.md#41-release-phases): **Beta**, **Public**, **Coach/Pro** or **Clinic**. A requirement applies to every later phase too.
- **Verification.** How the requirement is checked: automated test, CI gate, load test, monitoring, audit or review.
- **Must, not should.** Every row is a requirement. Aspirations are listed in [§17](#17-open-items) instead.

### 1.1 Sizing assumptions

The targets are sized for the first 12 months of the public member app. The architecture must reach 10× this load (about 100k members) without a redesign; only plan tiers and instance counts may change.

| Measure | Launch assumption | Design headroom |
|---|---|---|
| Registered members | 10,000 | 100,000 |
| Daily active members | 2,000 | 20,000 |
| Active coaches and clinicians | 300 | 3,000 |
| Peak API load | 50 requests/s sustained, bursts to 150 requests/s | 500 requests/s sustained, bursts to 1,500 requests/s |
| Diary entries | ~20,000 per day | ~200,000 per day |
| Stored data (database + photos) | ≤ 100 GB after year 1 | ≤ 1 TB |

## 2. Availability and recovery

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-AVL-1 | Backend availability (API, Auth, database) is at least **99.5% per calendar month** (about 3.6 hours of downtime). Beta is best effort. | Public | External uptime monitoring; monthly SLO report |
| NFR-AVL-2 | Planned maintenance is at most 2 hours per month, announced at least 48 hours ahead, and scheduled outside 06:00–22:00 US Eastern. It does not count against NFR-AVL-1. | Public | Maintenance log |
| NFR-AVL-3 | **Recovery point objective (RPO) ≤ 1 hour**: no more than one hour of committed data can be lost in a disaster. In Beta the limit is ≤ 24 hours (daily backups); it tightens to ≤ 1 hour at the Public launch. | Beta | Point-in-time recovery enabled; restore drill |
| NFR-AVL-4 | **Recovery time objective (RTO) ≤ 8 hours** from a declared disaster to service restored. | Public | Restore drill |
| NFR-AVL-5 | A full database and storage restore is rehearsed at least once per quarter, and the measured RPO and RTO are recorded. | Public | Drill report |
| NFR-AVL-6 | An outage of a third-party service (USDA, commercial food database, AI provider, health-platform sync, email, push) degrades only the feature that uses it. Diary logging, custom foods, recents and saved meals keep working. | Beta | Fault-injection tests |
| NFR-AVL-7 | When the backend is unreachable, the device apps show cached data read-only, keep queued offline writes ([NFR-REL-2](#5-reliability-and-data-integrity)), and say clearly that the app is offline. | Public | UI tests with network disabled |

## 3. Performance

Targets are measured at the [launch peak load](#11-sizing-assumptions). Device targets are on a mid-range reference device: iPhone 12, Pixel 6, a 4-core Windows laptop with 8 GB RAM, or an M1 Mac.

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-PRF-1 | API reads: p95 ≤ 300 ms, p99 ≤ 1 s, measured at the server. | Beta | APM metrics; load test |
| NFR-PRF-2 | API writes (diary entry, weigh-in, workout set): p95 ≤ 500 ms, p99 ≤ 1.5 s. | Beta | APM metrics; load test |
| NFR-PRF-3 | Food search returns first results in p95 ≤ 800 ms, including the USDA or commercial lookup or its cache. | Beta | Load test; synthetic check |
| NFR-PRF-4 | Barcode scan to a matched food on screen: p95 ≤ 2 s on a 4G connection. | Public | Device test |
| NFR-PRF-5 | Today screen is usable in ≤ 1 s from a warm app and ≤ 2.5 s after a cold start. | Beta | Device performance tests |
| NFR-PRF-6 | Device app cold start to first interactive screen: ≤ 3 s. | Beta | Device performance tests |
| NFR-PRF-7 | Adding a food or a workout set gives visible feedback in ≤ 100 ms (optimistic UI), whatever the network latency. | Beta | UI tests |
| NFR-PRF-8 | Blazor Web App meets Core Web Vitals "good" at p75 of real users: LCP ≤ 2.5 s, INP ≤ 200 ms, CLS ≤ 0.1. | Beta | Real-user monitoring; Lighthouse in CI |
| NFR-PRF-9 | AI photo or text meal estimate returns in p95 ≤ 10 s, with progress shown after 1 s and a way to cancel. | Public | Synthetic check; APM |
| NFR-PRF-10 | Offline changes sync within 30 s of the device regaining a connection. | Public | Device tests |
| NFR-PRF-11 | Coach client roster with 200 clients loads in ≤ 2 s; client lists and diaries are paginated. | Coach/Pro | Load test |
| NFR-PRF-12 | Data export ([NFR-DAT-1](#8-data-retention-export-and-deletion)) for a member with 3 years of data completes in ≤ 15 minutes. | Public | Load test with seeded data |

## 4. Scalability and capacity

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-SCL-1 | Before the public launch, a load test at **3× launch peak** (150 requests/s sustained for 30 minutes) meets every NFR-PRF target with an error rate below 0.1%. | Public | Load test report |
| NFR-SCL-2 | ASP.NET Core services are stateless and scale horizontally; no request depends on instance-local state. | Beta | Architecture review |
| NFR-SCL-3 | Every query on member data filters by an indexed member or grant key. No unbounded list endpoint exists; all lists are paginated with a maximum page size. | Beta | Query-plan review; API lint |
| NFR-SCL-4 | Capacity is reviewed when any resource (database CPU, connections, storage, egress, plan quota) passes 70% of its limit for 7 days. | Public | Monitoring alert |
| NFR-SCL-5 | Rate limits use a token bucket per member and per IP. General API: 300 requests/min per member and 600 requests/min per IP. Food search: 60 requests/min per member. Sign-in and password reset: 10 attempts/min per IP and 5 failures per account per 15 minutes. AI endpoints: 10 requests/min per member, plus the daily quota in NFR-CST-3. Bursts up to 2× the per-minute limit are allowed while the bucket has tokens. Over the limit, the API returns HTTP 429 with a `Retry-After` header, and the apps back off without losing entered data. | Beta | Integration tests per endpoint class |
| NFR-SCL-6 | Photos are resized and compressed on the device before upload (long edge ≤ 2,048 px). Uploads over 10 MB are rejected. | Public | Unit and integration tests |

## 5. Reliability and data integrity

These make the BRD's data contracts ([BRD §6.1](BRD.md#61-data-contracts)) and reliability rules testable.

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-REL-1 | A failed save never loses what the member entered. The form keeps its state and offers retry. | Beta | UI tests with injected failures |
| NFR-REL-2 | Workout logging works fully offline, and the queue survives app restarts and device reboots. Diary and weight entries are queued offline too. | Public | Device tests |
| NFR-REL-3 | Sync is idempotent: every record has a client-generated UUID, and replaying a sync produces **zero duplicates**. Conflicts resolve last-writer-wins per record, and the member is told when their change was overwritten. | Public | Automated sync tests, including replay and conflict cases |
| NFR-REL-4 | Log-entry snapshots are immutable: editing a food, recipe or price never changes a past entry's nutrients or cost. | Beta | Domain and database tests |
| NFR-REL-5 | Unknown values are never stored or shown as zero. Totals with unknowns are shown as a lower bound with an incomplete-item count. | Beta | Domain table tests |
| NFR-REL-6 | Money is stored as integer minor units plus an ISO 4217 code; no floating-point money anywhere in storage, APIs or the domain library. | Beta | Schema check; domain tests |
| NFR-REL-7 | Impossible unit conversions (mass↔volume without a density) are rejected with an error, never guessed. | Beta | Domain table tests |
| NFR-REL-8 | Crash-free sessions ≥ 99.5% on each device platform, measured over 7 days. | Public | Crash reporting |
| NFR-REL-9 | Payment and webhook handlers (RevenueCat, Stripe Connect) are idempotent and verify signatures; a replayed webhook changes nothing. | Coach/Pro | Integration tests |

## 6. Security

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-SEC-1 | The system meets **OWASP ASVS 5.0.0 Level 2**. | Public | ASVS checklist review before each phase launch |
| NFR-SEC-2 | Passwords follow NIST SP 800-63B-4: at least 15 characters when the password is the only factor and at least 8 when MFA is on, at least 64 characters allowed, no composition rules, checked against a breached-password list. | Beta | Auth tests |
| NFR-SEC-3 | MFA (TOTP or passkey) is available to all users, and **required** for clinicians and admins. Coaches are prompted to enable it. | Coach/Pro (admins: Public) | Auth tests; admin policy |
| NFR-SEC-4 | Sign-in endpoints are rate-limited and lock out progressively after repeated failures. Sessions use short-lived access tokens (≤ 1 hour) and rotating refresh tokens; signing out revokes them. | Beta | Auth tests |
| NFR-SEC-5 | Secrets (service-role keys, vendor API keys) exist only in server-side secret storage, never in client builds or the repository. They are rotated at least every 90 days and immediately after suspected exposure. | Beta | Secret scanning in CI; rotation log |
| NFR-SEC-6 | TLS 1.2 or later on every connection, with HSTS on the web. Data is encrypted at rest in the database, storage and backups. | Beta | TLS scan; provider configuration |
| NFR-SEC-7 | Progress photos and other member files live in private storage buckets, served only through short-lived signed URLs (≤ 15 minutes). | Public | Integration tests |
| NFR-SEC-8 | On-device data that can hold health information is stored in the platform's protected storage (Keychain, Keystore, DPAPI) or an encrypted local database. | Public | Security review |
| NFR-SEC-9 | CI runs dependency vulnerability scanning, static analysis (SAST) and secret scanning on every pull request. | Beta | CI gate |
| NFR-SEC-10 | Known vulnerabilities are fixed within: **critical 7 days, high 30 days, medium 90 days**. | Beta | Vulnerability tracker |
| NFR-SEC-11 | An independent penetration test is completed, and its critical and high findings fixed, before the Public launch and before the Coach/Pro launch, then yearly. | Public | Pen-test report |
| NFR-SEC-12 | Admin tooling is reachable only by authenticated admins with MFA, and every admin action is audited ([§9](#9-auditability)). | Public | Access tests; audit review |

## 7. Privacy and access control

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-PRV-1 | Every table that holds member data has row-level security enabled, and no private table is readable with the public (anon) key. | Beta | pgTAP test that lists tables without RLS and fails |
| NFR-PRV-2 | Every private table has pgTAP tests for each actor: owner, other member, active grantee within scope, grantee outside scope, revoked grantee, and admin. | Beta (owner/other), Coach/Pro (grants) | pgTAP suite in CI |
| NFR-PRV-3 | Professionals reach member data only through an active grant and its scopes, with exactly the access in the [permission matrix](BRD.md#32-permission-matrix). | Coach/Pro | pgTAP suite; API tests |
| NFR-PRV-4 | Revoking a grant takes effect on the professional's **next request**, and any copy cached on the professional's devices is cleared at their next sync, within 1 minute while online. | Coach/Pro | Integration tests |
| NFR-PRV-5 | Admins and support staff have no path to diary, measurement, photo, wellness or message content, including through support tools, logs and backups in normal operation. | Public | pgTAP; access review |
| NFR-PRV-6 | No health data (foods, nutrients, weights, measurements, wellness values, photos, messages, conditions) appears in product-analytics events, application logs, crash reports, error messages or push notification payloads. | Beta | Log/event schema allow-list; automated redaction tests |
| NFR-PRV-7 | Consent for collecting health data, for sharing it, and for sending photos to the AI provider is recorded separately, with time, version of the notice and channel, and can be withdrawn in the app. | Public | Consent-record tests |
| NFR-PRV-8 | No location data is collected, so no geofencing is possible (Washington MHMDA). | Public | Permission manifest review |
| NFR-PRV-9 | Health-platform data (HealthKit, Health Connect) is used only for the member's own features and never for advertising or sold, as the platform rules require. | Public | Data-flow review |
| NFR-PRV-10 | Every vendor that receives member data is listed in a data inventory with purpose, data categories, region and contract status (DPA; BAA in the Clinic phase). | Public | Inventory review before each phase launch |

## 8. Data retention, export and deletion

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-DAT-1 | Self-service export of all the member's data is available within **24 hours** of the request, as a ZIP archive with one UTF-8 CSV file per data area, the original photo files in a `photos/` folder referenced by file name from the CSVs, and a `manifest.json` listing every file with its record count. | Public | Integration test |
| NFR-DAT-2 | Self-service account deletion removes the member's data from primary stores within **30 days** and from backups within **35 days**, inside the CCPA/CPRA 45-day response window. *Review.* | Public | Deletion test; backup expiry check |
| NFR-DAT-3 | Content the member's professionals authored (comments, plans) is deleted with the member's account; the professional keeps only their own roster record showing the relationship ended. *Review.* | Coach/Pro | Deletion test |
| NFR-DAT-4 | The audit log ([§9](#9-auditability)) is kept for **6 years**. After account deletion its entries are pseudonymized: no names, emails or health values remain. *Review.* | Public | Retention job test |
| NFR-DAT-5 | Application logs are kept for 30 days; error and crash reports for 90 days. | Beta | Provider configuration |
| NFR-DAT-6 | Support can run export or deletion on a verified request without seeing diary content. | Public | Support-tool review |
| NFR-DAT-7 | A security-incident process can determine within 72 hours whether a breach of health data occurred, so that FTC Health Breach Notification Rule notices go out within its 60-day limit. | Public | Incident runbook; yearly tabletop exercise |

## 9. Auditability

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-AUD-1 | An append-only audit log records: granting, changing and revoking access; every professional write to a member's data; target guardrail overrides and acceptances; admin catalog changes; support-run exports and deletions; admin sign-ins. | Public (grants: Coach/Pro) | Audit tests per event type |
| NFR-AUD-2 | Each entry has actor, actor role, member affected, action, time (UTC), and before and after values where a value changed. | Public | Audit tests |
| NFR-AUD-3 | No user, including admins, can edit or delete audit entries through the app or API. | Public | pgTAP |
| NFR-AUD-4 | Members can view the audit entries about their own data in the app. | Coach/Pro | UI tests |

## 10. Observability and operations

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-OPS-1 | Services emit structured logs with a correlation ID per request, passing through the redaction rules in NFR-PRV-6. | Beta | Log review |
| NFR-OPS-2 | Errors and crashes from all clients and services are reported to an error-tracking service, with health data scrubbed. | Beta | Configuration review |
| NFR-OPS-3 | External uptime checks run every minute against API, Auth and web. Alerts fire on downtime and on fast burn of the monthly availability budget. | Public | Monitoring configuration |
| NFR-OPS-4 | An on-call owner responds to critical alerts within 1 hour during US business hours and within 4 hours outside them. | Public | Alert response log |
| NFR-OPS-5 | A public status page reports incidents and planned maintenance. | Public | Status page live |
| NFR-OPS-6 | Every production deployment is automated, repeatable, and can be rolled back within 30 minutes. Database migrations are forward-only and tested in CI. | Beta | Deployment runbook; rollback drill |

## 11. Accessibility and usability

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-ACC-1 | The Blazor Web App conforms to **WCAG 2.2 AA**. | Beta | Automated axe checks in CI; manual audit before each phase launch |
| NFR-ACC-2 | Device apps work with VoiceOver, TalkBack, Narrator and keyboard navigation on Windows and macOS. Every control has an accessible name. | Beta | Manual screen-reader test per release |
| NFR-ACC-3 | Device apps support system text size up to 200% without clipped or overlapping text. | Beta | UI tests at maximum text size |
| NFR-ACC-4 | Touch targets are at least 44×44 pt; text contrast is at least 4.5:1; color is never the only way information is shown (for example, incomplete or unpriced items). | Beta | Design review; automated contrast checks |
| NFR-ACC-5 | Hide-numbers mode removes every calorie and weight figure on every surface, including notifications, widgets, emails, exports shown in the app, and coach messages shown to the member. | Public | UI tests across all surfaces |
| NFR-ACC-6 | Motion respects the system "reduce motion" setting. | Beta | UI tests |

## 12. Compatibility and platforms

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-CMP-1 | Supported device OS versions: **iOS and iPadOS 17+, Android 10 (API 29)+, Windows 10 22H2 and Windows 11, macOS 13+**. Re-checked against the .NET MAUI version in use at each major release. | Beta | Device test matrix |
| NFR-CMP-2 | Supported browsers: the latest two major versions of Chrome, Edge, Firefox and Safari. | Beta | Browser test matrix |
| NFR-CMP-3 | Every feature ships on both UIs (MAUI XAML and Blazor) in the same release, or the gap is recorded in the release notes with a date. | Beta | Feature-parity checklist per release |
| NFR-CMP-4 | Phone layouts work from 360 pt wide; tablet and desktop layouts use the extra space. | Beta | UI tests at reference sizes |
| NFR-CMP-5 | An older app version keeps working for at least 90 days after a newer one ships, or is told to update with a clear in-app message. APIs are versioned. | Public | API contract tests |

## 13. Maintainability and quality

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-MNT-1 | The shared C# domain library has at least **90% line coverage**, and every rule in [BRD §6](BRD.md#6-business-rules) has table tests. | Beta | Coverage report in CI |
| NFR-MNT-2 | Every database migration ships with pgTAP tests, and the pgTAP suite runs in CI. | Beta | CI gate |
| NFR-MNT-3 | CI must pass (build, unit tests, pgTAP, lint, security scans) before merging to `main`. | Beta | Branch protection |
| NFR-MNT-4 | View models and service clients are shared between the MAUI and Blazor UIs; business logic lives only in the shared library or the server, never in UI code. | Beta | Code review |
| NFR-MNT-5 | Architecture decisions are recorded as decision records in the repository. | Beta | Review |

## 14. Localization

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-L10N-1 | Launch language is US English, with USD and US customary display units by default. | Beta | Review |
| NFR-L10N-2 | All user-visible strings are externalized in resource files, so another language can be added without code changes. | Beta | Lint for hard-coded strings |
| NFR-L10N-3 | Units, currency and timezone come from the member's profile; numbers, dates and currency are formatted for the member's locale. Diary dates are the member's local calendar date. | Beta | Domain tests |

## 15. Cost

| ID | Requirement | Phase | Verification |
|---|---|---|---|
| NFR-CST-1 | Total monthly infrastructure cost at the [launch sizing](#11-sizing-assumptions) is **at most $500**. It covers Supabase, ASP.NET Core hosting, email, push, monitoring, error tracking and AI calls. Store fees, payment fees and the pen test are excluded. | Public | Monthly cost report |
| NFR-CST-2 | Billing alerts fire at 80% and 100% of the monthly budget, per vendor and in total. | Beta | Billing alert configuration |
| NFR-CST-3 | AI calls are limited to **20 requests per member per day** (reset at the member's local midnight) and a **global cap of $150 per calendar month (UTC)**, part of the NFR-CST-1 budget. When either limit is reached, AI logging pauses with a clear message and manual logging keeps working. | Public | Integration tests |
| NFR-CST-4 | Cost per monthly active member is reported monthly, so the budget can be re-set before the Coach/Pro launch. | Public | Monthly cost report |

## 16. Compliance mapping

How the obligations in [BRD §7](BRD.md#7-compliance-and-constraints) map to these NFRs.

| Obligation | NFRs |
|---|---|
| FTC Health Breach Notification Rule | NFR-DAT-7, NFR-PRV-6, NFR-PRV-10, NFR-SEC-11 |
| Washington My Health My Data Act | NFR-PRV-7, NFR-PRV-8, NFR-DAT-1, NFR-DAT-2 |
| CCPA/CPRA (sensitive personal information) | NFR-PRV-7, NFR-DAT-1, NFR-DAT-2, NFR-DAT-6 |
| Apple App Review (HealthKit), Google Play (Health Connect) | NFR-PRV-9, NFR-SEC-8, NFR-ACC-2 |
| AI vendor (DPA, no training, consent before photos) | NFR-PRV-7, NFR-PRV-10, NFR-CST-3 |
| Grants and professional access ([BRD §3](BRD.md#3-permissions)) | NFR-PRV-2, NFR-PRV-3, NFR-PRV-4, NFR-AUD-1 to NFR-AUD-4 |
| HIPAA (Clinic phase only) | NFR-PRV-10 (BAAs with every PHI vendor), NFR-DAT-4 (6-year audit retention), NFR-SEC-3 (clinician MFA), NFR-AUD-1. Before the Clinic phase, add a HIPAA Security Rule risk assessment, HIPAA-eligible hosting and access reviews to this document. |

## 17. Open items

1. **Legal review** of the deletion and backup windows (NFR-DAT-2), deletion of professional-authored content (NFR-DAT-3) and audit retention (NFR-DAT-4).
2. **Budget re-check** of NFR-CST-1 once the commercial food database and AI vendors are chosen ([BRD §10](BRD.md#10-open-questions-and-decisions-needed)); either can exceed the budget on its own.
3. **Availability for paying customers.** Whether the Coach/Pro or Clinic phases need a contractual SLA above 99.5%, which would need redundancy and a higher Supabase plan.
