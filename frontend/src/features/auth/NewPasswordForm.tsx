import type { ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/Button'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { MIN_PASSWORD, newPasswordSchema, type NewPasswordValues } from './schemas'

/** Password + confirm, shared by invite activation and password reset. */
export function NewPasswordForm({
  passwordLabel,
  submitLabel,
  pending,
  serverPasswordError,
  before,
  onSubmit,
}: {
  passwordLabel: string
  submitLabel: string
  pending: boolean
  serverPasswordError?: string
  before?: ReactNode
  onSubmit: (password: string) => void
}) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<NewPasswordValues>({ resolver: zodResolver(newPasswordSchema), defaultValues: { password: '', confirm: '' } })

  return (
    <form
      noValidate
      onSubmit={handleSubmit((v) => {
        if (!pending) onSubmit(v.password)
      })}
      className="flex flex-col gap-[18px]"
    >
      {before}
      <Field
        label={passwordLabel}
        hint={`At least ${MIN_PASSWORD} characters. Avoid a password you use elsewhere.`}
        error={errors.password?.message ?? serverPasswordError}
      >
        {(p) => <Input {...p} {...register('password')} type="password" autoComplete="new-password" controlSize="lg" />}
      </Field>
      <Field label="Confirm password" error={errors.confirm?.message}>
        {(p) => <Input {...p} {...register('confirm')} type="password" autoComplete="new-password" controlSize="lg" />}
      </Field>
      <Button type="submit" size="lg" block loading={pending}>
        {submitLabel}
      </Button>
    </form>
  )
}
