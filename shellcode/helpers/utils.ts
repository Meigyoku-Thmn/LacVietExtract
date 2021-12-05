const keeper = [];
/**
 * Keep data from being collected by garbage collector.
 */
export function keep<T extends unknown[]>(...data: T): T {
   data.forEach(datum => keeper.push(datum));
   return data;
}

export function sendCommand(command: string): { message: unknown, data: ArrayBuffer } {
   let message: unknown;
   let data: ArrayBuffer;
   send({ command });
   recv(command, (_payload, _data) => { message = _payload.message; data = _data; }).wait();
   return { message, data };
}

export function printBackTrace(context: CpuContext, mode: Backtracer, callback?: (e: DebugSymbol) => void) {
   callback ??= e => console.log(`${e.address}\t${e.moduleName}!${e.name}`);
   Thread.backtrace(context, mode)
      .map(DebugSymbol.fromAddress)
      .forEach(callback);
}