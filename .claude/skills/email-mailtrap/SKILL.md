---
name: email-mailtrap
description: How HTN sends transactional email through the Mailtrap Email API from .NET — IEmailSender, outbox + background worker, sandbox vs live, templates (billing, ticket with QR, invite, password reset, speaker link + OTP, plus-one voucher, invitation letters, request decisions), branding, retries and testing. Use for any feature that sends email.
---

# Email via Mailtrap

## Configuration (environment variables only)
| Var | Dev | Prod |
|---|---|---|
| `MAILTRAP_API_TOKEN` | sandbox token | sending token |
| `MAILTRAP_MODE` | `sandbox` | `live` |
| `MAILTRAP_INBOX_ID` | sandbox inbox id | — |
| `MAIL_FROM` | `HackTheNorth.PH <no-reply@hackthenorth.ph>` | same |

Endpoints:
- live: `POST https://send.api.mailtrap.io/api/send`
- sandbox: `POST https://sandbox.api.mailtrap.io/api/send/{inboxId}`

Header `Authorization: Bearer <token>`, JSON body: `from`, `to[]`, `subject`, `html`, `text`, `category`, optional `attachments[]` (base64), `custom_variables`.

## Architecture
- `IEmailSender.SendAsync(EmailMessage msg, CancellationToken)` → `MailtrapEmailSender`, a typed `HttpClient` with a Polly retry (3×, exponential backoff, retry only on 429/5xx).
- **Never call the sender inside a request.** Endpoints add an `OutboxMessage { Type = Email, Template, ModelJson }` in the same transaction.
  - `OutboxWorker` (a `BackgroundService`) picks up pending rows, renders, sends, and marks them sent or failed with the attempt count.
  - After 5 failures the row is flagged for admin review.
- **Rendering:** Razor component templates (`Htn.Infrastructure/Email/Templates/*.razor`) via `HtmlRenderer`. Each message has a plain-text alternative.
- **Branding:** logo URL, theme color, payment accounts and deadline come from event or org settings. The layout is fixed; admins can't edit HTML.
- **Categories** (for Mailtrap analytics): `billing`, `ticket`, `invite`, `password-reset`, `speaker-link`, `speaker-otp`, `voucher`, `invitation-letter`, `finance`.

## Templates and required content
| Template | Trigger | Must include |
|---|---|---|
| Billing | registration created | amount, participant ID as payment reference, GCash + bank details, deadline, proof-of-payment link |
| Ticket | marked Paid | QR image (signed token, PNG attachment + inline CID), participant ID, ticket type, date/venue, add-to-calendar (.ics) |
| Invite | account created | inviter, level/committee, single-use link (48 h) |
| Password reset | requested | link (30 min); same response whether or not the account exists |
| Speaker link / OTP | link sent / form opened | personal link (no tokens in subject), 6-digit code (10 min) |
| Plus-one voucher | speaker adds plus-one | single-use 50% code, registration link, note that the room is free but the ticket isn't |
| Invitation letter | invitations sent | merged addressee (verified only), signed PDF attachment |
| Request decision | CFO approves/rejects | decision, note, amount |

## Rules
- Personal data in emails is limited to what the template needs. No full lists of other people.
- Track the Mailtrap message ID on the outbox row. Bounces from webhooks (`/api/webhooks/mailtrap`, verified signature) update the invitation or contact status.
- Tests use a `FakeEmailSender` that captures messages. Never hit Mailtrap in CI.
