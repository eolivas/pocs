/** Loan application status payload composed by the Application BFF from Origination, Credit Engine, and Contracts. */
export interface ApplicationStatusDto {
  bff: string;
  status: string;
  composesFrom: string[];
}
