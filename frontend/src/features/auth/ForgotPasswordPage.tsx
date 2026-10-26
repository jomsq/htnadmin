import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router'
import { errorMessage } from '@/app/errors'
import { isApiError } from '@/app/api'
import { AuthLayout } from '@/app/layouts/AuthLayout'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { buttonClass } from '@/components/ui/button-styles'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { useForgotPasswordMutation } from './api'
import { forgotSchema, type ForgotValues } from './schemas'

export const RESET_SENT_MESSAGE = 'If that email has an account, a reset link is on its way. It expires in 30 minutes.'

export function ForgotPasswordPage() {
  const mutation = useForgotPasswordMutation()
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ForgotValues>({ resolver: zodResolver(forgotSchema), defaultValues: { email: '' } })

  // The response never reveals whether the email exists; only rate limits / outages surface.
  const failed = mutation.isError && (!isApiError(mutation.error) || mutation.error.status === 429 || mutation.error.status >= 500)
  const sent = mutation.isSuccess || (mutation.isError && !failed)

  if (sent) {
    return (
      <AuthLayout title="Check your email">
        <div role="status" className="flex flex-col gap-3.5">
          <p className="text-sm leading-normal text-gray-700 dark:text-gray-300">{RESET_SENT_MESSAGE}</p>
          <Link to="/login" className={buttonClass('secondary', 'lg', 'w-full')}>
            Back to sign in
          </Link>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Reset your password" subtitle="We will email you a link to set a new password.">
      <form
        noValidate
        onSubmit={handleSubmit((v) => {
          if (!mutation.isPending) mutation.mutate(v)
        })}
        className="flex flex-col gap-[18px]"
      >
        {failed ? (
          <Alert tone="red" live="alert">
            {errorMessage(mutation.error)}
          </Alert>
        ) : null}
        <Field label="Email" error={errors.email?.message}>
          {(p) => <Input {...p} {...register('email')} type="email" autoComplete="username" controlSize="lg" />}
        </Field>
        <Button type="submit" size="lg" block loading={mutation.isPending}>
          Send reset link
        </Button>
        <Link to="/login" className={buttonClass('ghost', 'md', 'w-full text-brand-600 dark:text-brand-300')}>
          Back to sign in
        </Link>
      </form>
    </AuthLayout>
  )
}
