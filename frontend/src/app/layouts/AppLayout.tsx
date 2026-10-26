import { useRef, useState, type ComponentType } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router'
import { useAuth } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { BrandSync } from '@/app/theme/BrandSync'
import { Avatar } from '@/components/ui/Avatar'
import { ModeSwitch } from '@/components/ui/ModeSwitch'
import { useModal } from '@/components/ui/useModal'
import {
  BagIcon,
  BuildingIcon,
  CalendarIcon,
  ChartIcon,
  CheckSquareIcon,
  CloseIcon,
  DashboardIcon,
  MenuIcon,
  PeopleIcon,
  SignOutIcon,
  SlidersIcon,
  WalletIcon,
} from '@/components/ui/icons'
import { useMyTasksQuery } from '@/features/tasks/api'
import { cn } from '@/lib/cn'
import { initials } from '@/lib/format'

type NavItem = {
  to: string
  label: string
  icon: ComponentType<{ size?: number }>
  end?: boolean
  locked?: boolean
  policy?: string
  /** Shows the open count from GET /api/tasks/mine. */
  badge?: 'myTasks'
}

/** Sidebar order and labels follow the canvas (Main.dc.html). */
const navItems: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: DashboardIcon, end: true },
  { to: '/people', label: 'Organization', icon: PeopleIcon },
  { to: '/events', label: 'Events', icon: CalendarIcon, policy: Policies.EventsView },
  { to: '/directory', label: 'Directory', icon: BuildingIcon },
  { to: '/store', label: 'Merch store', icon: BagIcon },
  { to: '/finance', label: 'Finance', icon: WalletIcon, locked: true, policy: Policies.FinanceView },
  { to: '/my-tasks', label: 'My tasks', icon: CheckSquareIcon, badge: 'myTasks' },
  { to: '/leaderboard', label: 'Leaderboard', icon: ChartIcon, policy: Policies.LeaderboardView },
  { to: '/settings', label: 'Settings', icon: SlidersIcon },
]

function Logo({ subtitle = 'Admin' }: { subtitle?: string }) {
  return (
    <div className="flex items-center gap-2.5">
      <div
        aria-hidden="true"
        className="flex size-8 items-center justify-center rounded-md bg-gray-900 text-[0.6875rem] font-bold text-white dark:bg-gray-50 dark:text-gray-900"
      >
        HTN
      </div>
      <div className="flex flex-col leading-tight">
        <span className="text-sm font-semibold text-gray-900 dark:text-gray-50">HackTheNorth.PH</span>
        <span className="text-xs text-gray-600 dark:text-gray-400">{subtitle}</span>
      </div>
    </div>
  )
}

function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  const { me, signOut } = useAuth()
  const navigate = useNavigate()
  const myTasks = useMyTasksQuery(!!me)
  const [signingOut, setSigningOut] = useState(false)

  const policies = me?.policies ?? []
  const items = navItems.filter((i) => !i.policy || policies.includes(i.policy))
  const name = me ? `${me.firstName} ${me.lastName}` : ''
  const role = me ? (me.level === 'Board' ? (me.isCfo ? 'Board · CFO' : 'Board · Admin') : (me.level ?? '')) : ''

  async function handleSignOut() {
    setSigningOut(true)
    try {
      await signOut()
    } finally {
      setSigningOut(false)
      navigate('/login', { replace: true })
    }
  }

  return (
    <div className="flex h-full flex-col gap-1 px-3 py-5">
      <div className="px-2 pt-1 pb-5">
        <Logo />
      </div>
      <ul className="flex flex-col gap-1">
        {items.map((item) => (
          <li key={item.to}>
            <NavLink
              to={item.to}
              end={item.end}
              onClick={onNavigate}
              className={({ isActive }) =>
                cn(
                  'flex h-10 items-center gap-2.5 rounded-md px-3 text-sm transition-colors',
                  isActive
                    ? 'bg-gray-100 font-semibold text-brand-700 dark:bg-white/5 dark:text-brand-300'
                    : 'font-medium text-gray-700 hover:bg-gray-50 hover:text-gray-900 dark:text-gray-300 dark:hover:bg-white/5 dark:hover:text-gray-50',
                )
              }
            >
              <item.icon />
              {item.label}
              {item.badge === 'myTasks' && myTasks.data && myTasks.data.openCount > 0 ? (
                <span className="ml-auto rounded-full bg-gray-100 px-2 py-0.5 text-xs font-semibold text-gray-700 tabular-nums dark:bg-white/10 dark:text-gray-200">
                  {myTasks.data.openCount}
                  <span className="sr-only"> open tasks</span>
                </span>
              ) : null}
              {item.locked ? (
                <svg role="img" aria-label="Board only" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="ml-auto">
                  <title>Board only</title>
                  <path d="M5 11h14v10H5zM8 11V7a4 4 0 0 1 8 0v4" />
                </svg>
              ) : null}
            </NavLink>
          </li>
        ))}
      </ul>

      <div className="mt-auto flex flex-col gap-4 pt-6">
        <div className="px-2">
          <ModeSwitch />
        </div>
        <div className="flex items-center gap-2.5 border-t border-gray-200 px-2 pt-4 dark:border-white/10">
          <Avatar initials={me ? initials(me.firstName, me.lastName) : ''} tone="blue" />
          <div className="flex min-w-0 flex-1 flex-col">
            <span className="truncate text-[0.8125rem] font-semibold text-gray-900 dark:text-gray-50">{name}</span>
            <span className="truncate text-xs text-gray-600 dark:text-gray-400">{role}</span>
          </div>
          <button
            type="button"
            onClick={handleSignOut}
            disabled={signingOut}
            aria-label="Sign out"
            title="Sign out"
            className="inline-flex size-10 shrink-0 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 hover:text-gray-900 disabled:opacity-60 dark:text-gray-400 dark:hover:bg-white/5 dark:hover:text-gray-50"
          >
            <SignOutIcon />
          </button>
        </div>
      </div>
    </div>
  )
}

