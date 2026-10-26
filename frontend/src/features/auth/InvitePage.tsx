import { Link, useNavigate, useParams } from 'react-router'
import { isApiError } from '@/app/api'
import { errorMessage, INVITE_INVALID_MESSAGE } from '@/app/errors'
import { AuthLayout } from '@/app/layouts/AuthLayout'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { buttonClass } from '@/components/ui/button-styles'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { useAcceptInviteMutation, useInviteQuery } from './api'
import { NewPasswordForm } from './NewPasswordForm'
import type { InviteInfo } from './schemas'

const TITLE = 'Set up your account'
const SUBTITLE = 'Welcome to HackTheNorth.PH. Choose a password to finish.'

export function InvitePage() {
  const { token = '' } = useParams()
  const invite = useInviteQuery(token)

  if (!token || invite.data === null) return <InvalidInvite />

  if (invite.isError) {
    return (
      <AuthLayout title={TITLE} subtitle={SUBTITLE}>
        <Alert
          tone="red"
          live="alert"
          action={
            <Button variant="secondary" size="sm" onClick={() => void invite.refetch()}>
              Try again
            </Button>
          }
        >
          {errorMessage(invite.error)}
        </Alert>
      </AuthLayout>
    )
  }

  if (invite.isPending) {
    return (
      <AuthLayout title={TITLE} subtitle={SUBTITLE}>
        <div className="flex flex-col gap-[18px]" aria-busy="true">
          <span className="sr-only" role="status">
            Checking your invite link
          </span>
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-11 w-full" />
          <Skeleton className="h-11 w-full" />
          <Skeleton className="h-11 w-full" />
        </div>
      </AuthLayout>
    )
  }

  return <AcceptInviteForm token={token} invite={invite.data} />
}

function InvalidInvite() {
  return (
    <AuthLayout title={TITLE}>
      <Alert tone="red" live="alert">
        {INVITE_INVALID_MESSAGE}
      </Alert>
      <Link to="/login" className={buttonClass('secondary', 'lg', 'w-full')}>
        Back to sign in
      </Link>
    </AuthLayout>
  )
}

function AcceptInviteForm({ token, invite }: { token: string; invite: InviteInfo }) {
  const navigate = useNavigate()
  const mutation = useAcceptInviteMutation()
  const passwordError = isApiError(mutation.error, 400) ? mutation.error.fieldErrors.password : undefined

  if (isApiError(mutation.error) && mutation.error.code === 'invite_invalid') return <InvalidInvite />

  return (
    <AuthLayout title={TITLE} subtitle={SUBTITLE}>
      <NewPasswordForm
        passwordLabel="Create a password"
        submitLabel="Activate account"
        pending={mutation.isPending}
        serverPasswordError={passwordError}
        before={
          <>
            {mutation.isError && !passwordError ? (
              <Alert tone="red" live="alert">
                {errorMessage(mutation.error)}
              </Alert>
            ) : null}
            <Alert tone="brand">
              {invite.inviterName ? `Invited by ${invite.inviterName} as ` : 'You were invited as '}
              <strong>{invite.level}</strong>. This link works once and expires in 48 hours.
            </Alert>
            <Field label="Email">
              {(p) => <Input {...p} type="email" value={invite.email} readOnly autoComplete="username" controlSize="lg" />}
            </Field>
          </>
        }
        onSubmit={(password) =>
          mutation.mutate(
            { token, password },
            {
              onSuccess: () =>
                navigate('/login', { replace: true, state: { notice: 'Your account is ready. Sign in with your new password.' } }),
            },
          )
        }
      />
    </AuthLayout>
  )
}
