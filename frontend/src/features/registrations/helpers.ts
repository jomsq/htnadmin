import type { EventDetail, TicketType } from '@/features/events/schemas'
import type { PaymentAccount } from '@/features/settings/schemas'
import type { PaymentMethod, PublicEvent, PublicTicket } from './schemas'

/** Payment accounts that fit a method (GCash → GCash accounts, Bank → bank accounts, Cash → cash on hand). */
export function accountsFor(method: PaymentMethod, accounts: PaymentAccount[]): PaymentAccount[] {
  if (method === 'Comp') return []
  return accounts.filter((a) => a.isActive && a.kind === method)
}

/** The default account for a method: the one marked default in Settings, else the first that fits. */
export function defaultAccountId(method: PaymentMethod, accounts: PaymentAccount[]): string {
  const fitting = accountsFor(method, accounts)
  return (fitting.find((a) => a.isDefault) ?? fitting[0])?.id ?? ''
}

/** GCash and bank payments need an account when Settings has one for that method. */
export function needsAccountFor(accounts: PaymentAccount[]) {
  return (m: PaymentMethod) => (m === 'GCash' || m === 'Bank') && accountsFor(m, accounts).length > 0
}

/** Tickets an admin may use: every active one, including Hidden (sponsor comp). Sold-out ones are listed but disabled. */
export function manualTickets(event: EventDetail): TicketType[] {
  return event.tickets.filter((t) => t.isActive && t.status !== 'Inactive').sort((a, b) => a.sortOrder - b.sortOrder)
}

/** Tickets on the public page. Group tickets only show when the event allows group registration. */
export function publicTickets(event: PublicEvent): PublicTicket[] {
  return event.tickets.filter((t) => event.groupRegistration || t.groupSize <= 1)
}
