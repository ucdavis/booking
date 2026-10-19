import {
  MapPinIcon,
  Squares2X2Icon,
  UserCircleIcon,
} from '@heroicons/react/24/outline';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useState } from 'react';

export const Route = createFileRoute('/temp/building')({
  component: TempBuildingPage,
});

const buildingImage = {
  alt: 'Tree-patterned glass facade of the Resnick Center at UC Davis',
  objectPosition: 'center center',
  src: 'https://agriculture.ucdavis.edu/sites/g/files/dgvnsk1721/files/styles/sf_landscape_16x9/public/media/images/Resnick%20Center%20exterior_002.jpg?h=c673cd1c&itok=TGdHGeag',
};

const buildingDescription =
  'A welcoming campus space for meetings, collaboration, and events.';

const sampleRooms = [
  { capacity: 20, floor: 1, name: 'Room 1155', type: 'Conference' },
  { capacity: 10, floor: 1, name: 'Room 1210', type: 'Meeting' },
  { capacity: 30, floor: 2, name: 'Room 2210', type: 'Classroom' },
  { capacity: 8, floor: 2, name: 'Room 2240', type: 'Meeting' },
  { capacity: 6, floor: 3, name: 'Room 3110', type: 'Collaboration' },
] as const;

function TempBuildingPage() {
  const [search, setSearch] = useState('');
  const [minimumCapacity, setMinimumCapacity] = useState(0);
  const [roomType, setRoomType] = useState('all');
  const [floor, setFloor] = useState('all');

  const visibleRooms = sampleRooms.filter((room) => {
    const matchesSearch = `${room.name} ${room.type}`
      .toLowerCase()
      .includes(search.trim().toLowerCase());

    return (
      matchesSearch &&
      room.capacity >= minimumCapacity &&
      (roomType === 'all' || room.type === roomType) &&
      (floor === 'all' || room.floor === Number(floor))
    );
  });

  return (
    <main className="content-container py-4 sm:py-6">
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
          <li>Buildings</li>
          <li aria-hidden="true">/</li>
          <li aria-current="page" className="text-base-content">
            Resnick Center
          </li>
        </ol>
      </nav>

      <article className="overflow-hidden rounded-sm border border-base-300 bg-base-200">
        <div className="border-b-8 border-secondary lg:grid lg:grid-cols-3">
          <div className="flex flex-col bg-primary text-primary-content lg:col-span-2">
            <div className="px-6 pb-2 pt-8 sm:px-10">
              <h1 className="text-xl leading-snug font-semibold sm:text-3xl">
                Resnick Agricultural Innovation Research Center
              </h1>
            </div>

            <div className="flex flex-1 flex-col px-6 pb-6 pt-2 sm:px-10">
              <p className="max-w-prose text-lg leading-relaxed text-primary-content/90">
                {buildingDescription}
              </p>

              <dl className="mt-8 lg:mt-auto">
                <div className="grid grid-cols-[1.5rem_minmax(0,1fr)] gap-x-4 gap-y-1 py-4 sm:grid-cols-[1.5rem_10rem_minmax(0,1fr)]">
                  <MapPinIcon
                    aria-hidden="true"
                    className="h-6 w-6 text-primary-content/70"
                  />
                  <dt className="text-sm font-bold tracking-wide text-primary-content/70 uppercase">
                    Location
                  </dt>
                  <dd className="col-start-2 text-base sm:col-start-3">
                    <a
                      className="text-primary-content/80 underline underline-offset-2 hover:text-primary-content"
                      href="https://www.google.com/maps/search/?api=1&query=123+Campus+Way%2C+Davis%2C+CA+95616"
                      rel="noopener noreferrer"
                      target="_blank"
                    >
                      123 Campus Way, Davis, CA 95616
                    </a>
                  </dd>
                </div>
                <div className="grid grid-cols-[1.5rem_minmax(0,1fr)] gap-x-4 gap-y-1 py-4 sm:grid-cols-[1.5rem_10rem_minmax(0,1fr)]">
                  <UserCircleIcon
                    aria-hidden="true"
                    className="h-6 w-6 text-primary-content/70"
                  />
                  <dt className="text-sm font-bold tracking-wide text-primary-content/70 uppercase">
                    Building contact
                  </dt>
                  <dd className="col-start-2 text-base sm:col-start-3">
                    Stephanie Machado
                    <a
                      className="mt-1 block break-all text-sm text-primary-content/80 underline underline-offset-2 hover:text-primary-content"
                      href="mailto:smachado@ucdavis.edu"
                    >
                      smachado@ucdavis.edu
                    </a>
                  </dd>
                </div>
                <div className="grid grid-cols-[1.5rem_minmax(0,1fr)] gap-x-4 gap-y-1 pt-4 sm:grid-cols-[1.5rem_10rem_minmax(0,1fr)]">
                  <Squares2X2Icon
                    aria-hidden="true"
                    className="h-6 w-6 text-primary-content/70"
                  />
                  <dt className="text-sm font-bold tracking-wide text-primary-content/70 uppercase">
                    Rooms
                  </dt>
                  <dd className="col-start-2 text-base sm:col-start-3">55</dd>
                </div>
              </dl>
            </div>
          </div>

          <figure className="relative h-72 overflow-hidden sm:h-80 lg:col-span-1 lg:h-auto lg:aspect-[4/5]">
            <img
              alt={buildingImage.alt}
              className="absolute inset-0 h-full w-full object-cover"
              src={buildingImage.src}
              style={{ objectPosition: buildingImage.objectPosition }}
            />
            <figcaption className="absolute right-3 bottom-3 rounded bg-primary/80 px-2 py-1 text-xs text-primary-content">
              Photo: TJ Ushing / UC Davis
            </figcaption>
          </figure>
        </div>

        <section
          aria-labelledby="room-search-heading"
          className="px-6 py-8 sm:px-10 sm:py-10"
        >
          <div className="flex flex-wrap items-end justify-between gap-3">
            <h2
              className="text-xl font-semibold text-primary"
              id="room-search-heading"
            >
              Room Search
            </h2>
            <p aria-live="polite" className="text-sm text-base-content/70">
              Showing {visibleRooms.length} sample rooms
            </p>
          </div>
          <div className="mt-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            <label className="lg:col-span-2">
              <span className="sr-only">Search rooms</span>
              <input
                className="input w-full"
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search by room name or type"
                type="search"
                value={search}
              />
            </label>
            <label>
              <span className="sr-only">Minimum capacity</span>
              <select
                className="select w-full"
                onChange={(event) =>
                  setMinimumCapacity(Number(event.target.value))
                }
                value={minimumCapacity}
              >
                <option value="0">Any capacity</option>
                <option value="10">10+ people</option>
                <option value="20">20+ people</option>
                <option value="30">30+ people</option>
              </select>
            </label>
            <label>
              <span className="sr-only">Room type</span>
              <select
                className="select w-full"
                onChange={(event) => setRoomType(event.target.value)}
                value={roomType}
              >
                <option value="all">All room types</option>
                <option value="Conference">Conference</option>
                <option value="Meeting">Meeting</option>
                <option value="Classroom">Classroom</option>
                <option value="Collaboration">Collaboration</option>
              </select>
            </label>
            <label>
              <span className="sr-only">Floor</span>
              <select
                className="select w-full"
                onChange={(event) => setFloor(event.target.value)}
                value={floor}
              >
                <option value="all">All floors</option>
                <option value="1">Floor 1</option>
                <option value="2">Floor 2</option>
                <option value="3">Floor 3</option>
              </select>
            </label>
          </div>

          <ul className="mt-6 space-y-3">
            {visibleRooms.map((room) => (
              <li key={room.name}>
                <Link
                  className="flex flex-wrap items-center justify-between gap-3 rounded-sm border border-base-300 bg-base-100 px-5 py-4 transition-colors hover:border-primary"
                  to="/temp/room"
                >
                  <div>
                    <h3 className="font-semibold text-primary">{room.name}</h3>
                    <p className="mt-1 text-sm text-base-content/70">
                      {room.type} · Floor {room.floor}
                    </p>
                  </div>
                  <div className="flex items-center gap-5 text-sm">
                    <span className="text-base-content/70">
                      Seats {room.capacity}
                    </span>
                    <span className="font-semibold text-primary">
                      View room →
                    </span>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
          {visibleRooms.length === 0 && (
            <p className="mt-6 rounded-sm border border-base-300 bg-base-100 px-5 py-8 text-center text-base-content/70">
              No sample rooms match those filters.
            </p>
          )}
        </section>
      </article>
    </main>
  );
}
