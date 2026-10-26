import { isRouteErrorResponse, useRouteError } from 'react-router'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'

export function RouteErrorPage() {
  const error = useRouteError()
  const message = isRouteErrorResponse(error) ? `${error.status} ${error.statusText}` : 'Something went wrong while loading this page.'
  return (
    <div className="mx-auto max-w-lg p-6">
      <Alert tone="red" live="alert" action={<Button variant="secondary" size="sm" onClick={() => window.location.reload()}>Reload</Button>}>
        {message}
      </Alert>
    </div>
  )
}
