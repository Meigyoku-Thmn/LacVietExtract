import fs from 'fs';
import * as frida from 'frida';
import { Message, MessageType, Script, ScriptMessageHandler, ScriptRuntime, Session } from 'frida';
import { spawnSync } from 'child_process';

const TargetPath = "C:/Program Files (x86)/LacViet/mtdEVA/mtd2012.exe";
// const TargetPath = "D:/Draftbook/LacVietHack/test.exe";
const ShellCodeConfigPath = './tsconfig.shellcode.json';
const ShellCodePath = './.build/shellcode.js';
// const ShellCodePath = './.build/shellcode_playground.js';

(async function main() {
   try {
      const pid = await frida.spawn(TargetPath);
      const session = await frida.attach(pid);

      session.detached.connect(() => console.log('Session detached.'));
      process.on('SIGINT', async () => {
         frida.kill(pid);
         console.log('Target process was terminated.');
      });

      let script: Script;
      try {
         script = await loadScript(session, message => onMessageReceived(script, message));
      } catch (err) {
         console.error(err);
         await frida.kill(pid);
         console.log('Target process was terminated.');
         process.exit();
      }

      await script.load();
      await frida.resume(pid);
   } catch (err) {
      console.error('Unexpected error occured:');
      console.error(err);
   }
})();

async function loadScript(session: Session, event: ScriptMessageHandler): Promise<Script> {
   const processRs = spawnSync('npx', [
      'tsc-bundle',
      `"${ShellCodeConfigPath}"`,
      `--outFile "${ShellCodePath}"`,
   ], {
      shell: true,
      stdio: ['inherit', 'inherit', 'inherit'],
      windowsVerbatimArguments: true,
   });
   if (processRs.error)
      throw processRs.error;

   const scriptContent = fs.readFileSync(ShellCodePath, 'utf8');
   const script = await session.createScript(scriptContent, {
      name: ShellCodePath.replace(/\.[^/.]+$/, ''),
      runtime: ScriptRuntime.V8,
   });
   script.message.connect(event);
   return script;
}

function onMessageReceived(script: Script, message: Message) {
   if (message.type === MessageType.Error) {
      console.error(message);
      return;
   }
   if (message.type !== MessageType.Send)
      return;
   const requestCmd = message.payload.command as string;
   switch (requestCmd) {
      case 'Ping':
         console.log('Received ping from shellcode.');
         script.post({ type: requestCmd });
         break;
   }
}