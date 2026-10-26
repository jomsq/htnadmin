import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useLocation, useNavigate } from 'react-router'
import { isApiError } from '@/app/api'
import { useAuth } from '@/app/auth/auth-context'
import { errorMessage } from '@/app/errors'
import { AuthLayout } from '@/app/layouts/AuthLayout'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { safeNext } from '@/lib/format'
import { useEnableTwoFactorMutation, useTwoFactorSetupQuery } from './api'
import { formatSharedKey } from './format'
import { QrCode } from './QrCode'
import { codeSchema, type CodeValues } from './schemas'


export function SetupTwoFactorPage() {
  const setup = useTwoFactorSetupQuery()
  const enable = useEnableTwoFactorMutation()
  const { twoFactorEnabled, signOut } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const next = safeNext((location.state as { next?: string } | null)?.next)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<CodeValues>({ resolver: zodResolver(codeSchema), defaultValues: { code: '' } })

  async function proceed() {
    await twoFactorEnabled()
    navigate(next, { replace: true })
  }

  // 409: two-step verification is already on (e.g. enabled in another tab).
  const alreadyOn = (e: unknown) => isApiError(e, 409) && e.code === '2fa_already_enabled'

  const onSubmit = handleSubmit((v) => {
    if (enable.isPending) return
    enable.mutate(v, { onSuccess: proceed })
  })

  async function handleSignOut() {
    await signOut().catch(() => undefined)
    navigate('/login', { replace: true })
  }

  return (
    <AuthLayout
      title="Turn on 2-step verification"
      subtitle="Board accounts must use 2-step verification. Use an authenticator app such as Google Authenticator, 1Password or Authy."
      footnote={false}
    >
      <ol className="flex flex-col gap-5 text-sm text-gray-700 dark:text-gray-300">
        <li className="flex flex-col gap-3">
          <span className="font-medium text-gray-900 dark:text-gray-50">1. Scan this QR code with your app</span>
          {setup.isPending ? (
            <div className="flex flex-col items-center gap-3">
              <Skeleton className="size-48" />
              <Skeleton className="h-5 w-56" />
            </div>
          ) : setup.isError ? (
            <Alert
              tone={alreadyOn(setup.error) ? 'green' : 'red'}
              live="alert"
              action={
                alreadyOn(setup.error) ? (
                  <Button variant="secondary" size="sm" onClick={() => void proceed()}>
                    Continue
                  </Button>
                ) : (
                  <Button variant="secondary" size="sm" onClick={() => void setup.refetch()}>
                    Try again
                  </Button>
                )
              }
            >
              {errorMessage(setup.error)}
            </Alert>
          ) : (
            <div className="flex flex-col items-center gap-3">
              <QrCode value={setup.data.authenticatorUri} label="QR code for your authenticator app" />
              <p className="text-center text-[0.8125rem] text-gray-600 dark:text-gray-400">Can't scan it? Enter this key instead:</p>
              <code className="rounded-md bg-gray-50 px-3 py-2 font-mono text-sm tracking-wider text-gray-900 select-all dark:bg-white/5 dark:text-gray-50">
                {formatSharedKey(setup.data.sharedKey)}
              </code>
            </div>
          )}
        </li>
        <li className="flex flex-col gap-3">
          <span className="font-medium text-gray-900 dark:text-gray-50">2. Enter the 6-digit code it shows</span>
          <form noValidate onSubmit={onSubmit} className="flex flex-col gap-[18px]">
            {enable.isError ? (
              <Alert
                tone={alreadyOn(enable.error) ? 'green' : 'red'}
                live="alert"
                action={
                  alreadyOn(enable.error) ? (
                    <Button variant="secondary" size="sm" onClick={() => void proceed()}>
                      Continue
                    </Button>
                  ) : undefined
                }
              >
                {errorMessage(enable.error, { 400: 'That code did not work. Check your app and try again.' })}
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
                  controlSize="lg"
                  className="font-mono tracking-[0.3em]"
                />
              )}
            </Field>
            <Button type="submit" size="lg" block loading={enable.isPending} disabled={!setup.isSuccess}>
              Verify and turn on
            </Button>
          </form>
        </li>
      </ol>
      <Button variant="ghost" block onClick={handleSignOut}>
        Sign out
      </Button>
    </AuthLayout>
  )
}
