# Business Requirements Document: Health, Fitness & Nutrition App

- **Status:** Draft v1, agreed with the product owner in a section-by-section interview
- **Date:** 2026-10-12
- **Related:** [`docs/PLAN.md`](PLAN.md) (technical plan for R1), [`docs/decisions/0002-dotnet-platform.md`](decisions/0002-dotnet-platform.md) (platform decision)

This document gives the business view: who uses the product, what it does, and which rules and integrations it must respect. Where it changes the current technical plan, the change is listed in [§11](#11-impact-on-docsplanmd-follow-ups) instead of being applied silently.

---

## 1. Purpose, vision and success metrics

**Purpose.** Help people reach their health and fitness goals by planning targets and tracking nutrition (including estimated food cost), training, body metrics and wellness habits in one app, with optional support from a coach or dietitian.

**Vision.** Start with a precise, trustworthy personal tracker. Grow it into a platform where members work with their own coach or clinician, without giving up privacy or safety.

**Primary goals the product serves** (all four are in scope):

| Goal | What it means in the product |
|---|---|
| Body composition | Calorie and macro targets, food diary, weight trend, training |
| General wellness | Sleep, hydration, steps, mood and habit tracking |
| Performance / sport | Training load, periodized programs, fueling |
| Clinician-guided nutrition | Plans entered by a dietitian or clinician and tracked by the member. The app does not generate them (see [§6](#6-business-rules)). |

**Success metrics.** Targets are TBD and get set before the public member app launches.

| Metric | Definition | Target |
|---|---|---|
| Activation | New members who log food on 3 of their first 7 days | TBD |
| Retention | Members active in week 4 and week 12 | TBD |
| Logging completeness | Share of logged days marked complete | TBD |
| Goal progress | Members moving toward their goal at the planned rate after 8 weeks | TBD |
| Coach adoption | Active coaches; clients per coach; check-in completion rate | TBD |
| Safety | Rate of guardrail overrides; ED-screening flags acted on | Monitored, no target |

## 2. Stakeholders and user roles

| Role | Who | Phase |
|---|---|---|
| **Member** | A person tracking their own goals, food, training, weight and wellness | Private beta onward |
| **Coach / trainer** | Manages a roster of clients who invited them; reviews logs; sets targets and plans; communicates | Coach/Pro tier |
| **Dietitian / clinician** | Like a coach, plus the right to enter condition-specific plans and targets below the hard floor. Independent professionals first, clinics later. | Coach/Pro tier (independents), Clinic phase (clinics) |
| **Admin / support (internal)** | Operates the service. Never browses member diaries. | From the public launch |
| **Product owner** | Sets scope, pricing and policy | Ongoing |

**Paid tiers are undecided** ([§10](#10-open-questions-and-decisions-needed)). Features and permissions must be designed so a tier can gate them later without a redesign.

## 3. Permissions

### 3.1 Sharing model

- **Private by default.** No one but the member sees their data.
- **Opt-in grants.** A member can invite a coach or clinician, or accept an invitation from one. A grant covers specific **scopes** (for example nutrition, training, body metrics, photos, wellness). Within a granted scope, the professional gets exactly the access the [permission matrix](#32-permission-matrix) lists for their role and nothing more. Professionals can always read granted data. They can write only targets and assigned plans, plus comments, messages and check-in responses. A member's own logs (diary, weight, measurements, photos, wellness) stay read-only to professionals.
- **Revocable at any time.** Revoking access stops it immediately. Content the professional authored, such as comments and plans, stays in the member's history.
- **Audited.** Granting, changing and revoking access is logged. So is every edit a professional makes to a member's data. The member can see that log.

### 3.2 Permission matrix

R = read, W = write, — = no access. "Granted" means within an active grant and its scopes.

| Data area | Member (own) | Coach (granted) | Clinician (granted) | Admin / support |
|---|---|---|---|---|
| Profile & account | R/W | R (name, goals) | R (name, goals) | Account metadata only (email, status), for support |
| Targets | R/W | R/W, below safety floor needs member acceptance | R/W, may go below hard floor; member is told | — |
| Food diary & estimated cost | R/W | R, comment | R, comment | — |
| Meal plans & recipes | R/W | R/W (assign) | R/W (assign, incl. condition-specific) | — |
| Workouts & programs | R/W | R/W (assign) | R | — |
| Weight & measurements | R/W | R | R | — |
| Progress photos | R/W | R, only with an explicit photo scope | R, only with an explicit photo scope | — |
| Wellness (sleep, steps, mood) | R/W | R | R | — |
| Messages & check-ins | R/W (own threads) | R/W (own threads) | R/W (own threads) | — |
| Shared food & exercise catalog | R, plus own custom items | R, plus own custom items | R, plus own custom items | **R/W** (curate, merge, remove) |
| Usage analytics | — | Own client roster summary | Own client roster summary | **Aggregate, anonymized only** |
| Data export / deletion | Self-service | — | — | Can run on the member's verified request |

## 4. Features and workflows

### 4.1 Release phases

| Phase | Audience | Contents |
|---|---|---|
| **Private beta (R1)** | Product owner and invited testers | Auth, profile, targets, custom + USDA foods, diary with estimated food cost, weight, Today screen |
| **Public member app** | Members (US, 18+) | Recipes, saved meals, meal planner, grocery list; workout logging; barcode; progress tracking; wellness metrics; photo/AI logging; health-platform sync |
| **Coach/Pro tier** | Independent coaches and dietitians | Coach workspace, grants, plan assignment, comments, messaging, check-ins, coach billing (Stripe Connect) |
| **Clinic phase** | Clinics and insurers | HIPAA program: BAAs, HIPAA-eligible hosting, clinician credential verification |

### 4.2 Feature list

| Area | Features | Phase |
|---|---|---|
| Account | Email/password, Google, Microsoft, Apple sign-in; profile (age, sex, height, activity, timezone, units, currency) | Beta |
| Targets | Estimated calorie/macro targets; manual override; guardrails and warnings ([§6.2](#62-targets-and-safety)) | Beta |
| Food logging | Search (USDA), custom foods with prices, servings, g/ml/servings; estimated food cost | Beta |
| | Barcode scanning; commercial food database; Open Food Facts | Public |
| | Photo / text AI logging (estimates must be confirmed by the member) | Public |
| | Logging from a coach-prescribed meal plan (check off meals) | Coach/Pro |
| Recipes & meals | Recipes with cooked yield; saved meals; recents/favorites; copy day/meal | Public |
| Planning & groceries | Weekly meal planner; grocery list with package rounding and pantry; purchases → actual spend vs. estimated cost; budget | Public |
| Training | Exercise library (shared + custom); templates; strength and cardio logging; PRs; rest timer; offline logging | Public |
| | Periodized programs; training calendar (planned vs. done); training load | Public |
| Progress | Weight trend; body measurements; private progress photos; goal projections; adaptive targets (data-quality gated) | Public |
| Wellness | Sleep, hydration, steps, mood, habits (manual and synced) | Public |
| Coaching | Client roster; invites and grants; plan and target assignment; comments on days and entries; in-app messaging; scheduled check-ins (weight, photos, adherence, notes) | Coach/Pro |
| Safety | ED screening, hide-numbers mode, support resources | Public |
| Data rights | Export (CSV), import (MyFitnessPal and similar), account deletion | Public |

### 4.3 Key user journeys

1. **Onboarding (member).** Sign up, confirm age 18+, set up profile, take the optional ED screening, then accept or override the estimated targets. If a goal is aggressive, the member sees the implied rate and a warning, then picks a later date, the capped rate, or an override.
2. **Daily logging (member).** Open Today, then add food by search, barcode, photo/AI (confirm the estimate), a saved meal or a planned meal. Today shows calories, macros and estimated food cost, with incomplete or unpriced items flagged. Then log a workout, a weigh-in and wellness items, and mark the day complete.
3. **Coaching loop.** The coach invites the member, and the member accepts and chooses scopes. The coach sets targets and assigns a meal or workout plan; a target below a safety floor stays pending until the member accepts it. The member logs, and the coach reviews and comments. A weekly check-in form goes out, the member submits it and the coach responds. The member can revoke access at any time.
4. **Data rights.** The member exports all their data or deletes their account themselves. Support handles verified requests without viewing diary content.

## 5. Platform and technology constraints

The product owner prefers **.NET on every platform**. The full rationale and trade-offs are in [decision 0002](decisions/0002-dotnet-platform.md) (status: Proposed).

| Area | Requirement |
|---|---|
| Client platforms | iOS, Android, Windows, macOS and web, all .NET: **.NET MAUI Blazor Hybrid** apps and a **Blazor Web App**, sharing one Razor component library. Native XAML pages only where they clearly pay off: the active workout / rest timer and the camera / barcode scanner. |
| Domain logic | One shared C# library: units, nutrition totals, estimated cost, targets and guardrails. It ports the rules and table tests from `packages/core`. |
| Backend | **Supabase** for Postgres, Auth, Storage and row-level security, plus **ASP.NET Core** services for server logic: food search/import, AI calls, payment webhooks, notifications. |
| Sign-in | Email/password, Google, Microsoft and **Sign in with Apple**. Apple App Review Guideline 4.8 requires an iOS app that offers third-party or social login to also offer an equivalent login option with defined privacy protections. Sign in with Apple is the option chosen to meet it. |
| Stores | Apple App Store, Google Play, Microsoft Store (MSIX), and the web |
| Existing code | The TypeScript/Expo/Electron R1 code becomes a reference implementation. The database schema, RLS policies, pgTAP tests and data contracts carry over. |

## 6. Business rules

### 6.1 Data contracts (carried over from PLAN.md)

- **Unknown is not zero.** Missing nutrient values stay unknown, and totals show as a lower bound with a count of incomplete items.
- **Days without a diary record are unknown**, never zero intake. Only days marked complete feed adaptive targets.
- **Money** is integer minor units plus an ISO 4217 code. No currency conversion in early releases.
- **"Estimated food cost"** is consumption cost and is always labelled as an estimate. It is never called "spend". Actual spend comes only from purchase records.
- **Units:** g and ml are canonical, with household measures as servings. Mass↔volume conversion only with a known density. Impossible conversions are rejected, never guessed.
- **Snapshots:** a log entry keeps the nutrients and cost at log time. Editing a food or price never rewrites history.
- **Dates:** diary dates are the member's local calendar date.
- **Shared catalog:** only the service writes shared food and exercise rows, and admins curate them. Members, coaches and clinicians write only their own custom items.

### 6.2 Targets and safety

| Rule | Behaviour |
|---|---|
| Estimate | Mifflin-St Jeor × activity factor. The member picks a weekly rate or a goal date, and the other is derived. |
| **Soft limits** | Weight loss ≤ 1% of body weight per week; calories ≥ 1,200 kcal/day (configurable); protein 1.6 g/kg by default. The member may override after an explicit warning, and the override is logged. |
| **Hard floor** | Extreme values are blocked for members and coaches. Initial values: under 800 kcal/day, or a goal weight with BMI under 18.5. Final values need clinical review. Only a clinician can set a target past the hard floor, and the member is told. |
| Coach targets | A coach target below a soft limit stays **pending** until the member reads the warning and accepts it. Coaches can't go past the hard floor. |
| Precedence | The newest accepted target wins. The member can always replace it with a manual target, unless a clinician set it below the hard floor; ending that requires the member to revoke the grant or the clinician to change it. *This is an initial position for clinical/legal review.* |
| ED safeguards | Optional screening at onboarding and on request. Hide-numbers mode (no calorie or weight figures). Support resources (for example the NEDA helpline). If a member is flagged, overrides past the soft limits are disabled. |
| Age | 18+ only. |
| AI estimates | AI-derived food items are labelled as estimates and saved only after the member confirms or edits them. |
| No generated medical advice | The app never generates condition-specific recommendations. Condition plans come only from a clinician. |
| Adaptive targets | Proposed only with enough data (at least 21 days with at least 80% complete days and at least 8 weigh-ins). Shown with an uncertainty range and the reasoning. Never applied without the member accepting. |

## 7. Compliance and constraints

| Topic | Requirement |
|---|---|
| Market | United States first: USD, US customary display units, English |
| Consumer health privacy | FTC Health Breach Notification Rule; Washington My Health My Data Act (separate consent for collecting and sharing health data, no geofencing near health facilities); CCPA/CPRA (sensitive personal information); other state consumer-health laws as they apply |
| HIPAA | Not applicable while the app serves members and independent professionals directly. **Triggered** when the app acts for a covered entity (clinics, insurers). The Clinic phase requires BAAs with every vendor that handles PHI and HIPAA-eligible hosting. |
| FDA | Positioned as a **general wellness** product. Clinician-entered plans and adherence tracking are in scope. The app does not generate diagnosis or treatment recommendations. Legal review is required before the public launch. |
| Age | 18+ (no COPPA scope) |
| App stores | Apple App Review (Guideline 4.8 privacy-preserving login option, met with Sign in with Apple; HealthKit data-use rules, in-app purchase for digital subscriptions), Google Play (Health Connect permissions declaration, Play Billing), Microsoft Store |
| Data licensing | USDA FDC is public domain (attribution shown). Open Food Facts is ODbL plus CC-BY-SA images, and needs a licensing review before use. A commercial food database needs a license review (caching, display, attribution). |
| AI vendor | A data processing agreement, no training on customer data, retention limits, and member consent before photos are sent |

## 8. Integration requirements

| System | Purpose | Phase | Data direction | Notes |
|---|---|---|---|---|
| USDA FoodData Central | Generic and branded foods, GTINs | Beta | In | Public domain; attribution shown |
| Open Food Facts | Barcode coverage | Public | In | Gated on ODbL review |
| Commercial food database (Nutritionix / FatSecret class) | US barcodes, restaurant items | Public | In | Vendor TBD; license terms |
| AI vision / LLM provider | Photo and text meal logging | Public | Out (photo/text), In (estimate) | Consent, DPA, no training on data |
| Apple Health (HealthKit) | Weight, steps, workouts, sleep, heart rate, energy | Public | In/Out | iOS native binding |
| Health Connect | Same on Android | Public | In/Out | Play permissions declaration |
| Garmin, Fitbit, Oura | Training load, sleep, HRV | Public | In | Partner programs and API approval for each vendor |
| Smart scales | Weight, body fat | Public | In | Via Apple Health / Health Connect first |
| Later candidates | Strava, Whoop, Polar, Withings direct; CGMs only under clinician-supervised positioning | TBD | In | Evaluate per demand |
| RevenueCat | Member subscriptions across App Store, Play and web | When tiers are set | Both | Has App Store and Play billing connectors |
| Stripe Connect | Coaches billing their clients | Coach/Pro | Both | Platform-fee model TBD |
| Push notifications | Reminders, comments, check-ins | Public | Out | APNs / FCM |
| Transactional email | Auth emails, check-in reminders | Beta (auth), Public | Out | Provider TBD |
| Calendar (Google, Apple, Microsoft) | Check-ins, workouts, meal plans | Public | Out | |
| Export / import | CSV export; MyFitnessPal and similar imports | Public | Both | Export also serves privacy requests |
| Product analytics | Usage metrics | Public | Out | Privacy-safe (for example PostHog); **no health data in events** |

## 9. Non-functional requirements

| Category | Requirement |
|---|---|
| Privacy & access control | Every private table has row-level security. Cross-user access is tested for every table (pgTAP). Professional access only goes through active grants. Admins have no diary access. |
| Security | Secrets live only server-side. TLS everywhere; encryption at rest. MFA available for coaches, clinicians and admins. Audit log for grants and professional edits. |
| Reliability | A failed save never loses form state. Workout logging works offline and syncs later without duplicates. |
| Accessibility | WCAG 2.2 AA on web; VoiceOver and TalkBack support on mobile |
| Platforms | iOS, Android, Windows, macOS, web ([§5](#5-platform-and-technology-constraints)) |
| Data retention | Members can export and delete their data. Deletion completes within the period the applicable privacy law sets. |
| Measurable targets | Availability, performance, scalability and cost targets: **to be defined** (next step) |

## 10. Open questions and decisions needed

1. **Pricing and tiers:** free, freemium or paid; which features are premium; coach pricing and platform fee.
2. **Legal review** of the general-wellness positioning, the clinician features, the hard-floor values and the state privacy-law obligations.
3. **Clinical review** of the soft-limit and hard-floor values and of the ED screening instrument.
4. **Commercial food database** vendor and **AI vision** vendor.
5. **Success-metric targets** ([§1](#1-purpose-vision-and-success-metrics)).
6. **Clinician credential verification** method (for example NPI lookup, license checks).
7. **Approval of decision 0002** (.NET rewrite) and its timing relative to finishing R1.

## 11. Impact on `docs/PLAN.md` (follow-ups)

These changes are **not** made in this document's PR. Each becomes its own follow-up.

| Area | Current plan | Change required |
|---|---|---|
| Platform | Expo + Electron; TypeScript `packages/core`; Deno Edge Function | .NET MAUI Blazor Hybrid + Blazor Web; C# domain library; ASP.NET Core services ([decision 0002](decisions/0002-dotnet-platform.md)). Update PLAN.md, AGENTS.md and CLAUDE.md. |
| Authorization | Owner-only RLS | Add a grant model (for example `care_relationships` with scopes, status and audit) and grant-aware policies, with pgTAP tests for grantee and non-grantee access |
| Targets | Cap at 1%/week; manual targets always win | Soft limits with logged override, a clinician-only hard floor, `targets.set_by` (member, coach, clinician) and an acceptance state |
| Roles | Members only | Coach, clinician and admin roles; admin catalog curation |
| Roadmap | R2–R6 | Add the coach workspace, messaging, check-ins, wellness metrics, AI logging, health-platform sync, ED safeguards and payments |
| Auth | Email/password | Add Google, Microsoft and Apple sign-in |
