import type { ComponentType } from 'react'
import { Link, useParams } from 'react-router'
import { Card, CardBody, CardHeader, cardClass } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { CalendarIcon, ChevronRightIcon, LaptopIcon, PhoneIcon } from '@/components/ui/icons'
import { useEventQuery } from '@/features/events/api'
import { cn } from '@/lib/cn'
import { useCheckInStatsQuery } from './api'
import { CheckInStats } from './components/CheckInStats'
import { DEFAULT_STATION, STATION_MAX } from './schemas'
import { useStationName } from './station'

/** Event tab: check-in numbers, this device's station name, and the two kiosk screens. */
export function CheckInPage() {
  const { eventId = '' } = useParams()
  // EventLayout has already loaded (and cached) the event.
  const event = useEventQuery(eventId).data
  const isDraft = event?.status === 'Draft'
  const { draft, station, setStation } = useStationName()
  const stats = useCheckInStatsQuery(eventId, station, !!event && !isDraft)
  const base = `/events/${encodeURIComponent(eventId)}/check-in`

  if (isDraft) {
    return (
      <Card aria-label="Check-in">
        <EmptyState icon={<CalendarIcon size={22} />} title="Check-in is not open yet" description="Check-in opens once the event is published." />
      </Card>
    )
  }

  return (
    <div className="flex flex-col gap-5">
      <Card>
        <CardHeader title="Check-in" description="Numbers refresh every 15 seconds and after every scan." />
        <CardBody>
          <CheckInStats query={stats} />
        </CardBody>
      </Card>

      <Card>
        <CardBody>
          <Field
            label="Station name"
            hint={`Saved on this device and shown with every scan from it, e.g. ${DEFAULT_STATION} or Desk 2.`}
            className="max-w-md"
          >
            {(p) => (
              <Input
                {...p}
                controlSize="lg"
                maxLength={STATION_MAX}
                autoComplete="off"
                value={draft}
                placeholder={DEFAULT_STATION}
                onChange={(e) => setStation(e.target.value)}
              />
            )}
          </Field>
        </CardBody>
      </Card>

      <div className="grid gap-4 sm:grid-cols-2">
        <KioskLink to={`${base}/scan`} icon={PhoneIcon} title="Phone scanner" description="Scan ticket QR codes with this phone's camera, or search by name." />
        <KioskLink
          to={`${base}/desk`}
          icon={LaptopIcon}
          title="Desk station"
          description="A laptop with a handheld scanner or webcam, recent scans and station settings."
        />
      </div>
    </div>
  )
}

function KioskLink({ to, icon: Icon, title, description }: { to: string; icon: ComponentType<{ size?: number }>; title: string; description: string }) {
  return (
    <Link
      to={to}
      className={cn(cardClass, 'group flex min-h-28 items-center gap-4 p-5 hover:bg-gray-50 dark:hover:bg-gray-800/60')}
    >
      <span aria-hidden="true" className="flex size-12 flex-none items-center justify-center rounded-full bg-brand-50 text-brand-700 dark:bg-brand-300/10 dark:text-brand-300">
        <Icon size={24} />
      </span>
      <span className="flex min-w-0 flex-1 flex-col gap-1">
        <span className="text-base font-semibold text-gray-900 dark:text-gray-50">{title}</span>
        <span className="text-sm text-gray-600 dark:text-gray-400">{description}</span>
      </span>
      <ChevronRightIcon size={20} className="flex-none text-gray-500 group-hover:text-gray-700 dark:group-hover:text-gray-300" />
    </Link>
  )
}
