import { useQuery } from '@tanstack/react-query';
import { http } from '../../../lib';
import type { PortalDashboardDto } from '../types';

/** Reads the borrower dashboard from the BFF composition endpoint. */
export function usePortalDashboard() {
  return useQuery<PortalDashboardDto>({
    queryKey: ['portal', 'dashboard'],
    queryFn: async () => {
      const response = await http.get<PortalDashboardDto>('/portal/dashboard');
      return response.data;
    },
  });
}
