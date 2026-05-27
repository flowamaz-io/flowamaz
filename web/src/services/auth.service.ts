import { http, unwrap } from './api.service';
import type {
  ApiEnvelope,
  AuthResponse,
  ForgotPasswordRequest,
  LoginRequest,
  MeResponse,
  RefreshResponse,
  RegisterRequest,
  ResetPasswordRequest,
} from '@/types';

export const authService = {
  async login(payload: LoginRequest): Promise<AuthResponse> {
    const res = await http.post<ApiEnvelope<AuthResponse>>('/api/v1/auth/login', payload);
    return unwrap(res);
  },

  async register(payload: RegisterRequest): Promise<AuthResponse> {
    const res = await http.post<ApiEnvelope<AuthResponse>>('/api/v1/auth/register', payload);
    return unwrap(res);
  },

  async refresh(): Promise<RefreshResponse> {
    const res = await http.post<ApiEnvelope<RefreshResponse>>('/api/v1/auth/refresh', {});
    return unwrap(res);
  },

  async logout(): Promise<void> {
    await http.post('/api/v1/auth/logout', {});
  },

  async me(): Promise<MeResponse> {
    const res = await http.get<ApiEnvelope<MeResponse>>('/api/v1/auth/me');
    return unwrap(res);
  },

  async forgotPassword(payload: ForgotPasswordRequest): Promise<void> {
    await http.post('/api/v1/auth/forgot-password', payload);
  },

  async resetPassword(payload: ResetPasswordRequest): Promise<void> {
    await http.post('/api/v1/auth/reset-password', payload);
  },
};
