import {
  ChevronLeftIcon,
  ChevronRightIcon,
  ClockIcon,
  Squares2X2Icon,
  UsersIcon,
} from '@heroicons/react/24/outline';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useState } from 'react';

export const Route = createFileRoute('/temp/room')({
  component: TempRoomPage,
});

const weekdays = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const availableEquipment = ['Display', 'Video conferencing', 'Whiteboard'];
const startTimes = Array.from({ length: 37 }, (_, index) => {
  const totalMinutes = 8 * 60 + index * 15;
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  const period = hours >= 12 ? 'PM' : 'AM';
  const displayHour = hours % 12 || 12;

  return `${displayHour}:${minutes.toString().padStart(2, '0')} ${period}`;
});

const monthFormatter = new Intl.DateTimeFormat('en-US', {
  month: 'long',
  year: 'numeric',
});

const dateFormatter = new Intl.DateTimeFormat('en-US', {
  day: 'numeric',
  month: 'long',
  weekday: 'long',
  year: 'numeric',
});

function TempRoomPage() {
  const today = new Date();
  today.setHours(0, 0, 0, 0);

  const [visibleMonth, setVisibleMonth] = useState(
    () => new Date(today.getFullYear(), today.getMonth(), 1)
  );
  const [selectedDates, setSelectedDates] = useState<Date[]>([]);
  const [selectedTime, setSelectedTime] = useState<string | null>(null);
  const [duration, setDuration] = useState(1);
  const [repeatType, setRepeatType] = useState('never');
  const [repeatUnit, setRepeatUnit] = useState('week');
  const [repeatInterval, setRepeatInterval] = useState(1);
  const [repeatDays, setRepeatDays] = useState<number[]>([]);
  const [repeatEnd, setRepeatEnd] = useState('never');
  const [reservationMessage, setReservationMessage] = useState('');

  const firstWeekday = new Date(
    visibleMonth.getFullYear(),
    visibleMonth.getMonth(),
    1
  ).getDay();
  const daysInMonth = new Date(
    visibleMonth.getFullYear(),
    visibleMonth.getMonth() + 1,
    0
  ).getDate();
  const calendarCellCount = Math.ceil((firstWeekday + daysInMonth) / 7) * 7;
  const isCurrentMonth =
    visibleMonth.getFullYear() === today.getFullYear() &&
    visibleMonth.getMonth() === today.getMonth();

  function changeMonth(offset: number) {
    setVisibleMonth(
      new Date(visibleMonth.getFullYear(), visibleMonth.getMonth() + offset, 1)
    );
    setSelectedTime(null);
    setReservationMessage('');
  }

  return (
    <main className="content-container py-8 sm:py-10">
      <nav
        aria-label="Breadcrumb"
        className="mb-4 text-xs text-base-content/70 uppercase"
      >
        <ol className="flex flex-wrap items-center gap-2">
          <li>
            <Link className="hover:text-primary hover:underline" to="/temp">
              Home
            </Link>
          </li>
          <li aria-hidden="true">/</li>
          <li>
            <Link
              className="hover:text-primary hover:underline"
              to="/temp/building"
            >
              Resnick Center
            </Link>
          </li>
          <li aria-hidden="true">/</li>
          <li aria-current="page" className="text-base-content">
            Room 1155
          </li>
        </ol>
      </nav>

      <article className="overflow-hidden rounded-sm border border-base-300 bg-base-200">
        <header className="border-b-8 border-secondary bg-primary px-6 py-8 text-primary-content sm:px-10 sm:py-10">
          <div className="grid gap-8 lg:grid-cols-2">
            <div>
              <p className="text-sm font-semibold tracking-wide text-primary-content/70 uppercase">
                Resnick Agricultural Innovation Research Center
              </p>
              <h1 className="mt-2 text-3xl font-semibold sm:text-4xl">
                Room 1155
              </h1>

              <section
                aria-labelledby="available-equipment-heading"
                className="mt-8"
              >
                <h2
                  className="text-sm font-bold tracking-wide text-primary-content/70 uppercase"
                  id="available-equipment-heading"
                >
                  Available equipment
                </h2>
                <ul className="mt-3 flex flex-wrap gap-2">
                  {availableEquipment.map((equipment) => (
                    <li
                      className="badge badge-outline border-primary-content/40 text-primary-content"
                      key={equipment}
                    >
                      {equipment}
                    </li>
                  ))}
                </ul>
              </section>
            </div>

            <dl className="grid grid-cols-3 self-start divide-x divide-primary-content/20 overflow-hidden rounded-sm border border-primary-content/25 bg-primary-content/5">
              <div className="flex items-start gap-2 px-3 py-4">
                <Squares2X2Icon
                  aria-hidden="true"
                  className="h-4 w-4 shrink-0 text-primary-content/70"
                />
                <div>
                  <dt className="text-sm font-bold tracking-wide text-primary-content/70 uppercase">
                    Type
                  </dt>
                  <dd className="mt-1">Conference</dd>
                </div>
              </div>
              <div className="flex items-start gap-2 px-3 py-4">
                <UsersIcon
                  aria-hidden="true"
                  className="h-4 w-4 shrink-0 text-primary-content/70"
                />
                <div>
                  <dt className="text-sm font-bold tracking-wide text-primary-content/70 uppercase">
                    Capacity
                  </dt>
                  <dd className="mt-1">20 people</dd>
                </div>
              </div>
              <div className="flex items-start gap-2 px-3 py-4">
                <ClockIcon
                  aria-hidden="true"
                  className="h-4 w-4 shrink-0 text-primary-content/70"
                />
                <div>
                  <dt className="text-sm font-bold tracking-wide text-primary-content/70 uppercase">
                    Hours
                  </dt>
                  <dd className="mt-1">8:00 AM–5:00 PM</dd>
                </div>
              </div>
            </dl>
          </div>
        </header>

        <div className="px-6 py-8 sm:px-10 sm:py-10">
          <h2 className="text-2xl font-semibold text-primary">
            Reserve this room
          </h2>
          <p className="mt-2">
            Choose one or more dates, a start time, and a duration. Availability
            shown here is sample data.
          </p>

          <div className="mt-8 grid gap-8 lg:grid-cols-3">
            <section
              aria-labelledby="calendar-heading"
              className="lg:col-span-2"
            >
              <div className="flex flex-wrap items-center justify-between gap-4">
                <h3
                  className="text-lg font-semibold text-primary"
                  id="calendar-heading"
                >
                  Select dates
                </h3>
                <div className="flex items-center gap-2">
                  <button
                    aria-label="Previous month"
                    className="btn btn-ghost btn-square btn-sm"
                    disabled={isCurrentMonth}
                    onClick={() => changeMonth(-1)}
                    type="button"
                  >
                    <ChevronLeftIcon aria-hidden="true" className="h-5 w-5" />
                  </button>
                  <span className="min-w-36 text-center font-semibold text-primary">
                    {monthFormatter.format(visibleMonth)}
                  </span>
                  <button
                    aria-label="Next month"
                    className="btn btn-ghost btn-square btn-sm"
                    onClick={() => changeMonth(1)}
                    type="button"
                  >
                    <ChevronRightIcon aria-hidden="true" className="h-5 w-5" />
                  </button>
                </div>
              </div>

              <div className="mt-5 grid grid-cols-7 gap-1 text-center sm:gap-2">
                {weekdays.map((day) => (
                  <span
                    className="py-2 text-xs font-bold tracking-wide text-base-content/60 uppercase"
                    key={day}
                  >
                    {day}
                  </span>
                ))}
                {Array.from({ length: calendarCellCount }, (_, index) => {
                  const dayNumber = index - firstWeekday + 1;

                  if (dayNumber < 1 || dayNumber > daysInMonth) {
                    return <span aria-hidden="true" key={index} />;
                  }

                  const date = new Date(
                    visibleMonth.getFullYear(),
                    visibleMonth.getMonth(),
                    dayNumber
                  );
                  const isPast = date < today;
                  const isSelected = selectedDates.some(
                    (selectedDate) => selectedDate.getTime() === date.getTime()
                  );
                  const isToday = date.getTime() === today.getTime();

                  return (
                    <button
                      aria-label={dateFormatter.format(date)}
                      aria-pressed={isSelected}
                      className={`h-11 w-full rounded-lg text-sm transition-colors sm:h-14 ${
                        isSelected
                          ? 'bg-primary font-semibold text-primary-content'
                          : isPast
                            ? 'text-base-content/30'
                            : 'text-base-content hover:bg-primary/10'
                      } ${isToday && !isSelected ? 'ring-1 ring-primary/40' : ''}`}
                      disabled={isPast}
                      key={dayNumber}
                      onClick={() => {
                        setSelectedDates((currentDates) => {
                          const isAlreadySelected = currentDates.some(
                            (selectedDate) =>
                              selectedDate.getTime() === date.getTime()
                          );

                          if (isAlreadySelected) {
                            return currentDates.filter(
                              (selectedDate) =>
                                selectedDate.getTime() !== date.getTime()
                            );
                          }

                          return [...currentDates, date].sort(
                            (firstDate, secondDate) =>
                              firstDate.getTime() - secondDate.getTime()
                          );
                        });
                        setSelectedTime(null);
                        setReservationMessage('');
                      }}
                      type="button"
                    >
                      {dayNumber}
                    </button>
                  );
                })}
              </div>
            </section>

            <section
              aria-labelledby="reservation-details-heading"
              className="rounded-sm border border-base-300 bg-base-100 p-5 sm:p-6"
            >
              <h3
                className="text-lg font-semibold text-primary"
                id="reservation-details-heading"
              >
                Reservation details
              </h3>
              <p className="mt-2 text-sm text-base-content/70">
                {selectedDates.length > 0
                  ? selectedDates.map(dateFormatter.format).join(', ')
                  : 'Select one or more dates on the calendar to begin.'}
              </p>

              <label className="mt-6 block text-sm font-semibold text-base-content">
                Start time
                <select
                  className="select mt-2 w-full"
                  disabled={selectedDates.length === 0}
                  onChange={(event) => {
                    setSelectedTime(event.target.value || null);
                    setReservationMessage('');
                  }}
                  value={selectedTime ?? ''}
                >
                  <option disabled value="">
                    Select a start time
                  </option>
                  {startTimes.map((time) => (
                    <option key={time} value={time}>
                      {time}
                    </option>
                  ))}
                </select>
              </label>

              <label className="mt-6 block text-sm font-semibold text-base-content">
                Duration
                <select
                  className="select mt-2 w-full"
                  onChange={(event) => {
                    setDuration(Number(event.target.value));
                    setReservationMessage('');
                  }}
                  value={duration}
                >
                  <option value="1">1 hour</option>
                  <option value="2">2 hours</option>
                  <option value="3">3 hours</option>
                </select>
              </label>

              <div className="mt-6">
                <label className="block text-sm font-semibold text-base-content">
                  Repeat
                  <select
                    className="select mt-2 w-full"
                    onChange={(event) => {
                      const nextRepeatType = event.target.value;
                      setRepeatType(nextRepeatType);
                      if (
                        nextRepeatType === 'custom' &&
                        repeatDays.length === 0
                      ) {
                        setRepeatDays([today.getDay()]);
                      }
                    }}
                    value={repeatType}
                  >
                    <option value="never">Never</option>
                    <option value="custom">Custom...</option>
                  </select>
                </label>

                {repeatType === 'custom' ? (
                  <div className="mt-4 space-y-5 rounded-sm border border-base-300 bg-base-200 p-4 sm:p-5">
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
                      <span className="text-sm font-semibold text-base-content">
                        Repeats every:
                      </span>
                      <label>
                        <span className="sr-only">Repeat interval</span>
                        <input
                          className="input input-sm w-16"
                          min="1"
                          onChange={(event) =>
                            setRepeatInterval(
                              Math.max(1, Number(event.target.value) || 1)
                            )
                          }
                          type="number"
                          value={repeatInterval}
                        />
                      </label>
                      <label className="text-sm font-semibold text-base-content">
                        <span className="sr-only">Repeat frequency</span>
                        <select
                          className="select select-sm"
                          onChange={(event) =>
                            setRepeatUnit(event.target.value)
                          }
                          value={repeatUnit}
                        >
                          <option value="day">day(s)</option>
                          <option value="week">week(s)</option>
                          <option value="month">month(s)</option>
                        </select>
                      </label>
                    </div>

                    {repeatUnit === 'week' ? (
                      <fieldset>
                        <legend className="text-sm font-semibold text-base-content">
                          On
                        </legend>
                        <div className="mt-2 flex flex-wrap gap-2">
                          {weekdays.map((day, dayIndex) => {
                            const isSelected = repeatDays.includes(dayIndex);

                            return (
                              <button
                                aria-label={`Repeat on ${day}`}
                                aria-pressed={isSelected}
                                className={`btn btn-circle btn-sm ${
                                  isSelected ? 'btn-primary' : 'btn-outline'
                                }`}
                                key={day}
                                onClick={() => {
                                  setRepeatDays((currentDays) =>
                                    currentDays.includes(dayIndex)
                                      ? currentDays.filter(
                                          (currentDay) =>
                                            currentDay !== dayIndex
                                        )
                                      : [...currentDays, dayIndex]
                                  );
                                }}
                                type="button"
                              >
                                {day.slice(0, 1)}
                              </button>
                            );
                          })}
                        </div>
                      </fieldset>
                    ) : null}

                    <label className="block border-t border-base-300 pt-4 text-sm font-semibold text-base-content">
                      End
                      <select
                        className="select select-sm mt-2 w-full"
                        onChange={(event) => setRepeatEnd(event.target.value)}
                        value={repeatEnd}
                      >
                        <option value="never">Never</option>
                        <option value="date">On a date</option>
                        <option value="occurrences">
                          After a number of occurrences
                        </option>
                      </select>
                    </label>

                    <p className="border-t border-base-300 pt-4 text-xs text-base-content/70">
                      Repeats every {repeatInterval} {repeatUnit}
                      {repeatUnit === 'week' && repeatDays.length > 0
                        ? ` on ${repeatDays.map((day) => weekdays[day]).join(', ')}`
                        : ''}
                      {repeatEnd === 'never' ? ', with no end date.' : '.'}
                    </p>
                  </div>
                ) : null}
              </div>

              <div className="mt-6 rounded-sm bg-base-200 p-4 text-sm">
                <p className="font-semibold text-primary">Your selection</p>
                <p className="mt-2 text-base-content/70">
                  {selectedDates.length > 0 && selectedTime
                    ? `${selectedDates.map(dateFormatter.format).join(', ')} at ${selectedTime} for ${duration} ${duration === 1 ? 'hour' : 'hours'}`
                    : 'Choose one or more dates and a start time to see your reservation.'}
                </p>
              </div>

              <button
                className="btn btn-secondary mt-6 w-full"
                disabled={selectedDates.length === 0 || !selectedTime}
                onClick={() =>
                  setReservationMessage(
                    'Demo only — this reservation was not submitted.'
                  )
                }
                type="button"
              >
                Reserve room
              </button>
              <p
                aria-live="polite"
                className="mt-3 text-sm text-base-content/70"
              >
                {reservationMessage || 'Reservations are not submitted yet.'}
              </p>
            </section>
          </div>
        </div>
      </article>
    </main>
  );
}
