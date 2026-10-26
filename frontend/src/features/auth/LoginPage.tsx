import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router'
import { useAuth } from '@/app/auth/auth-context'
import type { Me } from '@/app/auth/me'
import { errorMessage } from '@/app/errors'
import { AuthLayout } from '@/app/layouts/AuthLayout'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Field } from '@/components/ui/Field'
import { Checkbox, Input } from '@/components/ui/Input'
import { safeNext } from '@/lib/format'
import { useLoginMutation, useLoginTwoFactorMutation } from './api'
import { codeSchema, loginSchema, type CodeValues, type LoginValues } from './schemas'

type Step = 'signin' | 'code'

export function LoginPage() {
  const [step, setStep] = useState<Step>('signin')
  const [params] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const { signedIn } = useAuth()
  const notice = (location.state as { notice?: string } | null)?.notice
  const next = safeNext(params.get('next'))

  function finish(me: Me) {
    signedIn(me)
    navigate(me.twoFactorRequired && !me.twoFactorEnabled ? '/setup-2fa' : next, { replace: true })
  }

  if (step === 'code') {
    return (
      <AuthLayout title="Enter your code" subtitle="Open your authenticator app and enter the 6-digit code for HTN Admin.">
        <CodeStep onDone={finish} onBack={() => setStep('signin')} />
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Sign in to HTN Admin" subtitle="For board members, goons and apprentices.">
      {notice ? (
        <Alert tone="green" live="status">
          {notice}
        </Alert>
      ) : null}
      <SignInStep onDone={finish} onTwoFactor={() => setStep('code')} />
    </AuthLayout>
  )
}

function SignInStep({ onDone, onTwoFactor }: { onDone: (me: Me) => void; onTwoFactor: () => void }) {
  const mutation = useLoginMutation()
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginValues>({ resolver: zodResolver(loginSchema), defaultValues: { email: '', password: '', rememberMe: false } })

  const onSubmit = handleSubmit((values) => {
    if (mutation.isPending) return
    mutation.mutate(values, {
      onSuccess: (result) => (result.kind === 'twoFactor' ? onTwoFactor() : onDone(result.me)),
    })
  })

  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-[18px]">
      {mutation.isError ? (
        <Alert tone="red" live="alert">
          {errorMessage(mutation.error, { 401: 'Email or password is incorrect.' })}
        </Alert>
      ) : null}
      <Field label="Email" error={errors.email?.message}>
        {(p) => <Input {...p} {...register('email')} type="email" autoComplete="username" controlSize="lg" />}
      </Field>
      <Field
        label="Password"
        error={errors.password?.message}
        labelAside={
          <Link to="/forgot" className="text-[0.8125rem] font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300">
            Forgot password?
          </Link>
        }
      >
        {(p) => <Input {...p} {...register('password')} type="password" autoComplete="current-password" controlSize="lg" />}
      </Field>
      <label className="flex items-center gap-2.5 text-sm text-gray-700 dark:text-gray-300">
        <Checkbox {...register('rememberMe')} />
        Keep me signed in on this device
      </label>
      <Button type="submit" size="lg" block loading={mutation.isPending}>
        Sign in
      </Button>
    </form>
  )
}

function CodeStep({ onDone, onBack }: { onDone: (me: Me) => void; onBack: () => void }) {
  const mutation = useLoginTwoFactorMutation()
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<CodeValues>({ resolver: zodResolver(codeSchema), defaultValues: { code: '' } })

  const onSubmit = handleSubmit((values) => {
    if (mutation.isPending) return
    mutation.mutate(values, { onSuccess: onDone })
  })

  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-[18px]">
      {mutation.isError ? (
        <Alert tone="red" live="alert">
          {errorMessage(mutation.error, { 401: 'That code did not work. Check your app and try again.' })}
        </Alert>
      ) : null}
      <Field label="Authentication code" error={errors.code?.message}>
        {(p) => (
          <Input
            {...p}
            {...register('code')}
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            autoFocus
            controlSize="lg"
            className="font-mono tracking-[0.3em]"
          />
        )}
      </Field>
      <Button type="submit" size="lg" block loading={mutation.isPending}>
        Verify
      </Button>
      <Button variant="ghost" block onClick={onBack} className="text-brand-600 dark:text-brand-300">
        Back to sign in
      </Button>
    </form>
  )
}
