import { useQuery } from '@tanstack/react-query';

/**
 * The server's environment name ("Development", "Demo"…), served only where the dev sign-in is enabled.
 * Null when unavailable (production has no /dev endpoints).
 */
export function useEnvironment() {
  return useQuery({
    queryKey: ['environment'],
    queryFn: async () => {
      const response = await fetch(new URL('/dev/environment', window.location.origin));
      return response.ok ? ((await response.json()) as { name: string }).name : null;
    },
    staleTime: Infinity,
  });
}

/** The tenant the demo seeds on an empty database (server: DemoSeeder.TenantName). */
export const demoTenantName = 'Demo Trading Ltd';
