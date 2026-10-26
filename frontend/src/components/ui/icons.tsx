import type { ReactNode, SVGProps } from 'react'

type IconProps = SVGProps<SVGSVGElement> & { size?: number }

function Icon({ size = 18, children, strokeWidth = 1.8, ...rest }: IconProps & { children: ReactNode }) {
  return (
    <svg
      aria-hidden="true"
      focusable="false"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
      {...rest}
    >
      {children}
    </svg>
  )
}

const createIcon = (d: string) =>
  function PathIcon(props: IconProps) {
    return (
      <Icon {...props}>
        <path d={d} />
      </Icon>
    )
  }

export const DashboardIcon = createIcon('M3 3h7v9H3zM14 3h7v5h-7zM14 12h7v9h-7zM3 16h7v5H3z')
export const PeopleIcon = createIcon('M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75')
export const CalendarIcon = createIcon('M3 5h18v16H3zM16 3v4M8 3v4M3 10h18')
export const BuildingIcon = createIcon('M3 21h18M5 21V7l7-4 7 4v14M9 10h.01M15 10h.01M9 14h.01M15 14h.01M10 21v-3h4v3')
export const BagIcon = createIcon('M4 8h16l-1 12H5zM8 8a4 4 0 0 1 8 0')
export const WalletIcon = createIcon('M3 6h18v12H3zM3 10h18M7 15h3')
export const CheckSquareIcon = createIcon('M9 11l3 3 8-8M20 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11')
export const ChartIcon = createIcon('M6 20V10M12 20V4M18 20v-6')
export const SlidersIcon = createIcon('M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6')
export const LockIcon = createIcon('M5 11h14v10H5zM8 11V7a4 4 0 0 1 8 0v4')
export const SunIcon = createIcon('M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4')
export const MonitorIcon = createIcon('M3 4h18v12H3zM8 20h8M12 16v4')
export const MoonIcon = createIcon('M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z')
export const SearchIcon = createIcon('M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16zM21 21l-4.3-4.3')
export const MenuIcon = createIcon('M3 6h18M3 12h18M3 18h18')
export const CloseIcon = createIcon('M18 6 6 18M6 6l12 12')
export const SignOutIcon = createIcon('M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9')
export const CheckIcon = createIcon('M20 6 9 17l-5-5')
export const AlertIcon = createIcon('M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z')
export const InfoIcon = createIcon('M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 16v-4M12 8h.01')
export const ToolIcon = createIcon('M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.8-3.8a6 6 0 0 1-7.9 7.9l-6.9 6.9a2.1 2.1 0 0 1-3-3l6.9-6.9a6 6 0 0 1 7.9-7.9z')
export const PlusIcon = createIcon('M12 5v14M5 12h14')
export const MinusIcon = createIcon('M5 12h14')
export const UploadIcon = createIcon('M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12')
export const ChevronRightIcon = createIcon('M9 18l6-6-6-6')
export const ChevronLeftIcon = createIcon('M15 18l-6-6 6-6')
export const CircleIcon = createIcon('M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z')

/** Circle with the left half filled ("own / scoped" access). */
export function HalfCircleIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 3a9 9 0 0 0 0 18z" fill="currentColor" />
    </Icon>
  )
}
