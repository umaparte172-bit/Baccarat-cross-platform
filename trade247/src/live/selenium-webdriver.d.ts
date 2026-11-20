// The parts of selenium-webdriver (4.x) that the Selenium table driver uses. The package ships no types of its own.
declare module 'selenium-webdriver' {
  export interface WebElement {
    click(): Promise<void>;
  }
  export class By {
    static css(selector: string): By;
  }
  export interface Actions {
    move(options: { origin: WebElement | 'viewport' | 'pointer'; x?: number; y?: number; duration?: number }): Actions;
    press(button?: number): Actions;
    release(button?: number): Actions;
    pause(ms: number): Actions;
    perform(): Promise<void>;
  }
  export interface TargetLocator {
    window(handle: string): Promise<void>;
    defaultContent(): Promise<void>;
    frame(target: WebElement | number | null): Promise<void>;
    parentFrame(): Promise<void>;
  }
  export interface WebDriver {
    getAllWindowHandles(): Promise<string[]>;
    getCurrentUrl(): Promise<string>;
    switchTo(): TargetLocator;
    findElements(by: By): Promise<WebElement[]>;
    executeScript<T = unknown>(script: string, ...args: unknown[]): Promise<T>;
    actions(options?: { async?: boolean; bridge?: boolean }): Actions;
    quit(): Promise<void>;
  }
  export class Builder {
    forBrowser(name: string): Builder;
    setChromeOptions(options: unknown): Builder;
    setChromeService(service: unknown): Builder;
    build(): Promise<WebDriver> & WebDriver;
  }
}

declare module 'selenium-webdriver/chrome.js' {
  export class Options {
    debuggerAddress(address: string): Options;
  }
  export class ServiceBuilder {
    constructor(executable?: string);
    addArguments(...args: string[]): ServiceBuilder;
  }
  const chrome: { Options: typeof Options; ServiceBuilder: typeof ServiceBuilder };
  export default chrome;
}
