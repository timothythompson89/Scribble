// Starts the .NET runtime and runs Program.main. runMain keeps the runtime alive afterwards, so
// the browser can keep calling into Scribble for events and frames.
import { dotnet } from "./_framework/dotnet.js";

const { runMain } = await dotnet.withApplicationArgumentsFromQuery().create();
await runMain();
