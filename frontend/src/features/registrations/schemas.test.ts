import { describe, expect, it } from 'vitest'
import {
  defaultFormOptions,
  DUPLICATE_MEMBER_EMAIL_MESSAGE,
  emptyRegistrationValues,
  NAME_MESSAGE,
  NO_LINKS_MESSAGE,
  ORG_TEXT_MESSAGE,
  registrationFormSchema,
  type RegistrationFormValues,
} from './schemas'

const schema = registrationFormSchema({ ...defaultFormOptions, mode: 'public' })

function values(over: Partial<RegistrationFormValues['buyer']> = {}, members: RegistrationFormValues['members'] = []): RegistrationFormValues {
  const v = emptyRegistrationValues('t1', members.length + 1)
  return {
    ...v,
    consent: true,
    buyer: { ...v.buyer, firstName: 'Maria', lastName: 'Santos', gender: 'Female', email: 'maria@gmail.com', mobile: '9171234567', ...over },
    members,
  }
}

function messagesAt(v: RegistrationFormValues, path: string): string[] {
  const r = schema.safeParse(v)
  return r.success ? [] : r.error.issues.filter((i) => i.path.join('.') === path).map((i) => i.message)
}

describe('registration field rules', () => {
  it.each(['José', 'Ma. Cristina', "D'Souza", 'Dela Cruz-Reyes', 'Nguyễn', 'Zoë'])('accepts the name %s', (name) => {
    expect(messagesAt(values({ firstName: name }), 'buyer.firstName')).toEqual([])
  })

  it.each([
    ['Maria2', NAME_MESSAGE],
    ['-Maria', NAME_MESSAGE],
    ['Maria_S', NAME_MESSAGE],
    ['<b>Maria</b>', NO_LINKS_MESSAGE],
    ['maria@x.ph', NO_LINKS_MESSAGE],
  ])('rejects the name %s with one plain message', (name, message) => {
    expect(messagesAt(values({ lastName: name }), 'buyer.lastName')).toEqual([message])
  })

  it('caps names at 80 characters', () => {
    expect(messagesAt(values({ firstName: 'A'.repeat(80) }), 'buyer.firstName')).toEqual([])
    expect(messagesAt(values({ firstName: 'A'.repeat(81) }), 'buyer.firstName')).toEqual(['Use 80 characters or fewer.'])
  })

  it('checks the optional middle name the same way', () => {
    expect(messagesAt(values({ middleName: '' }), 'buyer.middleName')).toEqual([])
    expect(messagesAt(values({ middleName: 'B4' }), 'buyer.middleName')).toEqual([NAME_MESSAGE])
  })

  it('allows digits and & , ( ) / in occupation and company, but no links or markup', () => {
    expect(messagesAt(values({ company: 'R&D Labs, Inc. (PH) 2/F' }), 'buyer.company')).toEqual([])
    expect(messagesAt(values({ occupation: 'Engineer III / Team lead' }), 'buyer.occupation')).toEqual([])
    expect(messagesAt(values({ company: 'https://evil.example' }), 'buyer.company')).toEqual([NO_LINKS_MESSAGE])
    expect(messagesAt(values({ company: '<script>' }), 'buyer.company')).toEqual([NO_LINKS_MESSAGE])
    expect(messagesAt(values({ occupation: 'Dev #1' }), 'buyer.occupation')).toEqual([ORG_TEXT_MESSAGE])
  })

  it('flags a group member who reuses an email (any case) on that member', () => {
    const v = values({}, [
      { firstName: 'Bea', lastName: 'Cruz', email: 'bea@gmail.com' },
      { firstName: 'Carl', lastName: 'Cruz', email: 'MARIA@gmail.com' },
      { firstName: 'Dani', lastName: 'Cruz', email: 'Bea@Gmail.com ' },
    ])
    expect(messagesAt(v, 'members.0.email')).toEqual([])
    expect(messagesAt(v, 'members.1.email')).toEqual([DUPLICATE_MEMBER_EMAIL_MESSAGE])
    expect(messagesAt(v, 'members.2.email')).toEqual([DUPLICATE_MEMBER_EMAIL_MESSAGE])
  })
})