function MobileNav({ onClose }: { onClose: () => void }) {
  const panelRef = useRef<HTMLDivElement>(null)
  useModal(true, panelRef, onClose)
  return (
    <div className="fixed inset-0 z-40 md:hidden">
      <div aria-hidden="true" className="absolute inset-0 bg-gray-950/40" onClick={onClose} />
      <div
        ref={panelRef}
        id="mobile-nav"
        role="dialog"
        aria-modal="true"
        aria-label="Main navigation"
        className="relative flex h-full w-72 max-w-[85vw] flex-col bg-white shadow-xl dark:bg-gray-900"
      >
        <button
          type="button"
          onClick={onClose}
          aria-label="Close menu"
          className="absolute top-4 right-3 inline-flex size-11 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-white/5"
        >
          <CloseIcon />
        </button>
        <nav aria-label="Main navigation" className="h-full overflow-y-auto">
          <SidebarContent onNavigate={onClose} />
        </nav>
      </div>
    </div>
  )
}

/** App shell: sidebar on desktop, top bar with a menu button under 768px. */
export function AppLayout() {
  const [menuOpen, setMenuOpen] = useState(false)

  return (
    <div className="min-h-screen bg-gray-50 md:flex dark:bg-gray-950">
      <BrandSync />
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-white px-3 py-2 text-sm font-medium text-gray-900 focus:not-sr-only focus:fixed focus:top-2 focus:left-2"
      >
        Skip to content
      </a>

      <header className="sticky top-0 z-30 flex h-14 items-center justify-between border-b border-gray-200 bg-white px-3 md:hidden dark:border-white/10 dark:bg-gray-900">
        <Logo />
        <button
          type="button"
          onClick={() => setMenuOpen(true)}
          aria-label="Open menu"
          aria-expanded={menuOpen}
          aria-controls="mobile-nav"
          className="inline-flex size-11 items-center justify-center rounded-md text-gray-700 hover:bg-gray-100 dark:text-gray-300 dark:hover:bg-white/5"
        >
          <MenuIcon size={22} />
        </button>
      </header>
      {menuOpen ? <MobileNav onClose={() => setMenuOpen(false)} /> : null}

      <nav
        aria-label="Main navigation"
        className="sticky top-0 hidden h-screen w-[232px] shrink-0 overflow-y-auto border-r border-gray-200 bg-white md:block dark:border-white/10 dark:bg-gray-900"
      >
        <SidebarContent />
      </nav>

      <main id="main" tabIndex={-1} className="flex min-w-0 flex-1 flex-col gap-6 px-4 py-6 sm:px-6 lg:px-10 lg:py-8">
        <Outlet />
      </main>
    </div>
  )
}
