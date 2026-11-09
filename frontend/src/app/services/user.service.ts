import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ActivationCode, CreatedUser, ManagedUser, PatchedUser, Perfil } from '../models/auth.model';

/** Gestão de usuários (somente Admin). */
@Injectable({ providedIn: 'root' })
export class UserService {
  private http = inject(HttpClient);
  private readonly url = '/api/users';

  list(): Observable<ManagedUser[]> {
    return this.http.get<ManagedUser[]>(this.url);
  }

  create(usuario: string, perfil: Perfil): Observable<CreatedUser> {
    return this.http.post<CreatedUser>(this.url, { usuario, perfil });
  }

  /** perfil e/ou ativo; reativar um Pendente sem senha devolve também um novo código. */
  patch(id: number, body: { perfil?: Perfil; ativo?: boolean }): Observable<PatchedUser> {
    return this.http.patch<PatchedUser>(`${this.url}/${id}`, body);
  }

  reset(id: number): Observable<ActivationCode> {
    return this.http.post<ActivationCode>(`${this.url}/${id}/reset`, null);
  }

  unlock(id: number): Observable<void> {
    return this.http.post<void>(`${this.url}/${id}/desbloquear`, null);
  }
}
