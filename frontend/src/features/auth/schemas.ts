import { z } from 'zod'

export const loginSchema = z.object({
  email: z.email('Enter a valid email address.'),
  password: z.string().min(1, 'Enter your password.'),
  rememberMe: z.boolean(),
})
export type LoginValues = z.infer<typeof loginSchema>

export const codeSchema = z.object({
  code: z
    .string()
    .trim()
    .regex(/^\d{6}$/, 'Enter the 6-digit code from your authenticator app.'),
})
export type CodeValues = z.infer<typeof codeSchema>

export const forgotSchema = z.object({ email: z.email('Enter a valid email address.') })
export type ForgotValues = z.infer<typeof forgotSchema>

export const MIN_PASSWORD = 12

export const newPasswordSchema = z
  .object({
    password: z.string().min(MIN_PASSWORD, `Use at least ${MIN_PASSWORD} characters.`),
    confirm: z.string().min(1, 'Type the password again.'),
  })
  .refine((v) => v.password === v.confirm, { path: ['confirm'], message: 'Passwords do not match.' })
export type NewPasswordValues = z.infer<typeof newPasswordSchema>

export const loginAcceptedSchema = z.object({ requires2fa: z.literal(true) })

export const twoFactorSetupSchema = z.object({ sharedKey: z.string(), authenticatorUri: z.string() })
export type TwoFactorSetup = z.infer<typeof twoFactorSetupSchema>

export const inviteInfoSchema = z.object({
  email: z.string(),
  firstName: z.string(),
  level: z.string(),
  inviterName: z.string().nullish(),
  expiresAt: z.string(),
})
export type InviteInfo = z.infer<typeof inviteInfoSchema>

/** POST /api/auth/invite/accept: board accounts must set up 2FA next. Older servers answer 204 (no body). */
export const acceptInviteResultSchema = z
  .object({ requiresTwoFactorSetup: z.boolean().default(false) })
  .loose()
  .optional()
  .transform((v) => ({ requiresTwoFactorSetup: v?.requiresTwoFactorSetup ?? false }))
export type AcceptInviteResult = z.output<typeof acceptInviteResultSchema>
