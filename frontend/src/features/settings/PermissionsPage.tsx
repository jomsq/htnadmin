import { Link } from 'react-router'
import { Card, CardBody, CardHeader } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { CheckIcon, ChevronLeftIcon } from '@/components/ui/icons'
import { permissionRows, permissionRules, roleColumns, roleNotes, type Access } from './permissions'

function AccessCell({ access }: { access: Access }) {
  if (access === true) {
    return (
      <span className="inline-flex items-center text-green-700 dark:text-green-400">
        <CheckIcon size={16} />
        <span className="sr-only">Allowed</span>
      </span>
    )
  }
  if (access === false) {
    return (
      <span className="text-gray-500 dark:text-gray-400">
        <span aria-hidden="true">—</span>
        <span className="sr-only">Not allowed</span>
      </span>
    )
  }
  return <span className="text-[0.8125rem] text-gray-700 dark:text-gray-300">{access}</span>
}

/** Read-only matrix of the named policies (canvas frame 03). The server enforces them. */
export function PermissionsPage() {
  return (
    <>
      <div className="flex flex-col gap-3">
        <Link
          to="/settings"
          className="inline-flex min-h-10 w-fit items-center gap-1 text-sm font-medium text-gray-600 hover:text-gray-900 dark:text-gray-400 dark:hover:text-gray-50"
        >
          <ChevronLeftIcon size={16} />
          Settings
        </Link>
        <PageHeader
          title="Roles & permissions"
          description="Who can do what. Read-only: roles come from levels and positions, and the server checks every action."
        />
      </div>

      <Card aria-labelledby="matrix-heading">
        <CardHeader title={<span id="matrix-heading">Permissions matrix</span>} description="Check = allowed, dash = not allowed, text = allowed within that scope." />
        <Table minWidth={820} aria-labelledby="matrix-heading">
          <THead>
            <tr>
              <Th>Policy</Th>
              {roleColumns.map((r) => (
                <Th key={r}>{r}</Th>
              ))}
            </tr>
          </THead>
          <TBody>
            {permissionRows.map((row) => (
              <Tr key={row.policy}>
                <Th scope="row" className="py-3 align-top font-normal">
                  <span className="block font-mono text-[0.8125rem] font-medium text-gray-900 dark:text-gray-50">{row.policy}</span>
                  {row.description ? <span className="block text-xs text-gray-600 dark:text-gray-400">{row.description}</span> : null}
                </Th>
                {row.access.map((a, i) => (
                  <Td key={roleColumns[i]} className="align-top">
                    <AccessCell access={a} />
                  </Td>
                ))}
              </Tr>
            ))}
          </TBody>
        </Table>
      </Card>

      <div className="grid gap-5 lg:grid-cols-2">
        <Card aria-labelledby="roles-heading">
          <CardHeader title={<span id="roles-heading">Roles and scoped rights</span>} />
          <CardBody className="flex flex-col gap-3 text-sm text-gray-700 dark:text-gray-300">
            <p>
              Sign-in roles: <span className="font-medium text-gray-900 dark:text-gray-50">{roleNotes.identityRoles.join(', ')}</span>.{' '}
              {roleNotes.noLogin}
            </p>
            <ul className="flex flex-col gap-2">
              {roleNotes.scopedRights.map((r) => (
                <li key={r.name}>
                  <span className="font-mono text-[0.8125rem] font-medium text-gray-900 dark:text-gray-50">{r.name}</span>
                  <span className="text-gray-600 dark:text-gray-400"> · {r.text}</span>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
        <Card aria-labelledby="rules-heading">
          <CardHeader title={<span id="rules-heading">Rules</span>} />
          <CardBody>
            <ul className="flex list-disc flex-col gap-2 pl-5 text-sm text-gray-700 dark:text-gray-300">
              {permissionRules.map((r) => (
                <li key={r}>{r}</li>
              ))}
            </ul>
          </CardBody>
        </Card>
      </div>
    </>
  )
}
