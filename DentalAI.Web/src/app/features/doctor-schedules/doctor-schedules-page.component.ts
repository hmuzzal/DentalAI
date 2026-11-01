import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzEmptyModule } from 'ng-zorro-antd/empty';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { Doctor } from '../../core/models/doctor.models';
import { DoctorAvailability } from '../../core/models/scheduling.models';
import { DoctorsService } from '../../core/services/doctors.service';
import { SchedulingService } from '../../core/services/scheduling.service';

@Component({
  selector: 'app-doctor-schedules-page',
  standalone: true,
  imports: [FormsModule, NzButtonModule, NzCardModule, NzEmptyModule, NzSelectModule, PageHeaderComponent],
  template: `
    <section class="da-page">
      <app-page-header title="Doctor schedules" subtitle="Weekly working hours, breaks and leave. Times are in the clinic's time zone.">
        <nz-select class="doctor-picker" nzPlaceHolder="Select a doctor" nzShowSearch [ngModel]="doctorId()" (ngModelChange)="selectDoctor($event)">
          @for (doctor of doctors(); track doctor.id) {
            <nz-option [nzValue]="doctor.id" [nzLabel]="doctor.firstName + ' ' + doctor.lastName" />
          }
        </nz-select>
      </app-page-header>
      @if (!doctorId()) {
        <nz-card><nz-empty nzNotFoundContent="Select a doctor to view and edit their schedule." /></nz-card>
      }
      @if (availability(); as model) {
        <nz-card nzTitle="Working hours">
          @for (period of model.schedules; track $index) {
            <div class="row"><select [(ngModel)]="period.dayOfWeek">@for(day of days;track day){<option [value]="day">{{day}}</option>}</select>
              <input type="time" [(ngModel)]="period.startTime"><span>to</span><input type="time" [(ngModel)]="period.endTime">
              <button nz-button nzDanger (click)="removeSchedule($index)">Remove</button></div>
          }
          <button nz-button (click)="addSchedule()">Add working period</button>
        </nz-card>
        <nz-card nzTitle="Break times">
          @for (period of model.breaks; track $index) {
            <div class="row"><select [(ngModel)]="period.dayOfWeek">@for(day of days;track day){<option [value]="day">{{day}}</option>}</select>
              <input type="time" [(ngModel)]="period.startTime"><span>to</span><input type="time" [(ngModel)]="period.endTime">
              <button nz-button nzDanger (click)="removeBreak($index)">Remove</button></div>
          }
          <button nz-button (click)="addBreak()">Add break</button>
        </nz-card>
        <nz-card nzTitle="Leave and holidays">
          @for (leave of model.leaves; track $index) {
            <div class="row"><input type="date" [(ngModel)]="leave.startDate"><span>to</span><input type="date" [(ngModel)]="leave.endDate">
              <input class="reason" placeholder="Reason" [(ngModel)]="leave.reason">
              <button nz-button nzDanger (click)="removeLeave($index)">Remove</button></div>
          }
          <button nz-button (click)="addLeave()">Add leave</button>
        </nz-card>
        <button nz-button nzType="primary" [nzLoading]="saving()" (click)="save()">Save availability</button>
      }
    </section>
  `,
  styles: [`
    .doctor-picker { min-width: 240px; }
    .row { display: flex; align-items: center; gap: var(--da-space-2); margin-bottom: var(--da-space-3); flex-wrap: wrap; }
    input, select { border: 1px solid var(--da-border); border-radius: var(--da-radius-sm); padding: 6px 10px; background: var(--da-surface); color: var(--da-ink); font: inherit; }
    input:focus, select:focus { outline: none; border-color: var(--da-brand); box-shadow: 0 0 0 2px var(--ant-primary-color-outline); }
    .reason { min-width: 260px; }
    @media (max-width: 640px) { .doctor-picker { width: 100%; } .row > * { flex: 1; min-width: 120px; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DoctorSchedulesPageComponent {
  private readonly scheduling = inject(SchedulingService);
  private readonly messages = inject(NzMessageService);
  readonly doctors = signal<Doctor[]>([]);
  readonly doctorId = signal('');
  readonly availability = signal<DoctorAvailability | null>(null);
  readonly saving = signal(false);
  readonly days = ['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];

  constructor(doctorsService: DoctorsService) { doctorsService.getAll().subscribe(x => this.doctors.set(x)); }
  selectDoctor(id: string): void { this.doctorId.set(id); this.scheduling.getAvailability(id).subscribe(x => this.availability.set(x)); }
  addSchedule(): void { this.update(x => x.schedules.push({ dayOfWeek:'Monday', startTime:'09:00', endTime:'17:00', isWorking:true })); }
  addBreak(): void { this.update(x => x.breaks.push({ dayOfWeek:'Monday', startTime:'13:00', endTime:'14:00' })); }
  addLeave(): void { const today=new Date().toISOString().slice(0,10); this.update(x => x.leaves.push({ startDate:today,endDate:today,reason:'' })); }
  removeSchedule(i:number):void{this.update(x=>x.schedules.splice(i,1));}
  removeBreak(i:number):void{this.update(x=>x.breaks.splice(i,1));}
  removeLeave(i:number):void{this.update(x=>x.leaves.splice(i,1));}
  save():void{const value=this.availability();if(!value)return;this.saving.set(true);this.scheduling.saveAvailability(value.doctorId,{schedules:value.schedules,breaks:value.breaks,leaves:value.leaves}).subscribe({
    next:x=>{this.availability.set(x);this.saving.set(false);this.messages.success('Availability saved');},
    error:()=>this.saving.set(false)});}
  private update(change:(x:DoctorAvailability)=>void):void{const value=this.availability();if(!value)return;change(value);this.availability.set({...value});}
}
