---
name: htn-domain
description: Business rules and glossary for the HackTheNorth.PH admin system (levels, events, points, registrations, QR/hash IDs, vouchers, sponsors, partners, directory, suppliers, speakers, merch/POS/pop-ups, membership, finance). Load before designing, building, testing or reviewing any feature so behaviour matches what the organization agreed.
---

# HTN domain rules

This is the single source of truth for behaviour. If code and this file disagree, follow this file and flag the difference. The screens live in the design canvas (see `docs/design.md`). Permissions are in `permissions.md` next to this file.

## Glossary
| Term | Meaning |
|---|---|
| Board | Board of Trustees of the non-stock corporation (Chairperson, Vice Chair, Treasurer/CFO, Corporate Secretary, Trustees). Board members are admins. |
| Goon | An organizer. Holds a position (e.g. Head of Logistics). Has a login. |
| Apprentice | A stepping stone to goon. Has a login. |
| Member | A stepping stone to apprentice. **Roster only, no login.** |
| Volunteer | Helps at a single event. Roster only, no login, no points. |
| Committee | A per-event team (Logistics, Programs, Partnerships, Marketing, Registration & Check-in, Tech & AV…), with one **lead** (a goon) and goons, apprentices and volunteers. |
| Main event | The yearly flagship event (e.g. HTN Con 2026). Membership is tied to it. |
| Fund | The **Organization fund**, or one fund per **event**. |

## Accounts
- **No public sign-up.** Only admins create accounts.
  - Board, Goon and Apprentice get an **invite email**. The link is single-use and expires in 48 h, and the user sets their own password (at least 12 characters).
  - Members and volunteers get no login and no email.
- **Board accounts must use 2-step verification.**
- **Password reset** links expire in 30 min. The response never reveals whether an email has an account.
- **Deactivate** instead of deleting.

## Levels and promotion
- Path: Member → Apprentice → Goon. Board is a separate seat. Every change is kept in the person's history.
- **To become a Member**, the person must have **checked in at at least one event**. The system verifies this from check-in records.
- **Membership is paid once per main-event year**, usually at the event (a POS item "Membership YYYY"). It is valid until the next main event, and posts to the Organization fund under *Membership dues*.

## Events and tasks
- An event has details, tickets, vouchers, registration settings, speaker travel settings, committees, a task board, agenda, registrations, emails, check-in, sponsors, leads, invitations and merch.
- **Task board** columns: Backlog, To do, In progress, Review, Done. A task has a checklist, attachments, comments, an activity log, labels and a due date.
- **Only admins and the committee lead** create, assign and score tasks in that committee.
- **Points:** base points × role multiplier, awarded when the task is **approved into Done** (by the lead or an admin).
  - Defaults are Lead 1.0, Collab 0.7, Assistant 0.3, configurable in Settings.
  - A task can have several people in each role.
  - **An award is a snapshot.** The row keeps the base points, the role, the multiplier used, the event and the
    **committee as of the moment of the award**. Changing the multipliers in Settings, editing the task's base points
    or later moving the task to another committee never rewrites history: reporting reads `points_awards`, never the
    task's current committee. Reopening a Done task reverses the awards with a reason instead of deleting them.
- **Leaderboard:** goons and apprentices only (board members earn points but are never ranked). It can be filtered by
  event, quarter (the calendar quarter in Asia/Manila) or all time, and by committee — the committee filter and the
  committee shown on a row come from the award's snapshotted committee, not from where the task sits today.
  Reversed awards count for nothing. Ties share a rank (dense ranking: 1, 1, 2).

## Registration, payment, tickets
- **Form fields:**
  - first name, middle name (optional), last name
  - gender, email, mobile (+63)
  - occupation, agency/company
  - shirt size (for counting only; shirts are not free)
  - "How did you hear about {event}?"
  - ticket type, voucher, Data Privacy Act consent
- **Group tickets:** one buyer fills in the details for every member of the group.
- **Manual registration** by an admin uses the same form.
- **Statuses:** `PendingPayment → Paid → CheckedIn`, or `Cancelled`. Never delete.
- **Payment is manual** (GCash or bank transfer).
  - On registration, a **billing email** is sent with the configured accounts and a deadline (default 48 h).
  - When an admin marks it **Paid**, a **ticket email** is sent with a **signed QR code**.
- **Participant ID:** `HTN-XXXX-XXXX`, using 8 random Crockford base32 characters (no 0/O/1/I/L).
  - It is **new per event**, never sequential, and is used as the payment reference and for searching.
  - The QR payload is a longer **HMAC-signed token** that includes the participant and event IDs, so the short ID alone can't be used to fake a ticket.
- **Check-in** (phone camera, laptop webcam, or USB/Bluetooth handheld scanner that types like a keyboard and sends Enter). Results:
  - **Welcome:** first valid scan.
  - **Already checked in:** shows when and at which desk.
  - **Not paid:** send to the help desk.
  - **Invalid signature.**
  - Every scan is logged with station and staff.

