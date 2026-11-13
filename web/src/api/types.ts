import type { components } from './schema';

type S = components['schemas'];

export type BankAccount = S['BankAccountDto'];
export type MappingProfile = S['MappingProfileDto'];
export type StatementImport = S['StatementImportDto'];
export type ExpectedImport = S['ExpectedTransactionImportDto'];
export type MatchDetails = S['MatchDetails'];
export type ItemDetail = S['ItemDetail'];
export type ExceptionDetails = S['ExceptionDetails'];
export type CandidateDetail = S['CandidateDetail'];
export type Summary = S['ReconciliationSummary'];
export type BatchConfirmResult = S['BatchConfirmResult'];
export type Tenant = S['TenantDto'];
export type Run = S['RunDto'];

export type Role = 'Admin' | 'Preparer' | 'Approver' | 'Viewer';

/** The API serializes decimals as JSON numbers; the schema also allows strings. */
export const num = (value: number | string | null | undefined): number => (value == null ? 0 : Number(value));
