import { Link } from 'react-router'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { Table, Td, Th, THead, Tr } from '@/components/ui/Table'
import { CheckIcon, ChevronLeftIcon, CircleIcon, HalfCircleIcon, MinusIcon } from '@/components/ui/icons'
import { cn } from '@/lib/cn'
import { accessLegend, permissionGroups, roleColumns, type Access, type AccessLevel } from './permissions'

const levelStyle: Record<AccessLevel, string> = {
  full: 'font-semibold text-green-700 dark:text-green-400',
  own: 'font-semibold text-brand-700 dark:text-brand-300',
  view: 'font-medium text-gray-700 dark:text-gray-300',
  none: 'font-normal text-gray-600 dark:text-gray-400',
}

function LevelIcon({ level }: { level: AccessLevel }) {
  if (level === 'full') return <CheckIcon size={14} />
  if (level === 'own') return <HalfCircleIcon size={14} />
  if (level === 'view') return <CircleIcon size={14} />
  return <MinusIcon size={14} />
}

function AccessCell({ access }: { access: Access }) {
  return (
    <span className={cn('inline-flex items-center gap-1.5 text-[0.8125rem]', levelStyle[access.level])}>
      <LevelIcon level={access.level} />
      {access.text}
    </span>
  )
}

/** Read-only matrix of who can see and do what (canvas frame 03). The server enforces every policy. */
export function PermissionsPage() {
  return (
    <>
      <header className="flex flex-col gap-1">
        <Link
          to="/settings"
          className="inline-flex min-h-10 w-fit items-center gap-1 text-[0.8125rem] font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300"
        >
          <ChevronLeftIcon size={16} />
          Settings
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight text-gray-900 dark:text-gray-50">Roles & permissions</h1>
        <p className="text-sm text-gray-600 dark:text-gray-400">Who can see and do what. Committee lead is a per-event role given to a goon.</p>
      </header>

      <Card>
        <Table minWidth={860} aria-label="Permissions matrix">
          <THead>
            <tr>
              <Th className="w-[32%] py-3.5">Capability</Th>
              {roleColumns.map((r) => (
                <Th key={r.name} className="py-3.5 text-center">
                  <Badge color={r.color} className="text-[0.8125rem] font-semibold">
                    {r.name}
                  </Badge>
                  <span className="mt-1 block text-xs font-normal text-gray-600 dark:text-gray-400">{r.login}</span>
                </Th>
              ))}
            </tr>
          </THead>
          {permissionGroups.map((g) => (
            <tbody key={g.title}>
              <tr className="border-t border-gray-200 bg-gray-50 dark:border-white/10 dark:bg-white/5">
                <th
                  scope="rowgroup"
                  colSpan={roleColumns.length + 1}
                  className="px-5 py-3 text-left text-xs font-semibold tracking-wide text-gray-700 uppercase dark:text-gray-300"
                >
                  {g.title}
                </th>
              </tr>
              {g.rows.map((r) => (
                <Tr key={r.capability}>
                  <Th scope="row" className="py-3 text-sm font-medium text-gray-900 dark:text-gray-50">
                    {r.capability}
                  </Th>
                  {r.access.map((a, i) => (
                    <Td key={roleColumns[i].name} className="text-center">
                      <AccessCell access={a} />
                    </Td>
                  ))}
                </Tr>
              ))}
            </tbody>
          ))}
        </Table>
      </Card>

      <ul aria-label="Legend" className="flex flex-wrap gap-x-6 gap-y-2 text-[0.8125rem] text-gray-600 dark:text-gray-400">
        {accessLegend.map((l) => (
          <li key={l.level} className="inline-flex items-center gap-1.5">
            <span className={cn('inline-flex items-center gap-1.5', levelStyle[l.level], l.level !== 'none' && 'font-semibold')}>
              <LevelIcon level={l.level} />
              {l.label}
            </span>
            <span>· {l.text}</span>
          </li>
        ))}
      </ul>
    </>
  )
}
