import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender, ChangeDetectionStrategy, Component, computed, ElementRef, inject, Injectable, Injector, signal, viewChild
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { finalize } from 'rxjs';
import { PortalSessionService } from '../../core/services/portal-session.service';
import { PortalApiService } from '../../core/services/portal.service';
import { PortalContext } from './portal-context';

interface ChatLine {
  from: 'patient' | 'assistant' | 'error';
  text: string;
  /** The assistant changed a visit: offer a link to the visit list. */
  changed?: boolean;
}

/** The chat survives moving between portal pages; it belongs to one patient and ends when they sign out. */
@Injectable({ providedIn: 'root' })
class AssistantChatState {
  readonly patientId = signal<string | null>(null);
  readonly conversationId = signal<string | null>(null);
  readonly lines = signal<ChatLine[]>([]);

  startFor(patientId: string | null): void {
    if (this.patientId() === patientId) return;
    this.patientId.set(patientId);
    this.conversationId.set(null);
    this.lines.set([]);
  }
}

/** /p/:slug/assistant — chat with the clinic's AI receptionist: questions, open times, booking and changes. */
@Component({
  selector: 'app-portal-assistant',
  standalone: true,
  imports: [FormsModule, RouterLink, NzButtonModule, NzIconModule, NzInputModule],
  template: `
    <div class="head">
      <h1>Ask us</h1>
      <p class="muted">Our virtual receptionist can find a time, book, move or cancel your visits, and answer questions.
        It can't give medical advice. In an emergency, call the clinic or emergency services.</p>
    </div>

    <section class="chat" aria-live="polite" #scroller>
      @if (!state.lines().length) {
        <div class="line line--assistant">
          <p>Hi {{ session.patient()?.firstName }}! How can I help? For example:</p>
          <div class="ideas">
            @for (idea of ideas; track idea) {
              <button nz-button nzSize="small" (click)="send(idea)" [disabled]="sending()">{{ idea }}</button>
            }
          </div>
        </div>
      }
      @for (line of state.lines(); track $index) {
        <div [class]="'line line--' + line.from">
          <p>{{ line.text }}</p>
          @if (line.changed) { <a [routerLink]="context.link()">See my visits</a> }
        </div>
      }
      @if (sending()) {
        <div class="line line--assistant typing" aria-label="The assistant is typing"><span></span><span></span><span></span></div>
      }
    </section>

    <form class="composer" (ngSubmit)="send(draft())">
      <textarea nz-input [ngModel]="draft()" (ngModelChange)="draft.set($event)" name="message" rows="2"
                [maxlength]="maxLength()" placeholder="Type your message" aria-label="Your message"
                (keydown.enter)="onEnter($any($event))"></textarea>
      <button nz-button nzType="primary" type="submit" [disabled]="!canSend()" [nzLoading]="sending()" aria-label="Send">
        <span nz-icon nzType="send"></span>
      </button>
    </form>
    @if (state.lines().length) {
      <button nz-button nzType="link" class="restart" (click)="restart()" [disabled]="sending()">Start a new chat</button>
    }
  `,
  styles: [`
    .head { margin-bottom: var(--da-space-3); }
    h1 { font-size: 24px; margin: 0 0 var(--da-space-1); }
    .muted { color: var(--da-ink-3); margin: 0; font-size: 13px; }
    .chat { display: flex; flex-direction: column; gap: var(--da-space-2); min-height: 280px; max-height: 60vh; overflow-y: auto;
      padding: var(--da-space-3); background: var(--da-surface); border: 1px solid var(--da-border); border-radius: var(--da-radius); }
    .line { max-width: 85%; padding: var(--da-space-2) var(--da-space-3); border-radius: 14px; white-space: pre-wrap; word-break: break-word; }
    .line p { margin: 0; }
    .line a { display: inline-block; margin-top: var(--da-space-1); font-weight: 500; }
    .line--assistant { align-self: flex-start; background: var(--da-surface-2); border-bottom-left-radius: 4px; }
    .line--patient { align-self: flex-end; background: var(--da-brand-soft); color: var(--da-brand-deep); border-bottom-right-radius: 4px; }
    .line--error { align-self: flex-start; background: #fff4e5; color: #8a4b00; border-bottom-left-radius: 4px; }
    .ideas { display: flex; flex-wrap: wrap; gap: var(--da-space-1); margin-top: var(--da-space-2); }
    .typing { display: inline-flex; gap: 4px; padding: 12px 14px; }
    .typing span { width: 6px; height: 6px; border-radius: 50%; background: var(--da-ink-3); animation: blink 1.2s infinite ease-in-out; }
    .typing span:nth-child(2) { animation-delay: .2s; }
    .typing span:nth-child(3) { animation-delay: .4s; }
    @keyframes blink { 0%, 80%, 100% { opacity: .25; } 40% { opacity: 1; } }
    .composer { display: flex; gap: var(--da-space-2); align-items: flex-end; margin-top: var(--da-space-3); }
    .composer textarea { resize: none; }
    .restart { padding-left: 0; margin-top: var(--da-space-1); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PortalAssistantComponent {
  readonly context = inject(PortalContext);
  readonly session = inject(PortalSessionService);
  readonly state = inject(AssistantChatState);
  private readonly api = inject(PortalApiService);
  private readonly injector = inject(Injector);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  readonly maxLength = this.context.assistantMaxLength;
  readonly ideas = ['When is my next visit?', 'Book a cleaning next week', 'I need to change my appointment'];
  readonly draft = signal('');
  readonly sending = signal(false);
  readonly canSend = computed(() => !this.sending() && this.draft().trim().length > 0);

  constructor() {
    this.state.startFor(this.session.patient()?.id ?? null);
    this.scrollToEnd();
  }

  onEnter(event: KeyboardEvent): void {
    if (event.shiftKey) return; // Shift+Enter: new line
    event.preventDefault();
    this.send(this.draft());
  }

  send(text: string): void {
    const message = text.trim().slice(0, this.maxLength());
    if (!message || this.sending()) return;

    this.add({ from: 'patient', text: message });
    this.draft.set('');
    this.sending.set(true);
    this.api.sendToAssistant(this.state.conversationId(), message)
      .pipe(finalize(() => {
        this.sending.set(false);
        this.scrollToEnd();
      }))
      .subscribe({
        next: (reply) => {
          this.state.conversationId.set(reply.conversationId);
          this.add({ from: 'assistant', text: reply.reply, changed: reply.appointmentsChanged });
        },
        error: (error: HttpErrorResponse) => this.add({
          from: 'error',
          text: error.status === 429
            ? 'You are sending messages quickly. Please wait a minute and try again.'
            : error.error?.message || 'The assistant is not available right now. Please try again, or call the clinic.'
        })
      });
  }

  restart(): void {
    this.state.conversationId.set(null);
    this.state.lines.set([]);
  }

  private add(line: ChatLine): void {
    this.state.lines.update((lines) => [...lines, line]);
    this.scrollToEnd();
  }

  private scrollToEnd(): void {
    afterNextRender(() => {
      const element = this.scroller()?.nativeElement;
      if (element) element.scrollTop = element.scrollHeight;
    }, { injector: this.injector });
  }
}
