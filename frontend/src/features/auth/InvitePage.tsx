import { useNavigate, useParams } from 'react-router'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { AuthLayout } from '@/app/layouts/AuthLayout'
import { Alert } from '@/components/ui/Alert'
import { useAcceptInviteMutation } from './api'
import { NewPasswordForm } from './NewPasswordForm'

export function InvitePage() {
  const { token = '' } = useParams()
  const navigate = useNavigate()
  const mutation = useAcceptInviteMutation()
  const fieldErrors = isApiError(mutation.error, 400) ? mutation.error.fieldErrors : {}
  const passwordError = fieldErrors.password

  return (
    <AuthLayout title="Set up your account" subtitle="Welcome to HackTheNorth.PH. Choose a password to finish.">
      <NewPasswordForm
        passwordLabel="Create a password"
        submitLabel="Activate account"
        pending={mutation.isPending}
        serverPasswordError={passwordError}
        before={
          mutation.isError && !passwordError ? (
            <Alert tone="red" live="alert">
              {errorMessage(mutation.error, {
                400: 'This invite link is invalid, already used or expired. Ask your committee lead to resend it.',
              })}
            </Alert>
          ) : (
            <Alert tone="brand">This link works once and expires in 48 hours.</Alert>
          )
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
