# FitCore

FitCore is a health, fitness and nutrition app. You set targets and track nutrition (including estimated food cost), training, body metrics and wellness habits in one place. A coach or dietitian can support you if you choose.

It starts as a precise personal tracker you can trust. Over time it grows into a platform where members work with their own coach or clinician without giving up privacy or safety.

The full business requirements are in [`docs/BRD.md`](docs/BRD.md), and the measurable non-functional requirements (availability, performance, security, privacy, cost) are in [`docs/NFR.md`](docs/NFR.md). This README summarizes them. Architecture options are compared in [`docs/architecture-options.md`](docs/architecture-options.md), and the accepted choice, a modular monolith on ASP.NET Core with Supabase, is recorded in [ADR 0003](docs/decisions/0003-modular-monolith.md).

## Goals the product serves

| Goal | In the product |
|---|---|
| Body composition | Calorie and macro targets, food diary, weight trend, training |
| General wellness | Sleep, hydration, steps, mood and habit tracking |
| Performance / sport | Training load, periodized programs, fueling |
| Clinician-guided nutrition | Plans entered by a dietitian or clinician and tracked by the member. FitCore never generates them. |

## Who it's for

| Role | Description |
|---|---|
| **Member** | Tracks their own goals, food, training, weight and wellness |
| **Coach / trainer** | Works with clients who invited them: reviews logs, sets targets and plans, communicates |
| **Dietitian / clinician** | Like a coach, and can also enter condition-specific plans and targets below the hard safety floor |
| **Admin / support** | Runs the service and curates the shared food and exercise catalog. Never browses member diaries. |

## Privacy and sharing

- **Private by default.** Only the member sees their data.
- **Opt-in grants.** A member invites a coach or clinician and picks scopes (nutrition, training, body metrics, photos, wellness). Professionals can read granted data. They can write only targets, assigned plans, comments, messages and check-in responses. The member's own logs stay read-only to them.
- **Revocable at any time**, effective immediately.
- **Audited.** Grant changes and every professional edit are logged, and the member can see the log.

## Roadmap

| Phase | Audience | Contents |
|---|---|---|
| **Private beta (R1)** | Product owner and invited testers | Auth, profile, targets, custom and USDA foods, diary with estimated food cost, weight, Today screen |
| **Public member app** | Members (US, 18+) | Recipes, saved meals, meal planner, grocery list, workout logging, barcode scanning, progress tracking, wellness metrics, photo/AI logging, health-platform sync, safety features, data export/import |
| **Coach/Pro tier** | Independent coaches and dietitians | Coach workspace, grants, plan assignment, comments, messaging, check-ins, coach billing |
| **Clinic phase** | Clinics and insurers | HIPAA program: BAAs, HIPAA-eligible hosting, clinician credential verification |

The [implementation plan](docs/implementation-plan.md) breaks the private beta into PRs and lists what each later phase needs.

## Core rules

- **Unknown is not zero.** Missing nutrient values stay unknown, and totals are shown as a lower bound with a count of incomplete items. Days with no diary record are unknown, not zero intake.
- **Money** is stored as integer minor units plus an ISO 4217 currency code. Early releases do no currency conversion.
- **"Estimated food cost"** means consumption cost and is always labelled as an estimate, never as "spend". Actual spend comes only from purchase records.
- **Units:** g and ml are canonical, and household measures are servings. Mass↔volume conversion needs a known density. An impossible conversion is rejected, never guessed.
- **Snapshots:** a log entry keeps the nutrients and cost from the time it was logged, so editing a food or price never rewrites history.
- **Dates** are the member's local calendar date.

## Targets and safety

- Targets are estimated with Mifflin-St Jeor × an activity factor. The member picks a weekly rate or a goal date, and FitCore derives the other.
- **Soft limits:** weight loss of at most 1% of body weight per week, at least 1,200 kcal/day, and protein at 1.6 g/kg by default. The member can override them after an explicit warning, and the override is logged.
- **Hard floor:** extreme values, such as under 800 kcal/day or a goal weight below BMI 18.5, are blocked for members and coaches. Only a clinician can go past it, and the member is told.
- If a coach sets a target below a soft limit, it stays pending until the member accepts it.
- Eating-disorder safeguards: optional screening, a hide-numbers mode and support resources.
- 18+ only. AI food estimates must be confirmed by the member. FitCore never generates medical advice.
- Adaptive targets are proposed only when there is enough good data, and never applied without the member accepting them.

## Platform

.NET on every platform is accepted. The device apps use native .NET MAUI XAML for the most native feel, and the web uses Blazor.

| Area | Choice |
|---|---|
| Clients | iOS, Android, Windows, macOS and web, all on .NET: native .NET MAUI XAML apps on devices and a Blazor Web App on the web, sharing view models and the domain library |
| Domain logic | A shared C# library for units, nutrition totals, estimated cost, targets and guardrails |
| Backend | Supabase (Postgres, Auth, Storage, row-level security) plus ASP.NET Core services |
| Sign-in | Email/password, Google, Microsoft and Sign in with Apple |
| Distribution | Apple App Store, Google Play, Microsoft Store and the web |

## Quality targets

FitCore is sized for about 10,000 members and 2,000 daily active members in its first public year, with room to grow 10× without a redesign. The full targets, each with an ID and a way to verify it, are in [`docs/NFR.md`](docs/NFR.md).

| Area | Target |
|---|---|
| Availability | 99.5% per month; at most 1 hour of data lost in a disaster, service restored within 8 hours |
| Speed | Server responses p95 ≤ 300 ms (reads) and ≤ 500 ms (writes); food search ≤ 800 ms; Today screen ≤ 1 s warm, ≤ 2.5 s cold |
| Offline | Workout, diary and weight logging work offline and sync without duplicates |
| Security | OWASP ASVS 5.0 Level 2; MFA required for clinicians and admins; an independent penetration test before the Public and Coach/Pro launches, with critical and high findings fixed first, then yearly |
| Privacy | Row-level security tested for every actor; revoking a coach's or clinician's access ends server access on their next request, and data cached on their devices stays readable offline for at most 24 hours; no health data in logs, analytics or push notifications |
| Data rights | Export within 24 hours; account deletion within 30 days (35 days for backups) |
| Accessibility | WCAG 2.2 AA on the web; screen readers and text up to 200% on devices |
| Cost | At most $500/month of infrastructure at launch, with an AI spending cap |

## Integrations

USDA FoodData Central (beta), with Open Food Facts and a commercial food database planned later. Also planned: an AI vision provider for meal logging, Apple Health and Health Connect, Garmin, Fitbit and Oura, RevenueCat for subscriptions, Stripe Connect for coach billing, push notifications, email, calendar sync, CSV export, and import from MyFitnessPal and similar apps.

Data from USDA FoodData Central is shown with the attribution "Data: USDA FoodData Central".

## Compliance

The US market comes first. FitCore follows consumer health privacy law (FTC Health Breach Notification Rule, Washington My Health My Data Act, CCPA/CPRA) and is positioned as a general wellness product. HIPAA applies only once the Clinic phase serves covered entities.

## Open questions

Pricing and tiers, legal and clinical review of the safety values and the product's positioning, food-database and AI vendors, success-metric targets, clinician credential verification, and legal review of the data-deletion and audit-retention periods and of deleting a coach's comments and plans with the member's account, in [`docs/NFR.md`](docs/NFR.md#17-open-items). See [`docs/BRD.md` §10](docs/BRD.md#10-open-questions-and-decisions-needed).
