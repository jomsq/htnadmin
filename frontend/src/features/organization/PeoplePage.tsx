import { useState } from 'react'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Avatar } from '@/components/ui/Avatar'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { PageHeader } from '@/components/ui/PageHeader'
import { Pagination } from '@/components/ui/Pagination'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { PeopleIcon, SearchIcon } from '@/components/ui/icons'
import { cn } from '@/lib/cn'
import { initials } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useLevelCounts, usePeopleQuery } from './api'
import { AccountStatusBadge, FinanceRoleBadges, LevelBadge } from './components/badges'
import { CreateAccountDialog } from './components/CreateAccountDialog'
import { ImportCsvDialog } from './components/ImportCsvDialog'
import { OrganizationTabs } from './components/OrganizationTabs'
import { PersonDrawer } from './components/PersonDrawer'
import { fullName } from './format'
import type { Level, Person } from './schemas'

const PAGE_SIZE = 25

const filters: ReadonlyArray<{ label: string; level?: Level }> = [
  { label: 'All' },
  { label: 'Board', level: 'Board' },
  { label: 'Goons', level: 'Goon' },
  { label: 'Apprentices', level: 'Apprentice' },
  { label: 'Members', level: 'Member' },
]

export function PeoplePage() {
  const canManage = useCan(Policies.PeopleManage)
  const [level, setLevel] = useState<Level | undefined>(undefined)
  const [searchInput, setSearchInput] = useState('')
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)
  const [importing, setImporting] = useState(false)
  const counts = useLevelCounts()
  const [selected, setSelected] = useState<Person | null>(null)
  const search = useDebouncedValue(searchInput.trim(), 300)
  const [lastSearch, setLastSearch] = useState(search)

  // New search text starts again at page 1 (adjusting state during render, no effect needed).
  if (search !== lastSearch) {
    setLastSearch(search)
    setPage(1)
  }

  const query = usePeopleQuery({ level, search: search || undefined, page, pageSize: PAGE_SIZE })
  const data = query.data
  const filtered = !!level || !!search
  const createButton = canManage ? <Button onClick={() => setCreating(true)}>+ Create account</Button> : null
  const headerActions = canManage ? (
    <>
      <Button variant="secondary" onClick={() => setImporting(true)}>
        Import CSV
      </Button>
      {createButton}
    </>
  ) : null

  return (
    <>
      <PageHeader
        title="Organization"
        description="Board, goons, apprentices and members. Promote people along the path Member → Apprentice → Goon."
        actions={headerActions}
      />
      <OrganizationTabs peopleCount={counts.All} />

      <Card aria-label="People">
        <div className="flex flex-wrap items-center gap-2 border-b border-gray-200 px-5 py-4 dark:border-white/10">
          <div role="group" aria-label="Filter by level" className="flex flex-wrap gap-2">
            {filters.map((f) => {
              const count = counts[f.level ?? 'All']
              return (
                <FilterChip
                  key={f.label}
                  selected={level === f.level}
                  onClick={() => {
                    setLevel(f.level)
                    setPage(1)
                  }}
                >
                  {f.label}
                  {count !== undefined ? (
                    <span className="text-gray-600 tabular-nums dark:text-gray-400">
                      <span className="sr-only">,</span> {count}
                    </span>
                  ) : null}
                </FilterChip>
              )
            })}
          </div>
          <label className="flex h-9 w-full min-w-[220px] items-center gap-2 rounded-md bg-white px-3 text-sm text-gray-600 ring-1 ring-gray-300 ring-inset focus-within:outline-2 focus-within:outline-brand-600 sm:ml-auto sm:w-auto dark:bg-gray-900 dark:text-gray-400 dark:ring-white/15">
            <span className="sr-only">Search people</span>
            <SearchIcon size={16} />
            <input
              type="search"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="Name or email"
              className="min-w-0 flex-1 bg-transparent text-gray-900 outline-none placeholder:text-gray-500 dark:text-gray-50"
            />
          </label>
        </div>

        {query.isPending ? (
          <LoadingRows />
        ) : query.isError || !data ? (
          <div className="p-5">
            <Alert
              tone="red"
              live="alert"
              action={
                <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
                  Try again
                </Button>
              }
            >
              Could not load people. {errorMessage(query.error)}
            </Alert>
          </div>
        ) : data.items.length === 0 ? (
          <EmptyState
            icon={<PeopleIcon size={22} />}
            title={filtered ? 'No people match' : 'No people yet'}
            description={filtered ? 'Try another name, email or level.' : 'Add board members, goons, apprentices and members to the organization.'}
            action={filtered ? null : createButton}
          />
        ) : (
          <>
            <Table aria-busy={query.isFetching || undefined}>
              <THead>
                <tr>
                  <Th>Name</Th>
                  <Th>Level</Th>
                  <Th>Position</Th>
                  <Th>Account</Th>
                </tr>
              </THead>
              <TBody>
                {data.items.map((p) => (
                  <Tr
                    key={p.id}
                    onClick={() => setSelected(p)}
                    className={cn('cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5', selected?.id === p.id && 'bg-gray-50 dark:bg-white/5')}
                  >
                    <Td>
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation()
                          setSelected(p)
                        }}
                        className="flex min-h-10 items-center gap-3 text-left"
                      >
                        <Avatar initials={initials(p.firstName, p.lastName)} />
                        <span className="flex flex-col">
                          <span className="font-medium text-gray-900 dark:text-gray-50">{fullName(p)}</span>
                          <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">{p.email}</span>
                        </span>
                      </button>
                    </Td>
                    <Td>
                      <span className="flex flex-wrap gap-1.5">
                        <LevelBadge level={p.level} />
                        <FinanceRoleBadges isCfo={p.isCfo} isFinanceAssistant={p.isFinanceAssistant} />
                      </span>
                    </Td>
                    <Td>{p.position || '—'}</Td>
                    <Td>
                      <AccountStatusBadge status={p.accountStatus} />
                    </Td>
                  </Tr>
                ))}
              </TBody>
            </Table>
            <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} />
          </>
        )}
      </Card>

      {canManage ? <CreateAccountDialog open={creating} onClose={() => setCreating(false)} /> : null}
      {canManage && importing ? <ImportCsvDialog onClose={() => setImporting(false)} /> : null}
      <PersonDrawer person={selected} onClose={() => setSelected(null)} />
    </>
  )
}

function LoadingRows() {
  return (
    <div role="status" aria-label="Loading people" className="flex flex-col">
      {Array.from({ length: 6 }, (_, i) => (
        <div key={i} className="flex items-center gap-3 border-t border-gray-100 px-5 py-3 first:border-t-0 dark:border-white/5">
          <Skeleton className="size-8 rounded-full" />
          <div className="flex flex-1 flex-col gap-1.5">
            <Skeleton className="h-3.5 w-40" />
            <Skeleton className="h-3 w-56" />
          </div>
          <Skeleton className="h-5 w-16" />
        </div>
      ))}
    </div>
  )
}
