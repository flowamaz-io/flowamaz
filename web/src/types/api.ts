// Response envelope shapes — match ResponseWrapperMiddleware / GlobalExceptionMiddleware.

/** Every successful 2xx JSON response is wrapped in this envelope. */
export interface ApiEnvelope<T> {
  success: boolean;
  statusCode: number;
  data: T;
  correlationId: string;
}

/** Error envelope produced by GlobalExceptionMiddleware. */
export interface ApiErrorEnvelope {
  success: false;
  statusCode: number;
  code: string;
  message: string;
  correlationId: string;
}

/** Pagination metadata returned by PagedResult<T>. */
export interface PaginationMeta {
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

/** Paged collection: `{ data: T[], pagination }`. */
export interface PagedResult<T> {
  data: T[];
  pagination: PaginationMeta;
}

/** Normalised, user-facing error surfaced to views (see error.util.ts). */
export interface UserFacingError {
  message: string;
  code?: string;
  field?: string;
  helpArticle?: string;
  correlationId?: string;
}
