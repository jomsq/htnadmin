import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { AuthLayout } from '@/app/layouts/AuthLayout'
import { Alert } from '@/components/ui/Alert'
import { buttonClass } from '@/components/ui/button-styles'
import { useResetPasswordMutation } from './api'
import { NewPasswordForm } from './NewPasswordForm'

export function ResetPasswordPage() {
  const { token = '' } = useParams()
  const [params] = useSearchParams()
  const email = params.get('email') ?? ''
  const navigate = useNavigate()
  const mutation = useResetPasswordMutation()
  const fieldErrors = isApiError(mutation.error, 400) ? mutation.error.fieldErrors : {}
  const passwordError = fieldErrors.password

  if (!email || !token) {
    return (
      <AuthLayout title="Reset your password">
        <Alert tone="red" live="alert">
          This reset link is incomplete. Open the link from the email again, or request a new one.
        </Alert>
        <Link to="/forgot" className={buttonClass('secondary', 'lg', 'w-full')}>
          Request a new link
        </Link>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Set a new password" subtitle={`For ${email}. The link expires 30 minutes after it was sent.`}>
      <NewPasswordForm
        passwordLabel="New password"
        submitLabel="Set new password"
        pending={mutation.isPending}
        serverPasswordError={passwordError}
        before={
          mutation.isError && !passwordError ? (
            <Alert
              tone="red"
              live="alert"
              action={
                <Link to="/forgot" className="font-medium text-red-800 underline dark:text-red-200">
                  Request a new link
                </Link>
              }
            >
              {errorMessage(mutation.error, { 400: 'This reset link is invalid or has expired.' })}
            </Alert>
          ) : null
        }
        onSubmit={(password) =>
          mutation.mutate(
            { token, email, password },
            {
              onSuccess: () =>
                navigate('/login', { replace: true, state: { notice: 'Password updated. Sign in with your new password.' } }),
            },
          )
        }
      />
    </AuthLayout>
  )
}
