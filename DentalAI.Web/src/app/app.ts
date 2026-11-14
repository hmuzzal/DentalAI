import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthStateService } from './core/services/auth-state.service';
import { isPortalPath } from './core/services/portal-session.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class App {
  private readonly authState = inject(AuthStateService);

  constructor() {
    // Portal pages are for patients and never use the staff session. Checking it there would let an old staff token
    // in the same browser fail, and the "session expired" handling would send the patient to the staff sign-in.
    if (!isPortalPath(window.location.pathname)) {
      this.authState.loadCurrentUser().subscribe();
    }
  }
}
