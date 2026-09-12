/** Borrower dashboard payload composed by the BFF from Loans, Contracts, and Customer Portal. */
export interface PortalDashboardDto {
  service: string;
  status: string;
  composesFrom: string[];
}
