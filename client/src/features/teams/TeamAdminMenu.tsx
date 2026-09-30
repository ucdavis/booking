import { ChevronDownIcon } from '@heroicons/react/24/outline';
import { useQuery } from '@tanstack/react-query';
import { Link, useMatch } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';
import {
  myTeamsQueryOptions,
  teamAccessQueryOptions,
} from '@/queries/teams.ts';

export function TeamAdminMenu({
  isSiteAdmin,
  userId,
}: {
  isSiteAdmin: boolean;
  userId: string;
}) {
  const teamsQuery = useQuery(myTeamsQueryOptions(userId));
  const teamMatch = useMatch({
    from: '/(authenticated)/teams/$teamSlug',
    shouldThrow: false,
  });
  const teamAccessQuery = useQuery({
    ...teamAccessQueryOptions(teamMatch?.params.teamSlug ?? ''),
    enabled: false,
  });
  const [isOpen, setIsOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!isOpen) {
      return;
    }

    const closeOnOutsideClick = (event: PointerEvent) => {
      if (!menuRef.current?.contains(event.target as Node | null)) {
        setIsOpen(false);
      }
    };
    document.addEventListener('pointerdown', closeOnOutsideClick);
    return () =>
      document.removeEventListener('pointerdown', closeOnOutsideClick);
  }, [isOpen]);

  if (!teamsQuery.isSuccess || teamsQuery.data.length === 0) {
    return null;
  }

  const teams = teamsQuery.data;
  const currentTeam =
    teamMatch?.status === 'success' && teamAccessQuery.isSuccess
      ? teamAccessQuery.data.team
      : null;

  if (teams.length === 1) {
    return (
      <Link
        activeProps={{ className: 'font-semibold text-primary' }}
        className="text-base-content/70 transition-colors hover:text-primary"
        params={{ teamSlug: teams[0].slug }}
        to="/teams/$teamSlug"
      >
        Team Admin
      </Link>
    );
  }

  return (
    <div
      className="relative"
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          setIsOpen(false);
        }
      }}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          setIsOpen(false);
          buttonRef.current?.focus();
        }
      }}
      ref={menuRef}
    >
      <button
        aria-controls="team-admin-navigation"
        aria-expanded={isOpen}
        className={`flex items-center gap-1.5 transition-colors hover:text-primary ${currentTeam ? 'font-semibold text-primary' : 'text-base-content/70'}`}
        onClick={() => setIsOpen((open) => !open)}
        ref={buttonRef}
        type="button"
      >
        <span className="max-w-48 truncate sm:max-w-64" title={currentTeam?.name}>
          {currentTeam?.name ?? 'Choose team'}
        </span>
        <ChevronDownIcon aria-hidden="true" className="h-4 w-4 shrink-0" />
      </button>
      {isOpen && (
        <ul
          className="menu absolute right-0 z-50 mt-3 w-64 max-w-[calc(100vw-2rem)] rounded-xl border border-base-300 bg-base-100 p-2 shadow-lg"
          id="team-admin-navigation"
        >
          {teams.map((team) => (
            <li key={team.id}>
              <Link
                activeProps={{ className: 'font-semibold text-primary' }}
                className="break-words"
                onClick={() => setIsOpen(false)}
                params={{ teamSlug: team.slug }}
                to="/teams/$teamSlug"
              >
                {team.name}
              </Link>
            </li>
          ))}
          {isSiteAdmin && (
            <li className="mt-2 border-t border-base-300 pt-2">
              <Link onClick={() => setIsOpen(false)} to="/admin/teams">
                All teams
              </Link>
            </li>
          )}
        </ul>
      )}
    </div>
  );
}
