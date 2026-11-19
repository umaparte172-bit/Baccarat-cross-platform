/** Keep a running desktop job awake without preventing the display from sleeping. */
export interface PowerBlocker {
  start(type: 'prevent-app-suspension'): number;
  stop(id: number): void;
}

export class SessionAwake {
  private id: number | undefined;
  constructor(private readonly blocker: PowerBlocker) {}

  update(running: boolean): void {
    if (running && this.id === undefined) this.id = this.blocker.start('prevent-app-suspension');
    if (!running && this.id !== undefined) {
      this.blocker.stop(this.id);
      this.id = undefined;
    }
  }
}
