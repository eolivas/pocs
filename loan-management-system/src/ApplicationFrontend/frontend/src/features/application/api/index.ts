import { useQuery } from '@tanstack/react-query';
import { http } from '../../../lib';
import type { ApplicationStatusDto } from '../types';

/** Reads the loan application status from the Application BFF composition endpoint. */
export function useApplicationStatus() {
  return useQuery<ApplicationStatusDto>({
    queryKey: ['application', 'status'],
    queryFn: async () => {
      const response = await http.get<ApplicationStatusDto>('/application/status');
      return response.data;
    },
  });
}
