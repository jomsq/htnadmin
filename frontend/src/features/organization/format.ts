import type { Person } from './schemas'

export function fullName(p: Pick<Person, 'firstName' | 'middleName' | 'lastName'>): string {
  return [p.firstName, p.middleName, p.lastName].filter(Boolean).join(' ')
}