## Vouchers
- Code, % or fixed amount, which tickets it applies to, usage limit and expiry.
- **Speaker plus-one:**
  - The plus-one stays **free in the speaker's room** (shared accommodation).
  - The plus-one **must register**. The system **automatically creates one single-use 50% voucher** (`SPK-<NAME>-<4 random>`) and emails it to them.
  - The voucher is cancelled if the plus-one is removed.

## Speakers
- **Leads:** Prospect → Contacted → In talks → Confirmed / Declined. A confirmed speaker can be sent a **confirmation link**.
- **Link security:**
  - a random 128-bit token, plus a 6-digit **email OTP** (10 min, locks after 5 tries)
  - expires after 14 days by default; can be revoked or re-sent
  - every open and submit is logged
  - search engines are told not to index it
- **The form adapts to the event's travel mode** (Plane / Bus / Plane or bus / No travel):
  - always: photo, arrival and departure dates
  - plane: flying from, preferred arrival time, name as on ID
  - bus: departure terminal, ETA, pickup needed
  - also: plus-one (name and email), dietary needs, consent
- Travel details are visible only to admins and the Programs lead. The ID name and flight details are deleted 30 days after the event.

## Sponsors, partners
- **Sponsor** is an organization or an **individual** (credited by name or as "Anonymous").
  - Type: Cash, In-kind, or Cash + In-kind.
  - Tier: class (Platinum/Gold/Silver/Partner/Individual), tier name (e.g. "Conference Tote Bag"), perks and comped seats.
  - Status: Awaiting contract → Confirmed.
  - **Once confirmed**, an organization sponsor needs a **logo and website** (the design flags missing ones).
- **Comped seats** are registrations on a hidden "Sponsor comp" ticket.
- **Partners:**
  - An organization becomes a partner when it reaches the event's **threshold** of registrants (configurable, default 20; counts paid by default). Admins can also tag one manually.
  - Registrations are grouped by the Agency/Company field and matched to the Directory, with a review queue for unmatched names.
  - An optional **group registration link** is offered **only after the threshold is reached**.

## Directory and invitations
- The Directory is **global**: agencies, companies, universities, non-profits.
  - The addressee is stored as a **position + current holder**. When the head changes, the old holder is kept in history.
  - A contact not verified in **90 days** is flagged "Needs check".
- **Invitations per event:**
  1. Pick organizations.
  2. Verify contacts.
  3. Merge into the letter template.
  4. Send — **only to verified contacts**.
  5. Track Sent → Opened → Replied → Confirmed / Declined, or Bounced. A bounce re-flags the contact.
- Every send is logged per event and on the organization's history.

## Suppliers
- The supplier list is **global**: category, contact, whether they issue an OR, documents (BIR 2303, business permit), TIN, bank details.
- **Hired per event** through the event budget: tied to a budget line, with contract amount, paid amount, next payment and status (Quotation → Booked → Deposit paid → Fully paid).
- **After the event**, each committee lead rates the suppliers their committee used: 1–5 stars, tags, "hire again?", a note. The rating shown is the average.
- Prices, TIN and bank details are visible to the board only.

## Merch, POS, pop-ups
- **Stock is per event**, with variants (sizes).
- **Discount per level, set per event:** default Goon 20%, Apprentice 15%, Member 10%. Attendees pay full price.
- **Every POS sale is tied to a buyer**, found by QR (ticket or member QR) or name search. Each sale records cashier, method (Cash/GCash/Bank) and items.
- **Leftover stock** moves to the **Merch store**.
  - **Pop-up stores** get stock allocated, sell through the same POS, then **close & count** (brought/sold/returned/missing, cash counted vs expected).
  - Missing items need a **CFO write-off**. Sales post to the **Organization fund**.
  - **Admins choose which committees** may create and run pop-ups.

## Finance (board only)
- **Visible to the board only.**
  - **Only the CFO** posts money in/out, transfers, voids, reconciles, closes months and edits budgets.
  - **One finance assistant** (a goon) can add entries; they stay *Pending* until the CFO approves.
  - Other board members are **view only** and may file **expense requests**.
- **Ledger entry fields:** date, reference (OR/bank ref), description, category, account, direction, amount, receipt (required), entered by, approved by, status (Pending / Posted / Voided with reason).
- **Funds:** Organization and per event.
  - Ticket, merch, sponsor and membership payments **auto-post** when marked paid.
  - **Transfers between funds** are recorded on both sides.
  - **When an event closes, its surplus transfers to the Organization fund.**
- **Balancing:** for each account (BPI, BDO, GCash, Cash on hand), the book balance is compared with the statement or counted cash. **A month can only be closed when everything is balanced and nothing is pending.**
- **Budgets:** per event, with income and expense lines. **Only the CFO edits them; there is no board approval.** Every save creates a new version with a reason, and the board is notified. Actuals come from the ledger.
- **Expense requests** (non-event spending, e.g. the year-end party): a board member requests → the CFO approves or rejects (reason required) → the CFO marks it paid, which posts to the Organization fund.
- **Audit log** is append-only. It records every add, edit (before/after), approve, void, reconcile, close and export.

## Emails (Mailtrap)
Billing, ticket with QR, account invite, password reset, speaker confirmation link + OTP, plus-one voucher, invitation letters, expense-request decisions. Admins can change the logo, theme color, payment accounts and deadline; the layout is fixed.
